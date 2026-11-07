using System.Collections.Immutable;
using System.Globalization;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Application.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using static Dewiride.Analytics.Api.Analytics.QueryVocabulary;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Reading what a website's posts have done: how many its sitemap lists, the newest and the most read
/// with their views across its whole record, and when the last twelve months' were published.
/// </summary>
/// <remarks>
/// <para>
/// Beside the readings of the whole record, and on its terms: a post's views are read from what was kept
/// of each month and, from the first month nothing was kept of the posts of, from the reports, by the
/// arithmetic of the dashboard's list of pages (<see cref="PostsInsights"/>). So a post's views are never
/// behind the record's own figures.
/// </para>
/// <para>
/// On the same terms as every other read of a website's numbers: everything the caller supplied is
/// checked before the website is resolved, and a website that does not exist and one the caller has
/// no role on are answered identically. A title and an address are the website's own text: they travel
/// in the answer as values and are never logged.
/// </para>
/// </remarks>
internal static class InsightsPostsEndpoints
{
    /// <summary>How a day is written in an answer.</summary>
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>How a month is written in an answer.</summary>
    private const string MonthFormat = "yyyy-MM";

    /// <summary>
    /// Maps the question about a website's posts.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapInsightsPosts(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/sites/{siteId:guid}/insights/posts", PostsAsync)
            .WithName("SiteInsightsPosts")
            .WithSummary("Returns what a website's posts have done: how many its sitemap lists, the newest ten and the most read with their views across its whole record, and the days of the last twelve months a post was published on.");
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
        var named = await NameAsync(scope, reading, posts, cancellationToken).ConfigureAwait(false);

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
        var live = liveFrom.Begins < asOf
            ? await telemetry
                .GetPostViewsAsync(
                    scope,
                    new PostViewsQuery(new TimeRange(liveFrom.Begins, asOf), PostsInsights.CountedPaths(listed))
                    {
                        Population = population,
                    },
                    cancellationToken)
                .ConfigureAwait(false)
            : [];

        return new PostViewing([.. kept, .. live], PostsInsights.ViewsFrom(postMonths, liveFrom));
    }

    /// <summary>
    /// Names the posts an answer shows — the newest and the most read — and no others.
    /// </summary>
    /// <returns>Each of them still listed, by its path.</returns>
    private static async Task<Dictionary<string, NamedPost>> NameAsync(
        TenantScope scope,
        PostsReading reading,
        ISitePosts posts,
        CancellationToken cancellationToken)
    {
        string[] shown =
        [
            .. reading.Latest
                .Select(post => post.Path)
                .Append(reading.MostRead?.Path)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal),
        ];

        var named = shown.Length == 0
            ? []
            : await posts.NamedAsync(scope, shown, cancellationToken).ConfigureAwait(false);

        return named.ToDictionary(post => post.Path, StringComparer.Ordinal);
    }

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
}
