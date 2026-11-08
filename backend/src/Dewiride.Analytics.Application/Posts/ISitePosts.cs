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
    /// posts at most. Only the posts that are shown are named, at most <see cref="MostNamed"/> at once, by
    /// <see cref="NamedAsync"/>, and a search is made where they are kept, by <see cref="MatchingAsync"/>.
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

    /// <summary>
    /// Finds the posts a website's sitemap lists now whose title or path holds a text.
    /// </summary>
    /// <remarks>
    /// The text is whatever a request searched for, and it is only ever a value: compared with what each
    /// post of this website alone is called and with its path — the part of its address after the website's
    /// name, which every address it lists shares — without regard to case, and holding no wildcard: every
    /// character of it stands for itself. A whole web address searched for is compared by the path it names.
    /// </remarks>
    /// <param name="scope">The website, and the proof that the caller may read it.</param>
    /// <param name="text">The words searched for, as <see cref="PostSearch"/> reads them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The paths of the posts that hold it, in no particular order; none where no post does.</returns>
    /// <exception cref="ArgumentException">
    /// No text, or more than <see cref="PostSearch.MostCharacters"/> characters of it.
    /// </exception>
    Task<ImmutableArray<string>> MatchingAsync(TenantScope scope, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Finds one post a website's sitemap lists now, by the path the collector knows its page by.
    /// </summary>
    /// <remarks>
    /// The path is whatever a request named, and it is compared exactly as it is written with the posts of
    /// this website alone. A path that names no post of it — a page that is not a post, a post no longer
    /// listed, another website's post, a post's path spelled any other way, or a path no listed post could
    /// have — finds nothing.
    /// </remarks>
    /// <param name="scope">The website, and the proof that the caller may read it.</param>
    /// <param name="path">The path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The post, or nothing where the path names no post of this website.</returns>
    Task<FoundPost?> FindAsync(TenantScope scope, string path, CancellationToken cancellationToken);
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

/// <summary>
/// One post a website's sitemap lists now, found to be read on a page of its own.
/// </summary>
/// <param name="Listed">The post, as everything that counts the website's posts reads it.</param>
/// <param name="Address">Its address, to link to. Written by the website, so it is text, never markup.</param>
/// <param name="Title">What the page says it is called, or nothing where it said nothing.</param>
/// <param name="LandedOnAPost">
/// Whether the path its page answered at is the own path of a post the website lists, which makes that
/// path the other post's rather than this one's.
/// </param>
/// <param name="FirstListedAt">
/// When a sitemap first listed it. A month of posts counted before then was counted without it, so it says
/// nothing of how much the post was read in that month.
/// </param>
public sealed record FoundPost(
    ListedPost Listed,
    string Address,
    string? Title,
    bool LandedOnAPost,
    DateTimeOffset FirstListedAt);
