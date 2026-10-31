using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Sites;

/// <summary>
/// Lists every site on this installation from the control-plane database.
/// </summary>
/// <remarks>
/// Uncached. It is read once per run of the background work rather than once per request, and a
/// site added a moment ago should start being judged on the next run rather than whenever a cache
/// happened to lapse. Sites of a closed account are left out of the list: nothing is measured for
/// them, and a verdict written into an account that is about to be deleted would be work nobody can
/// read.
/// </remarks>
/// <param name="database">Control-plane database.</param>
public sealed class SiteRoster(ControlPlaneDbContext database) : ISiteRoster
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SiteRegistration>> ListAsync(CancellationToken cancellationToken) =>
        await database.OpenSites
            .AsNoTracking()
            .OrderBy(site => site.CreatedAt)
            .ThenBy(site => site.Id)
            .Select(site => new SiteRegistration(site.Id, site.Domain, site.CreatedAt, site.TimeZoneId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    /// <remarks>
    /// The work runs inside a transaction holding the lock removal takes before it deletes a site's
    /// telemetry, and the site is looked for only once the lock is held — so a removal under way is
    /// waited for, and then the site is gone.
    /// </remarks>
    public async Task<bool> WhileItExistsAsync(
        Guid siteId,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await AdvisoryLocks
            .TakeAsync(database, AdvisoryLocks.SiteTelemetryNamespace, siteId, cancellationToken)
            .ConfigureAwait(false);

        var exists = await database.Sites
            .AsNoTracking()
            .AnyAsync(site => site.Id == siteId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return false;
        }

        await work(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }
}
