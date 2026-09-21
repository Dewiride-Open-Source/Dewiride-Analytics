using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// Reads an organisation under the lock that closing, restoring and deleting an account share.
/// </summary>
internal static class LockedOrganizations
{
    /// <summary>
    /// Takes the account's lock and reads the organisation under it, tracked.
    /// </summary>
    /// <remarks>
    /// Read after the lock rather than before, because whatever was true before the lock was
    /// taken may have been changed by whoever held it. Must be called inside a transaction: the
    /// lock is released when the transaction ends, and outside one it would be released at once.
    /// </remarks>
    /// <param name="database">Control-plane database, with a transaction open.</param>
    /// <param name="organizationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The organisation, or <see langword="null"/> where there is no longer one.</returns>
    public static async Task<Organization?> LockedOrganizationAsync(
        this ControlPlaneDbContext database,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        await AdvisoryLocks
            .TakeAsync(database, AdvisoryLocks.AccountClosureNamespace, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await database.Organizations
            .FirstOrDefaultAsync(organization => organization.Id == organizationId, cancellationToken)
            .ConfigureAwait(false);
    }
}
