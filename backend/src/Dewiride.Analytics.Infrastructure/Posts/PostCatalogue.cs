using System.Collections.Immutable;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Infrastructure.Persistence;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Reads the paths a website's posts are counted at, from the control plane, for the work that counts
/// them month by month.
/// </summary>
/// <param name="database">Control-plane database.</param>
internal sealed class PostCatalogue(ControlPlaneDbContext database) : IPostCatalogue
{
    /// <inheritdoc />
    public async Task<ImmutableArray<string>> CountedPathsAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var listed = await ListedPosts.ReadAsync(database, siteId, cancellationToken).ConfigureAwait(false);

        return PostsInsights.CountedPaths(listed);
    }
}
