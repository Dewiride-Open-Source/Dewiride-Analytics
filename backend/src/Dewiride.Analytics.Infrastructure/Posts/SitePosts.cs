using System.Collections.Immutable;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Application.Tenancy;
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
