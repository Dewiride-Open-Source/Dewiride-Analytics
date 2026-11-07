using System.Collections.Immutable;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// The posts a website's sitemap lists now, as everything that reads them reads them.
/// </summary>
/// <remarks>
/// One definition for the screen that shows a website's posts, the count Settings gives, and the work
/// that counts their views month by month, so a post shown is a post counted and the number on the
/// screen is the number in Settings: a page the sitemap lists now that is known to be a post.
/// </remarks>
internal static class ListedPosts
{
    /// <summary>
    /// Selects the posts a website lists now.
    /// </summary>
    /// <param name="database">Control-plane database.</param>
    /// <param name="siteId">The website.</param>
    /// <returns>The posts, unread and untracked.</returns>
    public static IQueryable<SitePost> Of(ControlPlaneDbContext database, Guid siteId) =>
        database.SitePosts
            .AsNoTracking()
            .Where(post => post.SiteId == siteId && post.UnlistedAt == null && post.IsPost);

    /// <summary>
    /// Reads every post a website lists now, with where each is read and when it was published.
    /// </summary>
    /// <param name="database">Control-plane database.</param>
    /// <param name="siteId">The website.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The posts, in no particular order.</returns>
    public static async Task<ImmutableArray<ListedPost>> ReadAsync(
        ControlPlaneDbContext database,
        Guid siteId,
        CancellationToken cancellationToken)
    {
        var listed = await Of(database, siteId)
            .Select(post => new ListedPost(post.Path, post.LandedPath, post.PublishedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. listed];
    }
}
