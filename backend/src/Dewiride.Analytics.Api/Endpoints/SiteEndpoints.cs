using System.Collections.Frozen;
using Dewiride.Analytics.Api.Analytics;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Reading a site's numbers.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these begins by asking for a <see cref="TenantScope"/>, which can only be
/// obtained by having a role on the site being read. Nothing here trusts the identifier in the
/// path: a site identifier is printed in the page source of every page it measures, so it
/// identifies a site and grants nothing.
/// </para>
/// <para>
/// A site that does not exist and a site the caller has no role on are answered identically, so
/// the endpoint cannot be used to discover which identifiers on an install are real.
/// </para>
/// </remarks>
internal static class SiteEndpoints
{
    /// <summary>
    /// Visits returned when the caller does not say how many they want. Enough to fill a screen
    /// and read down, and small enough that the evidence on each one still arrives quickly.
    /// </summary>
    private const int DefaultVisits = 50;

    /// <summary>
    /// Pages returned when the caller does not say how many they want. A list somebody reads down
    /// in one glance to see where a period's attention went, rather than a site map.
    /// </summary>
    private const int DefaultPages = 10;

    /// <summary>
    /// What each role is called on the wire.
    /// </summary>
    /// <remarks>
    /// Written out rather than derived from the enumeration, so that renaming a member in C#
    /// cannot silently change a published wire format the dashboard reads.
    /// </remarks>
    private static readonly FrozenDictionary<SiteRole, string> RoleNames =
        new Dictionary<SiteRole, string>
        {
            [SiteRole.Viewer] = "viewer",
            [SiteRole.Editor] = "editor",
            [SiteRole.Owner] = "owner",
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, TimeSeriesMetric> Metrics =
        new Dictionary<string, TimeSeriesMetric>(StringComparer.OrdinalIgnoreCase)
        {
            ["pageviews"] = TimeSeriesMetric.PageViews,
            ["visitors"] = TimeSeriesMetric.Visitors,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, TimeGranularity> Granularities =
        new Dictionary<string, TimeGranularity>(StringComparer.OrdinalIgnoreCase)
        {
            ["hour"] = TimeGranularity.Hour,
            ["day"] = TimeGranularity.Day,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The spelling each measure is reported back under.
    /// </summary>
    /// <remarks>
    /// The answer names what it counted rather than repeating the caller's own spelling, so a
    /// request for <c>PageViews</c> and one for <c>pageviews</c> produce identical documents.
    /// </remarks>
    private static readonly FrozenDictionary<TimeSeriesMetric, string> MetricNames =
        Metrics.ToFrozenDictionary(entry => entry.Value, entry => entry.Key);

    private static readonly FrozenDictionary<TimeGranularity, string> GranularityNames =
        Granularities.ToFrozenDictionary(entry => entry.Value, entry => entry.Key);

    /// <summary>
    /// Maps the site listing and the two questions the first screen asks.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapSites(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/sites", ListAsync)
            .WithName("ListSites")
            .WithSummary("Lists the websites the signed-in person may look at.");

        routes.MapGet("/api/sites/{siteId:guid}/overview", OverviewAsync)
            .WithName("SiteOverview")
            .WithSummary("Returns headline totals for a website over a period.");

        routes.MapGet("/api/sites/{siteId:guid}/series", SeriesAsync)
            .WithName("SiteSeries")
            .WithSummary("Returns one measure counted in buckets across a period.");

        routes.MapGet("/api/sites/{siteId:guid}/pages", PagesAsync)
            .WithName("SitePages")
            .WithSummary("Returns the busiest pages on a website over a period.");

        routes.MapGet("/api/sites/{siteId:guid}/traffic", TrafficAsync)
            .WithName("SiteTraffic")
            .WithSummary("Returns judged visits grouped by what generated them.");

        routes.MapGet("/api/sites/{siteId:guid}/visits", VisitsAsync)
            .WithName("SiteVisits")
            .WithSummary("Returns individual judged visits and the evidence behind each verdict.");
    }

    private static async Task<Results<Ok<PagesResponse>, NotFound, ProblemHttpResult>> PagesAsync(
        [AsParameters] PagesParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var limit = parameters.Limit ?? DefaultPages;
        var offset = parameters.Offset ?? 0;

        if (limit < 1 || limit > SitePagesQuery.MostPages)
        {
            return Unusable($"Ask for between 1 and {SitePagesQuery.MostPages} pages at a time.");
        }

        if (offset < 0)
        {
            return Unusable("Start the list at the beginning or further along it, never before it.");
        }

        if (!RequestedWindow.TryResolve(
                parameters.From,
                parameters.To,
                RequestedWindow.Longest,
                clock,
                out var range,
                out var refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var pages = await telemetry
            .GetSitePagesAsync(scope, new SitePagesQuery(range, limit, offset), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new PagesResponse(
                range.From,
                range.To,
                pages.TotalPageViews,
                pages.TotalPaths,
                pages.MostPageViews,
                [.. pages.Pages.Select(page => new PageRow(page.Path, page.PageViews, page.Visitors))]));
    }

    private static async Task<Results<Ok<TrafficResponse>, NotFound, ProblemHttpResult>> TrafficAsync(
        [AsParameters] OverviewParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!RequestedWindow.TryResolve(
                parameters.From,
                parameters.To,
                RequestedWindow.Longest,
                clock,
                out var range,
                out var refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var groups = await telemetry
            .GetTrafficBreakdownAsync(scope, new TrafficBreakdownQuery(range), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new TrafficResponse(
                range.From,
                range.To,
                groups.Sum(group => group.Sessions),
                groups.Sum(group => group.PageViews),
                [
                    .. groups.Select(group => new TrafficGroup(
                        ReportedNames.Categories[group.Category],
                        ReportedNames.Strengths[group.Strength],
                        group.Sessions,
                        group.PageViews)),
                ]));
    }

    private static async Task<Results<Ok<VisitsResponse>, NotFound, ProblemHttpResult>> VisitsAsync(
        [AsParameters] VisitsParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var limit = parameters.Limit ?? DefaultVisits;

        if (limit < 1 || limit > JudgedSessionsQuery.MostSessions)
        {
            return Unusable($"Ask for between 1 and {JudgedSessionsQuery.MostSessions} visits at a time.");
        }

        if (!RequestedWindow.TryResolve(
                parameters.From,
                parameters.To,
                RequestedWindow.Longest,
                clock,
                out var range,
                out var refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var visits = await telemetry
            .GetJudgedSessionsAsync(scope, new JudgedSessionsQuery(range, limit), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new VisitsResponse(range.From, range.To, [.. visits.Select(Describe)]));
    }

    private static VisitSummary Describe(JudgedSession visit) => new(
        visit.SessionKey,
        visit.StartedAt,
        visit.EndedAt,
        visit.PageCount,
        [.. visit.Surfaces.Select(surface => ReportedNames.Surfaces[surface])],
        ReportedNames.Categories[visit.Verdict.Category],
        ReportedNames.Strengths[visit.Verdict.Strength],
        visit.Verdict.IsProvisional,
        visit.Verdict.RulesetVersion.ToString(),
        [.. visit.Verdict.Supporting.Select(Explain)],
        [.. visit.Verdict.Contradicting.Select(Explain)]);

    private static VisitReason Explain(Signal signal) => new(
        signal.Code,
        ReportedNames.Directions[signal.Direction],
        signal.Weight,
        signal.Parameters);

    private static async Task<Results<Ok<IReadOnlyList<SiteSummary>>, UnauthorizedHttpResult>> ListAsync(
        ISiteDirectory directory,
        ICurrentPrincipalAccessor caller,
        CancellationToken cancellationToken)
    {
        var userId = caller.GetUserId();

        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var sites = await directory.ListForUserAsync(userId.Value, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SiteSummary> summaries =
        [
            .. sites.Select(site => new SiteSummary(
                site.Id,
                site.Domain,
                site.DisplayName,
                site.TimeZoneId,
                RoleNames[site.Role])),
        ];

        return TypedResults.Ok(summaries);
    }

    private static async Task<Results<Ok<OverviewResponse>, NotFound, ProblemHttpResult>> OverviewAsync(
        [AsParameters] OverviewParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!RequestedWindow.TryResolve(
                parameters.From,
                parameters.To,
                RequestedWindow.Longest,
                clock,
                out var range,
                out var refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var totals = await telemetry
            .GetOverviewAsync(scope, new OverviewQuery(range), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new OverviewResponse(range.From, range.To, totals.PageViews, totals.Visitors, totals.Events));
    }

    private static async Task<Results<Ok<SeriesResponse>, NotFound, ProblemHttpResult>> SeriesAsync(
        [AsParameters] SeriesParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!Metrics.TryGetValue(parameters.Metric ?? string.Empty, out var metric))
        {
            return Unusable("Ask for either page views or visitors.");
        }

        if (!Granularities.TryGetValue(parameters.Granularity ?? string.Empty, out var granularity))
        {
            return Unusable("Ask for buckets of either an hour or a day.");
        }

        var longest = granularity == TimeGranularity.Hour
            ? RequestedWindow.LongestByHour
            : RequestedWindow.Longest;

        if (!RequestedWindow.TryResolve(
                parameters.From,
                parameters.To,
                longest,
                clock,
                out var range,
                out var refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var points = await telemetry
            .GetTimeSeriesAsync(scope, new TimeSeriesQuery(range, granularity, metric), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new SeriesResponse(
                range.From,
                range.To,
                MetricNames[metric],
                GranularityNames[granularity],
                [.. points.Select(point => new SeriesPoint(point.BucketStart, point.Value))]));
    }

    private static ProblemHttpResult Unusable(string? detail) =>
        TypedResults.Problem(
            title: "That request could not be answered as asked.",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>
/// What the overview endpoint reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The site to summarise.</param>
/// <param name="From">Inclusive start of the period. Defaults to a week before the end.</param>
/// <param name="To">Exclusive end of the period. Defaults to now.</param>
internal readonly record struct OverviewParameters(
    Guid SiteId,
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? To);

/// <summary>
/// What the series endpoint reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The site to count over.</param>
/// <param name="Metric">Either <c>pageviews</c> or <c>visitors</c>.</param>
/// <param name="Granularity">Either <c>hour</c> or <c>day</c>.</param>
/// <param name="From">Inclusive start of the period. Defaults to a week before the end.</param>
/// <param name="To">Exclusive end of the period. Defaults to now.</param>
internal readonly record struct SeriesParameters(
    Guid SiteId,
    [FromQuery] string? Metric,
    [FromQuery] string? Granularity,
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? To);

/// <summary>
/// What the pages endpoint reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The site to count over.</param>
/// <param name="From">Inclusive start of the period. Defaults to a week before the end.</param>
/// <param name="To">Exclusive end of the period. Defaults to now.</param>
/// <param name="Limit">How many pages to return. Defaults to a list somebody reads in one glance.</param>
/// <param name="Offset">How many of the busiest pages to pass over first. Defaults to none.</param>
internal readonly record struct PagesParameters(
    Guid SiteId,
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? To,
    [FromQuery] int? Limit,
    [FromQuery] int? Offset);

/// <summary>
/// What the visits endpoint reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The site to list visits for.</param>
/// <param name="From">Inclusive start of the period. Defaults to a week before the end.</param>
/// <param name="To">Exclusive end of the period. Defaults to now.</param>
/// <param name="Limit">How many visits to return. Defaults to a screenful.</param>
internal readonly record struct VisitsParameters(
    Guid SiteId,
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? To,
    [FromQuery] int? Limit);
