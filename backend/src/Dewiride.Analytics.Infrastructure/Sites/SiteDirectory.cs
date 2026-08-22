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
/// <param name="allowance">Decides whether the organisation may take on another site.</param>
public sealed class SiteDirectory(
    ControlPlaneDbContext database,
    TimeProvider timeProvider,
    ITelemetryPurge telemetry,
    HybridCache cache,
    ISiteAllowance allowance)
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
    /// <remarks>
    /// Both claims are read, and the wider applies, exactly as resolving a scope on one site does.
    /// A list built from grants alone would leave somebody invited into an account looking at an
    /// empty dashboard while every screen below it would have let them in.
    /// </remarks>
    public async Task<IReadOnlyList<SiteMembershipView>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var rows = await database.Sites
            .AsNoTracking()
            .Select(site => new
            {
                site.Id,
                site.Domain,
                site.DisplayName,
                site.TimeZoneId,
                Granted = database.SiteMemberships
                    .Where(membership => membership.SiteId == site.Id && membership.UserId == userId)
                    .Select(membership => (SiteRole?)membership.Role)
                    .FirstOrDefault(),
                Standing = database.OrganizationMemberships
                    .Where(membership =>
                        membership.OrganizationId == site.OrganizationId && membership.UserId == userId)
                    .Select(membership => (OrganizationRole?)membership.Role)
                    .FirstOrDefault(),
            })
            .Where(row => row.Granted != null || row.Standing != null)
            // Ordered before the result is shaped, not after. Sorting on a property of a value
            // the query has already constructed is not something the database can be asked to do,
            // and the whole table would be read back to do it here instead.
            .OrderBy(row => row.DisplayName)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new SiteMembershipView(
                row.Id,
                row.Domain,
                row.DisplayName,
                row.TimeZoneId,
                OrganizationRoles.Widest(row.Granted, row.Standing)!.Value)),
        ];
    }

    /// <inheritdoc />
    public async Task<SiteAddition> AddAsync(
        Guid userId,
        NewSite site,
        CancellationToken cancellationToken)
    {
        // Which organisation the new site joins comes from one they already own a site in, so a
        // person can only ever add a site alongside the ones they are already responsible for.
        var organizationId = await OrganizationForNewSiteAsync(userId, cancellationToken).ConfigureAwait(false);

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

        // Asked last of the four, and after the hostname has been recognised as one already here.
        // Somebody adding a site they already have should be told that, whatever room they have
        // left; the answer they need is the one about the site they named.
        if (!await allowance.AllowsAnotherAsync(organizationId.Value, cancellationToken).ConfigureAwait(false))
        {
            return new SiteAddition(SiteAdditionOutcome.LimitReached, null);
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
    /// leaving somebody whose only claim to an organisation was those sites with none. That is not
    /// a recoverable state for them: a new site joins an organisation they already have a claim to,
    /// and the first-run claim that would otherwise hand out an organisation is long spent.
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

        if (await WouldLeaveNowhereToBeginAgainAsync(userId, cancellationToken).ConfigureAwait(false))
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
    /// The site a person may destroy, where the one they named is one of them.
    /// </summary>
    /// <remarks>
    /// Owning the site itself, or owning the account it belongs to. The grant is part of the lookup
    /// rather than a question asked before it, so a site that does not exist and a site the caller
    /// may not destroy are the same answer and this cannot be used to find out which identifiers on
    /// an installation are real. It also means the right to destroy is established here, by the
    /// component that does the destroying, rather than trusted from whoever called it.
    /// </remarks>
    /// <param name="userId">The person asking.</param>
    /// <param name="siteId">The site they named.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The site, tracked for deletion, or nothing where they do not own it.</returns>
    private async Task<Site?> OwnedSiteAsync(Guid userId, Guid siteId, CancellationToken cancellationToken) =>
        await database.Sites
            .Where(candidate => candidate.Id == siteId
                && (database.SiteMemberships.Any(membership =>
                        membership.SiteId == candidate.Id
                        && membership.UserId == userId
                        && membership.Role == SiteRole.Owner)
                    || database.OrganizationMemberships.Any(membership =>
                        membership.OrganizationId == candidate.OrganizationId
                        && membership.UserId == userId
                        && membership.Role == OrganizationRole.Owner)))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Whether removing a site would leave this person with nowhere to put another.
    /// </summary>
    /// <remarks>
    /// Only ever true of somebody whose sole claim to an organisation is the site they are
    /// removing. Somebody who helps run the account keeps that standing whatever they remove, and
    /// a new site of theirs joins the organisation that standing is in — so the rule that protects
    /// the first case would only get in the second one's way.
    /// </remarks>
    /// <param name="userId">The person asking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> where the removal must be refused.</returns>
    private async Task<bool> WouldLeaveNowhereToBeginAgainAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var helpsRunAnAccount = await database.OrganizationMemberships
            .AsNoTracking()
            .AnyAsync(
                membership => membership.UserId == userId && membership.Role >= OrganizationRole.Admin,
                cancellationToken)
            .ConfigureAwait(false);

        if (helpsRunAnAccount)
        {
            return false;
        }

        var owned = await database.SiteMemberships
            .AsNoTracking()
            .CountAsync(
                membership => membership.UserId == userId && membership.Role == SiteRole.Owner,
                cancellationToken)
            .ConfigureAwait(false);

        return owned == 1;
    }

    /// <summary>
    /// The organisation a new site of this person's would join, if there is one.
    /// </summary>
    /// <remarks>
    /// A standing in an organisation counts as well as owning one of its sites, and is looked at
    /// first. Somebody asked to help run an account is expected to be able to add a website to it,
    /// and until they have added one they own none — so grants alone would make the first thing
    /// they were invited to do the one thing they could not.
    /// </remarks>
    /// <param name="userId">The person asking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The organisation, or nothing where they have no claim to one.</returns>
    private async Task<Guid?> OrganizationForNewSiteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var standing = await database.OrganizationMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId && membership.Role >= OrganizationRole.Admin)
            .OrderByDescending(membership => membership.Role)
            .ThenBy(membership => membership.GrantedAt)
            .ThenBy(membership => membership.OrganizationId)
            .Select(membership => (Guid?)membership.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (standing is not null)
        {
            return standing;
        }

        return await database.SiteMemberships
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
