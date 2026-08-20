using System.Collections.Frozen;
using System.Collections.Immutable;
using Dewiride.Analytics.Api.Analytics;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Api.Security;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Reading a site's numbers, and keeping the list of sites they belong to.
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

    /// <summary>Places returned when the caller does not say how many they want.</summary>
    private const int DefaultPlaces = 10;

    /// <summary>
    /// How a place list is grouped when the caller does not say.
    /// </summary>
    /// <remarks>
    /// Countries. They are the reliable half of the answer, and the one a reader can hold in
    /// their head — a site with traffic from thirty countries has traffic from several hundred
    /// towns.
    /// </remarks>
    private const string DefaultGrouping = "country";

    /// <summary>Sources returned when the caller does not say how many they want.</summary>
    private const int DefaultSources = 10;

    /// <summary>
    /// How a source list is grouped when the caller does not say.
    /// </summary>
    /// <remarks>
    /// By kind. It is five rows that answer the question outright — how much of an audience search
    /// brings, and how much arrives by nothing that named itself — where a list of hostnames
    /// answers it only for a reader who already knows which of the names are search engines and is
    /// willing to add them up.
    /// </remarks>
    private const string DefaultSourceGrouping = "kind";

    /// <summary>Software names returned when the caller does not say how many they want.</summary>
    private const int DefaultNames = 10;

    /// <summary>
    /// How a software list is grouped when the caller does not say.
    /// </summary>
    /// <remarks>
    /// Browsers. They are the half of the answer that changes what somebody would do about it —
    /// an operating system is rarely something a site is built or tested against on its own.
    /// </remarks>
    private const string DefaultSoftwareGrouping = "browser";

    /// <summary>Controls returned when the caller does not say how many.</summary>
    private const int DefaultControls = 10;

    /// <summary>
    /// How a list of presses is gathered when the caller does not say.
    /// </summary>
    /// <remarks>
    /// By the control. What was pressed is the question; where the presses led is a narrower one
    /// that only has an answer on a site that links off itself.
    /// </remarks>
    private const string DefaultActionGrouping = "control";

    /// <summary>Pages returned from the reading list when the caller does not say how many.</summary>
    private const int DefaultReadPages = 10;

    /// <summary>
    /// What a reading list is ordered by when the caller does not say.
    /// </summary>
    /// <remarks>
    /// Attention. It is the figure somebody came to the question for, and the one a page can be
    /// improved against — how far down a page a reader got is a property of how long the page is
    /// as much as of what it was worth.
    /// </remarks>
    private const string DefaultRanking = "attention";

    /// <summary>Pages returned from an arrival or departure list when the caller does not say how many.</summary>
    private const int DefaultVisitPages = 10;

    /// <summary>
    /// Which end of a visit is counted when the caller does not say.
    /// </summary>
    /// <remarks>
    /// Where visits began. It is the half somebody can act on — a page people arrive at is a page
    /// worth writing more of — while where they left is mostly a description of where a site ends.
    /// </remarks>
    private const string DefaultPosition = "entry";

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

    /// <summary>
    /// What a place list may be grouped by, by the word used on the wire.
    /// </summary>
    /// <remarks>
    /// Written out rather than derived from the enumeration, so renaming a member in C# cannot
    /// change an address the dashboard already asks for. Anything not in this table is refused
    /// before it reaches the compiler, which is a second answer to the same question the compiler
    /// already settles by taking its column from a fixed table of its own.
    /// </remarks>
    private static readonly FrozenDictionary<string, LocationGrouping> Groupings =
        new Dictionary<string, LocationGrouping>(StringComparer.OrdinalIgnoreCase)
        {
            ["country"] = LocationGrouping.Country,
            ["town"] = LocationGrouping.Town,
            ["network"] = LocationGrouping.Network,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<LocationGrouping, string> GroupingNames =
        Groupings.ToFrozenDictionary(entry => entry.Value, entry => entry.Key);

    /// <summary>
    /// What a source list may be grouped by, by the word used on the wire.
    /// </summary>
    /// <remarks>
    /// Written out for the same reason the place groupings are, and refused here before the
    /// compiler is reached for the same reason.
    /// </remarks>
    private static readonly FrozenDictionary<string, SourceGrouping> SourceGroupings =
        new Dictionary<string, SourceGrouping>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = SourceGrouping.Kind,
            ["site"] = SourceGrouping.Site,
            ["page"] = SourceGrouping.Page,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<SourceGrouping, string> SourceGroupingNames =
        SourceGroupings.ToFrozenDictionary(entry => entry.Value, entry => entry.Key);

    /// <summary>
    /// What a software list may be grouped by, by the word used on the wire.
    /// </summary>
    /// <remarks>
    /// Written out for the same reason the place groupings are, and refused here before the
    /// compiler is reached for the same reason.
    /// </remarks>
    private static readonly FrozenDictionary<string, SoftwareGrouping> SoftwareGroupings =
        new Dictionary<string, SoftwareGrouping>(StringComparer.OrdinalIgnoreCase)
        {
            ["browser"] = SoftwareGrouping.Browser,
            ["system"] = SoftwareGrouping.OperatingSystem,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<SoftwareGrouping, string> SoftwareGroupingNames =
        SoftwareGroupings.ToFrozenDictionary(entry => entry.Value, entry => entry.Key);

    /// <summary>
    /// How a list of presses may be gathered, by the word used on the wire.
    /// </summary>
    /// <remarks>
    /// Written out for the same reason the place groupings are, and refused here before the
    /// compiler is reached for the same reason.
    /// </remarks>
    private static readonly FrozenDictionary<string, ActionGrouping> ActionGroupings =
        new Dictionary<string, ActionGrouping>(StringComparer.OrdinalIgnoreCase)
        {
            ["control"] = ActionGrouping.Control,
            ["destination"] = ActionGrouping.Destination,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<ActionGrouping, string> ActionGroupingNames =
        ActionGroupings.ToFrozenDictionary(entry => entry.Value, entry => entry.Key);

    /// <summary>
    /// What a reading list may be ordered by, by the word used on the wire.
    /// </summary>
    /// <remarks>
    /// Written out for the same reason the place groupings are, and refused here before the
    /// compiler is reached for the same reason.
    /// </remarks>
    private static readonly FrozenDictionary<string, EngagementRanking> Rankings =
        new Dictionary<string, EngagementRanking>(StringComparer.OrdinalIgnoreCase)
        {
            ["attention"] = EngagementRanking.Attention,
            ["depth"] = EngagementRanking.Depth,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<EngagementRanking, string> RankingNames =
        Rankings.ToFrozenDictionary(entry => entry.Value, entry => entry.Key);

    /// <summary>
    /// Which end of a visit an arrival list may count, by the word used on the wire.
    /// </summary>
    /// <remarks>
    /// Written out for the same reason the place groupings are, and refused here before the
    /// compiler is reached for the same reason.
    /// </remarks>
    private static readonly FrozenDictionary<string, VisitPosition> Positions =
        new Dictionary<string, VisitPosition>(StringComparer.OrdinalIgnoreCase)
        {
            ["entry"] = VisitPosition.Entry,
            ["exit"] = VisitPosition.Exit,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<VisitPosition, string> PositionNames =
        Positions.ToFrozenDictionary(entry => entry.Value, entry => entry.Key);

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

        routes.MapPost("/api/sites", AddAsync)
            .WithName("AddSite")
            .WithSummary("Adds a website to measure, owned by whoever added it.")
            .WithDescription(
                "The new website joins the organisation the caller already owns a website in, so "
                + "somebody who owns none cannot add one.")
            .RequireProofOfOrigin();

        routes.MapDelete("/api/sites/{siteId:guid}", RemoveAsync)
            .WithName("RemoveSite")
            .WithSummary("Removes a website and everything measured for it.")
            .WithDescription(
                "Only the website's owner may remove it, and the last website somebody owns is "
                + "kept, so that they are always able to add another. What was measured for it "
                + "is deleted outright and cannot be brought back.")
            .RequireProofOfOrigin();

        routes.MapGet("/api/sites/{siteId:guid}/overview", OverviewAsync)
            .WithName("SiteOverview")
            .WithSummary("Returns headline totals for a website over a period.");

        routes.MapGet("/api/sites/{siteId:guid}/series", SeriesAsync)
            .WithName("SiteSeries")
            .WithSummary("Returns one measure counted in buckets across a period.");

        routes.MapGet("/api/sites/{siteId:guid}/pages", PagesAsync)
            .WithName("SitePages")
            .WithSummary("Returns the busiest pages on a website over a period.");

        routes.MapGet("/api/sites/{siteId:guid}/actions", ActionsAsync)
            .WithName("SiteActions")
            .WithSummary("Returns what a website's visitors operated over a period.");

        routes.MapGet("/api/sites/{siteId:guid}/locations", LocationsAsync)
            .WithName("SiteLocations")
            .WithSummary("Returns where a website's audience was over a period.");

        routes.MapGet("/api/sites/{siteId:guid}/sources", SourcesAsync)
            .WithName("SiteSources")
            .WithSummary("Returns where a website's visitors came from over a period.");

        routes.MapGet("/api/sites/{siteId:guid}/devices", DevicesAsync)
            .WithName("SiteDevices")
            .WithSummary("Returns what kinds of device a website's audience read on over a period.");

        routes.MapGet("/api/sites/{siteId:guid}/software", SoftwareAsync)
            .WithName("SiteSoftware")
            .WithSummary("Returns the browsers or operating systems a website's audience used over a period.");

        routes.MapGet("/api/sites/{siteId:guid}/engagement", EngagementAsync)
            .WithName("SiteEngagement")
            .WithSummary("Returns how a website's pages were read over a period.");

        routes.MapGet("/api/sites/{siteId:guid}/engagement/pages", PageEngagementAsync)
            .WithName("SitePageEngagement")
            .WithSummary("Returns a website's pages ranked by how they were read over a period.");

        routes.MapGet("/api/sites/{siteId:guid}/traffic", TrafficAsync)
            .WithName("SiteTraffic")
            .WithSummary("Returns judged visits grouped by what generated them.");

        routes.MapGet("/api/sites/{siteId:guid}/visits", VisitsAsync)
            .WithName("SiteVisits")
            .WithSummary("Returns individual judged visits and the evidence behind each verdict.");

        routes.MapGet("/api/sites/{siteId:guid}/visits/totals", VisitTotalsAsync)
            .WithName("SiteVisitTotals")
            .WithSummary("Returns how many visits a website had over a period and how many were a single page.");

        routes.MapGet("/api/sites/{siteId:guid}/visits/pages", VisitPagesAsync)
            .WithName("SiteVisitPages")
            .WithSummary("Returns the pages a website's visits began or ended on over a period.");

        routes.MapGet("/api/sites/{siteId:guid}/visits/{visitKey}/journey", VisitJourneyAsync)
            .WithName("SiteVisitJourney")
            .WithSummary("Returns the pages one visit went through, in order.");
    }

    private static async Task<Results<Ok<PagesResponse>, NotFound, ProblemHttpResult>> PagesAsync(
        [AsParameters] PagesParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryReadSlice(
                new ListRequest(parameters.Limit, parameters.Offset, parameters.From, parameters.To),
                new ListBounds(DefaultPages, SitePagesQuery.MostPages, "pages"),
                clock,
                out var slice,
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
            .GetSitePagesAsync(scope, new SitePagesQuery(slice.Range, slice.Limit, slice.Offset), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new PagesResponse(
                slice.Range.From,
                slice.Range.To,
                pages.TotalPageViews,
                pages.TotalPaths,
                pages.MostPageViews,
                [.. pages.Pages.Select(page => new PageRow(page.Path, page.PageViews, page.Visitors))]));
    }

    /// <summary>
    /// Describes the control a step was a press on, where it was one.
    /// </summary>
    /// <param name="press">What was operated, or nothing where the step is an arrival.</param>
    /// <returns>The control, in the vocabulary the dashboard reads.</returns>
    private static VisitPressed? Operated(VisitPress? press) =>
        press is null
            ? null
            : new VisitPressed(
                press.Value.Name,
                ReportedNames.Controls[press.Value.Control],
                press.Value.Target,
                ReportedNames.Targets[press.Value.TargetKind]);

    /// <summary>
    /// Answers what a website's visitors operated.
    /// </summary>
    /// <remarks>
    /// Everything the caller supplied is checked before the site is resolved, so a malformed
    /// request is refused identically whether or not the site exists.
    /// </remarks>
    private static async Task<Results<Ok<ActionsResponse>, NotFound, ProblemHttpResult>> ActionsAsync(
        [AsParameters] ActionsParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!ActionGroupings.TryGetValue(parameters.Grouping ?? DefaultActionGrouping, out var grouping))
        {
            return Unusable("Gather the list by control or by destination.");
        }

        if (!TryReadSlice(
                new ListRequest(parameters.Limit, parameters.Offset, parameters.From, parameters.To),
                new ListBounds(DefaultControls, SiteActionsQuery.MostControls, "controls"),
                clock,
                out var slice,
                out var refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var actions = await telemetry
            .GetSiteActionsAsync(
                scope,
                new SiteActionsQuery(slice.Range, grouping, slice.Limit, slice.Offset),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new ActionsResponse(
                slice.Range.From,
                slice.Range.To,
                ActionGroupingNames[grouping],
                actions.TotalPresses,
                actions.TotalControls,
                actions.MostPresses,
                [
                    .. actions.Controls.Select(control => new ActionRow(
                        control.Name,
                        ReportedNames.Controls[control.Control],
                        control.Presses,
                        control.Visitors)),
                ]));
    }

    /// <summary>
    /// Answers where a website's audience was.
    /// </summary>
    /// <remarks>
    /// Everything the caller supplied is checked before the site is resolved, so a malformed
    /// request is refused identically whether or not the site exists. Checking in the other order
    /// would turn a bad grouping into a way of finding out which identifiers on an install are
    /// real.
    /// </remarks>
    private static async Task<Results<Ok<LocationsResponse>, NotFound, ProblemHttpResult>> LocationsAsync(
        [AsParameters] LocationsParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!Groupings.TryGetValue(parameters.Grouping ?? DefaultGrouping, out var grouping))
        {
            return Unusable("Group the places by country, by town or by network.");
        }

        if (!TryReadSlice(
                new ListRequest(parameters.Limit, parameters.Offset, parameters.From, parameters.To),
                new ListBounds(DefaultPlaces, SiteLocationsQuery.MostPlaces, "places"),
                clock,
                out var slice,
                out var refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var places = await telemetry
            .GetSiteLocationsAsync(
                scope,
                new SiteLocationsQuery(slice.Range, grouping, slice.Limit, slice.Offset),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new LocationsResponse(
                slice.Range.From,
                slice.Range.To,
                GroupingNames[grouping],
                places.TotalVisitors,
                places.TotalPlaces,
                places.MostVisitors,
                [
                    .. places.Places.Select(place => new LocationRow(
                        place.Place,
                        place.CountryCode,
                        place.Visitors,
                        place.PageViews)),
                ]));
    }

    /// <summary>
    /// Answers where a website's visitors came from before they arrived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything the caller supplied is checked before the site is resolved, on the same terms as
    /// every other list here: checking in the other order would turn a bad grouping into a way of
    /// finding out which identifiers on an install are real.
    /// </para>
    /// <para>
    /// The site's own address is read here and handed to the question, because traffic from the
    /// measured site is a reader moving between its pages rather than a source. It is taken from
    /// what is stored against the site and never from the request, so a caller cannot decide whose
    /// traffic gets left out of somebody else's list.
    /// </para>
    /// </remarks>
    /// <param name="parameters">What the caller asked for.</param>
    /// <param name="scopes">Resolves the caller's authority over the site.</param>
    /// <param name="sites">Resolves the site's own address.</param>
    /// <param name="telemetry">The telemetry store.</param>
    /// <param name="clock">The clock the default window is measured back from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The slice, a refusal, or nothing where the caller may not read this site.</returns>
    private static async Task<Results<Ok<SourcesResponse>, NotFound, ProblemHttpResult>> SourcesAsync(
        [AsParameters] SourcesParameters parameters,
        ITenantScopeProvider scopes,
        ISiteCatalog sites,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!SourceGroupings.TryGetValue(parameters.Grouping ?? DefaultSourceGrouping, out var grouping))
        {
            return Unusable("Group the sources by kind, by site or by page.");
        }

        if (!TryReadSlice(
                new ListRequest(parameters.Limit, parameters.Offset, parameters.From, parameters.To),
                new ListBounds(DefaultSources, SiteSourcesQuery.MostSources, "sources"),
                clock,
                out var slice,
                out var refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var site = await sites.FindAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (site is null)
        {
            return TypedResults.NotFound();
        }

        var sources = await telemetry
            .GetSiteSourcesAsync(
                scope,
                new SiteSourcesQuery(slice.Range, grouping, site.Domain, slice.Limit, slice.Offset),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new SourcesResponse(
                slice.Range.From,
                slice.Range.To,
                SourceGroupingNames[grouping],
                sources.TotalVisitors,
                sources.TotalSources,
                sources.MostVisitors,
                [
                    .. sources.Sources.Select(source => new SourceRow(
                        source.Source,
                        source.Site,
                        source.Visitors,
                        source.PageViews)),
                ]));
    }

    /// <summary>
    /// Answers what a website's audience read on.
    /// </summary>
    /// <remarks>
    /// Every visitor is on exactly one row, so the caller is told the total once and the rows add
    /// up to it. Nothing is paged: there are five kinds and there always will be until the engine
    /// learns a sixth.
    /// </remarks>
    private static async Task<Results<Ok<DevicesResponse>, NotFound, ProblemHttpResult>> DevicesAsync(
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

        var devices = await telemetry
            .GetSiteDeviceKindsAsync(scope, new SiteDeviceKindsQuery(range), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new DevicesResponse(
                range.From,
                range.To,
                devices.Sum(device => device.Visitors),
                [
                    .. devices.Select(device => new DeviceRow(
                        ReportedNames.Devices[device.Device],
                        device.Visitors,
                        device.PageViews)),
                ]));
    }

    /// <summary>
    /// Answers what software a website's audience used.
    /// </summary>
    /// <remarks>
    /// Everything the caller supplied is checked before the site is resolved, so a malformed
    /// request is refused identically whether or not the site exists.
    /// </remarks>
    private static async Task<Results<Ok<SoftwareResponse>, NotFound, ProblemHttpResult>> SoftwareAsync(
        [AsParameters] SoftwareParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!SoftwareGroupings.TryGetValue(parameters.Grouping ?? DefaultSoftwareGrouping, out var grouping))
        {
            return Unusable("Group the list by browser or by system.");
        }

        if (!TryReadSlice(
                new ListRequest(parameters.Limit, parameters.Offset, parameters.From, parameters.To),
                new ListBounds(DefaultNames, SiteSoftwareQuery.MostNames, "names"),
                clock,
                out var slice,
                out var refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var software = await telemetry
            .GetSiteSoftwareAsync(
                scope,
                new SiteSoftwareQuery(slice.Range, grouping, slice.Limit, slice.Offset),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new SoftwareResponse(
                slice.Range.From,
                slice.Range.To,
                SoftwareGroupingNames[grouping],
                software.TotalVisitors,
                software.TotalNames,
                software.MostVisitors,
                [
                    .. software.Names.Select(name => new SoftwareRow(
                        name.Name,
                        name.Visitors,
                        name.PageViews)),
                ]));
    }

    /// <summary>
    /// Answers how a website's pages were read.
    /// </summary>
    /// <remarks>
    /// How many readings could be measured is answered beside every figure. Only the browser
    /// tracker observes any of this, so a website measured solely from its own server answers with
    /// nothing measured — which the dashboard is obliged to show as such rather than as an audience
    /// that did nothing.
    /// </remarks>
    private static async Task<Results<Ok<EngagementResponse>, NotFound, ProblemHttpResult>> EngagementAsync(
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

        var reading = await telemetry
            .GetSiteEngagementAsync(scope, new SiteEngagementQuery(range), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new EngagementResponse(
                range.From,
                range.To,
                reading.TotalReadings,
                reading.MeasuredReadings,
                reading.MedianEngagedMs,
                reading.InteractedReadings,
                new DepthBands(
                    reading.Reach.Top,
                    reading.Reach.Quarter,
                    reading.Reach.Half,
                    reading.Reach.Whole)));
    }

    /// <summary>
    /// Answers which of a website's pages held attention.
    /// </summary>
    /// <remarks>
    /// Everything the caller supplied is checked before the site is resolved, so a malformed
    /// request is refused identically whether or not the site exists.
    /// </remarks>
    private static async Task<Results<Ok<PageEngagementResponse>, NotFound, ProblemHttpResult>> PageEngagementAsync(
        [AsParameters] PageEngagementParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!Rankings.TryGetValue(parameters.Ranking ?? DefaultRanking, out var ranking))
        {
            return Unusable("Order the pages by attention or by depth.");
        }

        if (!TryReadSlice(
                new ListRequest(parameters.Limit, parameters.Offset, parameters.From, parameters.To),
                new ListBounds(DefaultReadPages, SitePageEngagementQuery.MostPages, "pages"),
                clock,
                out var slice,
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
            .GetSitePageEngagementAsync(
                scope,
                new SitePageEngagementQuery(slice.Range, ranking, slice.Limit, slice.Offset),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new PageEngagementResponse(
                slice.Range.From,
                slice.Range.To,
                RankingNames[ranking],
                pages.TotalPages,
                pages.LongestMedianEngagedMs,
                [
                    .. pages.Pages.Select(page => new PageEngagementRow(
                        page.Path,
                        page.Readings,
                        page.MedianEngagedMs,
                        page.MedianScrollDepthPercent,
                        page.InteractedReadings)),
                ]));
    }

    /// <summary>
    /// Answers how many visits a website had, and how many were a single page.
    /// </summary>
    /// <remarks>
    /// Answered from activity rather than from stored verdicts, so it keeps step with the headline
    /// totals instead of waiting for a visit to be judged. Only visits that have finished are
    /// counted, which is why the boundaries carry an instant as well as a timeout.
    /// </remarks>
    private static async Task<Results<Ok<VisitTotalsResponse>, NotFound, ProblemHttpResult>> VisitTotalsAsync(
        [AsParameters] OverviewParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        IOptions<ClassificationOptions> classification,
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

        var shape = await telemetry
            .GetSiteVisitShapeAsync(
                scope,
                new SiteVisitShapeQuery(range, Boundaries(classification.Value, clock)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new VisitTotalsResponse(
                range.From,
                range.To,
                shape.Visits,
                shape.SinglePageVisits,
                shape.PageViews));
    }

    /// <summary>
    /// Answers which pages a website's visits began or ended on.
    /// </summary>
    /// <remarks>
    /// Everything the caller supplied is checked before the site is resolved, so a malformed
    /// request is refused identically whether or not the site exists.
    /// </remarks>
    private static async Task<Results<Ok<VisitPagesResponse>, NotFound, ProblemHttpResult>> VisitPagesAsync(
        [AsParameters] VisitPagesParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        IOptions<ClassificationOptions> classification,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!Positions.TryGetValue(parameters.Position ?? DefaultPosition, out var position))
        {
            return Unusable("Count the pages visits started on or the pages they ended on.");
        }

        if (!TryReadSlice(
                new ListRequest(parameters.Limit, parameters.Offset, parameters.From, parameters.To),
                new ListBounds(DefaultVisitPages, SiteVisitFlowQuery.MostPages, "pages"),
                clock,
                out var slice,
                out var refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var flow = await telemetry
            .GetSiteVisitFlowAsync(
                scope,
                new SiteVisitFlowQuery(
                    slice.Range,
                    Boundaries(classification.Value, clock),
                    position,
                    slice.Limit,
                    slice.Offset),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new VisitPagesResponse(
                slice.Range.From,
                slice.Range.To,
                PositionNames[position],
                flow.TotalVisits,
                flow.TotalPaths,
                flow.MostVisits,
                [.. flow.Pages.Select(page => new VisitPageRow(page.Path, page.Visits))]));
    }

    /// <summary>
    /// Answers which pages one visit went through.
    /// </summary>
    /// <remarks>
    /// The identity is read before the site is resolved, so a value that names no visit is refused
    /// identically whether or not the site exists. What comes back names the visit in the engine's
    /// own spelling of it rather than in the caller's, so nothing a caller wrote is echoed.
    /// </remarks>
    private static async Task<Results<Ok<VisitJourneyResponse>, NotFound, ProblemHttpResult>> VisitJourneyAsync(
        [AsParameters] VisitJourneyParameters parameters,
        ITenantScopeProvider scopes,
        ISiteCatalog sites,
        ITelemetryQueries telemetry,
        IOptions<ClassificationOptions> classification,
        CancellationToken cancellationToken)
    {
        if (!VisitKey.TryParse(parameters.VisitKey, out var visit))
        {
            return Unusable("Ask for a visit by the identifier the visit list gives it.");
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var site = await sites.FindAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (site is null)
        {
            return TypedResults.NotFound();
        }

        var journey = await telemetry
            .GetSiteVisitJourneyAsync(
                scope,
                new SiteVisitJourneyQuery(
                    visit,
                    classification.Value.IdleTimeout,
                    site.Domain,
                    SiteVisitJourneyQuery.MostSteps),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new VisitJourneyResponse(
                visit.ToString(),
                Established(journey.Context),
                [
                    .. journey.Steps.Select(step => new VisitJourneyStep(
                        step.At,
                        step.Path,
                        step.StatusCode,
                        step.EngagedMs,
                        step.ScrollDepthPercent,
                        Operated(step.Press))),
                ]));
    }

    /// <summary>
    /// Reports what could be established about the visitor behind a visit.
    /// </summary>
    /// <remarks>
    /// The kind of source is spelled by the same vocabulary the source lists use, so one visit and
    /// the list it appears on describe the same arrival with the same word.
    /// </remarks>
    /// <param name="context">What was established.</param>
    /// <returns>The account, as the wire carries it.</returns>
    private static VisitContextResponse Established(VisitContext context) =>
        new(
            context.SendingSite,
            TrafficSources.Spelling(context.Channel),
            context.CountryCode,
            context.Town,
            context.NetworkOwner,
            ReportedNames.Devices[context.Device],
            context.Browser,
            context.OperatingSystem);

    /// <summary>
    /// What counts as one visit, and which visits have finished.
    /// </summary>
    /// <remarks>
    /// The idle timeout is the engine's own setting rather than a second copy of it, so the visits
    /// these answers count are the visits the engine is judging. A visit is treated as finished
    /// once it has been silent for a full timeout, which is the point at which falling silent has
    /// been observed rather than assumed.
    /// </remarks>
    private static VisitBoundaries Boundaries(ClassificationOptions settings, TimeProvider clock) =>
        new(settings.IdleTimeout, clock.GetUtcNow() - settings.IdleTimeout);

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

    /// <summary>
    /// Answers with individual judged visits and the case behind each verdict.
    /// </summary>
    /// <remarks>
    /// Everything the caller supplied — the paging, the window, and what they narrowed to — is
    /// checked before the site is resolved, so a malformed request is refused identically whether
    /// or not the site exists.
    /// </remarks>
    private static async Task<Results<Ok<VisitsResponse>, NotFound, ProblemHttpResult>> VisitsAsync(
        [AsParameters] VisitsParameters parameters,
        ITenantScopeProvider scopes,
        ITelemetryQueries telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!TryReadSlice(
                new ListRequest(parameters.Limit, parameters.Offset, parameters.From, parameters.To),
                new ListBounds(DefaultVisits, JudgedSessionsQuery.MostSessions, "visits"),
                clock,
                out var slice,
                out var refusal))
        {
            return Unusable(refusal);
        }

        if (!TryReadNarrowing(parameters, out var narrowing, out refusal))
        {
            return Unusable(refusal);
        }

        var scope = await scopes.ResolveAsync(parameters.SiteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var visits = await telemetry
            .GetJudgedSessionsAsync(
                scope,
                new JudgedSessionsQuery(slice.Range, slice.Limit, slice.Offset)
                {
                    Categories = narrowing.Categories,
                    LeastStrength = narrowing.LeastStrength,
                    LeastPages = narrowing.LeastPages,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(
            new VisitsResponse(
                slice.Range.From,
                slice.Range.To,
                visits.TotalVisits,
                [.. visits.Visits.Select(Describe)]));
    }

    /// <summary>
    /// What a caller narrowed a list of visits to.
    /// </summary>
    /// <param name="Categories">Which conclusions to return, or empty for all of them.</param>
    /// <param name="LeastStrength">The lowest band worth returning, or nothing for every band.</param>
    /// <param name="LeastPages">The fewest pages a visit must have gone to.</param>
    private readonly record struct VisitNarrowing(
        ImmutableArray<TrafficCategory> Categories,
        EvidenceStrength? LeastStrength,
        int LeastPages);

    /// <summary>
    /// Reads what a caller narrowed a list of visits to, refusing anything outside the vocabulary.
    /// </summary>
    /// <remarks>
    /// Every name is resolved through the same table the answers are written with, so a value this
    /// product does not report cannot be asked for either. Nothing supplied here reaches a
    /// statement as text: what comes out is a member of a closed set or a whole number.
    /// </remarks>
    /// <param name="asked">What the caller supplied.</param>
    /// <param name="narrowing">What it means, where it was usable.</param>
    /// <param name="refusal">Why it was not, where it was not.</param>
    /// <returns><see langword="true"/> when the request can be answered as asked.</returns>
    private static bool TryReadNarrowing(
        VisitsParameters asked,
        out VisitNarrowing narrowing,
        out string? refusal)
    {
        narrowing = default;

        var categories = ImmutableArray.CreateBuilder<TrafficCategory>();

        foreach (var name in asked.Category ?? [])
        {
            if (!ReportedNames.CategoriesByName.TryGetValue(name, out var category))
            {
                refusal = "Narrow to the conclusions this product reaches, or leave it out for all of them.";

                return false;
            }

            categories.Add(category);
        }

        EvidenceStrength? leastStrength = null;

        if (!string.IsNullOrEmpty(asked.Strength))
        {
            if (!ReportedNames.StrengthsByName.TryGetValue(asked.Strength, out var strength))
            {
                refusal = "Narrow to a strength of evidence this product reports, or leave it out for any.";

                return false;
            }

            leastStrength = strength;
        }

        var leastPages = asked.MinPages ?? 0;

        if (leastPages < 0)
        {
            refusal = "Ask for visits that went to no pages or more.";

            return false;
        }

        narrowing = new VisitNarrowing(categories.DrainToImmutable(), leastStrength, leastPages);
        refusal = null;

        return true;
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

    /// <summary>
    /// Adds a website.
    /// </summary>
    /// <remarks>
    /// Every refusal that is about the caller rather than about what they typed answers the same
    /// way, so this cannot be used to find out who owns what on an installation.
    /// </remarks>
    private static async Task<Results<Ok<SiteSummary>, UnauthorizedHttpResult, ForbidHttpResult, ProblemHttpResult>> AddAsync(
        AddSiteRequest? request,
        ISiteDirectory directory,
        ICurrentPrincipalAccessor caller,
        CancellationToken cancellationToken)
    {
        var userId = caller.GetUserId();

        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        if (request is null)
        {
            return Unaddable();
        }

        var addition = await directory
            .AddAsync(
                userId.Value,
                new NewSite(request.Domain ?? string.Empty, request.TimeZoneId ?? string.Empty),
                cancellationToken)
            .ConfigureAwait(false);

        return addition.Outcome switch
        {
            SiteAdditionOutcome.Added when addition.Added is { } added => TypedResults.Ok(
                new SiteSummary(
                    added.Id,
                    added.Domain,
                    added.DisplayName,
                    added.TimeZoneId,
                    RoleNames[added.Role])),
            SiteAdditionOutcome.NotAllowed => TypedResults.Forbid(),
            SiteAdditionOutcome.AlreadyMeasured => AlreadyMeasured(),
            _ => Unaddable(),
        };
    }

    /// <summary>
    /// Removes a website.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The identifier in the path is not a secret — it is printed in the tracking snippet on every
    /// page the website measures — so what stands between somebody and another person's website is
    /// the owner's role, the proof-of-origin pair every state-changing endpoint carries, and the
    /// confirmation the dashboard asks for before it sends this. Nothing about the request itself
    /// is trusted.
    /// </para>
    /// <para>
    /// A website nobody signed in has a role on and one that was never there answer identically,
    /// as everywhere else, so this cannot be used to discover which identifiers are real.
    /// </para>
    /// </remarks>
    private static async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> RemoveAsync(
        Guid siteId,
        ITenantScopeProvider scopes,
        ISiteDirectory directory,
        ICurrentPrincipalAccessor caller,
        CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(siteId, cancellationToken).ConfigureAwait(false);
        var userId = caller.GetUserId();

        if (scope is null || userId is null)
        {
            return TypedResults.NotFound();
        }

        if (scope.Role != SiteRole.Owner)
        {
            return TypedResults.Forbid();
        }

        var removal = await directory.RemoveAsync(userId.Value, siteId, cancellationToken).ConfigureAwait(false);

        return removal.Outcome switch
        {
            SiteRemovalOutcome.Removed => TypedResults.NoContent(),
            SiteRemovalOutcome.OnlyOne => OnlyOne(),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>Names the reason a website could not be built from what was asked for.</summary>
    private const string DetailsRejectedCode = "SiteDetailsRejected";

    /// <summary>Names the reason a website is already being measured here.</summary>
    private const string AlreadyMeasuredCode = "SiteAlreadyMeasured";

    /// <summary>Names the reason a website is the last one its owner has.</summary>
    private const string OnlyOneCode = "SiteIsOnlyOne";

    private static ProblemHttpResult Unaddable() =>
        Refused(
            "That website could not be added.",
            StatusCodes.Status400BadRequest,
            new RefusedReason(
                DetailsRejectedCode,
                "Give the address of the website, such as blog.example.com, and choose the time "
                    + "zone its days should be counted in."));

    private static ProblemHttpResult AlreadyMeasured() =>
        Refused(
            "That website is already here.",
            StatusCodes.Status409Conflict,
            new RefusedReason(
                AlreadyMeasuredCode,
                "It is already in the list of websites you can switch between."));

    private static ProblemHttpResult OnlyOne() =>
        Refused(
            "That website cannot be removed.",
            StatusCodes.Status409Conflict,
            new RefusedReason(
                OnlyOneCode,
                "It is the only website you own. Add another one first, then remove this."));

    /// <summary>
    /// Refuses with a reason the dashboard can write its own sentence for.
    /// </summary>
    /// <param name="title">What went wrong, in one line.</param>
    /// <param name="status">The status to answer with.</param>
    /// <param name="reason">The specific reason.</param>
    /// <returns>The refusal.</returns>
    private static ProblemHttpResult Refused(string title, int status, RefusedReason reason) =>
        TypedResults.Problem(
            title: title,
            detail: reason.Description,
            statusCode: status,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["problems"] = new[] { reason },
            });

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

    /// <summary>
    /// What a list request asked for, before any of it has been checked.
    /// </summary>
    /// <param name="Limit">How many rows to return, or nothing to take the list's own default.</param>
    /// <param name="Offset">How many leading rows to pass over, or nothing for none.</param>
    /// <param name="From">Inclusive start of the window.</param>
    /// <param name="To">Exclusive end of the window.</param>
    private readonly record struct ListRequest(
        int? Limit,
        int? Offset,
        DateTimeOffset? From,
        DateTimeOffset? To);

    /// <summary>
    /// What one list allows, and what its rows are called when a request for them is refused.
    /// </summary>
    /// <param name="Fallback">How many rows to return when the caller does not say.</param>
    /// <param name="Most">How many rows one request may ask for.</param>
    /// <param name="Rows">What the rows are called, for the sentence a refusal answers with.</param>
    private readonly record struct ListBounds(int Fallback, int Most, string Rows);

    /// <summary>
    /// A checked list request: how much to return, from where, over which window.
    /// </summary>
    /// <param name="Limit">How many rows to return.</param>
    /// <param name="Offset">How many leading rows to pass over.</param>
    /// <param name="Range">The window to count over.</param>
    private readonly record struct ListSlice(int Limit, int Offset, TimeRange Range);

    /// <summary>
    /// Checks the paging and the window a list request asked for.
    /// </summary>
    /// <param name="asked">What the caller supplied.</param>
    /// <param name="bounds">What this particular list allows.</param>
    /// <param name="clock">Where now comes from.</param>
    /// <param name="slice">The checked request, where it was usable.</param>
    /// <param name="refusal">Why it was not, where it was not.</param>
    /// <returns><see langword="true"/> when the request can be answered as asked.</returns>
    /// <remarks>
    /// Every list on the dashboard is read a slice at a time under identical rules, so they are
    /// written here once. Called before the site is resolved, so a malformed request is refused
    /// identically whether or not the site exists: checking in the other order would turn a bad
    /// limit into a way of finding out which identifiers on an install are real.
    /// </remarks>
    private static bool TryReadSlice(
        ListRequest asked,
        ListBounds bounds,
        TimeProvider clock,
        out ListSlice slice,
        out string? refusal)
    {
        slice = default;

        var limit = asked.Limit ?? bounds.Fallback;
        var offset = asked.Offset ?? 0;

        if (limit < 1 || limit > bounds.Most)
        {
            refusal = $"Ask for between 1 and {bounds.Most} {bounds.Rows} at a time.";

            return false;
        }

        if (offset < 0)
        {
            refusal = "Start the list at the beginning or further along it, never before it.";

            return false;
        }

        if (!RequestedWindow.TryResolve(
                asked.From,
                asked.To,
                RequestedWindow.Longest,
                clock,
                out var range,
                out refusal))
        {
            return false;
        }

        slice = new ListSlice(limit, offset, range);

        return true;
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
/// What the locations endpoint reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The site to count over.</param>
/// <param name="Grouping">What each row should stand for: <c>country</c> or <c>town</c>.</param>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Limit">How many places to return.</param>
/// <param name="Offset">How many of the busiest places to pass over first.</param>
internal readonly record struct LocationsParameters(
    Guid SiteId,
    [FromQuery] string? Grouping,
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? To,
    [FromQuery] int? Limit,
    [FromQuery] int? Offset);

/// <summary>
/// What the sources endpoint reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The site to count over.</param>
/// <param name="Grouping">What each row should stand for: <c>site</c> or <c>page</c>.</param>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Limit">How many sources to return.</param>
/// <param name="Offset">How many of the busiest sources to pass over first.</param>
internal readonly record struct SourcesParameters(
    Guid SiteId,
    [FromQuery] string? Grouping,
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? To,
    [FromQuery] int? Limit,
    [FromQuery] int? Offset);

/// <summary>
/// What the software endpoint reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The site to count over.</param>
/// <param name="Grouping">What each row should stand for: <c>browser</c> or <c>system</c>.</param>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Limit">How many names to return.</param>
/// <param name="Offset">How many of the commonest names to pass over first.</param>
internal readonly record struct SoftwareParameters(
    Guid SiteId,
    [FromQuery] string? Grouping,
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? To,
    [FromQuery] int? Limit,
    [FromQuery] int? Offset);

/// <summary>
/// What the operated-controls endpoint reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The site to count over.</param>
/// <param name="Grouping">What to gather by: <c>control</c> or <c>destination</c>.</param>
/// <param name="From">Inclusive start of the period. Defaults to a week before the end.</param>
/// <param name="To">Exclusive end of the period. Defaults to now.</param>
/// <param name="Limit">How many rows to return. Defaults to a screenful.</param>
/// <param name="Offset">How many of the most pressed to pass over first. Defaults to none.</param>
internal readonly record struct ActionsParameters(
    Guid SiteId,
    [FromQuery] string? Grouping,
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
/// <param name="Offset">How many of the most recent visits to pass over first. Defaults to none.</param>
/// <param name="Category">
/// Which conclusions to return, named once each. Absent for all of them.
/// </param>
/// <param name="Strength">The lowest band of evidence worth returning. Absent for every band.</param>
/// <param name="MinPages">The fewest pages a visit must have gone to. Defaults to none.</param>
internal readonly record struct VisitsParameters(
    Guid SiteId,
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? To,
    [FromQuery] int? Limit,
    [FromQuery] int? Offset,
    [FromQuery] string[]? Category,
    [FromQuery] string? Strength,
    [FromQuery] int? MinPages);

/// <summary>
/// What the page-reading endpoint reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The site to count over.</param>
/// <param name="Ranking">What to order the pages by: <c>attention</c> or <c>depth</c>.</param>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Limit">How many pages to return.</param>
/// <param name="Offset">How many of the leading pages to pass over first.</param>
internal readonly record struct PageEngagementParameters(
    Guid SiteId,
    [FromQuery] string? Ranking,
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? To,
    [FromQuery] int? Limit,
    [FromQuery] int? Offset);

/// <summary>
/// What the arrival and departure list reads from the path and the query string.
/// </summary>
/// <param name="SiteId">The site to count over.</param>
/// <param name="Position">Which end of a visit to count: <c>entry</c> or <c>exit</c>.</param>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Limit">How many pages to return.</param>
/// <param name="Offset">How many of the commonest pages to pass over first.</param>
internal readonly record struct VisitPagesParameters(
    Guid SiteId,
    [FromQuery] string? Position,
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? To,
    [FromQuery] int? Limit,
    [FromQuery] int? Offset);

/// <summary>
/// What the journey endpoint reads from the path.
/// </summary>
/// <remarks>
/// No window: a visit's identity already says when it began, and a journey is the whole of one
/// visit rather than a slice of a period.
/// </remarks>
/// <param name="SiteId">The site the visit belongs to.</param>
/// <param name="VisitKey">The visit, as the visit list names it.</param>
internal readonly record struct VisitJourneyParameters(Guid SiteId, string? VisitKey);
