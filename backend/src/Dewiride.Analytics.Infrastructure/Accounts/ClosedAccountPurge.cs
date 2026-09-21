using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Dashboard;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Infrastructure.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// Reminds the owners of a closed account that deletion is coming, and deletes it when the time
/// has run.
/// </summary>
/// <remarks>
/// <para>
/// One pass does two things, each over its own set of accounts. A closed account a week from
/// deletion is reminded once; a closed account whose retention has run is deleted — its telemetry
/// first, then its rows, then the people who belonged nowhere else — in the order that leaves the
/// least behind if it stops half way. Telemetry goes before the rows for the reason removing a
/// website does: a site is the only handle the telemetry store has, and a row deleted first would
/// leave its rows unreachable and undeletable.
/// </para>
/// <para>
/// Each account is handled in a scope of its own, with its own database context and its own
/// transaction under the account's advisory lock, and re-read under that lock before anything is
/// done. An account brought back after this pass chose it is left alone; one that fails is logged
/// and left for the next pass, and nothing of it — no tracked row, no half-written change — can
/// ride along into the next account's transaction, because the scope that held it is gone.
/// </para>
/// <para>
/// The reminder is claimed before it is sent: one guarded update, under the account's lock, stamps
/// the row, and only the pass that stamped it sends. That is what makes it once — against a
/// restore that lands between the choosing and the sending, and across two engines sweeping the
/// same hour — without leaning on a mail server to notice a repeat, which the free product's mail
/// server cannot. The lock is what keeps the claim and a restore in turn: a restore reads the row
/// under it and writes the row under it, so the stamp is either seen and cleared by the restore or
/// refused by the claim, and never written into a row that has just been reopened.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates the scope each account's work is resolved from.</param>
/// <param name="telemetry">Deletes what the telemetry store holds for a site.</param>
/// <param name="cache">The collector's cache of sites, to be emptied of a deleted account's.</param>
/// <param name="dashboard">Where the product is published, for the link in a message.</param>
/// <param name="clock">Clock.</param>
/// <param name="logger">Log.</param>
public sealed partial class ClosedAccountPurge(
    IServiceScopeFactory scopeFactory,
    ITelemetryPurge telemetry,
    HybridCache cache,
    IOptions<DashboardOptions> dashboard,
    TimeProvider clock,
    ILogger<ClosedAccountPurge> logger)
{
    /// <summary>
    /// Runs one pass: reminders for the accounts a week from deletion, deletion for the accounts
    /// whose time has run.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        foreach (var due in await ReminderCandidatesAsync(now, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await RemindAsync(due, now, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                Log.ReminderFailed(logger, failure);
            }
        }

        foreach (var organizationId in await PurgeCandidatesAsync(now, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await PurgeAsync(organizationId, now, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                Log.PurgeFailed(logger, failure);
            }
        }
    }

    private async Task<IReadOnlyList<ReminderDue>> ReminderCandidatesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var deletionFrom = now - Organization.ClosureRetention;
        var reminderFrom = now - (Organization.ClosureRetention - Organization.DeletionReminderLead);

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        return await database.Organizations
            .AsNoTracking()
            .Where(organization => organization.ClosedAt != null
                && organization.DeletionReminderSentAt == null
                && organization.ClosedAt <= reminderFrom
                && organization.ClosedAt > deletionFrom)
            .OrderBy(organization => organization.ClosedAt)
            .Select(organization => new ReminderDue(organization.Id, organization.ClosedAt!.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Guid>> PurgeCandidatesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var deletionFrom = now - Organization.ClosureRetention;

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        return await database.Organizations
            .AsNoTracking()
            .Where(organization => organization.ClosedAt != null && organization.ClosedAt <= deletionFrom)
            .OrderBy(organization => organization.ClosedAt)
            .Select(organization => organization.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RemindAsync(ReminderDue due, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        string? name;

        await using (var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            await AdvisoryLocks
                .TakeAsync(database, AdvisoryLocks.AccountClosureNamespace, due.OrganizationId, cancellationToken)
                .ConfigureAwait(false);

            // The claim. It matches only the closure this pass chose — an account brought back and
            // closed again has a different instant — and only while nobody has stamped it, so
            // exactly one pass ever sees a row change here.
            var claimed = await database.Organizations
                .Where(organization => organization.Id == due.OrganizationId
                    && organization.ClosedAt == due.ClosedAt
                    && organization.DeletionReminderSentAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(organization => organization.DeletionReminderSentAt, now),
                    cancellationToken)
                .ConfigureAwait(false);

            if (claimed != 1)
            {
                return;
            }

            name = await database.Organizations
                .AsNoTracking()
                .Where(organization => organization.Id == due.OrganizationId)
                .Select(organization => organization.Name)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (name is null)
        {
            return;
        }

        var owners = scope.ServiceProvider.GetRequiredService<OrganizationOwners>();
        var link = AccountLinks.To(dashboard.Value.PublishedAt, DashboardScreens.SignIn);
        var deletionDue = due.ClosedAt + Organization.ClosureRetention;

        await owners.TellAsync(
                due.OrganizationId,
                to => AccountDeletionReminderMessage.For(to, due.OrganizationId, name, due.ClosedAt, deletionDue, link),
                cancellationToken)
            .ConfigureAwait(false);

        Log.Reminded(logger);
    }

    private async Task PurgeAsync(Guid organizationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var observers = scope.ServiceProvider.GetRequiredService<IEnumerable<IAccountClosureObserver>>();
        List<Guid> siteIds;
        int people;

        await using (var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            var organization = await database
                .LockedOrganizationAsync(organizationId, cancellationToken)
                .ConfigureAwait(false);

            // Gone, brought back, or closed again more recently than this pass believed: whoever
            // held the lock before this transaction changed the facts, and none of those is an
            // account to delete now.
            if (organization?.DeletionDue is not { } deletionDue || deletionDue > now)
            {
                return;
            }

            siteIds = await database.Sites
                .AsNoTracking()
                .Where(site => site.OrganizationId == organizationId)
                .Select(site => site.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var members = await database.OrganizationMemberships
                .AsNoTracking()
                .Where(membership => membership.OrganizationId == organizationId)
                .Select(membership => membership.UserId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var siteId in siteIds)
            {
                await telemetry.PurgeSiteAsync(siteId, cancellationToken).ConfigureAwait(false);
            }

            database.Organizations.Remove(organization);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            people = await DeletePeopleAsync(database, members, now, cancellationToken).ConfigureAwait(false);

            foreach (var observer in observers)
            {
                await observer.PurgingAsync(organizationId, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var siteId in siteIds)
        {
            await cache.RemoveAsync(CachedSiteCatalog.CacheKey(siteId), cancellationToken).ConfigureAwait(false);
        }

        Log.Purged(logger, siteIds.Count, people);
    }

    /// <summary>
    /// Deletes the people the account leaves with nowhere to belong.
    /// </summary>
    /// <remarks>
    /// The account's own members who hold no standing anywhere else and no grant on any site; and,
    /// when this was the last organisation on the installation, everybody — an installation with
    /// no organisation has nothing for any account to belong to. The claim is recorded first, on
    /// every purge, if it never was: an account being deleted is an account that existed, so the
    /// installation is claimed whatever a neighbouring engine has or has not yet committed — and
    /// an installation whose accounts all arrived by signing up does not look unclaimed once they
    /// are gone. One statement that does nothing where the row exists, so two purges at once
    /// cannot collide on it.
    /// </remarks>
    private static async Task<int> DeletePeopleAsync(
        ControlPlaneDbContext database,
        IReadOnlyList<Guid> members,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await database.Database
            .ExecuteSqlAsync(
                $"""
                INSERT INTO installation_claims (id, claimed_at)
                SELECT {InstallationClaim.TheOnlyRow}, COALESCE(MIN(created_at), {now})
                FROM users
                ON CONFLICT (id) DO NOTHING
                """,
                cancellationToken)
            .ConfigureAwait(false);

        var emptied = !await database.Organizations.AnyAsync(cancellationToken).ConfigureAwait(false);

        return await database.Users
            .Where(user => (emptied || members.Contains(user.Id))
                && !database.OrganizationMemberships.Any(membership => membership.UserId == user.Id)
                && !database.SiteMemberships.Any(membership => membership.UserId == user.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private readonly record struct ReminderDue(Guid OrganizationId, DateTimeOffset ClosedAt);

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 3604,
            Level = LogLevel.Information,
            Message = "The owners of a closed account were reminded that it is a week from deletion.")]
        public static partial void Reminded(ILogger logger);

        [LoggerMessage(
            EventId = 3605,
            Level = LogLevel.Information,
            Message = "A closed account was deleted: {Sites} website(s) and {People} account(s) that belonged nowhere else.")]
        public static partial void Purged(ILogger logger, int sites, int people);

        [LoggerMessage(
            EventId = 3606,
            Level = LogLevel.Error,
            Message = "A closed account could not be deleted. It is tried again on the next pass.")]
        public static partial void PurgeFailed(ILogger logger, Exception failure);

        [LoggerMessage(
            EventId = 3607,
            Level = LogLevel.Error,
            Message = "The owners of a closed account could not be reminded that it is a week from deletion.")]
        public static partial void ReminderFailed(ILogger logger, Exception failure);
    }
}
