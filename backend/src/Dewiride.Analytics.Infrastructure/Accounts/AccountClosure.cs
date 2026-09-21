using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Application.Dashboard;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Infrastructure.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// Closes an account, brings it back, and describes a closed one, in the control-plane database.
/// </summary>
/// <remarks>
/// <para>
/// Closing and restoring are each one transaction that opens by taking the account's own advisory
/// lock, re-reads the organisation under it, and only then decides. The lock is shared with the
/// sweep that deletes closed accounts, so the three acts take turns: two owners closing at once
/// close it once, an owner restoring while it is being deleted waits and then finds nothing to
/// restore, and nothing is ever half done.
/// </para>
/// <para>
/// What an edition has to do alongside — end a paid arrangement, say — happens inside the same
/// transaction, before it commits, so that a refusal there is a refusal here. What happens after
/// the commit is what only needs to happen eventually: the collector's cache is emptied so it
/// stops accepting reports at once rather than in a minute, and every owner is written to.
/// </para>
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="organizations">Where somebody stands.</param>
/// <param name="observers">What the edition does alongside.</param>
/// <param name="owners">Who is told.</param>
/// <param name="cache">The collector's cache of sites, to be emptied of this account's.</param>
/// <param name="dashboard">Where the product is published, for the link in a message.</param>
/// <param name="clock">Clock.</param>
/// <param name="logger">Log.</param>
public sealed partial class AccountClosure(
    ControlPlaneDbContext database,
    IOrganizationDirectory organizations,
    IEnumerable<IAccountClosureObserver> observers,
    OrganizationOwners owners,
    HybridCache cache,
    IOptions<DashboardOptions> dashboard,
    TimeProvider clock,
    ILogger<AccountClosure> logger) : IAccountClosure
{
    /// <inheritdoc />
    public async Task<ClosureOutcome> CloseAsync(Guid userId, CancellationToken cancellationToken)
    {
        var standing = await organizations.StandingForAsync(userId, cancellationToken).ConfigureAwait(false);

        if (standing is null)
        {
            return ClosureOutcome.NoSuchAccount;
        }

        if (standing.Value.Role != OrganizationRole.Owner)
        {
            return ClosureOutcome.NotOwner;
        }

        var organizationId = standing.Value.OrganizationId;
        var now = clock.GetUtcNow();
        IReadOnlyList<Guid> siteIds;
        string name;
        DateTimeOffset deletionDue;

        await using (var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            var organization = await database
                .LockedOrganizationAsync(organizationId, cancellationToken)
                .ConfigureAwait(false);

            if (organization is null)
            {
                return ClosureOutcome.NoSuchAccount;
            }

            if (organization.IsClosed)
            {
                return ClosureOutcome.AlreadyClosed;
            }

            organization.Close(userId, now);
            siteIds = await SiteIdsAsync(organizationId, cancellationToken).ConfigureAwait(false);

            foreach (var observer in observers)
            {
                await observer.ClosingAsync(organizationId, cancellationToken).ConfigureAwait(false);
            }

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            name = organization.Name;
            deletionDue = organization.DeletionDue!.Value;
        }

        await EvictAsync(siteIds, cancellationToken).ConfigureAwait(false);

        var closedBy = await NameOfAsync(userId, cancellationToken).ConfigureAwait(false) ?? name;
        var link = AccountLinks.To(dashboard.Value.PublishedAt, DashboardScreens.SignIn);

        await owners.TellAsync(
                organizationId,
                to => AccountClosedMessage.For(to, organizationId, name, closedBy, now, deletionDue, link),
                cancellationToken)
            .ConfigureAwait(false);

        Log.Closed(logger, siteIds.Count);

        return ClosureOutcome.Closed;
    }

    /// <inheritdoc />
    public async Task<ClosureOutcome> RestoreAsync(Guid userId, CancellationToken cancellationToken)
    {
        // The closed account comes first here where the open one comes first everywhere else,
        // because this is the one act that is about the closed one. The rest of the ordering is the
        // same as choosing where somebody stands, so the account brought back is the account the
        // closed screen described.
        var held = await HeldStandings.Of(database, userId, closedFirst: true)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (held is null)
        {
            return ClosureOutcome.NoSuchAccount;
        }

        if (held.Role != OrganizationRole.Owner)
        {
            return ClosureOutcome.NotOwner;
        }

        var organizationId = held.OrganizationId;
        var now = clock.GetUtcNow();
        IReadOnlyList<Guid> siteIds;
        string name;

        await using (var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            var organization = await database
                .LockedOrganizationAsync(organizationId, cancellationToken)
                .ConfigureAwait(false);

            if (organization is null)
            {
                return ClosureOutcome.NoSuchAccount;
            }

            if (!organization.IsClosed)
            {
                return ClosureOutcome.NotClosed;
            }

            organization.Reopen();
            siteIds = await SiteIdsAsync(organizationId, cancellationToken).ConfigureAwait(false);

            foreach (var observer in observers)
            {
                await observer.RestoringAsync(organizationId, cancellationToken).ConfigureAwait(false);
            }

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            name = organization.Name;
        }

        await EvictAsync(siteIds, cancellationToken).ConfigureAwait(false);

        var restoredBy = await NameOfAsync(userId, cancellationToken).ConfigureAwait(false) ?? name;
        var link = AccountLinks.To(dashboard.Value.PublishedAt, DashboardScreens.SignIn);

        await owners.TellAsync(
                organizationId,
                to => AccountRestoredMessage.For(to, organizationId, name, restoredBy, now, link),
                cancellationToken)
            .ConfigureAwait(false);

        Log.Restored(logger, siteIds.Count);

        return ClosureOutcome.Restored;
    }

    /// <inheritdoc />
    public async Task<ClosedAccount?> DescribeForAsync(Guid userId, CancellationToken cancellationToken)
    {
        var closed = await database.OrganizationMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Join(
                database.Organizations.Where(organization => organization.ClosedAt != null),
                membership => membership.OrganizationId,
                organization => organization.Id,
                (membership, organization) => new
                {
                    organization.Id,
                    organization.Name,
                    organization.ClosedAt,
                    organization.ClosedByUserId,
                    membership.Role,
                    membership.GrantedAt,
                })
            .OrderByDescending(held => held.Role)
            .ThenBy(held => held.GrantedAt)
            .ThenBy(held => held.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (closed is null)
        {
            return null;
        }

        var closedBy = closed.ClosedByUserId is null
            ? null
            : await NameOfAsync(closed.ClosedByUserId.Value, cancellationToken).ConfigureAwait(false);

        var hasOpenAccount = await database.OpenOrganizations
                .AnyAsync(
                    organization => database.OrganizationMemberships.Any(membership =>
                        membership.OrganizationId == organization.Id && membership.UserId == userId),
                    cancellationToken)
                .ConfigureAwait(false)
            || await database.OpenSites
                .AnyAsync(
                    site => database.SiteMemberships.Any(membership =>
                        membership.SiteId == site.Id && membership.UserId == userId),
                    cancellationToken)
                .ConfigureAwait(false);

        return new ClosedAccount(
            closed.Name,
            closed.ClosedAt!.Value,
            closed.ClosedAt.Value + Organization.ClosureRetention,
            closedBy,
            closed.ClosedByUserId == userId,
            closed.Role == OrganizationRole.Owner,
            hasOpenAccount);
    }

    private async Task<IReadOnlyList<Guid>> SiteIdsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await database.Sites
            .AsNoTracking()
            .Where(site => site.OrganizationId == organizationId)
            .Select(site => site.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Throws away what the collector holds for each site, so the change reaches the ingest path
    /// now rather than when the entry lapses.
    /// </summary>
    private async Task EvictAsync(IReadOnlyList<Guid> siteIds, CancellationToken cancellationToken)
    {
        foreach (var siteId in siteIds)
        {
            await cache.RemoveAsync(CachedSiteCatalog.CacheKey(siteId), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> NameOfAsync(Guid userId, CancellationToken cancellationToken) =>
        await database.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.DisplayName ?? user.Email)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 3601,
            Level = LogLevel.Information,
            Message = "An account was closed. Measurement has stopped for its {Sites} website(s) until it is brought back.")]
        public static partial void Closed(ILogger logger, int sites);

        [LoggerMessage(
            EventId = 3602,
            Level = LogLevel.Information,
            Message = "A closed account was brought back. Measurement has resumed for its {Sites} website(s).")]
        public static partial void Restored(ILogger logger, int sites);
    }
}
