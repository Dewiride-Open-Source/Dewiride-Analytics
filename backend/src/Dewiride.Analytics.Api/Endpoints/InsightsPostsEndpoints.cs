using System.Collections.Immutable;
using System.Globalization;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Application.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Dewiride.Analytics.Api.Analytics.QueryVocabulary;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Reading what a website's posts have done: how many its sitemap lists, the newest and the most read
/// with their views across its whole record, when the last twelve months' were published, and every one
/// of them a page at a time.
/// </summary>
/// <remarks>
/// <para>
/// Beside the readings of the whole record, and on its terms: a post's views are read from what was kept
/// of each month and, from the first month nothing was kept of the posts of, from the reports, by the
/// arithmetic of the dashboard's list of pages (<see cref="PostsInsights"/>). So a post's views are never
/// behind the record's own figures, and a post listed with every other one has the figure Insights gives it.
/// </para>
/// <para>
/// On the same terms as every other read of a website's numbers: everything the caller supplied is
/// checked before the website is resolved, and a website that does not exist and one the caller has
/// no role on are answered identically. A title and an address are the website's own text: they travel
/// in the answer as values and are never logged. A search is the caller's own text, and is only ever
/// compared with them: it is never logged, no refusal repeats it, and the telemetry store never sees it.
/// </para>
/// </remarks>
internal static class InsightsPostsEndpoints
{
    /// <summary>How a day is written in an answer.</summary>
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>How a month is written in an answer.</summary>
    private const string MonthFormat = "yyyy-MM";

    /// <summary>Posts on a page when the caller does not say how many: a page somebody reads down.</summary>
    private const int DefaultPosts = 25;

    /// <summary>What a page of posts allows: no more posts than one question may have named.</summary>
    private static readonly ListBounds PostsBounds = new(DefaultPosts, ISitePosts.MostNamed, "posts");

    /// <summary>
    /// Maps the questions about a website's posts: what they have done, and every one of them a page at a
    /// time.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapInsightsPosts(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/sites/{siteId:guid}/insights/posts", PostsAsync)
            .WithName("SiteInsightsPosts")
            .WithSummary("Returns what a website's posts have done: how many its sitemap lists, the newest ten and the most read with their views across its whole record, and the days of the last twelve months a post was published on.");

        routes.MapGet("/api/sites/{siteId:guid}/posts", PostsPageAsync)
            .WithName("SitePostsPage")
            .WithSummary("Returns one page of a website's posts with their views across its whole record, searchable by title or address and ordered by the most read or the newest.");
    }

    /// <summary>
    /// Answers what a website's posts have done.
    /// </summary>
    /// <remarks>
    /// A website that lists no posts is answered at once, from the control plane alone. Otherwise only the
    /// posts shown are named.
    /// </remarks>
    private static async Task<Results<Ok<InsightsPostsResponse>, NotFound, ProblemHttpResult>> PostsAsync(
        [AsParameters] WholeRecordParameters parameters,
        ITenantScopeProvider scopes,
        ISitePosts posts,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryReadPopulation(parameters.Only, out var population))
        {
            return Unusable(UnknownPopulation);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var asOf = clock.GetUtcNow();
        var listed = await posts.ListAsync(scope, cancellationToken).ConfigureAwait(false);
        var viewing = listed.IsEmpty
            ? PostViewing.Nothing
            : await PostViewsAsync(scope, listed, population, telemetry, asOf, cancellationToken).ConfigureAwait(false);
        var reading = PostsInsights.Read(
            listed,
            viewing.Views,
            TimeZoneInfo.FindSystemTimeZoneById(scope.TimeZoneId),
            asOf);
        var named = await NameAsync(
                scope,
                reading.Latest.Select(post => post.Path).Append(reading.MostRead?.Path).OfType<string>(),
                posts,
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new InsightsPostsResponse(
                reading.Posts,
                reading.Dated,
                viewing.From?.ToString(MonthFormat, CultureInfo.InvariantCulture),
                [.. reading.Latest.Select(post => Described(post, named)).OfType<InsightsPost>()],
                reading.MostRead is { } most ? Described(most, named) : null,
                reading.CalendarFrom.ToString(DayFormat, CultureInfo.InvariantCulture),
                reading.CalendarTo.ToString(DayFormat, CultureInfo.InvariantCulture),
                [
                    .. reading.Calendar.Select(day => new PostingDayResponse(
                        day.Day.ToString(DayFormat, CultureInfo.InvariantCulture),
                        day.Posts)),
                ]));
    }

    /// <summary>
    /// Answers with one page of a website's posts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ranking a page by views takes the views of every post it is a page of, so they are read exactly as
    /// Insights reads them; only the posts on the page are named.
    /// </para>
    /// <para>
    /// A website that lists no posts, and a search no post holds, are answered at once from the control
    /// plane alone.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<PostsPageResponse>, NotFound, ProblemHttpResult>> PostsPageAsync(
        [AsParameters] PostsPageParameters parameters,
        ITenantScopeProvider scopes,
        ISitePosts posts,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryReadAsked(parameters, out var asked, out var refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var listed = await posts.ListAsync(scope, cancellationToken).ConfigureAwait(false);
        ImmutableArray<string>? matching = asked.Search is { } search && !listed.IsEmpty
            ? await posts.MatchingAsync(scope, search, cancellationToken).ConfigureAwait(false)
            : null;

        if (listed.IsEmpty || matching is { IsEmpty: true })
        {
            return TypedResults.Ok(new PostsPageResponse(listed.Length, 0, null, 0, []));
        }

        var viewing = await PostViewsAsync(
                scope,
                listed,
                asked.Population,
                telemetry,
                clock.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);
        var page = PostsInsights.List(
            listed,
            viewing.Views,
            TimeZoneInfo.FindSystemTimeZoneById(scope.TimeZoneId),
            new PostsListing(asked.Order, asked.Paging.Offset, asked.Paging.Limit) { Matching = matching });
        var named = await NameAsync(scope, page.Shown.Select(post => post.Path), posts, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(Answer(page, viewing, named));
    }

    /// <summary>
    /// Checks everything a question about a page of posts asked, in the order its refusal names the first
    /// thing wrong with it: the order, how much of the list and from where, the search, and who it is about.
    /// </summary>
    /// <param name="parameters">What the caller supplied.</param>
    /// <param name="asked">The checked question, where it can be answered as asked.</param>
    /// <param name="refusal">Why it cannot, where it cannot.</param>
    /// <returns><see langword="true"/> when the question can be answered as asked.</returns>
    private static bool TryReadAsked(PostsPageParameters parameters, out PostsAsked asked, out string? refusal)
    {
        asked = default;

        if (!TryReadPostsOrder(parameters.Order, out var order))
        {
            refusal = UnknownPostsOrder;

            return false;
        }

        if (!TryReadPaging(parameters.Limit, parameters.Offset, PostsBounds, out var paging, out refusal))
        {
            return false;
        }

        if (!PostSearch.TryRead(parameters.Search, out var search))
        {
            refusal = SearchTooLong;

            return false;
        }

        if (!TryReadPopulation(parameters.Only, out var population))
        {
            refusal = UnknownPopulation;

            return false;
        }

        asked = new PostsAsked(order, paging, search, population);

        return true;
    }

    /// <summary>
    /// Counts the pages delivered at a website's posts across its whole record: what was kept of each
    /// month before the first answered from its reports, and the reports from there to the present.
    /// </summary>
    /// <remarks>
    /// Every month instant comes from the store, through the record the other readings of it read, so the
    /// stretch counted from the reports begins where a month of the record begins.
    /// </remarks>
    /// <returns>The views, and the month they are counted from; none where nothing of the website is held.</returns>
    private static async Task<PostViewing> PostViewsAsync(
        TenantScope scope,
        ImmutableArray<ListedPost> listed,
        Population population,
        ITelemetryQueries telemetry,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var record = await telemetry
            .GetKeptMonthsAsync(scope, new KeptMonthsQuery(asOf), cancellationToken)
            .ConfigureAwait(false);
        var postMonths = await telemetry
            .GetKeptPostMonthsAsync(scope, new KeptPostMonthsQuery(asOf), cancellationToken)
            .ConfigureAwait(false);

        if (PostsInsights.LiveFrom(record.Months, postMonths, asOf) is not { } liveFrom)
        {
            return PostViewing.Nothing;
        }

        var kept = postMonths.Any(month => month < liveFrom.Month)
            ? await telemetry
                .GetKeptPostViewsAsync(
                    scope,
                    new KeptPostViewsQuery(asOf, liveFrom.Month) { Population = population },
                    cancellationToken)
                .ConfigureAwait(false)
            : [];
        var live = PostsInsights.LiveWindow(liveFrom, asOf) is { } window
            ? await telemetry
                .GetPostViewsAsync(
                    scope,
                    new PostViewsQuery(window, PostsInsights.CountedPaths(listed)) { Population = population },
                    cancellationToken)
                .ConfigureAwait(false)
            : [];

        return new PostViewing([.. kept, .. live], PostsInsights.ViewsFrom(postMonths, liveFrom));
    }

    /// <summary>
    /// Names the posts an answer shows, and no others.
    /// </summary>
    /// <param name="scope">The proof the caller may read the website.</param>
    /// <param name="shown">The paths of the posts shown, which may name one more than once.</param>
    /// <param name="posts">The posts the website lists.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Each of them still listed, by its path.</returns>
    private static async Task<Dictionary<string, NamedPost>> NameAsync(
        TenantScope scope,
        IEnumerable<string> shown,
        ISitePosts posts,
        CancellationToken cancellationToken)
    {
        string[] paths = [.. shown.Distinct(StringComparer.Ordinal)];

        var named = paths.Length == 0
            ? []
            : await posts.NamedAsync(scope, paths, cancellationToken).ConfigureAwait(false);

        return named.ToDictionary(post => post.Path, StringComparer.Ordinal);
    }

    /// <summary>
    /// A page of posts as the answer carries it, saying since when views are counted only where a post
    /// matched.
    /// </summary>
    private static PostsPageResponse Answer(
        PostsPage page,
        PostViewing viewing,
        Dictionary<string, NamedPost> named) =>
        new(
            page.Posts,
            page.Matching,
            page.Matching == 0 ? null : viewing.From?.ToString(MonthFormat, CultureInfo.InvariantCulture),
            page.MostPageViews,
            [.. page.Shown.Select(post => Described(post, named)).OfType<InsightsPost>()]);

    /// <summary>
    /// A post as the answer carries it, or nothing where it stopped being listed while it was read.
    /// </summary>
    private static InsightsPost? Described(ShownPost post, Dictionary<string, NamedPost> named) =>
        named.TryGetValue(post.Path, out var name)
            ? new InsightsPost(
                post.Path,
                name.Address,
                name.Title,
                post.Published?.ToString(DayFormat, CultureInfo.InvariantCulture),
                post.PageViews)
            : null;

    /// <summary>The pages delivered at a website's posts, and the month they are counted from.</summary>
    /// <param name="Views">Each path counted, from what was kept and from the reports.</param>
    /// <param name="From">The month the views are counted from, or nothing where nothing of the website is held.</param>
    private sealed record PostViewing(ImmutableArray<PathViews> Views, DateOnly? From)
    {
        /// <summary>No views, counted from nowhere.</summary>
        public static PostViewing Nothing { get; } = new([], null);
    }

    /// <summary>What a question about a page of posts asked, once checked.</summary>
    /// <param name="Order">The order the posts are listed in.</param>
    /// <param name="Paging">How many posts the page holds, and how many come before it.</param>
    /// <param name="Search">What a post's title or address must hold, or nothing where nothing was searched for.</param>
    /// <param name="Population">Who the posts' views are counted for.</param>
    private readonly record struct PostsAsked(PostsOrder Order, Paging Paging, string? Search, Population Population);
}

/// <summary>
/// What the question about a page of a website's posts reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The website.</param>
/// <param name="Search">
/// What a post's title or address must hold, without regard to case, or absent for every post. Written by
/// whoever asks, so it is only ever a value: never logged, never repeated, and never sent to the telemetry
/// store.
/// </param>
/// <param name="Order"><c>most-read</c>, which is what nothing means, or <c>newest</c>.</param>
/// <param name="Limit">How many posts the page holds, from 1 to 100. Defaults to 25.</param>
/// <param name="Offset">How many posts in that order come before the page. Defaults to none.</param>
/// <param name="Only">
/// Who the answer is about: <c>people</c> for the visits the engine concluded were people alone, or absent
/// for everybody.
/// </param>
internal readonly record struct PostsPageParameters(
    Guid SiteId,
    [FromQuery] string? Search,
    [FromQuery] string? Order,
    [FromQuery] int? Limit,
    [FromQuery] int? Offset,
    [FromQuery] string? Only);
