using System.Collections.Immutable;
using Dewiride.Analytics.Application.Tenancy;

namespace Dewiride.Analytics.Application.Posts;

/// <summary>
/// What is known about the posts a website lists in its sitemap.
/// </summary>
/// <remarks>
/// <para>
/// Every read takes a scope, which is proof that somebody may read this website. The identifier in
/// a request's address is printed in the page source of every page the website measures, so nothing
/// that answers a request may be able to name a website any other way.
/// </para>
/// <para>
/// A post is a page the sitemap lists now that is known to be a post: the posts Settings counts.
/// Whatever a post says about itself was written by the website, so a title and an address are text to
/// show and never anything else.
/// </para>
/// </remarks>
public interface ISitePosts
{
    /// <summary>Most posts one question may ask to have named.</summary>
    const int MostNamed = 100;

    /// <summary>
    /// Says how reading a website's sitemap is going.
    /// </summary>
    /// <param name="scope">The website, and the proof that the caller may read it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Where the reading stands.</returns>
    Task<SitemapReport> DescribeSitemapAsync(TenantScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// Lists every post a website's sitemap lists now, with where each is read and when it was
    /// published.
    /// </summary>
    /// <remarks>
    /// Read whole, with nothing a post says about itself but its date: a website lists fifty thousand
    /// posts at most, and only the few that are shown are named, by <see cref="NamedAsync"/>.
    /// </remarks>
    /// <param name="scope">The website, and the proof that the caller may read it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The posts, in no particular order; none where the website lists none.</returns>
    Task<ImmutableArray<ListedPost>> ListAsync(TenantScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// Names some of the posts a website's sitemap lists now: where each is, and what it is called.
    /// </summary>
    /// <param name="scope">The website, and the proof that the caller may read it.</param>
    /// <param name="paths">The posts' paths, at most <see cref="MostNamed"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Each of them the website still lists, in no particular order. A path that names no post of this
    /// website names nothing.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">More paths than <see cref="MostNamed"/>.</exception>
    Task<ImmutableArray<NamedPost>> NamedAsync(
        TenantScope scope,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken);
}

/// <summary>
/// One post a website's sitemap lists now.
/// </summary>
/// <param name="Path">The path the collector knows its page by.</param>
/// <param name="LandedPath">
/// The path its page answered at, where the website redirected it to another of its own paths, or
/// nothing where it answered at its own.
/// </param>
/// <param name="PublishedAt">When the page says it was published, or nothing where it did not say.</param>
public sealed record ListedPost(string Path, string? LandedPath, DateTimeOffset? PublishedAt);

/// <summary>
/// One post, named to be shown.
/// </summary>
/// <param name="Path">The path the collector knows its page by.</param>
/// <param name="Address">Its address, to link to. Written by the website, so it is text, never markup.</param>
/// <param name="Title">What the page says it is called, or nothing where it said nothing.</param>
/// <param name="PublishedAt">When the page says it was published, or nothing where it did not say.</param>
public sealed record NamedPost(string Path, string Address, string? Title, DateTimeOffset? PublishedAt);
