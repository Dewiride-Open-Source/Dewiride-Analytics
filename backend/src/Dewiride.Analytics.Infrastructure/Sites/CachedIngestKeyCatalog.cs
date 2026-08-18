using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Dewiride.Analytics.Infrastructure.Sites;

/// <summary>
/// Resolves a presented secret to the site it may report for, cached.
/// </summary>
/// <remarks>
/// <para>
/// Held for a minute, in both directions. A server-side reporter presents the same secret on
/// every batch, so an uncached lookup would put the control-plane database in front of the
/// highest-volume write path in the product; and a secret that matches nothing is exactly what a
/// guessing attack looks like, so caching that answer too is what stops each guess from costing a
/// query. A key withdrawn a moment ago therefore keeps working for up to a minute, which is the
/// price of both.
/// </para>
/// <para>
/// The cache is keyed by the hash, never by the secret. Nothing that could be replayed is held
/// under a name anywhere, not even in memory.
/// </para>
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="cache">Cache used to hold resolved keys.</param>
/// <param name="timeProvider">Clock used to record that a key is in use.</param>
public sealed class CachedIngestKeyCatalog(
    ControlPlaneDbContext database,
    HybridCache cache,
    TimeProvider timeProvider) : IIngestKeyCatalog
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(1),
        LocalCacheExpiration = TimeSpan.FromMinutes(1),
    };

    /// <inheritdoc />
    public async Task<IngestAuthorization?> AuthorizeAsync(
        string presentedSecret,
        CancellationToken cancellationToken)
    {
        if (!IngestKeySecret.LooksWellFormed(presentedSecret))
        {
            return null;
        }

        var hash = IngestKeySecret.Hash(presentedSecret);

        return await cache.GetOrCreateAsync(
            $"ingest-key:{hash}",
            (database, timeProvider, hash),
            static async (state, token) => await ResolveAsync(state.database, state.timeProvider, state.hash, token)
                .ConfigureAwait(false),
            CacheOptions,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Looks a hash up and, if it is live, records that it is being used.
    /// </summary>
    /// <remarks>
    /// The use is recorded here rather than on every report, so it costs one statement per cache
    /// lapse instead of one per batch. That makes the recorded time accurate to about a minute,
    /// which is the resolution the question it answers — is anything still reporting with this? —
    /// actually needs.
    /// </remarks>
    private static async Task<IngestAuthorization?> ResolveAsync(
        ControlPlaneDbContext database,
        TimeProvider timeProvider,
        string hash,
        CancellationToken cancellationToken)
    {
        var live = database.SiteIngestKeys
            .Where(key => key.TokenHash == hash && key.RevokedAt == null);

        var found = await live
            .AsNoTracking()
            .Select(key => new IngestAuthorization { SiteId = key.SiteId, KeyId = key.Id })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (found is null)
        {
            return null;
        }

        await live
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(key => key.LastUsedAt, timeProvider.GetUtcNow()),
                cancellationToken)
            .ConfigureAwait(false);

        return found;
    }
}
