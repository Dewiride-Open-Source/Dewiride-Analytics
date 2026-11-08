using System.Collections.Immutable;
using System.Globalization;
using Dewiride.Analytics.Api.Analytics;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using static Dewiride.Analytics.Api.Analytics.QueryVocabulary;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Reading what one of a website's posts has done: its views across its whole record, every month it was
/// read in and its recent weeks day by day, and its views in buckets across a period.
/// </summary>
/// <remarks>
/// <para>
/// Its record is put together from the sources the reading of every post is
/// (<see cref="InsightsPostsEndpoints"/>): what was kept of each month, and the reports from the first month
/// nothing was kept of the posts of — read at the paths Insights reads the post at, so its views are the
/// figure Insights gives it (<see cref="PostRecord"/>).
/// </para>
/// <para>
/// Its views across a period are the website's own series of page views kept to those paths
/// (<see cref="PostSeriesQuery"/>): read from the reports over any window that series may cover, cut into
/// its buckets, refused where it is refused, and answered in its shape.
/// </para>
/// <para>
/// The post is found by the path the caller names among the posts the website's sitemap lists now, and
/// compared exactly. A path that names no post of the website is answered exactly as a website that does
/// not exist and one the caller has no role on are, and the telemetry store is never asked about it. What
/// the store is asked about is the path the website's own row holds. The path a caller names is never
/// logged and no refusal repeats it; a title and an address are the website's own text, and travel in the
/// answer as values.
/// </para>
/// <para>
/// Everything else the caller supplied is checked before the website is resolved, on the same terms as
/// every other read of a website's numbers.
/// </para>
/// </remarks>
internal static class InsightsPostEndpoints
{
    /// <summary>How a day is written in an answer.</summary>
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>How a month is written in an answer.</summary>
    private const string MonthFormat = "yyyy-MM";

    /// <summary>
    /// Maps the questions about one of a website's posts: what it has done across its whole record, and
    /// its views in buckets across a period.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapInsightsPost(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/sites/{siteId:guid}/insights/post", PostAsync)
            .WithName("SiteInsightsPost")
            .WithSummary("Returns what one of a website's posts has done: its views across its whole record, every month it was read in, and its recent weeks day by day.");

        routes.MapGet("/api/sites/{siteId:guid}/insights/post/series", PostSeriesAsync)
            .WithName("SiteInsightsPostSeries")
            .WithSummary("Returns the views of one of a website's posts, counted in buckets across a period.");
    }

    /// <summary>
    /// Answers what one of a website's posts has done.
    /// </summary>
    /// <remarks>
    /// Asked about people, how far judging has reached is read before anything it qualifies, for the
    /// reason <see cref="JudgedUpToAsync"/> gives.
    /// </remarks>
    private static async Task<Results<Ok<InsightsPostResponse>, NotFound, ProblemHttpResult>> PostAsync(
        [AsParameters] PostParameters parameters,
        [AsParameters] PostReaders readers,
        ITenantScopeProvider scopes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(parameters.Path))
        {
            return Unusable(NoPost);
        }

        if (!TryReadPopulation(parameters.Only, out var population))
        {
            return Unusable(UnknownPopulation);
        }

        var located = await LocateAsync(parameters.SiteId, parameters.Path, scopes, readers.Posts, cancellationToken)
            .ConfigureAwait(false);

        if (located is not { } found)
        {
            return TypedResults.NotFound();
        }

        var (scope, post) = found;
        var asOf = readers.Clock.GetUtcNow();
        var caughtUp = population == Population.People
            ? await readers.Progress
                .CaughtUpToAsync(scope.SiteId, RulesetVersion.Current, cancellationToken)
                .ConfigureAwait(false)
            : null;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(scope.TimeZoneId);
        var weeks = PostRecord.Weeks(asOf, zone);
        var sources = await ReadSourcesAsync(
                new PostAsked(scope, post, PostsInsights.PathsOf(post), population),
                asOf,
                weeks,
                readers.Telemetry,
                cancellationToken)
            .ConfigureAwait(false);
        var completeTo = population == Population.People
            ? PostRecord.JudgedTo(
                sources,
                JudgingReach.Settled(
                    caughtUp ?? PostRecord.ReportsFrom(sources, weeks) ?? asOf,
                    asOf,
                    readers.Classification.Value.IdleTimeout),
                asOf)
            : asOf;

        return TypedResults.Ok(Answer(post, PostRecord.Read(sources, scope.AddedAt, zone, asOf), asOf, completeTo));
    }

    /// <summary>
    /// Answers how many times one of a website's posts was read in each bucket across a period.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The website's own series of page views kept to the paths the post is read at: the same checks in
    /// the same order, the same window and buckets, and an answer of the same shape. There is no measure to
    /// choose, because page views are the only one counted for a post.
    /// </para>
    /// <para>
    /// Counted for everybody, the answer is complete to the end of its own window. Counted for people, it
    /// is complete only to where judging has reached; that is read before the series it qualifies, for the
    /// reason <see cref="JudgedUpToAsync"/> gives.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<SeriesResponse>, NotFound, ProblemHttpResult>> PostSeriesAsync(
        [AsParameters] PostSeriesParameters parameters,
        [AsParameters] PostReaders readers,
        ITenantScopeProvider scopes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(parameters.Path))
        {
            return Unusable(NoPost);
        }

        if (!Granularities.TryGetValue(parameters.Granularity ?? string.Empty, out var granularity))
        {
            return Unusable(UnknownGranularity);
        }

        if (!RequestedWindow.TryResolve(
                parameters.From,
                parameters.To,
                RequestedWindow.LongestFor(granularity),
                readers.Clock,
                out var range,
                out var refusal))
        {
            return Unusable(refusal);
        }

        if (!TryReadPopulation(parameters.Only, out var population))
        {
            return Unusable(UnknownPopulation);
        }

        var located = await LocateAsync(parameters.SiteId, parameters.Path, scopes, readers.Posts, cancellationToken)
            .ConfigureAwait(false);

        if (located is not { } found)
        {
            return TypedResults.NotFound();
        }

        var (scope, post) = found;
        var completeTo = population == Population.People
            ? await JudgedUpToAsync(
                    scope,
                    range,
                    readers.Progress,
                    readers.Classification.Value,
                    readers.Clock,
                    cancellationToken)
                .ConfigureAwait(false)
            : range.To;
        var points = await SeriesAsync(
                new PostAsked(scope, post, PostsInsights.PathsOf(post), population),
                range,
                granularity,
                readers.Telemetry,
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new SeriesResponse(
                range.From,
                range.To,
                MetricNames[TimeSeriesMetric.PageViews],
                GranularityNames[granularity],
                completeTo,
                [.. points.Select(point => new SeriesPoint(point.BucketStart, point.Value))]));
    }

    /// <summary>
    /// Finds the website and the post on it, or neither.
    /// </summary>
    /// <remarks>
    /// A website that does not exist, one the caller has no role on, and a path that names no post the
    /// website lists come back as one answer, so nothing about another account's website or posts can be
    /// told from it.
    /// </remarks>
    /// <returns>The proof the caller may read the website, and the post; nothing where either is not found.</returns>
    private static async Task<(TenantScope Scope, FoundPost Post)?> LocateAsync(
        Guid siteId,
        string path,
        ITenantScopeProvider scopes,
        ISitePosts posts,
        CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(siteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return null;
        }

        var post = await posts.FindAsync(scope, path, cancellationToken).ConfigureAwait(false);

        return post is null ? null : (scope, post);
    }

    /// <summary>
    /// Reads everything a post's record is put together from.
    /// </summary>
    /// <remarks>
    /// The website's record first, and nothing more where it is empty. Then the months kept of the post,
    /// which also say which months the posts were kept for and so where the reports take over; the views
    /// the reports hold from there, by the statement Insights counts them by; the reports' months, only
    /// where they answer more than one; and the recent weeks, day by day.
    /// </remarks>
    private static async Task<PostRecordSources> ReadSourcesAsync(
        PostAsked asked,
        DateTimeOffset asOf,
        TimeRange weeks,
        ITelemetryQueries telemetry,
        CancellationToken cancellationToken)
    {
        var record = await telemetry
            .GetKeptMonthsAsync(asked.Scope, new KeptMonthsQuery(asOf), cancellationToken)
            .ConfigureAwait(false);
        var kept = record.Months.IsEmpty
            ? []
            : await telemetry
                .GetKeptMonthsOfPostAsync(
                    asked.Scope,
                    new KeptMonthsOfPostQuery(asOf, asked.Paths) { Population = asked.Population },
                    cancellationToken)
                .ConfigureAwait(false);

        if (PostsInsights.LiveFrom(record.Months, [.. kept.Select(month => month.Month)], asOf) is not { } liveFrom)
        {
            return new PostRecordSources(asked.Post, record, kept, null, 0, [], []);
        }

        var live = PostsInsights.LiveWindow(liveFrom, asOf);
        var liveViews = live is { } window
            ? await ViewsAsync(asked, window, telemetry, cancellationToken).ConfigureAwait(false)
            : 0;
        var liveMonths = live is { } months && PostRecord.SpansMonths(liveFrom, asOf)
            ? await SeriesAsync(asked, months, TimeGranularity.Month, telemetry, cancellationToken).ConfigureAwait(false)
            : [];
        var days = await SeriesAsync(asked, weeks, TimeGranularity.Day, telemetry, cancellationToken)
            .ConfigureAwait(false);

        return new PostRecordSources(asked.Post, record, kept, liveFrom, liveViews, liveMonths, days);
    }

    /// <summary>
    /// Counts the pages delivered at a post over a stretch, by the statement Insights counts every post by,
    /// whose answer is kept to the post's paths as it is read.
    /// </summary>
    private static async Task<long> ViewsAsync(
        PostAsked asked,
        TimeRange window,
        ITelemetryQueries telemetry,
        CancellationToken cancellationToken)
    {
        var views = await telemetry
            .GetPostViewsAsync(
                asked.Scope,
                new PostViewsQuery(window, asked.Paths) { Population = asked.Population },
                cancellationToken)
            .ConfigureAwait(false);

        return views.Sum(view => view.PageViews);
    }

    /// <summary>Counts the pages delivered at a post in buckets across a stretch.</summary>
    private static Task<IReadOnlyList<TimeSeriesPoint>> SeriesAsync(
        PostAsked asked,
        TimeRange range,
        TimeGranularity granularity,
        ITelemetryQueries telemetry,
        CancellationToken cancellationToken) =>
        telemetry.GetPostSeriesAsync(
            asked.Scope,
            new PostSeriesQuery(range, granularity, asked.Paths) { Population = asked.Population },
            cancellationToken);

    /// <summary>A post's record as the answer carries it.</summary>
    private static InsightsPostResponse Answer(
        FoundPost post,
        PostRecordReading reading,
        DateTimeOffset asOf,
        DateTimeOffset completeTo) =>
        new(
            new InsightsPost(post.Listed.Path, post.Address, post.Title, Day(reading.Published), reading.PageViews),
            reading.ViewsFrom?.ToString(MonthFormat, CultureInfo.InvariantCulture),
            Day(reading.FirstDay),
            Day(reading.From),
            asOf,
            completeTo,
            [
                .. reading.Months.Select(month => new InsightsMonth(
                    month.Month.ToString(MonthFormat, CultureInfo.InvariantCulture),
                    month.PageViews,
                    month.DaysMeasured)),
            ],
            [
                .. reading.Days.Select(day => new InsightsPostDay(
                    day.Day.ToString(DayFormat, CultureInfo.InvariantCulture),
                    day.PageViews)),
            ]);

    private static string? Day(DateOnly? day) => day?.ToString(DayFormat, CultureInfo.InvariantCulture);

    /// <summary>Which post is asked about, at which paths, by whom, and about whom.</summary>
    /// <param name="Scope">The proof the caller may read the website.</param>
    /// <param name="Post">The post, as the website lists it.</param>
    /// <param name="Paths">The paths it is read at, from the website's own row.</param>
    /// <param name="Population">Who the answer is about.</param>
    private readonly record struct PostAsked(
        TenantScope Scope,
        FoundPost Post,
        ImmutableArray<string> Paths,
        Population Population);
}

/// <summary>
/// What the question about what one of a website's posts has done reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The website.</param>
/// <param name="Path">
/// The post's path, exactly as Insights lists it. Written by whoever asks, so it is only ever looked up
/// among the website's posts: never logged, never repeated, and never sent to the telemetry store.
/// </param>
/// <param name="Only">
/// Who the answer is about: <c>people</c> for the visits the engine concluded were people alone, or
/// absent for everybody.
/// </param>
internal readonly record struct PostParameters(
    Guid SiteId,
    [FromQuery] string? Path,
    [FromQuery] string? Only);

/// <summary>
/// What the question about one of a website's posts' views across a period reads from the path and the
/// query string.
/// </summary>
/// <param name="SiteId">The website.</param>
/// <param name="Path">
/// The post's path, exactly as Insights lists it. Written by whoever asks, so it is only ever looked up
/// among the website's posts: never logged, never repeated, and never sent to the telemetry store.
/// </param>
/// <param name="Granularity">
/// <c>hour</c>, <c>day</c>, <c>week</c> or <c>month</c>. Hours are cut over a period of up to 31 days,
/// and everything else over a period of up to 400.
/// </param>
/// <param name="From">Inclusive start of the period. Defaults to a week before the end.</param>
/// <param name="To">Exclusive end of the period. Defaults to now.</param>
/// <param name="Only">
/// Who the answer is about: <c>people</c> for the visits the engine concluded were people alone, or
/// absent for everybody.
/// </param>
internal readonly record struct PostSeriesParameters(
    Guid SiteId,
    [FromQuery] string? Path,
    [FromQuery] string? Granularity,
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? To,
    [FromQuery] string? Only);

/// <summary>
/// What reading one of a website's posts needs.
/// </summary>
/// <param name="Posts">The posts the website lists.</param>
/// <param name="Telemetry">The telemetry store.</param>
/// <param name="Progress">Where judging has reached.</param>
/// <param name="Classification">The engine's own idle timeout.</param>
/// <param name="Clock">The present moment.</param>
internal readonly record struct PostReaders(
    ISitePosts Posts,
    ITelemetryQueries Telemetry,
    IClassificationProgressStore Progress,
    IOptions<ClassificationOptions> Classification,
    TimeProvider Clock);
