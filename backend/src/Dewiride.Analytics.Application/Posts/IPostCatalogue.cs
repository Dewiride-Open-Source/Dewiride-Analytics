using System.Collections.Immutable;
using Dewiride.Analytics.Application.Consolidation;

namespace Dewiride.Analytics.Application.Posts;

/// <summary>
/// The paths a website's posts are read at, for the work that counts them month by month.
/// </summary>
/// <remarks>
/// For the system rather than for a person, on the same terms as <see cref="IMonthConsolidation"/>:
/// the website comes from the control plane rather than from a request, so there is no caller whose
/// membership could be checked and no <see cref="Tenancy.TenantScope"/> to take. Nothing reachable from
/// a request may use this. What a person reads of a website's posts is asked through
/// <see cref="ISitePosts"/>, with proof of a role on the website.
/// </remarks>
public interface IPostCatalogue
{
    /// <summary>
    /// Lists the paths a website's posts are counted at, as <see cref="PostsInsights.CountedPaths"/>
    /// works them out from the posts its sitemap lists now.
    /// </summary>
    /// <param name="siteId">The website.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The paths, distinct and in ordinal order; none where the website lists no posts.</returns>
    Task<ImmutableArray<string>> CountedPathsAsync(Guid siteId, CancellationToken cancellationToken);
}
