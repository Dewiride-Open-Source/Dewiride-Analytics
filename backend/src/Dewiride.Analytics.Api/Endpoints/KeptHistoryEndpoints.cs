using System.Collections.Immutable;
using System.Globalization;
using Dewiride.Analytics.Api.Analytics;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using static Dewiride.Analytics.Api.Analytics.QueryVocabulary;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Reading a website's whole record: every month from the first one kept or still held to the one
/// running now.
/// </summary>
/// <remarks>
/// <para>
/// Each answer is put together from two sources, by <see cref="KeptHistory"/>. A finished month is
/// read from the totals kept for it, which outlive the reports they were counted from; the month
/// running now, and any month nothing has been kept of yet, is counted from its reports by the
/// arithmetic the rest of the dashboard uses. So the record is level with every other period, and
/// reaches back further than any of them.
/// </para>
/// <para>
/// On the same terms as every other read of a website's numbers: everything the caller supplied is
/// checked before the website is resolved, and a website that does not exist and one the caller has
/// no role on are answered identically.
/// </para>
/// </remarks>
internal static class KeptHistoryEndpoints
{
    /// <summary>How a day is written in an answer.</summary>
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>
    /// Maps the questions about a website's whole record.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapKeptHistory(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/sites/{siteId:guid}/all-time", AllTimeAsync)
            .WithName("SiteAllTime")
            .WithSummary("Returns a website's headline totals across its whole record: every month from the first one kept or still held to the one running now.");

        routes.MapGet("/api/sites/{siteId:guid}/all-time/series", SeriesAsync)
            .WithName("SiteAllTimeSeries")
            .WithSummary("Returns one measure across a website's whole record, month by month or year by year.");

        routes.MapGet("/api/sites/{siteId:guid}/all-time/traffic/series", TrafficSeriesAsync)
            .WithName("SiteAllTimeTrafficSeries")
            .WithSummary("Returns judged visits by what generated them across a website's whole record, month by month or year by year.");

        routes.MapGet("/api/sites/{siteId:guid}/insights", InsightsAsync)
            .WithName("SiteInsights")
            .WithSummary("Returns what a website's whole record says about when it is read: its totals, its best day, its busiest day of the week and hour, and every month's page views.");
    }

    private static async Task<Results<Ok<AllTimeResponse>, NotFound, ProblemHttpResult>> AllTimeAsync(
        [AsParameters] WholeRecordParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        IClassificationProgressStore progress,
        IOptions<ClassificationOptions> classification,
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

        var reading = await ReadAsync(
                scope,
                population,
                population == Population.People,
                new Readers(telemetry, progress, classification.Value, clock),
                cancellationToken)
            .ConfigureAwait(false);

        var pageViews = await LiveAsync(scope, reading, TimeSeriesMetric.PageViews, population, telemetry, cancellationToken)
            .ConfigureAwait(false);
        var visitors = await LiveAsync(scope, reading, TimeSeriesMetric.Visitors, population, telemetry, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new AllTimeResponse(
                reading.Months.FirstOrDefault()?.Month.Begins,
                reading.AsOf,
                reading.FirstDay?.ToString(DayFormat, CultureInfo.InvariantCulture),
                Retention.DetailFrom(reading.AsOf, TimeZoneInfo.FindSystemTimeZoneById(scope.TimeZoneId)),
                Total(reading, figures => figures.Figures.PageViews, pageViews),
                Total(reading, figures => figures.Figures.Visitors, visitors),
                reading.CompleteTo,
                KeptHistory.Missing(reading.Months),
                KeptHistory.Unjudged(reading.Months)));
    }

    private static async Task<Results<Ok<AllTimeSeriesResponse>, NotFound, ProblemHttpResult>> SeriesAsync(
        [AsParameters] AllTimeSeriesParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        IClassificationProgressStore progress,
        IOptions<ClassificationOptions> classification,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!Metrics.TryGetValue(parameters.Metric ?? string.Empty, out var metric))
        {
            return Unusable(UnknownMetric);
        }

        if (!KeptGranularities.TryGetValue(parameters.Granularity ?? string.Empty, out var granularity))
        {
            return Unusable(UnknownKeptGranularity);
        }

        if (!TryReadPopulation(parameters.Only, out var population))
        {
            return Unusable(UnknownPopulation);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var reading = await ReadAsync(
                scope,
                population,
                population == Population.People,
                new Readers(telemetry, progress, classification.Value, clock),
                cancellationToken)
            .ConfigureAwait(false);

        var live = await LiveAsync(scope, reading, metric, population, telemetry, cancellationToken)
            .ConfigureAwait(false);
        var points = KeptHistory.Series(reading.Months, Measured(metric), live, granularity);

        return TypedResults.Ok(
            new AllTimeSeriesResponse(
                points.IsEmpty ? reading.AsOf : points[0].BucketStart,
                reading.AsOf,
                MetricNames[metric],
                KeptGranularityNames[granularity],
                reading.CompleteTo,
                [.. points.Select(point => new AllTimeSeriesPoint(point.BucketStart, point.Value))],
                KeptHistory.Missing(reading.Months),
                KeptHistory.Unjudged(reading.Months)));
    }

    private static async Task<Results<Ok<AllTimeTrafficSeriesResponse>, NotFound, ProblemHttpResult>> TrafficSeriesAsync(
        [AsParameters] AllTimeTrafficSeriesParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        IClassificationProgressStore progress,
        IOptions<ClassificationOptions> classification,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!KeptGranularities.TryGetValue(parameters.Granularity ?? string.Empty, out var granularity))
        {
            return Unusable(UnknownKeptGranularity);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var reading = await ReadAsync(
                scope,
                Population.Everybody,
                judged: true,
                new Readers(telemetry, progress, classification.Value, clock),
                cancellationToken)
            .ConfigureAwait(false);

        var live = reading.Live is { } range
            ? await telemetry
                .GetTrafficSeriesAsync(scope, new TrafficSeriesQuery(range, TimeGranularity.Month), cancellationToken)
                .ConfigureAwait(false)
            : new TrafficSeries([], []);
        var traffic = KeptHistory.Traffic(reading.Months, live, granularity);

        return TypedResults.Ok(
            new AllTimeTrafficSeriesResponse(
                traffic.Buckets.IsEmpty ? reading.AsOf : traffic.Buckets[0],
                reading.AsOf,
                KeptGranularityNames[granularity],
                reading.CompleteTo,
                traffic.Buckets,
                [
                    .. traffic.Groups.Select(group => new AllTimeCategorySeries(
                        ReportedNames.Categories[group.Category],
                        group.Visits,
                        group.PageViews)),
                ],
                KeptHistory.Missing(reading.Months),
                KeptHistory.Unjudged(reading.Months)));
    }

    private static async Task<Results<Ok<InsightsResponse>, NotFound, ProblemHttpResult>> InsightsAsync(
        [AsParameters] WholeRecordParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        IClassificationProgressStore progress,
        IOptions<ClassificationOptions> classification,
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

        var reading = await ReadAsync(
                scope,
                population,
                population == Population.People,
                new Readers(telemetry, progress, classification.Value, clock),
                cancellationToken)
            .ConfigureAwait(false);

        var pageViews = await LiveAsync(scope, reading, TimeSeriesMetric.PageViews, population, telemetry, cancellationToken)
            .ConfigureAwait(false);
        var visitors = await LiveAsync(scope, reading, TimeSeriesMetric.Visitors, population, telemetry, cancellationToken)
            .ConfigureAwait(false);
        var days = await LivePagesAsync(scope, reading, TimeGranularity.Day, population, telemetry, cancellationToken)
            .ConfigureAwait(false);
        var hours = await LivePagesAsync(scope, reading, TimeGranularity.Hour, population, telemetry, cancellationToken)
            .ConfigureAwait(false);

        var insights = KeptHistory.Insights(
            reading.Months,
            reading.FirstDay,
            new LiveMonths(pageViews, days, hours),
            TimeZoneInfo.FindSystemTimeZoneById(scope.TimeZoneId),
            reading.AsOf);

        return TypedResults.Ok(
            new InsightsResponse(
                reading.FirstDay?.ToString(DayFormat, CultureInfo.InvariantCulture),
                reading.AsOf,
                reading.CompleteTo,
                Total(reading, figures => figures.Figures.PageViews, pageViews),
                Total(reading, figures => figures.Figures.Visitors, visitors),
                insights.BestDay is { } best
                    ? new InsightsBestDay(best.ToString(DayFormat, CultureInfo.InvariantCulture), insights.BestDayPageViews)
                    : null,
                insights.Weekdays,
                insights.BusiestWeekdays,
                insights.Hours,
                insights.BusiestHours,
                [
                    .. insights.Months.Select(month => new InsightsMonth(
                        month.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                        month.PageViews,
                        month.DaysMeasured)),
                ],
                KeptHistory.Missing(reading.Months),
                KeptHistory.Unjudged(reading.Months)));
    }

    /// <summary>
    /// Reads a website's record and decides where each of its months is read from.
    /// </summary>
    /// <remarks>
    /// Where the answer rests on verdicts, how far judging has reached is read before anything it
    /// qualifies, for the reason <see cref="QueryVocabulary.JudgedUpToAsync"/> gives.
    /// </remarks>
    private static async Task<Reading> ReadAsync(
        TenantScope scope,
        Population population,
        bool judged,
        Readers readers,
        CancellationToken cancellationToken)
    {
        var asOf = readers.Clock.GetUtcNow();
        var caughtUp = judged
            ? await readers.Progress
                .CaughtUpToAsync(scope.SiteId, RulesetVersion.Current, cancellationToken)
                .ConfigureAwait(false)
            : null;

        var record = await readers.Telemetry
            .GetKeptMonthsAsync(scope, new KeptMonthsQuery(asOf) { Population = population }, cancellationToken)
            .ConfigureAwait(false);
        var months = KeptHistory.Resolve(record, asOf, judged);
        var firstDay = KeptHistory.FirstDay(months, record, scope.AddedAt, asOf);
        var live = KeptHistory.LiveFrom(months) is { } from && from < asOf ? new TimeRange(from, asOf) : (TimeRange?)null;

        var completeTo = judged
            ? KeptHistory.JudgedTo(
                months,
                JudgingReach.Settled(caughtUp ?? live?.From ?? asOf, asOf, readers.Classification.IdleTimeout),
                asOf)
            : asOf;

        return new Reading(months, firstDay, live, asOf, completeTo);
    }

    /// <summary>
    /// Counts one measure, month by month, for the months a record answers from their reports.
    /// </summary>
    /// <returns>Each month's figure by the instant it begins; nothing where no month is answered so.</returns>
    private static async Task<IReadOnlyDictionary<DateTimeOffset, long>> LiveAsync(
        TenantScope scope,
        Reading reading,
        TimeSeriesMetric metric,
        Population population,
        ITelemetryQueries telemetry,
        CancellationToken cancellationToken)
    {
        if (reading.Live is not { } range)
        {
            return ImmutableDictionary<DateTimeOffset, long>.Empty;
        }

        var points = await telemetry
            .GetTimeSeriesAsync(
                scope,
                new TimeSeriesQuery(range, TimeGranularity.Month, metric) { Population = population },
                cancellationToken)
            .ConfigureAwait(false);

        return points.ToDictionary(point => point.BucketStart, point => point.Value);
    }

    /// <summary>
    /// Counts the pages delivered, day by day or hour by hour, over the stretch a record answers from
    /// its reports.
    /// </summary>
    /// <returns>Each bucket's figure; nothing where no month is answered so.</returns>
    private static async Task<IReadOnlyList<TimeSeriesPoint>> LivePagesAsync(
        TenantScope scope,
        Reading reading,
        TimeGranularity granularity,
        Population population,
        ITelemetryQueries telemetry,
        CancellationToken cancellationToken)
    {
        if (reading.Live is not { } range)
        {
            return [];
        }

        return await telemetry
            .GetTimeSeriesAsync(
                scope,
                new TimeSeriesQuery(range, granularity, TimeSeriesMetric.PageViews) { Population = population },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static Func<KeptFigures, long> Measured(TimeSeriesMetric metric) =>
        metric == TimeSeriesMetric.Visitors
            ? figures => figures.Figures.Visitors
            : figures => figures.Figures.PageViews;

    private static long Total(
        Reading reading,
        Func<KeptFigures, long> kept,
        IReadOnlyDictionary<DateTimeOffset, long> live) =>
        KeptHistory.Series(reading.Months, kept, live, KeptGranularity.Month).Sum(point => point.Value ?? 0);

    /// <summary>What reading a record needs.</summary>
    private readonly record struct Readers(
        ITelemetryQueries Telemetry,
        IClassificationProgressStore Progress,
        ClassificationOptions Classification,
        TimeProvider Clock);

    /// <summary>A record, read at a moment, with its first day and where each month comes from.</summary>
    private sealed record Reading(
        ImmutableArray<MonthOfRecord> Months,
        DateOnly? FirstDay,
        TimeRange? Live,
        DateTimeOffset AsOf,
        DateTimeOffset CompleteTo);
}

/// <summary>
/// What a question about a website's whole record for one population reads from the path and the
/// query string: its headline totals for all time, and its insights.
/// </summary>
/// <param name="SiteId">The website.</param>
/// <param name="Only">
/// Who the answer is about: <c>people</c> for the visits the engine concluded were people alone, or
/// absent for everybody.
/// </param>
internal readonly record struct WholeRecordParameters(
    Guid SiteId,
    [FromQuery] string? Only);

/// <summary>
/// What the question about one measure across a website's whole record reads from the path and the
/// query string.
/// </summary>
/// <param name="SiteId">The website.</param>
/// <param name="Metric">Either <c>pageviews</c> or <c>visitors</c>.</param>
/// <param name="Granularity"><c>month</c> or <c>year</c>.</param>
/// <param name="Only">
/// Who the answer is about: <c>people</c> for the visits the engine concluded were people alone, or
/// absent for everybody.
/// </param>
internal readonly record struct AllTimeSeriesParameters(
    Guid SiteId,
    [FromQuery] string? Metric,
    [FromQuery] string? Granularity,
    [FromQuery] string? Only);

/// <summary>
/// What the question about who came across a website's whole record reads from the path and the
/// query string.
/// </summary>
/// <remarks>
/// No population: a verdict already says what each visit was, and the answer carries every
/// conclusion for the caller to read by.
/// </remarks>
/// <param name="SiteId">The website.</param>
/// <param name="Granularity"><c>month</c> or <c>year</c>.</param>
internal readonly record struct AllTimeTrafficSeriesParameters(
    Guid SiteId,
    [FromQuery] string? Granularity);
