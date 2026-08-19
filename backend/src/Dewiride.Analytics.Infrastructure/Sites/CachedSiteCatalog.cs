using System.Collections.Immutable;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Dewiride.Analytics.Infrastructure.Sites;

/// <summary>
/// Resolves sites for the ingest path, cached.
/// </summary>
/// <remarks>
/// The collector resolves a site on every beacon, so an uncached lookup would put the
/// control-plane database on the hot path of the highest-volume endpoint in the product. Entries
/// are held briefly: a site's settings taking up to a minute to take effect is a fair trade, and
/// a negative result is cached too, because an unknown site identifier is the shape a flood of
/// junk traffic takes and it must not become a database query per request.
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="cache">Cache used to hold resolved sites.</param>
public sealed class CachedSiteCatalog(ControlPlaneDbContext database, HybridCache cache) : ISiteCatalog
{
    /// <summary>
    /// Where one site's settings are held.
    /// </summary>
    /// <remarks>
    /// Written here rather than at each use because whatever changes a site has to throw the same
    /// entry away, and a second spelling of this would leave a saved setting waiting out the
    /// minute below before it took effect.
    /// </remarks>
    /// <param name="siteId">The site.</param>
    /// <returns>The cache entry's name.</returns>
    internal static string CacheKey(Guid siteId) => $"site:{siteId}";

    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(1),
        LocalCacheExpiration = TimeSpan.FromMinutes(1),
    };

    /// <inheritdoc />
    public async Task<SiteSnapshot?> FindAsync(Guid siteId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheKey(siteId),
            (database, siteId),
            static async (state, token) => await state.database.Sites
                .AsNoTracking()
                .Where(site => site.Id == state.siteId)
                // Projected in the database query rather than after loading the aggregate, so what
                // is cached is a set of values that survives being written out and read back. The
                // aggregate would not: it is restored through a constructor, and everything set
                // after construction would silently come back at its default.
                .Select(site => new SiteSnapshot
                {
                    Id = site.Id,
                    Domain = site.Domain,
                    RetainQueryStrings = site.RetainQueryStrings,
                    CaptureClicks = site.CaptureClicks,
                    AllowedOrigins = site.AllowedOrigins.ToImmutableArray(),
                })
                .FirstOrDefaultAsync(token)
                .ConfigureAwait(false),
            CacheOptions,
            cancellationToken: cancellationToken).ConfigureAwait(false);
}
