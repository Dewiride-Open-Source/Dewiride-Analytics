using System.Buffers.Binary;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Dewiride.Analytics.Infrastructure.Sites;

/// <summary>
/// Lists a person's sites from the control-plane database, and adds and removes them.
/// </summary>
/// <remarks>
/// Uncached, unlike the catalogue the collector uses. The dashboard asks this once when it loads
/// rather than once per beacon, and holding a person's list of sites in a cache is how somebody
/// keeps seeing a site after their access to it was taken away.
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="timeProvider">Source of the time a site and its ownership are stamped with.</param>
/// <param name="telemetry">Removes what the telemetry store holds for a site.</param>
/// <param name="cache">Cache the collector resolves sites through.</param>
public sealed class SiteDirectory(
    ControlPlaneDbContext database,
    TimeProvider timeProvider,
    ITelemetryPurge telemetry,
    HybridCache cache)
    : ISiteDirectory
{
    /// <summary>
    /// Namespace the removal lock is taken in.
    /// </summary>
    /// <remarks>
    /// PostgreSQL keeps two advisory lock spaces, one addressed by a single 64-bit key and one by a
    /// pair of 32-bit keys, and they do not overlap. Using the pair puts this lock somewhere the
    /// single-key lock the first-run claim takes cannot reach, so neither has to know the other's
    /// number.
    /// </remarks>
    private const int RemovalLockNamespace = 0x44_57_53_52;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SiteMembershipView>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await database.SiteMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Join(
                database.Sites.AsNoTracking(),
                membership => membership.SiteId,
                site => site.Id,
                (membership, site) => new { Site = site, membership.Role })
            // Ordered before the result is shaped, not after. Sorting on a property of a value
            // the query has already constructed is not something the database can be asked to do,
            // and the whole table would be read back to do it here instead.
            .OrderBy(row => row.Site.DisplayName)
            .ThenBy(row => row.Site.Id)
            .Select(row => new SiteMembershipView(
                row.Site.Id,
                row.Site.Domain,
                row.Site.DisplayName,
                row.Site.TimeZoneId,
                row.Role))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<SiteAddition> AddAsync(
        Guid userId,
        NewSite site,
        CancellationToken cancellationToken)
    {
        // Which organisation the new site joins comes from one they already own a site in, so a
        // person can only ever add a site alongside the ones they are already responsible for.
        var organizationId = await OwnedOrganizationAsync(userId, cancellationToken).ConfigureAwait(false);

        if (organizationId is null)
        {
            return new SiteAddition(SiteAdditionOutcome.NotAllowed, null);
        }

        var now = timeProvider.GetUtcNow();

        if (!TryDescribe(organizationId.Value, site, now, out var described))
        {
            return new SiteAddition(SiteAdditionOutcome.Unusable, null);
        }

        // Checked against the normalised hostname rather than against what was typed, so the same
        // site in different letters is still the same site. Two rows for one hostname would split
        // its traffic between two entries nobody could tell apart.
        var measured = await database.Sites
            .AsNoTracking()
            .AnyAsync(
                existing => existing.OrganizationId == organizationId.Value
                    && existing.Domain == described.Domain,
                cancellationToken)
            .ConfigureAwait(false);

        if (measured)
        {
            return new SiteAddition(SiteAdditionOutcome.AlreadyMeasured, null);
        }

        database.Sites.Add(described);
        database.SiteMemberships.Add(
            new SiteMembership(Guid.CreateVersion7(now), described.Id, userId, SiteRole.Owner, now));

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SiteAddition(
            SiteAdditionOutcome.Added,
            new SiteMembershipView(
                described.Id,
                described.Domain,
                described.DisplayName,
                described.TimeZoneId,
                SiteRole.Owner));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Everything runs inside a single transaction that begins by taking an advisory lock on the
    /// person asking, because the last-site rule is a decision made from a count and then acted on.
    /// Two removals arriving together would both read two owned sites, both pass, and both delete —
    /// leaving an account owning none. That is not a recoverable state: a new site joins an
    /// organisation the person already owns one in, so owning none means never being able to add
    /// one, and the first-run claim that would otherwise hand out an organisation is long spent.
    /// The lock is per person rather than global so that two customers removing sites at the same
    /// moment never wait on each other.
    /// </para>
    /// <para>
    /// The site is looked up before anything is counted, because every outcome after this point is
    /// a statement about the site that was named. Counting first would answer a question about a
    /// site the caller may hold no role on at all, and report the last site they own as being the
    /// one they asked about.
    /// </para>
    /// <para>
    /// The order the remaining steps run in is the whole of what makes this safe to retry. The
    /// telemetry goes first, because a purge that fails rolls the transaction back and leaves every
    /// row and every control-plane row exactly where they were, so the removal can simply be asked
    /// for again. Deleting the control-plane row first and then failing to purge would strand the
    /// telemetry: every read is scoped through a site, so rows belonging to one that no longer
    /// exists cannot be named, cannot be read, and cannot be deleted, while still occupying the
    /// disk. It also has to sit inside the guard rather than ahead of it, or a removal refused for
    /// being somebody's last site would already have destroyed that site's history on its way to
    /// saying no.
    /// </para>
    /// <para>
    /// The memberships, the ingest keys and the classification bookmarks go with the site, by
    /// cascade in the database rather than by four deletions here.
    /// </para>
    /// <para>
    /// The collector's cached snapshot is thrown away last, once the deletion is committed. Until
    /// it is, reports for the site are still accepted for up to a minute and written against a site
    /// nothing can read — so the eviction is what makes the removal take effect on the ingest path
    /// at once rather than eventually.
    /// </para>
    /// </remarks>
    public async Task<SiteRemoval> RemoveAsync(Guid userId, Guid siteId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await database.Database
            .ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock({RemovalLockNamespace}, {LockKeyFor(userId)})",
                cancellationToken)
            .ConfigureAwait(false);

        var site = await OwnedSiteAsync(userId, siteId, cancellationToken).ConfigureAwait(false);

        if (site is null)
        {
            return new SiteRemoval(SiteRemovalOutcome.NoSuchSite);
        }

        var owned = await database.SiteMemberships
            .AsNoTracking()
            .CountAsync(
                membership => membership.UserId == userId && membership.Role == SiteRole.Owner,
                cancellationToken)
            .ConfigureAwait(false);

        if (owned == 1)
        {
            return new SiteRemoval(SiteRemovalOutcome.OnlyOne);
        }

        await telemetry.PurgeSiteAsync(siteId, cancellationToken).ConfigureAwait(false);

        database.Sites.Remove(site);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        await cache.RemoveAsync(CachedSiteCatalog.CacheKey(siteId), cancellationToken).ConfigureAwait(false);

        return new SiteRemoval(SiteRemovalOutcome.Removed);
    }


    /// <summary>
    /// Folds an account's identity into the 32 bits an advisory lock key is addressed by.
    /// </summary>
    /// <remarks>
    /// Every byte contributes, because these identifiers are time-ordered and their leading bytes
    /// barely differ between two accounts created in the same week. Two accounts folding onto one
    /// key would only make one of them wait for the other; it can never let a removal past the
    /// guard, so the fold does not have to be collision-free.
    /// </remarks>
    /// <param name="userId">The person asking.</param>
    /// <returns>The key to lock on.</returns>
    private static int LockKeyFor(Guid userId)
    {
        Span<byte> bytes = stackalloc byte[16];
        userId.TryWriteBytes(bytes);

        return BinaryPrimitives.ReadInt32LittleEndian(bytes)
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[4..])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[8..])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
    }

    /// <summary>
    /// The site a person owns, where the one they named is one of them.
    /// </summary>
    /// <remarks>
    /// The grant is part of the lookup rather than a question asked before it, so a site that does
    /// not exist and a site the caller does not own are the same answer and this cannot be used to
    /// find out which identifiers on an installation are real. It also means the right to destroy
    /// is established here, by the component that does the destroying, rather than trusted from
    /// whoever called it.
    /// </remarks>
    /// <param name="userId">The person asking.</param>
    /// <param name="siteId">The site they named.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The site, tracked for deletion, or nothing where they do not own it.</returns>
    private async Task<Site?> OwnedSiteAsync(Guid userId, Guid siteId, CancellationToken cancellationToken) =>
        await database.Sites
            .Where(candidate => candidate.Id == siteId
                && database.SiteMemberships.Any(membership =>
                    membership.SiteId == candidate.Id
                    && membership.UserId == userId
                    && membership.Role == SiteRole.Owner))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// The organisation a person owns a site in, if they own one at all.
    /// </summary>
    /// <param name="userId">The person asking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The organisation, or nothing where they own no site.</returns>
    private async Task<Guid?> OwnedOrganizationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var found = await database.SiteMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId && membership.Role == SiteRole.Owner)
            .Join(
                database.Sites.AsNoTracking(),
                membership => membership.SiteId,
                existing => existing.Id,
                (_, existing) => new { existing.OrganizationId, existing.Id })
            .OrderBy(row => row.Id)
            .Select(row => (Guid?)row.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return found;
    }

    /// <summary>
    /// Builds the site, or reports that it cannot be built from what was asked for.
    /// </summary>
    /// <remarks>
    /// The hostname and the time zone are checked by the site itself rather than a second time
    /// here, so there is one definition of an acceptable site and it is the one that stores it.
    /// </remarks>
    /// <param name="organizationId">Organisation the site joins.</param>
    /// <param name="asked">What was asked for.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="site">The site, where one could be built.</param>
    /// <returns>Whether it could be built.</returns>
    private static bool TryDescribe(
        Guid organizationId,
        NewSite asked,
        DateTimeOffset now,
        out Site site)
    {
        try
        {
            site = new Site(
                Guid.CreateVersion7(now),
                organizationId,
                asked.Domain,
                asked.TimeZoneId,
                now);

            return true;
        }
        catch (ArgumentException)
        {
            site = null!;

            return false;
        }
    }
}
