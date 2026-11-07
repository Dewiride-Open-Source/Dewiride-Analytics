using Dewiride.Analytics.Application.Tenancy;

namespace Dewiride.Analytics.Application.Posts;

/// <summary>
/// What is known about the posts a website lists in its sitemap.
/// </summary>
/// <remarks>
/// Every read takes a scope, which is proof that somebody may read this website. The identifier in
/// a request's address is printed in the page source of every page the website measures, so nothing
/// that answers a request may be able to name a website any other way.
/// </remarks>
public interface ISitePosts
{
    /// <summary>
    /// Says how reading a website's sitemap is going.
    /// </summary>
    /// <param name="scope">The website, and the proof that the caller may read it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Where the reading stands.</returns>
    Task<SitemapReport> DescribeSitemapAsync(TenantScope scope, CancellationToken cancellationToken);
}
