namespace Dewiride.Analytics.Application.Sites;

/// <summary>
/// Looks up sites on the ingest hot path.
/// </summary>
/// <remarks>
/// Separate from the general site repository because the access pattern is different: the
/// collector resolves a site on every single request and needs a cached, read-only view, while
/// site administration needs tracked entities and writes. Implementations cache aggressively;
/// a site's configuration changing a few seconds after it was saved is an acceptable trade for
/// not querying the control-plane database on every beacon.
/// </remarks>
public interface ISiteCatalog
{
    /// <summary>
    /// Resolves a site by its public identifier.
    /// </summary>
    /// <param name="siteId">The identifier carried in the tracker snippet.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The site, or <see langword="null"/> when no such site exists.</returns>
    Task<SiteSnapshot?> FindAsync(Guid siteId, CancellationToken cancellationToken);
}
