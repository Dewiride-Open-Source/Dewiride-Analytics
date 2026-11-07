using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Decides which reader may read which website, across every engine sharing a control plane.
/// </summary>
/// <remarks>
/// <para>
/// A claim is a lease written on the website's row: a token, which every write a pass makes names, and
/// a time it runs out. It holds nothing open while the pass goes off to somebody's website for minutes
/// at a time, and an engine that dies leaves nothing behind but a claim that runs out by itself. An
/// advisory lock held for a whole pass would hold a transaction and a connection for just as long.
/// </para>
/// <para>
/// The time a claim runs out is set and compared by the database's own clock, and never an engine's. A
/// pass ending before its claim can be taken is the whole of what keeps two passes apart, and it should
/// not depend on how closely two machines agree about what time it is.
/// </para>
/// <para>
/// The limit on how often a website is asked for anything is the website's, however many rows name it.
/// Domains are not unique across an installation, so two accounts can name one website, and a website
/// answers at both of a host's names. A claim is therefore refused while another row for the same
/// website holds one. That is a question about one row and a change to another, and two of those side by
/// side would each see nothing in the other's way, so the two steps run under a lock for the website.
/// The lock lasts for the two statements and is released before the pass begins.
/// </para>
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="options">How long a claim lasts.</param>
/// <param name="clock">The clock everything but a claim's own time is written by.</param>
internal sealed class SitemapClaims(
    ControlPlaneDbContext database,
    IOptions<SitemapOptions> options,
    TimeProvider clock)
{
    /// <summary>How many websites one look for something to read tries to claim.</summary>
    private const int MostTried = 8;

    /// <summary>
    /// Puts the rows right before anything is claimed: one row for each sitemap somebody has named on an
    /// open account, describing that address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Changing an address deletes its row in the same transaction, so a row describing another address
    /// comes only from a look that read the old address a moment before the change. Nothing could ever
    /// claim it, or replace it, so it is cleared first. A claim still running is left to run out, since
    /// its writes come to nothing either way.
    /// </para>
    /// <para>
    /// A new row takes its place in the queue as of now, behind every website that has been waiting
    /// longer. Saving an address is free and can be done as often as anybody likes, so a row that went to
    /// the front would let one account keep every reader for itself by saving again and again.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the rows are right.</returns>
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await database.Database
            .ExecuteSqlAsync(
                $"""
                WITH gone AS (
                    DELETE FROM sitemap_reads r
                    USING sites s
                    WHERE s.id = r.site_id
                      AND s.sitemap_address IS DISTINCT FROM r.address
                      AND (r.claimed_until IS NULL OR r.claimed_until < clock_timestamp())
                    RETURNING r.site_id
                )
                DELETE FROM sitemap_parts p
                USING gone
                WHERE p.site_id = gone.site_id
                """,
                cancellationToken)
            .ConfigureAwait(false);

        var neverYet = DateTimeOffset.UnixEpoch;
        var now = clock.GetUtcNow();

        await database.Database
            .ExecuteSqlAsync(
                $"""
                INSERT INTO sitemap_reads
                    (site_id, address, host, state, next_pass_at, next_cycle_at, updated_at)
                SELECT s.id,
                       s.sitemap_address,
                       split_part(s.sitemap_address, '/', 3),
                       {nameof(SitemapState.Waiting)},
                       {now},
                       {neverYet},
                       {now}
                FROM sites s
                JOIN organizations o ON o.id = s.organization_id AND o.closed_at IS NULL
                WHERE s.sitemap_address IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM sitemap_reads r WHERE r.site_id = s.id)
                ON CONFLICT (site_id) DO NOTHING
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Claims the website that has waited longest, if any may be claimed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The claim, or nothing where there is nothing to read just now.</returns>
    public async Task<SitemapLease?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var candidates = await CandidatesAsync(clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);

        foreach (var candidate in candidates)
        {
            var lease = await TryClaimAsync(candidate, cancellationToken).ConfigureAwait(false);

            if (lease is not null)
            {
                return lease;
            }
        }

        return null;
    }

    /// <summary>
    /// Lets go of a claim, and says when the website's time next comes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Names the claim, so that letting go of one that was lost touches nobody else's.
    /// </para>
    /// <para>
    /// The website stays taken for one more gap, with no claim anybody could write under. Requests from
    /// one engine are kept apart by its pacer, but another engine knows nothing of those, and could
    /// otherwise pick up another row for this website and ask it within a moment of this pass's last
    /// request.
    /// </para>
    /// </remarks>
    /// <param name="lease">The claim.</param>
    /// <param name="nextPassAt">The earliest the website may be claimed again.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it has been let go of.</returns>
    public Task ReleaseAsync(SitemapLease lease, DateTimeOffset nextPassAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        var now = clock.GetUtcNow();
        var gap = options.Value.RequestGap;

        return database.Database.ExecuteSqlAsync(
            $"""
            UPDATE sitemap_reads
            SET claim_token = NULL,
                claimed_until = clock_timestamp() + {gap},
                next_pass_at = {nextPassAt},
                updated_at = {now}
            WHERE site_id = {lease.SiteId} AND claim_token = {lease.Token}
            """,
            cancellationToken);
    }

    /// <summary>
    /// Tries to claim one website.
    /// </summary>
    /// <remarks>
    /// Each statement reads the database afresh, so the second sees whatever the last holder of the lock
    /// committed, which is the reason they are two statements and not one.
    /// </remarks>
    /// <param name="candidate">The website's row, as it was found.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The claim, or nothing where it may not be claimed after all.</returns>
    public async Task<SitemapLease?> TryClaimAsync(SitemapRead candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var now = clock.GetUtcNow();
        var website = PublishedHosts.WebsiteName(candidate.Host);
        var twin = $"www.{website}";
        var token = Guid.NewGuid();
        var lasting = options.Value.Lease;

        await using var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await AdvisoryLocks
            .TakeAsync(database, AdvisoryLocks.SitemapWebsiteNamespace, website, cancellationToken)
            .ConfigureAwait(false);

        var claimed = await database.Database
            .ExecuteSqlAsync(
                $"""
                UPDATE sitemap_reads r
                SET claim_token = {token}, claimed_until = clock_timestamp() + {lasting}, updated_at = {now}
                WHERE r.site_id = {candidate.SiteId}
                  AND r.address = {candidate.Address}
                  AND r.next_pass_at <= {now}
                  AND (r.claimed_until IS NULL OR r.claimed_until < clock_timestamp())
                  AND NOT EXISTS (
                      SELECT 1 FROM sitemap_reads held
                      WHERE held.host IN ({website}, {twin}) AND held.claimed_until >= clock_timestamp())
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return claimed == 1 ? new SitemapLease(candidate.SiteId, candidate.Address, token) : null;
    }

    /// <summary>
    /// Finds the websites whose time has come, the longest waiting first.
    /// </summary>
    /// <remarks>
    /// A website already being read, at either of its names, is left out of even being tried, so that a
    /// website named by more rows than are tried at once cannot hold up every other website behind it.
    /// The claim itself is still what decides.
    /// </remarks>
    private Task<List<SitemapRead>> CandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        database.SitemapReads
            .FromSql(
                $"""
                SELECT r.*
                FROM sitemap_reads r
                JOIN sites s ON s.id = r.site_id AND s.sitemap_address = r.address
                JOIN organizations o ON o.id = s.organization_id AND o.closed_at IS NULL
                WHERE r.next_pass_at <= {now}
                  AND (r.claimed_until IS NULL OR r.claimed_until < clock_timestamp())
                  AND NOT EXISTS (
                      SELECT 1 FROM sitemap_reads held
                      WHERE (held.host = r.host OR held.host = 'www.' || r.host OR 'www.' || held.host = r.host)
                        AND held.claimed_until >= clock_timestamp())
                ORDER BY r.next_pass_at
                LIMIT {MostTried}
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
}

/// <summary>
/// One reader's claim on one website.
/// </summary>
/// <param name="SiteId">The website.</param>
/// <param name="Address">The sitemap the claim is for.</param>
/// <param name="Token">What names the claim in every write made under it.</param>
internal sealed record SitemapLease(Guid SiteId, string Address, Guid Token);
