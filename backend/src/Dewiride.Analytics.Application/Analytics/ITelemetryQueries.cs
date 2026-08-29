using System.Collections.Immutable;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// Reads the telemetry store.
/// </summary>
/// <remarks>
/// Every method takes a <see cref="TenantScope"/>, which can only be produced by
/// <see cref="ITenantScopeProvider"/> after it has checked membership. Tenant isolation is
/// therefore a property of the type signature rather than a rule implementers are asked to
/// remember: there is no way to express a telemetry read without having been authorised for
/// the site it reads.
/// </remarks>
public interface ITelemetryQueries
{
    /// <summary>Returns headline totals for a site over a window.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window to summarise.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The totals.</returns>
    Task<OverviewResult> GetOverviewAsync(
        TenantScope scope,
        OverviewQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns one metric bucketed over time.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The metric, window and bucket size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The buckets, in ascending time order, with empty buckets present and zeroed.</returns>
    Task<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
        TenantScope scope,
        TimeSeriesQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns one slice of the pages traffic went to over a window, busiest first.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window, how many pages to return, and how many to pass over.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The slice, with the figures the whole window gives it its meaning against.</returns>
    Task<SitePages> GetSitePagesAsync(
        TenantScope scope,
        SitePagesQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns one slice of the places traffic came from over a window, busiest first.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window, the grouping, and which slice of the list to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The slice, with the figures the whole window gives it its meaning against.</returns>
    Task<SiteLocations> GetSiteLocationsAsync(
        TenantScope scope,
        SiteLocationsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns one slice of where a window's visitors came from, busiest first.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window, the grouping, and which slice of the list to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The slice, with the figures the whole window gives it its meaning against.</returns>
    Task<SiteSources> GetSiteSourcesAsync(
        TenantScope scope,
        SiteSourcesQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns one slice of what a site's visitors operated, most pressed first.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window, the grouping, and which slice of the list to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The slice, with the figures the whole window gives it its meaning against.</returns>
    Task<SiteActions> GetSiteActionsAsync(
        TenantScope scope,
        SiteActionsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns how many of a site's audience were on each kind of device.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window to count over.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One row per kind of device that was seen, commonest first.</returns>
    Task<IReadOnlyList<SiteDeviceKindRow>> GetSiteDeviceKindsAsync(
        TenantScope scope,
        SiteDeviceKindsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns one slice of the software a site's audience used, commonest first.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window, the grouping, and which slice of the list to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The slice, with the figures the whole window gives it its meaning against.</returns>
    Task<SiteSoftware> GetSiteSoftwareAsync(
        TenantScope scope,
        SiteSoftwareQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns how a site's pages were read over a window.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window to count over.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The figures, with how much of the window they could be taken from.</returns>
    Task<SiteEngagement> GetSiteEngagementAsync(
        TenantScope scope,
        SiteEngagementQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns one slice of a site's pages ranked by how they were read.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window, the ranking, and which slice of the list to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The slice, with the figures the whole window gives it its meaning against.</returns>
    Task<SitePageEngagement> GetSitePageEngagementAsync(
        TenantScope scope,
        SitePageEngagementQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns how a window's finished visits were shaped.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window, and what counts as one visit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The totals.</returns>
    Task<SiteVisitShape> GetSiteVisitShapeAsync(
        TenantScope scope,
        SiteVisitShapeQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns one slice of the pages a window's visits began or ended on.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window, which end of a visit to count, and which slice to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The slice, with the figures the whole window gives it its meaning against.</returns>
    Task<SiteVisitFlow> GetSiteVisitFlowAsync(
        TenantScope scope,
        SiteVisitFlowQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns what one visit was, and what it did.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">Which visit, and how many steps to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The steps, oldest first, and the account of who the visit was. Empty, and knowing nothing,
    /// where the identity names no visit on this site.
    /// </returns>
    Task<VisitJourney> GetSiteVisitJourneyAsync(
        TenantScope scope,
        SiteVisitJourneyQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns judged visits grouped by what generated them.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window to group over.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One row per category and evidence strength, busiest first.</returns>
    Task<IReadOnlyList<TrafficBreakdownRow>> GetTrafficBreakdownAsync(
        TenantScope scope,
        TrafficBreakdownQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns judged visits counted by what generated them, bucket by bucket.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window to count over, and how finely to cut it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every bucket in the window, and one entry per category the window held.</returns>
    Task<TrafficSeries> GetTrafficSeriesAsync(
        TenantScope scope,
        TrafficSeriesQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns individual judged visits with the evidence behind each verdict.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window, and which slice of it to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The slice, newest first, and how many visits the window holds altogether.</returns>
    Task<JudgedSessions> GetJudgedSessionsAsync(
        TenantScope scope,
        JudgedSessionsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns what each detail of a window's judged visits held.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window to describe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The commonest values each detail held, counted per visit, busiest first. A detail holds
    /// nothing where the window has no judged visits, or where the activity behind them has already
    /// aged out of the telemetry store.
    /// </returns>
    Task<VisitFacets> GetSiteVisitFacetsAsync(
        TenantScope scope,
        SiteVisitFacetsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns everyone seen on a site in the last stretch of minutes.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The stretch of minutes, and how many visitors to carry back.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The visitors, the one most recently active first, and how many there were altogether — which
    /// is counted over the whole window and is therefore right even when the list was cut short.
    /// </returns>
    Task<LiveVisitors> GetSiteLiveVisitorsAsync(
        TenantScope scope,
        SiteLiveVisitorsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns how much of a site was read in each minute of the last stretch of them.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The stretch of minutes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every minute the window covers, oldest first, including the empty ones.</returns>
    Task<IReadOnlyList<LiveMinute>> GetSiteLiveActivityAsync(
        TenantScope scope,
        SiteLiveActivityQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns the pages being read in the last stretch of minutes, busiest first.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The stretch of minutes, and how many pages to carry back.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The busiest pages, and how many visitors each of them held.</returns>
    Task<IReadOnlyList<LivePage>> GetSiteLivePagesAsync(
        TenantScope scope,
        SiteLivePagesQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns what one visitor has done in the last stretch of minutes.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The stretch of minutes, whose activity, and how many steps to carry back.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The steps, oldest first. Empty where the key names nobody the window holds — including a
    /// visitor who has since left it, which is a visitor who has gone rather than a failure.
    /// </returns>
    Task<IReadOnlyList<VisitStep>> GetSiteLiveTrailAsync(
        TenantScope scope,
        SiteLiveTrailQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// One slice of a window's judged visits, newest first.
/// </summary>
/// <remarks>
/// The total is counted across the whole window rather than over the slice, so a list can say how
/// far through it somebody is and stop when there is genuinely nothing left rather than when a
/// screenful runs out.
/// </remarks>
/// <param name="TotalVisits">How many judged visits the window holds, across every slice.</param>
/// <param name="Visits">The slice, newest first.</param>
public sealed record JudgedSessions(long TotalVisits, ImmutableArray<JudgedSession> Visits);

/// <summary>
/// One group of visits that reached the same conclusion with the same weight behind it.
/// </summary>
/// <remarks>
/// Category and strength are reported together rather than summed across strengths, because a
/// hundred visits called a crawler on weak evidence is a different statement from a hundred
/// called one on strong evidence, and collapsing the two would hide exactly the distinction this
/// product exists to make.
/// </remarks>
/// <param name="Category">What the engine concluded generated these visits.</param>
/// <param name="Strength">How much weight stood behind that conclusion.</param>
/// <param name="Sessions">How many visits fell into the group.</param>
/// <param name="PageViews">How many pages those visits asked for between them.</param>
public readonly record struct TrafficBreakdownRow(
    TrafficCategory Category,
    EvidenceStrength Strength,
    long Sessions,
    long PageViews);

/// <summary>
/// What generated a window's judged visits, bucket by bucket.
/// </summary>
/// <remarks>
/// Written as a table rather than as a list of its own cells. Every group's arrays are as long as
/// <paramref name="Buckets"/> and hold their counts in the same order, so a reader indexes rather
/// than joins — and a category the window never held is absent altogether rather than present as a
/// row of zeroes.
/// </remarks>
/// <param name="Buckets">Where each bucket in the window begins, oldest first.</param>
/// <param name="Groups">One entry per category the window held.</param>
public sealed record TrafficSeries(
    ImmutableArray<DateTimeOffset> Buckets,
    ImmutableArray<TrafficSeriesGroup> Groups);

/// <summary>
/// One category's counts across every bucket of a window.
/// </summary>
/// <param name="Category">What the engine concluded generated these visits.</param>
/// <param name="Sessions">How many visits began in each bucket.</param>
/// <param name="PageViews">How many pages those visits asked for, bucket by bucket.</param>
public sealed record TrafficSeriesGroup(
    TrafficCategory Category,
    ImmutableArray<long> Sessions,
    ImmutableArray<long> PageViews);

/// <summary>
/// One judged visit, as it is read back for display.
/// </summary>
public sealed record JudgedSession
{
    /// <summary>Identity of the visit, derived from the visitor key and when the visit began.</summary>
    public required string SessionKey { get; init; }

    /// <summary>When the visit began.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the last activity on it was seen.</summary>
    public required DateTimeOffset EndedAt { get; init; }

    /// <summary>How many pages the visit asked for.</summary>
    public required int PageCount { get; init; }

    /// <summary>Which capture surfaces saw it.</summary>
    public required ImmutableArray<IngestSurface> Surfaces { get; init; }

    /// <summary>The conclusion, with the evidence for and against it.</summary>
    public required ClassificationVerdict Verdict { get; init; }
}

/// <summary>
/// What each detail of a period's judged visits held, and how many visits held each value.
/// </summary>
/// <remarks>
/// <para>
/// A list per detail rather than one list of rows carrying the detail's name, because two of the
/// nine are members of this product's own closed vocabularies and the other seven are whatever the
/// store holds. One list would have to give that distinction up, and give it up exactly where a
/// screen has to know whether it may translate a value or must print it as it found it.
/// </para>
/// <para>
/// A detail holding nothing is an empty list, which is an answer rather than a gap: a period whose
/// visitors all arrived by typing the address genuinely has no sending sites to offer.
/// </para>
/// </remarks>
public sealed record VisitFacets
{
    /// <summary>A period that offers nothing, which is what one holding no judged visits holds.</summary>
    public static VisitFacets Nothing { get; } = new();

    /// <summary>Kinds of device, commonest first.</summary>
    /// <remarks>
    /// What nothing established reads as <see cref="DeviceClass.Unknown"/>, which is the same
    /// answer a single visit gives when nothing said what it was on.
    /// </remarks>
    public ImmutableArray<VisitDetailCount<DeviceClass>> Devices { get; init; } = [];

    /// <summary>Kinds of place the visits were sent by.</summary>
    /// <remarks>
    /// What nothing established reads as <see cref="SourceChannel.Direct"/>, which is not "nobody
    /// sent them" but "nothing said who did".
    /// </remarks>
    public ImmutableArray<VisitDetailCount<SourceChannel>> SourceKinds { get; init; } = [];

    /// <summary>Browsers the visits were made with.</summary>
    public ImmutableArray<VisitDetailCount<string>> Browsers { get; init; } = [];

    /// <summary>The systems those browsers were running on.</summary>
    public ImmutableArray<VisitDetailCount<string>> OperatingSystems { get; init; } = [];

    /// <summary>Countries the visits arrived from, as two-letter codes.</summary>
    public ImmutableArray<VisitDetailCount<string>> Countries { get; init; } = [];

    /// <summary>Towns within them.</summary>
    public ImmutableArray<VisitDetailCount<string>> Towns { get; init; } = [];

    /// <summary>Who runs the networks the visits arrived over.</summary>
    public ImmutableArray<VisitDetailCount<string>> Networks { get; init; } = [];

    /// <summary>The sites that sent the visits.</summary>
    public ImmutableArray<VisitDetailCount<string>> Sources { get; init; } = [];

    /// <summary>The pages the visits began on.</summary>
    public ImmutableArray<VisitDetailCount<string>> EntryPages { get; init; } = [];
}

/// <summary>
/// One value a detail held, and how many of a period's judged visits held it.
/// </summary>
/// <remarks>
/// Counted per visit rather than per report, because a visit is what the list this narrows is made
/// of — a value offered as four hundred that handed back ninety would be a promise the product
/// could not keep. Among the free-text details an empty value is a value rather than a gap: it is
/// what the store holds where nothing could be established, so it counts the visits nothing is
/// known about.
/// </remarks>
/// <typeparam name="T">How the value is spelled: a member of a closed set, or free text.</typeparam>
/// <param name="Value">The value.</param>
/// <param name="Visits">How many of the period's judged visits held it.</param>
public readonly record struct VisitDetailCount<T>(T Value, long Visits);

/// <summary>
/// Headline totals for a site over a window.
/// </summary>
/// <param name="PageViews">Page views observed.</param>
/// <param name="Visitors">
/// Distinct visitor keys observed. Because the key rotates daily, a window longer than a day
/// counts a returning visitor once per day rather than once overall — which is stated in the
/// UI rather than quietly presented as a unique-people count.
/// </param>
/// <param name="Events">Total events of every kind, including engagement and exit reports.</param>
public readonly record struct OverviewResult(long PageViews, long Visitors, long Events);

/// <summary>
/// One bucket of a time series.
/// </summary>
/// <param name="BucketStart">Inclusive start of the bucket.</param>
/// <param name="Value">The metric's value within the bucket.</param>
public readonly record struct TimeSeriesPoint(DateTimeOffset BucketStart, long Value);

/// <summary>
/// One slice of the pages traffic went to over a window.
/// </summary>
/// <remarks>
/// The three figures beside the rows all describe the whole window rather than the slice, because
/// a slice on its own says nothing: a row is only worth reading against everything it was drawn
/// from. They are computed before the slice is taken, so they do not change as somebody moves
/// through the list.
/// </remarks>
/// <param name="TotalPageViews">
/// Pages delivered across the whole window, counting every address this slice does not contain.
/// Every share is taken against this, so ten rows from a site with a thousand addresses do not
/// add up to the whole of its traffic.
/// </param>
/// <param name="TotalPaths">
/// How many addresses had traffic in the window. What tells a caller how much of the list is
/// still ahead of them.
/// </param>
/// <param name="MostPageViews">
/// Pages delivered at the single busiest address. A bar drawn against this stays the same length
/// for the same figure wherever in the list it appears; drawn against whatever happened to be
/// busiest in one slice, every slice would start with a full bar.
/// </param>
/// <param name="Pages">The slice, busiest first.</param>
public sealed record SitePages(
    long TotalPageViews,
    long TotalPaths,
    long MostPageViews,
    ImmutableArray<SitePageRow> Pages);

/// <summary>
/// One page and how much of a window's traffic went to it.
/// </summary>
/// <param name="Path">
/// Path of the page, exactly as it was asked for. Written by whoever made the request, so it is
/// data everywhere it travels and never anything else.
/// </param>
/// <param name="PageViews">Pages delivered at this path.</param>
/// <param name="Visitors">Distinct visitor keys that asked for it, on the same daily terms as the headline count.</param>
public readonly record struct SitePageRow(string Path, long PageViews, long Visitors);

/// <summary>
/// One slice of what a window's visitors operated, and what the whole window makes of it.
/// </summary>
/// <param name="TotalPresses">Presses across the whole window, which every share is taken against.</param>
/// <param name="TotalControls">How many distinct rows the window holds, so a reader knows how far the list runs.</param>
/// <param name="MostPresses">Presses at the most pressed row, which every bar is drawn against.</param>
/// <param name="Controls">The slice itself, most pressed first.</param>
public sealed record SiteActions(
    long TotalPresses,
    long TotalControls,
    long MostPresses,
    ImmutableArray<SiteActionRow> Controls);

/// <summary>
/// One thing a window's visitors operated, and how often.
/// </summary>
/// <param name="Name">
/// What the row is called: the control's own name under <see cref="ActionGrouping.Control"/>, and
/// the host a press led to under <see cref="ActionGrouping.Destination"/>. Written by whoever wrote
/// the page, so it is data everywhere it travels and never anything else. Empty where a site gave
/// its control no name, which is a finding rather than a gap.
/// </param>
/// <param name="Control">
/// What sort of thing it was. Always <see cref="ControlKind.Unknown"/> under
/// <see cref="ActionGrouping.Destination"/>, where the row is a place rather than a control.
/// </param>
/// <param name="Presses">How many times it was operated.</param>
/// <param name="Visitors">How many distinct visitors operated it.</param>
public readonly record struct SiteActionRow(
    string Name,
    ControlKind Control,
    long Presses,
    long Visitors);


/// <summary>
/// One slice of the places traffic came from over a window.
/// </summary>
/// <remarks>
/// The figures beside the rows describe the whole window rather than the slice, on the same terms
/// and for the same reason as <see cref="SitePages"/>.
/// </remarks>
/// <param name="TotalVisitors">
/// Visitors across the whole window, including those in places this slice does not contain and
/// those whose place could not be established. Every share is taken against this, so a country
/// showing a small share on a site whose traffic is mostly unresolved is telling the truth.
/// </param>
/// <param name="TotalPlaces">
/// How many places had traffic in the window, counting the unresolved group as one of them.
/// </param>
/// <param name="MostVisitors">
/// Visitors in the single busiest place, so a bar means the same thing wherever in the list it
/// appears.
/// </param>
/// <param name="Places">The slice, busiest first.</param>
public sealed record SiteLocations(
    long TotalVisitors,
    long TotalPlaces,
    long MostVisitors,
    ImmutableArray<SiteLocationRow> Places);

/// <summary>
/// One place and how much of a window's audience was there.
/// </summary>
/// <param name="Place">
/// The country's two-letter code, or the town's name, depending on how the list was grouped.
/// Empty when the address resolved to nothing — which is a place on the list rather than a row to
/// be dropped, because a site whose traffic is largely unresolved should be able to see that.
/// </param>
/// <param name="CountryCode">
/// The country this row is in. The same as <paramref name="Place"/> on a country list, and what
/// tells a town apart from the identically-named town in another country on a town list.
/// </param>
/// <param name="Visitors">Distinct visitors, on the same daily terms as the headline count.</param>
/// <param name="PageViews">Pages those visitors were delivered.</param>
public readonly record struct SiteLocationRow(
    string Place,
    string CountryCode,
    long Visitors,
    long PageViews);

/// <summary>
/// One slice of where a window's visitors came from, with the whole window's figures.
/// </summary>
/// <param name="TotalVisitors">Visitors across every source in the window, not only this slice.</param>
/// <param name="TotalSources">How many sources the window holds, so a slice can say what it is part of.</param>
/// <param name="MostVisitors">The busiest source's count, which every bar on the list is drawn against.</param>
/// <param name="Sources">The slice itself, busiest first.</param>
public sealed record SiteSources(
    long TotalVisitors,
    long TotalSources,
    long MostVisitors,
    ImmutableArray<SiteSourceRow> Sources);

/// <summary>
/// One source and how many of a window's visitors it sent.
/// </summary>
/// <param name="Source">
/// The sending site's address, or that address with the sending page's path after it, depending
/// on how the list was grouped. Empty when the arrival named nowhere, which is a row on the list
/// rather than one to be dropped.
/// </param>
/// <param name="Site">
/// The sending site's address on its own. The same as <paramref name="Source"/> on a site list,
/// and what lets a page list show where a page belongs without parsing the address on screen.
/// </param>
/// <param name="Visitors">Distinct visitors, on the same daily terms as the headline count.</param>
/// <param name="PageViews">Pages those visitors went on to be delivered.</param>
public readonly record struct SiteSourceRow(
    string Source,
    string Site,
    long Visitors,
    long PageViews);

/// <summary>
/// One kind of device and how much of a window's audience was on it.
/// </summary>
/// <remarks>
/// Typed rather than named, because the kinds are a closed set the engine decides between. The
/// unresolved group is <see cref="DeviceClass.Unknown"/> and is a row like any other: much of what
/// reaches a website is not a device at all, and a list that hid that would be describing a
/// different audience from the one that was there.
/// </remarks>
/// <param name="Device">Which kind of device.</param>
/// <param name="Visitors">Distinct visitors, on the same daily terms as the headline count.</param>
/// <param name="PageViews">Pages those visitors were delivered.</param>
public readonly record struct SiteDeviceKindRow(DeviceClass Device, long Visitors, long PageViews);

/// <summary>
/// One slice of the software a window's audience used.
/// </summary>
/// <remarks>
/// The figures beside the rows describe the whole window rather than the slice, on the same terms
/// and for the same reason as <see cref="SitePages"/>.
/// </remarks>
/// <param name="TotalVisitors">
/// Visitors across the whole window, including those outside this slice and those whose software
/// could not be established. Every share is taken against this.
/// </param>
/// <param name="TotalNames">How many distinct names the window holds, counting the unresolved group as one.</param>
/// <param name="MostVisitors">
/// Visitors on the single commonest name, so a bar means the same thing wherever in the list it
/// appears.
/// </param>
/// <param name="Names">The slice, commonest first.</param>
public sealed record SiteSoftware(
    long TotalVisitors,
    long TotalNames,
    long MostVisitors,
    ImmutableArray<SiteSoftwareRow> Names);

/// <summary>
/// One piece of software and how much of a window's audience used it.
/// </summary>
/// <param name="Name">
/// The browser family or the operating system, as the engine's own catalogue spells it — never as
/// the client wrote it. Empty when nothing could be established.
/// </param>
/// <param name="Visitors">Distinct visitors, on the same daily terms as the headline count.</param>
/// <param name="PageViews">Pages those visitors were delivered.</param>
public readonly record struct SiteSoftwareRow(string Name, long Visitors, long PageViews);

/// <summary>
/// How a window's pages were read, across a whole site.
/// </summary>
/// <remarks>
/// A <em>reading</em> is one visitor on one page. A page reports its progress repeatedly while it
/// is open and every report carries a running total, so the largest report is what that reading
/// came to and the reading is counted once.
/// </remarks>
/// <param name="TotalReadings">
/// Readings in the window, whether or not anything about them could be measured. What
/// <paramref name="MeasuredReadings"/> is read against, so that a site measured only from its own
/// server reads as unmeasured rather than as unengaged.
/// </param>
/// <param name="MeasuredReadings">
/// Readings a browser reported progress for. Every other figure here is taken over these and only
/// these, because the remainder are readings nobody was watching rather than readings where nobody
/// did anything.
/// </param>
/// <param name="MedianEngagedMs">
/// The middle reading's attention, in milliseconds — time the page was genuinely in front of
/// somebody rather than merely open. The middle rather than the mean, because a handful of very
/// long readings would otherwise describe an audience nobody in it resembles. Nought when nothing
/// could be measured.
/// </param>
/// <param name="InteractedReadings">Measured readings where a pointer or a key was used at all.</param>
/// <param name="Reach">How far down the page those readings got.</param>
public sealed record SiteEngagement(
    long TotalReadings,
    long MeasuredReadings,
    int MedianEngagedMs,
    long InteractedReadings,
    ScrollReach Reach);

/// <summary>
/// How far down a page a window's readings got, in quarters.
/// </summary>
/// <remarks>
/// Quarters rather than a finer division because the depth itself is an estimate: it is measured
/// against the document's height at the moment the reader stopped, and a page whose images arrive
/// late is a different height a second later. Four bands say what a quarter-percentage point
/// cannot pretend to.
/// </remarks>
/// <param name="Top">Readings that got less than a quarter of the way down.</param>
/// <param name="Quarter">Readings that reached a quarter but not half.</param>
/// <param name="Half">Readings that reached half but not three-quarters.</param>
/// <param name="Whole">Readings that reached three-quarters or more.</param>
public readonly record struct ScrollReach(long Top, long Quarter, long Half, long Whole);

/// <summary>
/// One slice of a site's pages ranked by how they were read.
/// </summary>
/// <param name="TotalPages">
/// How many pages in the window had at least one reading that could be measured. Pages nothing
/// could be measured on are not on the list at all, so this is smaller than the number of pages
/// that had traffic.
/// </param>
/// <param name="LongestMedianEngagedMs">
/// The largest middle attention any page in the window held, so a bar drawn against it means the
/// same thing wherever in the list it appears.
/// </param>
/// <param name="Pages">The slice, leading the chosen ranking first.</param>
public sealed record SitePageEngagement(
    long TotalPages,
    int LongestMedianEngagedMs,
    ImmutableArray<SitePageEngagementRow> Pages);

/// <summary>
/// One page and how it was read.
/// </summary>
/// <param name="Path">
/// Path of the page, exactly as it was asked for. Written by whoever made the request, so it is
/// data everywhere it travels and never anything else.
/// </param>
/// <param name="Readings">Readings of this page that could be measured.</param>
/// <param name="MedianEngagedMs">The middle reading's attention, in milliseconds.</param>
/// <param name="MedianScrollDepthPercent">How far down the middle reading got, as a percentage.</param>
/// <param name="InteractedReadings">Readings where a pointer or a key was used at all.</param>
public readonly record struct SitePageEngagementRow(
    string Path,
    long Readings,
    int MedianEngagedMs,
    int MedianScrollDepthPercent,
    long InteractedReadings);

/// <summary>
/// How a window's finished visits were shaped.
/// </summary>
/// <remarks>
/// Every figure is exact rather than sampled or estimated, and counts only visits that had
/// finished when the question was asked. A visit still under way has an unfinished page count, and
/// on a quiet site a handful of those would decide the answer on their own.
/// </remarks>
/// <param name="Visits">Finished visits that began in the window.</param>
/// <param name="SinglePageVisits">
/// How many of them asked for exactly one page. Reported as a count rather than as a rate, so the
/// share is taken against <paramref name="Visits"/> wherever it is shown and there is no second
/// number that could disagree with it.
/// </param>
/// <param name="PageViews">Pages those visits asked for between them.</param>
public readonly record struct SiteVisitShape(long Visits, long SinglePageVisits, long PageViews);

/// <summary>
/// One slice of the pages a window's visits began or ended on.
/// </summary>
/// <remarks>
/// The figures beside the rows describe the whole window rather than the slice, on the same terms
/// and for the same reason as <see cref="SitePages"/>.
/// </remarks>
/// <param name="TotalVisits">
/// Finished visits across the whole window, including those on pages this slice does not contain.
/// Every share is taken against this.
/// </param>
/// <param name="TotalPaths">How many distinct pages the window holds at this end of a visit.</param>
/// <param name="MostVisits">
/// Visits at the single commonest page, so a bar means the same thing wherever in the list it
/// appears.
/// </param>
/// <param name="Pages">The slice, commonest first.</param>
public sealed record SiteVisitFlow(
    long TotalVisits,
    long TotalPaths,
    long MostVisits,
    ImmutableArray<SiteVisitFlowRow> Pages);

/// <summary>
/// One page and how many visits began or ended on it.
/// </summary>
/// <param name="Path">
/// Path of the page, exactly as it was asked for. Written by whoever made the request, so it is
/// data everywhere it travels and never anything else.
/// </param>
/// <param name="Visits">Visits that began or ended there.</param>
public readonly record struct SiteVisitFlowRow(string Path, long Visits);

/// <summary>
/// One thing a visit did: arriving at a page, or operating a control on one.
/// </summary>
/// <remarks>
/// A page step is one arrival at one page, so a visitor who comes back to a page later in the same
/// visit produces two steps rather than one with the two readings added together. A press is a step
/// of its own — somebody who pressed the same button twice pressed it twice — and carries the page
/// it happened on so it can be read against the arrival above it.
/// </remarks>
/// <param name="At">When the step happened: the first report of an arrival, or the press itself.</param>
/// <param name="Path">
/// Path of the page, exactly as it was asked for. Written by whoever made the request, so it is
/// data everywhere it travels and never anything else.
/// </param>
/// <param name="StatusCode">
/// What the site answered with, where a reporter on the site's own server saw the request. Nothing
/// where only the browser reported the page: a tracker runs on a page that was delivered and has
/// nothing to say about one that was not.
/// </param>
/// <param name="EngagedMs">
/// How long the page was genuinely in front of somebody, in milliseconds. Nothing where no browser
/// watched the step — which is a different statement from a reader who left immediately, and is
/// kept distinct all the way to the screen.
/// </param>
/// <param name="ScrollDepthPercent">How far down the page the reader got, on the same terms.</param>
/// <param name="Press">
/// The control that was operated, where this step is a press rather than an arrival. Nothing on an
/// arrival, which is what tells the two apart.
/// </param>
public readonly record struct VisitStep(
    DateTimeOffset At,
    string Path,
    int? StatusCode,
    int? EngagedMs,
    int? ScrollDepthPercent,
    VisitPress? Press);

/// <summary>
/// One visit: who it was, and what it did.
/// </summary>
/// <remarks>
/// The two halves are answered together because they are one question. A list of pages says what
/// happened and a verdict says what the engine made of it; neither says whether the reader came
/// from a search, where they were, or what they were reading on — and those are what turn a row on
/// a list into somebody a site's owner can picture.
/// </remarks>
/// <param name="Context">What can be said about the visitor, which may be nothing.</param>
/// <param name="Steps">What the visit did, oldest first.</param>
public sealed record VisitJourney(VisitContext Context, ImmutableArray<VisitStep> Steps)
{
    /// <summary>A visit nothing is known about, which is what an unknown identity answers with.</summary>
    public static VisitJourney None { get; } = new(VisitContext.Nothing, []);
}

/// <summary>
/// What can be said about the visitor behind one visit.
/// </summary>
/// <remarks>
/// <para>
/// Every field is empty rather than absent where nothing established it, and empty is a fact
/// this product states rather than hides: a site behind something that does not pass the visitor's
/// address along places nobody at all, and a visit only its own server saw carries no browser.
/// </para>
/// <para>
/// Each is settled as the earliest report of the visit that carried one. A visit watched by both a
/// tracker in the browser and a reporter on the site's own server holds reports that resolved
/// these and reports that did not, and taking the first that did is both correct and repeatable —
/// a panel that named a different browser on each reading would be a defect.
/// </para>
/// </remarks>
/// <param name="SendingSite">
/// The site that sent them, named from the catalogue where it is in it and left as its own address
/// where it is not. Empty where the browser named nowhere.
/// </param>
/// <param name="Channel">What kind of thing that was. <see cref="SourceChannel.Direct"/> where nothing said.</param>
/// <param name="CountryCode">Two-letter country code, or empty.</param>
/// <param name="Town">
/// Town or city, as the free geolocation data spells it, which is English. An estimate from the
/// visitor's network rather than a position from their device. Empty where nothing placed them.
/// </param>
/// <param name="NetworkOwner">Who runs the network the visit came over, or empty.</param>
/// <param name="Device">What sort of device it was, which is <see cref="DeviceClass.Unknown"/> where nothing said.</param>
/// <param name="Browser">What it was read with, or empty.</param>
/// <param name="OperatingSystem">What that was running on, or empty.</param>
public readonly record struct VisitContext(
    string SendingSite,
    SourceChannel Channel,
    string CountryCode,
    string Town,
    string NetworkOwner,
    DeviceClass Device,
    string Browser,
    string OperatingSystem)
{
    /// <summary>An account that establishes nothing, which is a correct answer rather than a gap.</summary>
    public static VisitContext Nothing { get; } =
        new(string.Empty, SourceChannel.Direct, string.Empty, string.Empty, string.Empty, DeviceClass.Unknown, string.Empty, string.Empty);
}

/// <summary>
/// One control a visitor operated, as it appears inside a visit.
/// </summary>
/// <param name="Name">
/// What the control said. Written by whoever wrote the page, so it is data everywhere it travels
/// and never anything else. Empty where the site gave the control no name.
/// </param>
/// <param name="Control">What sort of thing it was.</param>
/// <param name="Target">
/// Where it pointed: a path on the same site, a host alone for anywhere else, and nothing at all
/// for an address to write to or ring.
/// </param>
/// <param name="TargetKind">What sort of place <paramref name="Target"/> describes.</param>
public readonly record struct VisitPress(
    string Name,
    ControlKind Control,
    string? Target,
    TargetKind TargetKind);

/// <summary>
/// Everyone seen on a site in the last stretch of minutes.
/// </summary>
/// <remarks>
/// How many there were is counted over the whole window and the list may be shorter than it, so a
/// reading that had room for a hundred of two hundred visitors still says two hundred rather than
/// quietly reporting the size of its own answer.
/// </remarks>
/// <param name="VisitorsSeen">How many visitors reported during the window.</param>
/// <param name="Visitors">The visitors, the one most recently active first.</param>
public sealed record LiveVisitors(int VisitorsSeen, ImmutableArray<LiveVisitor> Visitors)
{
    /// <summary>What a site nobody has been on answers with.</summary>
    public static LiveVisitors None { get; } = new(0, []);
}

/// <summary>
/// One visitor seen during the window, and everything known about their time in it.
/// </summary>
/// <remarks>
/// <para>
/// Two accounts of the same visitor, kept apart on purpose. <see cref="Evidence"/> is the closed
/// set the detection engine reasons about and nothing else may be added to it;
/// <see cref="Context"/> is what a reader is shown and the engine never sees. The split is the same
/// one the classifier works to, and it is what stops a detector reaching a conclusion from where
/// somebody lives.
/// </para>
/// <para>
/// The evidence is what the window holds rather than what the whole visit will eventually hold, so
/// a conclusion drawn from it is only safe to show where more evidence could not withdraw it. That
/// judgement is not made here.
/// </para>
/// </remarks>
/// <param name="VisitorKey">
/// The visitor's derived identity, after the two halves of the measurement have been folded onto
/// one key. It names a visitor rather than a person and rotates daily; it is a handle for asking a
/// second question about the same visitor, and is never shown.
/// </param>
/// <param name="Evidence">Everything the engine is allowed to reason about, over this window.</param>
/// <param name="Context">What can be said about the visitor to a reader, which may be nothing.</param>
/// <param name="CurrentPath">
/// The page the visitor was on most recently. Written by whoever is visiting the site, so it is
/// shown as text and never followed.
/// </param>
/// <param name="ConfirmedOperator">
/// The company that vouches for the address the visitor arrived from, or empty where none does.
/// Settled at ingest against what the company publishes about its own machines, so it is the one
/// thing here that cannot be withdrawn by anything the visitor does next.
/// </param>
/// <param name="AutonomousSystem">
/// The routing number of the network the visitor arrived over, or nought where nothing resolved
/// one. Carried beside the owner's name because the number outlives the names its holders trade
/// under, which is what makes it the thing to match on.
/// </param>
public readonly record struct LiveVisitor(
    string VisitorKey,
    SessionEvidence Evidence,
    VisitContext Context,
    string CurrentPath,
    string ConfirmedOperator,
    long AutonomousSystem);

/// <summary>
/// One minute of a site's reading.
/// </summary>
/// <param name="Start">The minute began here, by the collector's own clock.</param>
/// <param name="PageViews">How many pages were delivered in it.</param>
public readonly record struct LiveMinute(DateTimeOffset Start, long PageViews);

/// <summary>
/// One page being read.
/// </summary>
/// <param name="Path">
/// The address, as it was asked for. Written by whoever is visiting the site, so it is shown as
/// text and never followed.
/// </param>
/// <param name="PageViews">How many times it was delivered during the window.</param>
/// <param name="Visitors">How many separate visitors were on it.</param>
public readonly record struct LivePage(string Path, long PageViews, long Visitors);
