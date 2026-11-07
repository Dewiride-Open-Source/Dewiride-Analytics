using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Everything a pass writes about a website's sitemap, each write one statement that names its claim.
/// </summary>
/// <remarks>
/// <para>
/// A write to the row a claim is on names the claim in what it looks for. A write to any other row asks
/// for the claim's row first, and takes a key lock on it. That is what makes the answer good for as long
/// as the statement runs: changing a website's address, or removing the website, deletes that row first,
/// and a deletion waits for the lock while a write waits for the deletion. So a write either finishes
/// before an address has changed or finds the row gone, and can never land just after because the statement
/// had already looked. A claim that has run out counts as lost, by the database's clock, whether or not
/// anybody has taken it.
/// </para>
/// <para>
/// A write that has to change something and did not means the claim is gone, and
/// <see cref="SitemapClaimLostException"/> stops the pass. Some writes can properly change nothing, such
/// as unlisting on a website where nothing has stopped being listed, and those say nothing: the next one
/// that has to change something finds out.
/// </para>
/// <para>
/// Whatever these hold was written by a website, so all of it travels as parameters, and a batch of pages
/// as one JSON parameter, which <c>jsonb_to_recordset</c> reads with nulls where they belong.
/// </para>
/// <para>
/// The moments written are all in UTC, which is the only offset the database client will take.
/// </para>
/// </remarks>
/// <param name="database">Control-plane database.</param>
internal sealed class PostWriter(ControlPlaneDbContext database)
{
    /// <summary>Uses up an attempt at the address, before anything is asked of the website.</summary>
    /// <param name="lease">The claim.</param>
    /// <param name="tryAgainAt">When it may be tried again, should this attempt come to nothing.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task BeginAddressAsync(
        SitemapLease lease,
        DateTimeOffset tryAgainAt,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            OnTheClaimedRow(
                lease,
                $"""
                UPDATE sitemap_reads
                SET next_cycle_at = {tryAgainAt}, updated_at = {now}
                """),
            cancellationToken);

    /// <summary>Gives back an attempt at the address nobody got an answer to, because the engine is stopping.</summary>
    /// <param name="lease">The claim.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task GiveBackAddressAsync(SitemapLease lease, DateTimeOffset now, CancellationToken cancellationToken) =>
        MustChangeAsync(
            OnTheClaimedRow(
                lease,
                $"""
                UPDATE sitemap_reads
                SET next_cycle_at = {now}, updated_at = {now}
                """),
            cancellationToken);

    /// <summary>
    /// Records that a sitemap could not be read at all.
    /// </summary>
    /// <param name="lease">The claim.</param>
    /// <param name="problem">What got in the way.</param>
    /// <param name="tryAgainAt">When it may be tried again.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task FailReadingAsync(
        SitemapLease lease,
        SitemapProblem problem,
        DateTimeOffset tryAgainAt,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            OnTheClaimedRow(
                lease,
                $"""
                UPDATE sitemap_reads
                SET state = {nameof(SitemapState.Failed)},
                    problem = {problem.ToString()},
                    read_at = {now},
                    next_cycle_at = {tryAgainAt},
                    cycle_started_at = NULL,
                    updated_at = {now}
                """),
            cancellationToken);

    /// <summary>
    /// Records what the address held, and that a reading of what it leads to is under way.
    /// </summary>
    /// <param name="lease">The claim.</param>
    /// <param name="opening">What the address held.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task OpenReadingAsync(
        SitemapLease lease,
        ReadingOpening opening,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            OnTheClaimedRow(
                lease,
                $"""
                UPDATE sitemap_reads
                SET cycle_started_at = {now},
                    root_kind = {opening.Kind.ToString()},
                    root_etag = {opening.Validators.ETag},
                    root_last_modified = {opening.Validators.LastModified?.ToUniversalTime()}::timestamptz,
                    root_problem = {opening.Problem?.ToString()},
                    updated_at = {now}
                """),
            cancellationToken);

    /// <summary>
    /// Keeps what a sitemap that is one list said about itself, now that all of the list has been read.
    /// </summary>
    /// <remarks>
    /// Only now, because asked with these it would answer that nothing has changed, and that answer would
    /// then stand for a list that was never read to its end.
    /// </remarks>
    /// <param name="lease">The claim.</param>
    /// <param name="validators">What it said.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task RememberAddressAsync(
        SitemapLease lease,
        WebsiteValidators validators,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            OnTheClaimedRow(
                lease,
                $"""
                UPDATE sitemap_reads
                SET root_etag = {validators.ETag},
                    root_last_modified = {validators.LastModified?.ToUniversalTime()}::timestamptz,
                    updated_at = {now}
                """),
            cancellationToken);

    /// <summary>
    /// Records how a reading ended.
    /// </summary>
    /// <param name="lease">The claim.</param>
    /// <param name="closing">How it ended.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task CloseReadingAsync(
        SitemapLease lease,
        ReadingClosing closing,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            OnTheClaimedRow(
                lease,
                $"""
                UPDATE sitemap_reads
                SET state = {closing.State.ToString()},
                    problem = {closing.Problem?.ToString()},
                    read_at = {now},
                    succeeded_at = COALESCE({closing.SucceededAt}::timestamptz, succeeded_at),
                    next_cycle_at = {closing.NextReadingAt},
                    cycle_started_at = NULL,
                    updated_at = {now}
                """),
            cancellationToken);

    /// <summary>
    /// Settles which parts a sitemap is made of, each waiting to be asked for.
    /// </summary>
    /// <remarks>
    /// A part it no longer names goes, and the pages it listed lose their reference to it, which a
    /// complete reading then finds them by. A part it still names keeps what it knows, so that it can be
    /// asked whether anything has changed. One statement, so nothing can see a list half replaced.
    /// </remarks>
    /// <param name="lease">The claim.</param>
    /// <param name="parts">The parts, each of a different address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once they are written.</returns>
    public Task NamePartsAsync(
        SitemapLease lease,
        IReadOnlyCollection<NamedPart> parts,
        CancellationToken cancellationToken)
    {
        var named = ToJson(parts, WritePart);

        return database.Database.ExecuteSqlAsync(
            $"""
            WITH held AS (
                SELECT 1
                FROM sitemap_reads r
                WHERE r.site_id = {lease.SiteId} AND r.claim_token = {lease.Token} AND r.claimed_until > clock_timestamp()
                FOR KEY SHARE
            ),
            named AS (
                SELECT e.id, e.address, e.is_posts
                FROM jsonb_to_recordset({named}::jsonb) AS e(id uuid, address text, is_posts boolean)
                WHERE EXISTS (SELECT 1 FROM held)
            ),
            gone AS (
                DELETE FROM sitemap_parts p
                WHERE p.site_id = {lease.SiteId}
                  AND EXISTS (SELECT 1 FROM held)
                  AND NOT EXISTS (SELECT 1 FROM named n WHERE n.address = p.address)
            )
            INSERT INTO sitemap_parts AS p (id, site_id, address, is_posts, entries, off_site_entries, query_entries)
            SELECT n.id, {lease.SiteId}, n.address, n.is_posts, 0, 0, 0
            FROM named n
            ON CONFLICT (site_id, address) DO UPDATE
            SET is_posts = EXCLUDED.is_posts, read_at = NULL, outcome = NULL
            """,
            cancellationToken);
    }

    /// <summary>
    /// Sets every part waiting to be asked for again, keeping what is known about each.
    /// </summary>
    /// <param name="lease">The claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task AskPartsAgainAsync(SitemapLease lease, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE sitemap_parts AS p
                SET read_at = NULL, outcome = NULL
                WHERE p.site_id = {lease.SiteId}
                """),
            cancellationToken);

    /// <summary>
    /// Records that a sitemap that is one list answered that nothing has changed.
    /// </summary>
    /// <param name="lease">The claim.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task KeepPartsAsTheyAreAsync(SitemapLease lease, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE sitemap_parts AS p
                SET read_at = {now}, outcome = {nameof(SitemapPartOutcome.Unchanged)}
                WHERE p.site_id = {lease.SiteId}
                """),
            cancellationToken);

    /// <summary>
    /// Uses up this reading's one attempt at a part, before anything is asked of the website.
    /// </summary>
    /// <remarks>
    /// It is written as having run out of time, because if nothing more is written that is what happened,
    /// by whichever way the engine stopped. The next reading asks again.
    /// </remarks>
    /// <param name="lease">The claim.</param>
    /// <param name="part">The part.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task BeginPartAsync(SitemapLease lease, Guid part, DateTimeOffset now, CancellationToken cancellationToken) =>
        MustChangeAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE sitemap_parts AS p
                SET read_at = {now}, outcome = {nameof(SitemapPartOutcome.TooSlow)}
                WHERE p.id = {part} AND p.site_id = {lease.SiteId}
                """),
            cancellationToken);

    /// <summary>Gives back an attempt at a part nobody got an answer to, because the engine is stopping.</summary>
    /// <param name="lease">The claim.</param>
    /// <param name="part">The part.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task GiveBackPartAsync(SitemapLease lease, Guid part, CancellationToken cancellationToken) =>
        MustChangeAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE sitemap_parts AS p
                SET read_at = NULL, outcome = NULL
                WHERE p.id = {part} AND p.site_id = {lease.SiteId}
                """),
            cancellationToken);

    /// <summary>
    /// Records that the whole of a part was read, and stamps it, which is what its pages are vouched for by.
    /// </summary>
    /// <param name="lease">The claim.</param>
    /// <param name="part">The part.</param>
    /// <param name="reading">What was read.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task FinishPartAsync(
        SitemapLease lease,
        Guid part,
        PartReading reading,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE sitemap_parts AS p
                SET generation = {reading.Generation},
                    etag = {reading.Validators.ETag},
                    last_modified = {reading.Validators.LastModified?.ToUniversalTime()}::timestamptz,
                    read_at = {now},
                    outcome = {nameof(SitemapPartOutcome.Read)},
                    entries = {reading.Entries},
                    off_site_entries = {reading.OffSiteEntries},
                    query_entries = {reading.QueryEntries}
                WHERE p.id = {part} AND p.site_id = {lease.SiteId}
                """),
            cancellationToken);

    /// <summary>
    /// Records that a part listed more than is kept.
    /// </summary>
    /// <remarks>
    /// It is not stamped, so nothing it listed is vouched for by this reading. How many it did list is
    /// kept all the same, since that is what the limit on the website as a whole is counted from.
    /// </remarks>
    /// <param name="lease">The claim.</param>
    /// <param name="part">The part.</param>
    /// <param name="entries">How many pages it got as far as.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task CutPartShortAsync(
        SitemapLease lease,
        Guid part,
        int entries,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE sitemap_parts AS p
                SET read_at = {now}, outcome = {nameof(SitemapPartOutcome.TooMany)}, entries = {entries}
                WHERE p.id = {part} AND p.site_id = {lease.SiteId}
                """),
            cancellationToken);

    /// <summary>
    /// Records what came of asking for a part, where that was not all of it being read.
    /// </summary>
    /// <param name="lease">The claim.</param>
    /// <param name="part">The part.</param>
    /// <param name="outcome">What came of it.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task SettlePartAsync(
        SitemapLease lease,
        Guid part,
        SitemapPartOutcome outcome,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE sitemap_parts AS p
                SET read_at = {now}, outcome = {outcome.ToString()}
                WHERE p.id = {part} AND p.site_id = {lease.SiteId}
                """),
            cancellationToken);

    /// <summary>
    /// Gives up on the parts not yet asked for, which end this reading as the last one did.
    /// </summary>
    /// <param name="lease">The claim.</param>
    /// <param name="outcome">What to record about them.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task GiveUpOnTheRestAsync(
        SitemapLease lease,
        SitemapPartOutcome outcome,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE sitemap_parts AS p
                SET read_at = {now}, outcome = {outcome.ToString()}
                WHERE p.site_id = {lease.SiteId} AND p.read_at IS NULL
                """),
            cancellationToken);

    /// <summary>
    /// Stores pages a part lists, each marking the reading of the part it was listed in.
    /// </summary>
    /// <remarks>
    /// A page never asked for stays due, and a page already asked for falls due again only if the sitemap
    /// says it has changed since then, which also sets its attempts back to nothing. Listing a page again
    /// does neither on its own, so reading a sitemap every hour does not read its pages every hour. A
    /// date that stands still or goes backwards is not a change.
    /// </remarks>
    /// <param name="lease">The claim.</param>
    /// <param name="listing">Where they were listed.</param>
    /// <param name="pages">The pages, each of a different path.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once they are written.</returns>
    public Task ListPagesAsync(
        SitemapLease lease,
        PartListing listing,
        IReadOnlyCollection<PostListing> pages,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var listed = ToJson(pages, WritePage);

        return MustChangeAsync(
            $"""
            INSERT INTO site_posts AS p
                (site_id, path, address, part_id, part_generation, listed_in_posts_part,
                 listed_modified_at, first_listed_at, page_due_at, page_attempts)
            SELECT {lease.SiteId}, e.path, e.address, {listing.Part}, {listing.Generation}, {listing.IsPosts},
                   e.lastmod, {now}, {now}, 0
            FROM jsonb_to_recordset({listed}::jsonb) AS e(path text, address text, lastmod timestamptz)
            WHERE EXISTS (
                SELECT 1 FROM sitemap_reads r
                WHERE r.site_id = {lease.SiteId} AND r.claim_token = {lease.Token} AND r.claimed_until > clock_timestamp()
                FOR KEY SHARE)
            ON CONFLICT (site_id, path) DO UPDATE
            SET address = EXCLUDED.address,
                part_id = EXCLUDED.part_id,
                part_generation = EXCLUDED.part_generation,
                listed_in_posts_part = EXCLUDED.listed_in_posts_part,
                listed_modified_at = EXCLUDED.listed_modified_at,
                unlisted_at = NULL,
                page_attempts = CASE
                    WHEN p.page_read_at IS NOT NULL
                     AND EXCLUDED.listed_modified_at IS NOT NULL
                     AND (p.page_read_lastmod IS NULL OR EXCLUDED.listed_modified_at > p.page_read_lastmod)
                    THEN 0
                    ELSE p.page_attempts
                END,
                page_due_at = CASE
                    WHEN p.page_read_at IS NULL
                    THEN COALESCE(p.page_due_at, EXCLUDED.page_due_at)
                    WHEN EXCLUDED.listed_modified_at IS NOT NULL
                     AND (p.page_read_lastmod IS NULL OR EXCLUDED.listed_modified_at > p.page_read_lastmod)
                    THEN EXCLUDED.page_due_at
                    ELSE p.page_due_at
                END
            """,
            cancellationToken);
    }

    /// <summary>
    /// Takes off the list every page the reading just finished did not vouch for.
    /// </summary>
    /// <remarks>
    /// Only ever called at the end of a reading that got through all of a sitemap, so a part that failed
    /// or was cut short can never unlist anything. A part answering that nothing has changed keeps its
    /// stamp, so its pages need nothing written to go on being vouched for.
    /// </remarks>
    /// <param name="lease">The claim.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task UnlistAsync(SitemapLease lease, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE site_posts AS p
                SET unlisted_at = {now}, page_due_at = NULL
                WHERE p.site_id = {lease.SiteId}
                  AND p.unlisted_at IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM sitemap_parts sp
                      WHERE sp.id = p.part_id AND sp.generation = p.part_generation)
                """),
            cancellationToken);

    /// <summary>
    /// Forgets pages that have been off the list for a long time, or that there are too many of.
    /// </summary>
    /// <remarks>
    /// The second is what stops a sitemap filling the database. One listing different pages each hour has
    /// them all taken off the list each hour, and a year is a long time for those to pile up. The oldest
    /// go first, so that a page that has only just gone is still known if it comes back.
    /// </remarks>
    /// <param name="lease">The claim.</param>
    /// <param name="before">How long ago they have to have been taken off it.</param>
    /// <param name="most">How many off the list may be known at once.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task ForgetUnlistedAsync(
        SitemapLease lease,
        DateTimeOffset before,
        int most,
        CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlAsync(
            WhileHeld(
                lease,
                $"""
                DELETE FROM site_posts AS p
                WHERE p.site_id = {lease.SiteId}
                  AND (p.unlisted_at < {before}
                       OR p.path IN (
                           SELECT unlisted.path
                           FROM site_posts unlisted
                           WHERE unlisted.site_id = {lease.SiteId} AND unlisted.unlisted_at IS NOT NULL
                           ORDER BY unlisted.unlisted_at DESC, unlisted.path
                           OFFSET {most}))
                """),
            cancellationToken);

    /// <summary>
    /// Forgets the pages no file of the sitemap vouches for any longer, keeping the most recently found
    /// longest, beyond what a website keeps between readings.
    /// </summary>
    /// <remarks>
    /// A part that never reads to its end never takes anything off the list, so without this what a website
    /// holds could only grow, and once it reached the ceiling no reading could store a page or finish again,
    /// however the sitemap was put right. Pages a whole part vouches for count towards what is kept and are
    /// never forgotten here.
    /// </remarks>
    /// <param name="lease">The claim.</param>
    /// <param name="pages">How many pages are kept at most.</param>
    /// <param name="bytes">How much their paths and addresses take up at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task ForgetUnvouchedAsync(SitemapLease lease, int pages, long bytes, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlAsync(
            WhileHeld(
                lease,
                $"""
                DELETE FROM site_posts AS p
                USING (
                    SELECT ranked.path
                    FROM (
                        SELECT s.path,
                               sp.id IS NOT NULL AS vouched,
                               row_number() OVER kept AS pages,
                               sum(octet_length(s.path) + octet_length(s.address)) OVER kept AS bytes
                        FROM site_posts s
                        LEFT JOIN sitemap_parts sp ON sp.id = s.part_id AND sp.generation = s.part_generation
                        WHERE s.site_id = {lease.SiteId}
                        WINDOW kept AS (ORDER BY sp.id IS NOT NULL DESC, s.first_listed_at DESC, s.path)
                    ) AS ranked
                    WHERE NOT ranked.vouched AND (ranked.pages > {pages} OR ranked.bytes > {bytes})
                ) AS beyond
                WHERE p.site_id = {lease.SiteId} AND p.path = beyond.path
                """),
            cancellationToken);

    /// <summary>
    /// Uses up an attempt at a page, before anything is asked of the website.
    /// </summary>
    /// <remarks>
    /// If nothing more is written, by whichever way the engine stopped, the page comes round again when
    /// this says and not before. After the last attempt it does not come round at all until the sitemap
    /// says it has changed.
    /// </remarks>
    /// <param name="lease">The claim.</param>
    /// <param name="path">The page's path.</param>
    /// <param name="attempt">Which attempt this is.</param>
    /// <param name="dueAgainAt">When the next attempt falls due, or nothing where this is the last.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task BeginPageAsync(
        SitemapLease lease,
        string path,
        short attempt,
        DateTimeOffset? dueAgainAt,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE site_posts AS p
                SET page_attempts = {attempt},
                    page_due_at = {dueAgainAt}::timestamptz,
                    page_read_at = {now},
                    page_read_lastmod = p.listed_modified_at
                WHERE p.site_id = {lease.SiteId} AND p.path = {path}
                """),
            cancellationToken);

    /// <summary>Records what a page says about itself, which is the end of reading it until it changes.</summary>
    /// <param name="lease">The claim.</param>
    /// <param name="path">The page's path.</param>
    /// <param name="saying">What it says, and what to ask with next time.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task SavePageAsync(
        SitemapLease lease,
        string path,
        PageSaying saying,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE site_posts AS p
                SET title = {saying.Facts.Title},
                    title_source = {saying.Facts.TitleSource?.ToString()},
                    published_at = {saying.Facts.PublishedAt?.ToUniversalTime()}::timestamptz,
                    published_source = {saying.Facts.PublishedSource?.ToString()},
                    page_type = {saying.Facts.PageType?.ToString()},
                    landed_path = {saying.LandedPath},
                    page_outcome = {nameof(PostPageOutcome.Read)},
                    page_attempts = 0,
                    page_due_at = NULL,
                    page_read_at = {now},
                    page_etag = {saying.Validators.ETag},
                    page_last_modified = {saying.Validators.LastModified?.ToUniversalTime()}::timestamptz
                WHERE p.site_id = {lease.SiteId} AND p.path = {path}
                """),
            cancellationToken);

    /// <summary>
    /// Records an answer about a page that will not change by being asked again, which is the end of
    /// reading it until the sitemap says it has changed.
    /// </summary>
    /// <param name="lease">The claim.</param>
    /// <param name="path">The page's path.</param>
    /// <param name="outcome">What came of asking.</param>
    /// <param name="pageType">What the answer says the page is, where it says.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task SettlePageAsync(
        SitemapLease lease,
        string path,
        PostPageOutcome outcome,
        PostPageType? pageType,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE site_posts AS p
                SET page_outcome = {outcome.ToString()},
                    page_type = COALESCE({pageType?.ToString()}::varchar, p.page_type),
                    page_attempts = 0,
                    page_due_at = NULL,
                    page_read_at = {now},
                    page_read_lastmod = p.listed_modified_at
                WHERE p.site_id = {lease.SiteId} AND p.path = {path}
                """),
            cancellationToken);

    /// <summary>
    /// Hands back an attempt at a page that nothing can be said about, because robots.txt could not be read at
    /// where it redirected to.
    /// </summary>
    /// <remarks>
    /// It was asked for, and that stays written. It has not used up one of its few attempts on trouble
    /// that has nothing to do with it, and it comes round again once the website has been left alone.
    /// </remarks>
    /// <param name="lease">The claim.</param>
    /// <param name="path">The page's path.</param>
    /// <param name="attempts">How many attempts it had used before this one.</param>
    /// <param name="dueAt">When it next falls due.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task GiveBackPageAsync(
        SitemapLease lease,
        string path,
        short attempts,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE site_posts AS p
                SET page_attempts = {attempts}, page_due_at = {dueAt}
                WHERE p.site_id = {lease.SiteId} AND p.path = {path}
                """),
            cancellationToken);

    /// <summary>
    /// Records an answer about a page that may be different next time.
    /// </summary>
    /// <remarks>
    /// The attempt it used and when the next falls due were written before asking. All this can do to
    /// that is push it further off, where the website asked for longer. It cannot bring back a page whose
    /// attempts have run out.
    /// </remarks>
    /// <param name="lease">The claim.</param>
    /// <param name="path">The page's path.</param>
    /// <param name="outcome">What came of asking.</param>
    /// <param name="notBefore">The earliest the website asked to be asked again, where it did.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once it is written.</returns>
    public Task PutPageOffAsync(
        SitemapLease lease,
        string path,
        PostPageOutcome outcome,
        DateTimeOffset? notBefore,
        CancellationToken cancellationToken) =>
        MustChangeAsync(
            WhileHeld(
                lease,
                $"""
                UPDATE site_posts AS p
                SET page_outcome = {outcome.ToString()},
                    page_due_at = CASE
                        WHEN p.page_due_at IS NULL THEN NULL
                        ELSE GREATEST(p.page_due_at, {notBefore}::timestamptz)
                    END
                WHERE p.site_id = {lease.SiteId} AND p.path = {path}
                """),
            cancellationToken);

    /// <summary>
    /// A statement about the row a claim is on, finished by looking for the row by its claim.
    /// </summary>
    /// <param name="lease">The claim.</param>
    /// <param name="statement">Everything but the <c>WHERE</c>.</param>
    /// <returns>The statement, with every value still a parameter.</returns>
    private static FormattableString OnTheClaimedRow(SitemapLease lease, FormattableString statement)
    {
        var site = statement.ArgumentCount;
        var token = site + 1;

        return FormattableStringFactory.Create(
            $"{statement.Format} WHERE site_id = {{{site}}} AND claim_token = {{{token}}} AND claimed_until > clock_timestamp()",
            [.. statement.GetArguments(), lease.SiteId, lease.Token]);
    }

    /// <summary>
    /// A statement about any other row, finished by asking for the claim's row and locking its key.
    /// </summary>
    /// <param name="lease">The claim.</param>
    /// <param name="statement">The statement, up to and including the conditions on the rows it changes.</param>
    /// <returns>The statement, with every value still a parameter.</returns>
    private static FormattableString WhileHeld(SitemapLease lease, FormattableString statement)
    {
        var site = statement.ArgumentCount;
        var token = site + 1;

        return FormattableStringFactory.Create(
            $$"""
            {{statement.Format}}
            AND EXISTS (
                SELECT 1 FROM sitemap_reads r
                WHERE r.site_id = {{{site}}} AND r.claim_token = {{{token}}} AND r.claimed_until > clock_timestamp()
                FOR KEY SHARE)
            """,
            [.. statement.GetArguments(), lease.SiteId, lease.Token]);
    }

    private static string ToJson<T>(IEnumerable<T> items, Action<Utf8JsonWriter, T> write)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();

            foreach (var item in items)
            {
                json.WriteStartObject();
                write(json, item);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static void WritePart(Utf8JsonWriter json, NamedPart part)
    {
        json.WriteString("id", part.Id);
        json.WriteString("address", part.Address);
        json.WriteBoolean("is_posts", part.IsPosts);
    }

    private static void WritePage(Utf8JsonWriter json, PostListing page)
    {
        json.WriteString("path", page.Path);
        json.WriteString("address", page.Address);

        if (page.LastModified is { } lastModified)
        {
            json.WriteString("lastmod", lastModified);
        }
    }

    private async Task MustChangeAsync(FormattableString statement, CancellationToken cancellationToken)
    {
        var changed = await database.Database.ExecuteSqlAsync(statement, cancellationToken).ConfigureAwait(false);

        if (changed == 0)
        {
            throw new SitemapClaimLostException();
        }
    }
}

/// <summary>
/// What a sitemap's own address held, as a reading of what it leads to begins.
/// </summary>
/// <param name="Kind">Whether it is a list of parts or a list of pages.</param>
/// <param name="Validators">What to ask with next time.</param>
/// <param name="Problem">What was wrong with the list of parts, where anything was.</param>
internal sealed record ReadingOpening(
    SitemapRootKind Kind,
    WebsiteValidators Validators,
    SitemapProblem? Problem);

/// <summary>
/// How a reading of a sitemap ended.
/// </summary>
/// <param name="State">How it stands.</param>
/// <param name="Problem">What got in the way, where anything did.</param>
/// <param name="SucceededAt">When it got through, or nothing where it did not.</param>
/// <param name="NextReadingAt">When the sitemap is next read from the top.</param>
internal sealed record ReadingClosing(
    SitemapState State,
    SitemapProblem? Problem,
    DateTimeOffset? SucceededAt,
    DateTimeOffset NextReadingAt);

/// <summary>
/// One file a sitemap's address leads to.
/// </summary>
/// <param name="Id">What it goes by, should it be new.</param>
/// <param name="Address">Its address.</param>
/// <param name="IsPosts">Whether it is recognised as holding the website's posts.</param>
internal sealed record NamedPart(Guid Id, string Address, bool IsPosts);

/// <summary>
/// What came of reading the whole of one part.
/// </summary>
/// <param name="Generation">What the pages it listed were marked with.</param>
/// <param name="Validators">What to ask with next time.</param>
/// <param name="Entries">How many pages it listed.</param>
/// <param name="OffSiteEntries">How many of its entries led off the website.</param>
/// <param name="QueryEntries">How many of its entries were told apart only by a query string.</param>
internal sealed record PartReading(
    Guid Generation,
    WebsiteValidators Validators,
    int Entries,
    int OffSiteEntries,
    int QueryEntries);

/// <summary>
/// Which reading of which part listed some pages.
/// </summary>
/// <param name="Part">The part.</param>
/// <param name="Generation">The reading of it, which goes on the part only once all of it has been read.</param>
/// <param name="IsPosts">Whether the part is recognised as holding the website's posts.</param>
internal sealed record PartListing(Guid Part, Guid Generation, bool IsPosts);

/// <summary>
/// One page as a sitemap lists it.
/// </summary>
/// <param name="Path">Its path, as the collector reduces a page to one.</param>
/// <param name="Address">Its address, without anything after its path.</param>
/// <param name="LastModified">When the sitemap says it last changed, where it says.</param>
internal sealed record PostListing(string Path, string Address, DateTimeOffset? LastModified);

/// <summary>
/// What a page that was read said.
/// </summary>
/// <param name="Facts">What it says about itself.</param>
/// <param name="Validators">What to ask with next time.</param>
/// <param name="LandedPath">The path it answered at, where a redirect took it from its own.</param>
internal sealed record PageSaying(PageFacts Facts, WebsiteValidators Validators, string? LandedPath);
