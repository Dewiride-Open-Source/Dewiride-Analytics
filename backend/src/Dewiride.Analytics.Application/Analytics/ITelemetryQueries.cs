using System.Collections.Immutable;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
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

    /// <summary>Returns the pages one visit went through, in order.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">Which visit, and how many steps to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The steps, oldest first. Empty where the identity names no visit on this site.</returns>
    Task<ImmutableArray<VisitStep>> GetSiteVisitJourneyAsync(
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

    /// <summary>Returns individual judged visits with the evidence behind each verdict.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window and how many visits to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The visits, newest first.</returns>
    Task<IReadOnlyList<JudgedSession>> GetJudgedSessionsAsync(
        TenantScope scope,
        JudgedSessionsQuery query,
        CancellationToken cancellationToken);
}

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
