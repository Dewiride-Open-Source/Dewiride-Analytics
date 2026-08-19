using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Dewiride.Analytics.Infrastructure.Sites;

/// <summary>
/// Reads and changes what a site collects.
/// </summary>
/// <remarks>
/// A save throws away what the collector has cached for that site. Without it the collector would
/// keep acting on the old setting for up to a minute, which on the way from on to off means a
/// panel saying nothing is being recorded while records are still arriving.
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="cache">Cache the collector resolves sites through.</param>
internal sealed class SiteSettings(ControlPlaneDbContext database, HybridCache cache) : ISiteSettings
{
    /// <inheritdoc />
    public async Task<CollectionSettings?> DescribeAsync(Guid siteId, CancellationToken cancellationToken) =>
        await database.Sites
            .AsNoTracking()
            .Where(site => site.Id == siteId)
            .Select(site => new CollectionSettings(site.CaptureClicks))
            .Cast<CollectionSettings?>()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<CollectionSettings?> ApplyAsync(
        Guid siteId,
        CollectionSettings settings,
        CancellationToken cancellationToken)
    {
        var site = await database.Sites
            .FirstOrDefaultAsync(candidate => candidate.Id == siteId, cancellationToken)
            .ConfigureAwait(false);

        if (site is null)
        {
            return null;
        }

        site.SetClickCapture(settings.CaptureClicks);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(CachedSiteCatalog.CacheKey(siteId), cancellationToken).ConfigureAwait(false);

        return new CollectionSettings(site.CaptureClicks);
    }
}
