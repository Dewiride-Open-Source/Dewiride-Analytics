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
/// happened to lapse. Sites of a closed account are left out: nothing is measured for them, and a
/// verdict written into an account that is about to be deleted would be work nobody can read.
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
            .Select(site => new SiteRegistration(site.Id, site.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
