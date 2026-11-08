using System.Collections.Immutable;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Reads what is known about the posts a website lists in its sitemap, from the control plane.
/// </summary>
/// <param name="database">Control-plane database.</param>
/// <param name="options">Whether this installation reads sitemaps.</param>
internal sealed class SitePosts(ControlPlaneDbContext database, IOptions<SitemapOptions> options) : ISitePosts
{
    /// <inheritdoc />
    /// <remarks>
    /// A row describing another address says nothing about this one. Changing the address deletes the
    /// row in the same transaction, so finding one here is a reader that got its row in just before
    /// the change, and until it is cleared away the sitemap that was named is still waiting. Posts are
    /// counted whichever address listed them, because they are kept across a change of address and
    /// stay listed until a complete reading of the new one says otherwise.
    /// </remarks>
    public async Task<SitemapReport> DescribeSitemapAsync(TenantScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var address = await database.Sites
            .AsNoTracking()
            .Where(site => site.Id == scope.SiteId)
            .Select(site => site.SitemapAddress)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (address is null)
        {
            return new SitemapReport(null, options.Value.Enabled ? SitemapState.None : SitemapState.Off, null, null, null, 0, 0, 0, 0);
        }

        var reading = await database.SitemapReads
            .AsNoTracking()
            .Where(read => read.SiteId == scope.SiteId && read.Address == address)
            .Select(read => new Reading(read.State, read.Problem, read.ReadAt, read.SucceededAt))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var counted = await CountAsync(scope.SiteId, cancellationToken).ConfigureAwait(false);

        return new SitemapReport(
            address,
            options.Value.Enabled ? reading?.State ?? SitemapState.Waiting : SitemapState.Off,
            reading?.Problem,
            reading?.ReadAt,
            reading?.SucceededAt,
            counted.Posts,
            counted.Dated,
            counted.Reading,
            counted.TurnedAway);
    }

    /// <inheritdoc />
    public Task<ImmutableArray<ListedPost>> ListAsync(TenantScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return ListedPosts.ReadAsync(database, scope.SiteId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ImmutableArray<NamedPost>> NamedAsync(
        TenantScope scope,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(paths.Count, ISitePosts.MostNamed);

        if (paths.Count == 0)
        {
            return [];
        }

        var asked = paths.ToArray();
        var named = await ListedPosts.Of(database, scope.SiteId)
            .Where(post => asked.Contains(post.Path))
            .Select(post => new NamedPost(post.Path, post.Address, post.Title, post.PublishedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. named];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Compared by the database over this website's rows alone, reached by the post's key, so only the paths
    /// that match come back rather than every post's title and address. A post's path is compared rather
    /// than its whole address, which begins with the website's name, and a whole address searched for is
    /// compared by the path it names (<see cref="PostMatch.PathIn"/>). The text travels as a value bound to
    /// the statement, in a pattern whose every wildcard is escaped (<see cref="PostMatch"/>), and case is set
    /// aside as far as the database's locale sets it aside.
    /// </para>
    /// <para>
    /// A text holding a character no title or address can hold matches nothing, and is answered so without
    /// asking: the database refuses that character outright rather than finding nothing.
    /// </para>
    /// </remarks>
    public async Task<ImmutableArray<string>> MatchingAsync(
        TenantScope scope,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(text.Length, PostSearch.MostCharacters);

        if (text.Contains('\0', StringComparison.Ordinal))
        {
            return [];
        }

        var named = PostMatch.Holding(text);
        var spelled = PostMatch.Holding(PostMatch.PathIn(text));
        var matching = await ListedPosts.Of(database, scope.SiteId)
            .Where(post =>
                (post.Title != null && EF.Functions.ILike(post.Title, named, PostMatch.Escape))
                || EF.Functions.ILike(post.Path, spelled, PostMatch.Escape))
            .Select(post => post.Path)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. matching];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Matched on the post's key, the website and the path exactly as written, so a path differing in
    /// a single character — its case, a slash, how it is escaped — is another path. A path longer than
    /// any a post can be listed at, or holding a character no listed path can hold, names no post and is
    /// answered so without asking: the database refuses that character outright rather than finding
    /// nothing.
    /// </para>
    /// <para>
    /// Where the post's page landed at another path, a second lookup says whether that path is another
    /// listed post's own.
    /// </para>
    /// </remarks>
    public async Task<FoundPost?> FindAsync(TenantScope scope, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length > Site.MaxSitemapAddressLength || path.Contains('\0', StringComparison.Ordinal))
        {
            return null;
        }

        var found = await ListedPosts.Of(database, scope.SiteId)
            .Where(post => post.Path == path)
            .Select(post => new FoundPost(
                new ListedPost(post.Path, post.LandedPath, post.PublishedAt),
                post.Address,
                post.Title,
                false,
                post.FirstListedAt))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (found?.Listed.LandedPath is not { } landed)
        {
            return found;
        }

        var landedOnAPost = await ListedPosts.Of(database, scope.SiteId)
            .AnyAsync(post => post.Path == landed, cancellationToken)
            .ConfigureAwait(false);

        return found with { LandedOnAPost = landedOnAPost };
    }

    /// <summary>
    /// Counts what a website's sitemap lists at the moment.
    /// </summary>
    /// <remarks>
    /// A website with nothing listed has no group to answer with, and the empty answer this comes back
    /// with then is the right one.
    /// </remarks>
    /// <param name="siteId">The website.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The counts.</returns>
    private async Task<Counted> CountAsync(Guid siteId, CancellationToken cancellationToken) =>
        await database.SitePosts
            .AsNoTracking()
            .Where(post => post.SiteId == siteId && post.UnlistedAt == null)
            .GroupBy(_ => 1)
            .Select(listed => new Counted(
                listed.Count(post => post.IsPost),
                listed.Count(post => post.IsPost && post.PublishedAt != null),
                listed.Count(post => post.PageDueAt != null),
                listed.Count(post => post.PageOutcome == PostPageOutcome.TurnedAway)))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>What a reading of a sitemap last came to.</summary>
    /// <param name="State">How it stands.</param>
    /// <param name="Problem">What got in its way.</param>
    /// <param name="ReadAt">When it last finished.</param>
    /// <param name="SucceededAt">When it last got through the sitemap.</param>
    private sealed record Reading(
        SitemapState State,
        SitemapProblem? Problem,
        DateTimeOffset? ReadAt,
        DateTimeOffset? SucceededAt);

    /// <summary>What a sitemap lists at the moment.</summary>
    /// <param name="Posts">Pages known to be posts.</param>
    /// <param name="Dated">Those posts with a publish date.</param>
    /// <param name="Reading">Pages not read yet.</param>
    /// <param name="TurnedAway">Pages the website turned the reader away from, the last time they were asked for.</param>
    private readonly record struct Counted(int Posts, int Dated, int Reading, int TurnedAway);
}
