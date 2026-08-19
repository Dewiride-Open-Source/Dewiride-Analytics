namespace Dewiride.Analytics.Api.Contracts;

/// <summary>
/// One of the caller's sites.
/// </summary>
/// <param name="Id">Identifier of the site, and the value in its tracking snippet.</param>
/// <param name="Domain">Primary hostname.</param>
/// <param name="DisplayName">Name shown in the dashboard.</param>
/// <param name="TimeZoneId">IANA time zone its days are counted in.</param>
/// <param name="Role">
/// What the caller may do with it: <c>viewer</c>, <c>editor</c> or <c>owner</c>.
/// </param>
public sealed record SiteSummary(
    Guid Id,
    string Domain,
    string DisplayName,
    string TimeZoneId,
    string Role);

/// <summary>
/// Headline totals for a site over a window.
/// </summary>
/// <param name="From">Inclusive start of the window that was counted.</param>
/// <param name="To">Exclusive end of the window that was counted.</param>
/// <param name="PageViews">Page views observed.</param>
/// <param name="Visitors">
/// Distinct visitor keys observed. The key is rebuilt daily, so a window longer than a day counts
/// a returning reader once per day rather than once in total. The interface says so beside the
/// number rather than presenting it as a count of people.
/// </param>
/// <param name="Events">Reports of every kind, including engagement and exit.</param>
public sealed record OverviewResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    long PageViews,
    long Visitors,
    long Events);

/// <summary>
/// One metric counted in buckets across a window.
/// </summary>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Metric">Which metric was counted.</param>
/// <param name="Granularity">How wide each bucket is.</param>
/// <param name="Points">The buckets, oldest first, with empty ones present and zeroed.</param>
public sealed record SeriesResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string Metric,
    string Granularity,
    IReadOnlyList<SeriesPoint> Points);

/// <summary>
/// One bucket of a series.
/// </summary>
/// <param name="BucketStart">Inclusive start of the bucket.</param>
/// <param name="Value">The metric's value inside it.</param>
public readonly record struct SeriesPoint(DateTimeOffset BucketStart, long Value);

/// <summary>
/// One slice of the pages a website's traffic went to over a window, busiest first.
/// </summary>
/// <remarks>
/// The whole list is read a slice at a time: ask again with <c>offset</c> advanced by as many
/// pages as were returned. The ordering is total, so successive slices neither repeat a page nor
/// skip one, and an offset past the end answers with an empty list rather than an error.
/// </remarks>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="PageViews">
/// Pages delivered across the whole window, counting every address outside this slice. Shares are
/// taken against this, so the rows returned need not add up to it.
/// </param>
/// <param name="TotalPaths">How many addresses had traffic in the window, across every slice.</param>
/// <param name="MostPageViews">
/// Pages delivered at the single busiest address in the window. Lets a slice be drawn to the same
/// scale as every other one.
/// </param>
/// <param name="Pages">The slice, busiest first.</param>
public sealed record PagesResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    long PageViews,
    long TotalPaths,
    long MostPageViews,
    IReadOnlyList<PageRow> Pages);

/// <summary>
/// One page and how much of a window's traffic went to it.
/// </summary>
/// <param name="Path">
/// Path of the page, as it was asked for. Written by whoever made the request and never
/// interpreted: it is shown as text and is not a link the dashboard follows.
/// </param>
/// <param name="PageViews">Pages delivered at this path.</param>
/// <param name="Visitors">Distinct visitors that asked for it, counted on the same daily terms as the headline.</param>
public sealed record PageRow(string Path, long PageViews, long Visitors);

/// <summary>
/// One slice of the places a website's audience was in over a window, busiest first.
/// </summary>
/// <remarks>
/// Read a slice at a time on the same terms as <see cref="PagesResponse"/>: ask again with
/// <c>offset</c> advanced, and an offset past the end answers with an empty list rather than an
/// error.
/// </remarks>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Grouping">What each row stands for: <c>country</c> or <c>town</c>.</param>
/// <param name="Visitors">
/// Visitors across the whole window, including those outside this slice and those whose place
/// could not be established. Shares are taken against this.
/// </param>
/// <param name="TotalPlaces">How many places the window holds, across every slice.</param>
/// <param name="MostVisitors">
/// Visitors in the single busiest place, so a slice can be drawn to the same scale as every
/// other one.
/// </param>
/// <param name="Places">The slice, busiest first.</param>
public sealed record LocationsResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string Grouping,
    long Visitors,
    long TotalPlaces,
    long MostVisitors,
    IReadOnlyList<LocationRow> Places);

/// <summary>
/// One place and how much of a window's audience was there.
/// </summary>
/// <param name="Place">
/// The country's two-letter code, or the town's name. Empty when the visitor's address resolved
/// to nothing, which is reported as a place rather than dropped: an installation that cannot see
/// its visitors' addresses should be able to tell.
/// </param>
/// <param name="CountryCode">
/// Which country the row is in, so two towns of the same name in different countries can be told
/// apart. Empty when the country itself did not resolve.
/// </param>
/// <param name="Visitors">Distinct visitors, counted on the same daily terms as the headline.</param>
/// <param name="PageViews">Pages those visitors were delivered.</param>
public sealed record LocationRow(string Place, string CountryCode, long Visitors, long PageViews);

/// <summary>
/// How much of a website's audience was on each kind of device over a window.
/// </summary>
/// <remarks>
/// Unpaged: the kinds are a closed set of five and the whole answer is always a whole answer.
/// </remarks>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Visitors">
/// Visitors behind the whole answer. Shares are taken against this, and because every visitor is
/// on exactly one row the rows add up to it.
/// </param>
/// <param name="Devices">The kinds that were seen, commonest first.</param>
public sealed record DevicesResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    long Visitors,
    IReadOnlyList<DeviceRow> Devices);

/// <summary>
/// One kind of device and how much of a window's audience was on it.
/// </summary>
/// <param name="Kind">
/// <c>phone</c>, <c>tablet</c>, <c>desktop</c>, <c>other</c>, or <c>unknown</c> where nothing
/// could be established — which is a row like any other rather than an omission.
/// </param>
/// <param name="Visitors">Distinct visitors, counted on the same daily terms as the headline.</param>
/// <param name="PageViews">Pages those visitors were delivered.</param>
public sealed record DeviceRow(string Kind, long Visitors, long PageViews);

/// <summary>
/// One slice of the software a website's audience used over a window, commonest first.
/// </summary>
/// <remarks>
/// Read a slice at a time on the same terms as <see cref="PagesResponse"/>.
/// </remarks>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Grouping">What each row stands for: <c>browser</c> or <c>system</c>.</param>
/// <param name="Visitors">
/// Visitors across the whole window, including those outside this slice and those whose software
/// could not be established. Shares are taken against this.
/// </param>
/// <param name="TotalNames">How many distinct names the window holds, across every slice.</param>
/// <param name="MostVisitors">
/// Visitors on the single commonest name, so a slice can be drawn to the same scale as every
/// other one.
/// </param>
/// <param name="Names">The slice, commonest first.</param>
public sealed record SoftwareResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string Grouping,
    long Visitors,
    long TotalNames,
    long MostVisitors,
    IReadOnlyList<SoftwareRow> Names);

/// <summary>
/// One piece of software and how much of a window's audience used it.
/// </summary>
/// <param name="Name">
/// The browser family or the operating system, spelled by the engine's own catalogue rather than
/// by the client. Empty where nothing could be established.
/// </param>
/// <param name="Visitors">Distinct visitors, counted on the same daily terms as the headline.</param>
/// <param name="PageViews">Pages those visitors were delivered.</param>
public sealed record SoftwareRow(string Name, long Visitors, long PageViews);

/// <summary>
/// One specific reason a request was refused.
/// </summary>
/// <remarks>
/// The code is what the dashboard looks up in its own catalogue to write a sentence somebody can
/// act on; the description is what it falls back to for a code it has never seen, because hiding
/// the only explanation behind a generic sentence helps nobody.
/// </remarks>
/// <param name="Code">Names the reason. Stable, and never shown to anybody.</param>
/// <param name="Description">The reason in words, for a reader whose dashboard has none.</param>
public sealed record RefusedReason(string Code, string Description);

/// <summary>
/// A website somebody is asking to measure.
/// </summary>
public sealed record AddSiteRequest
{
    /// <summary>
    /// The website's address, such as <c>blog.example.com</c>. Normalised where the site is built.
    /// </summary>
    public string? Domain { get; init; }

    /// <summary>IANA time zone its days should be counted in.</summary>
    public string? TimeZoneId { get; init; }
}

/// <summary>
/// What a website's visitors operated over a window, most pressed first.
/// </summary>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Grouping">What the rows are gathered by, echoed back so a slow answer can be told apart.</param>
/// <param name="Presses">Presses across the whole window, which every share is taken against.</param>
/// <param name="TotalControls">How many distinct rows the window holds, across every slice.</param>
/// <param name="MostPresses">
/// Presses on the single most pressed row, so a slice can be drawn to the same scale as every
/// other one.
/// </param>
/// <param name="Controls">The slice, most pressed first.</param>
public sealed record ActionsResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string Grouping,
    long Presses,
    long TotalControls,
    long MostPresses,
    IReadOnlyList<ActionRow> Controls);

/// <summary>
/// One thing a window's visitors operated, and how often.
/// </summary>
/// <param name="Name">
/// The control's own name, or the host a press led to. Written by whoever wrote the page, so it
/// is data everywhere it travels and never anything else. Empty where a site gave its control no
/// name at all.
/// </param>
/// <param name="Control">
/// What sort of thing it was, as an identifier the dashboard looks up in its own catalogue. Always
/// <c>unknown</c> where the row is a place rather than a control.
/// </param>
/// <param name="Presses">How many times it was operated.</param>
/// <param name="Visitors">How many distinct visitors operated it.</param>
public sealed record ActionRow(string Name, string Control, long Presses, long Visitors);

/// <summary>
/// Judged visits over a window, grouped by what generated them.
/// </summary>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Sessions">Visits that have been judged. Visits still in progress are not counted.</param>
/// <param name="PageViews">Pages those visits asked for between them.</param>
/// <param name="Groups">The groups, busiest first.</param>
public sealed record TrafficResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    long Sessions,
    long PageViews,
    IReadOnlyList<TrafficGroup> Groups);

/// <summary>
/// One group of visits that reached the same conclusion with the same weight behind it.
/// </summary>
/// <param name="Category">What generated them.</param>
/// <param name="Strength">
/// How much weight stood behind that conclusion. Reported alongside the category rather than
/// folded into it, because a hundred visits called a crawler on weak evidence is a different
/// statement from a hundred called one on strong evidence.
/// </param>
/// <param name="Sessions">How many visits.</param>
/// <param name="PageViews">How many pages they asked for.</param>
public sealed record TrafficGroup(string Category, string Strength, long Sessions, long PageViews);

/// <summary>
/// One slice of the individual judged visits over a window, newest first.
/// </summary>
/// <remarks>
/// Read a slice at a time on the same terms as <see cref="PagesResponse"/>: ask again with a
/// larger offset for the next one. Every visit carries its whole evidence list, so the slice bounds
/// the answer's size rather than merely its length.
/// </remarks>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="TotalVisits">
/// How many judged visits the window holds, across every slice. Counted over the whole window, so
/// the rows returned need not add up to it.
/// </param>
/// <param name="Visits">The slice, newest first.</param>
public sealed record VisitsResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    long TotalVisits,
    IReadOnlyList<VisitSummary> Visits);

/// <summary>
/// One judged visit and why it was judged that way.
/// </summary>
/// <param name="Id">Identity of the visit.</param>
/// <param name="StartedAt">When it began.</param>
/// <param name="EndedAt">When the last activity on it was seen.</param>
/// <param name="PageCount">How many pages it asked for.</param>
/// <param name="Surfaces">Which capture surfaces saw it.</param>
/// <param name="Category">What generated it.</param>
/// <param name="Strength">How much weight stands behind that.</param>
/// <param name="IsProvisional">Whether the verdict was reached before the visit finished.</param>
/// <param name="Ruleset">Which set of detection rules produced the verdict.</param>
/// <param name="Supporting">The evidence behind the verdict.</param>
/// <param name="Contradicting">
/// The evidence that pointed the other way, kept and shown rather than discarded.
/// </param>
public sealed record VisitSummary(
    string Id,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int PageCount,
    IReadOnlyList<string> Surfaces,
    string Category,
    string Strength,
    bool IsProvisional,
    string Ruleset,
    IReadOnlyList<VisitReason> Supporting,
    IReadOnlyList<VisitReason> Contradicting);

/// <summary>
/// One observation behind a verdict.
/// </summary>
/// <param name="Code">
/// Stable identifier for the observation. The sentence a reader sees is looked up from this in
/// their own language, so nothing here is prose.
/// </param>
/// <param name="Direction">Which way the observation points: human, automation, or neither.</param>
/// <param name="Weight">How much it counted, from nought to a hundred.</param>
/// <param name="Values">Values the sentence substitutes, such as how many pages were asked for.</param>
public sealed record VisitReason(
    string Code,
    string Direction,
    int Weight,
    IReadOnlyDictionary<string, string> Values);

/// <summary>
/// How a website's pages were actually read over a window.
/// </summary>
/// <remarks>
/// A <em>reading</em> is one visitor on one page. Only the browser tracker can observe any of
/// this, so <paramref name="Measured"/> is always reported beside <paramref name="Readings"/>:
/// every other figure here is taken over the measured ones alone, and a website measured only from
/// its own server answers with nothing measured rather than with nobody engaged.
/// </remarks>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Readings">Readings in the window, measured or not.</param>
/// <param name="Measured">Readings a browser reported progress for.</param>
/// <param name="MedianEngagedMs">
/// The middle measured reading's attention, in milliseconds — time the page was genuinely in front
/// of somebody rather than merely open. The middle rather than the mean, because a handful of very
/// long readings would otherwise describe an audience nobody in it resembles.
/// </param>
/// <param name="Interacted">Measured readings where a pointer or a key was used at all.</param>
/// <param name="Depths">How far down the page the measured readings got, in quarters.</param>
public sealed record EngagementResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    long Readings,
    long Measured,
    int MedianEngagedMs,
    long Interacted,
    DepthBands Depths);

/// <summary>
/// How far down a page a window's readings got, in quarters.
/// </summary>
/// <remarks>
/// Quarters rather than a finer division because the depth is itself an estimate: it is measured
/// against the document's height at the moment the reader stopped, and a page whose images arrive
/// late is a different height a second later.
/// </remarks>
/// <param name="Top">Readings that got less than a quarter of the way down.</param>
/// <param name="Quarter">Readings that reached a quarter but not half.</param>
/// <param name="Half">Readings that reached half but not three-quarters.</param>
/// <param name="Whole">Readings that reached three-quarters or more.</param>
public sealed record DepthBands(long Top, long Quarter, long Half, long Whole);

/// <summary>
/// One slice of a website's pages ranked by how they were read, rather than by how often.
/// </summary>
/// <remarks>
/// The whole list is read a slice at a time, on the same terms as the busiest-pages list. Only
/// pages at least one reading could be measured on appear at all: a page seen solely by a reporter
/// on the website's own server has nothing to say about how it was read.
/// </remarks>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Ranking">What the list was ordered by: <c>attention</c> or <c>depth</c>.</param>
/// <param name="TotalPages">How many pages could be measured at all, across every slice.</param>
/// <param name="LongestMedianEngagedMs">
/// The largest middle attention any page in the window held. Lets a slice be drawn to the same
/// scale as every other one.
/// </param>
/// <param name="Pages">The slice, leading the chosen ranking first.</param>
public sealed record PageEngagementResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string Ranking,
    long TotalPages,
    int LongestMedianEngagedMs,
    IReadOnlyList<PageEngagementRow> Pages);

/// <summary>
/// One page and how it was read.
/// </summary>
/// <param name="Path">
/// Path of the page, exactly as it was asked for. Written by whoever made the request, so it is
/// data everywhere it travels and never anything else.
/// </param>
/// <param name="Readings">Readings of this page that could be measured.</param>
/// <param name="MedianEngagedMs">The middle reading's attention, in milliseconds.</param>
/// <param name="MedianDepthPercent">How far down the middle reading got, as a percentage.</param>
/// <param name="Interacted">Readings where a pointer or a key was used at all.</param>
public sealed record PageEngagementRow(
    string Path,
    long Readings,
    int MedianEngagedMs,
    int MedianDepthPercent,
    long Interacted);

/// <summary>
/// How a window's finished visits were shaped.
/// </summary>
/// <remarks>
/// A visit is one reader's activity up to the first half-hour of silence. Only visits that had
/// finished when the question was asked are counted: one still under way has an unfinished page
/// count, and a handful of those would decide the answer on a quiet website.
/// </remarks>
/// <param name="From">Inclusive start of the window, by when each visit began.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Visits">Finished visits that began in the window.</param>
/// <param name="SinglePageVisits">
/// How many of them asked for exactly one page. A count rather than a rate, so that the share and
/// the number behind it are the same arithmetic wherever they are shown.
/// </param>
/// <param name="PageViews">Pages those visits asked for between them.</param>
public sealed record VisitTotalsResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    long Visits,
    long SinglePageVisits,
    long PageViews);

/// <summary>
/// One slice of the pages a window's visits began or ended on.
/// </summary>
/// <remarks>
/// Counted per visit rather than per page view: arriving somewhere happens once, however many
/// times the page is read afterwards.
/// </remarks>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Position">Which end of a visit was counted: <c>entry</c> or <c>exit</c>.</param>
/// <param name="TotalVisits">Finished visits across the whole window, which every share is taken against.</param>
/// <param name="TotalPaths">How many distinct pages the window holds at this end of a visit.</param>
/// <param name="MostVisits">
/// Visits at the single commonest page. Lets a slice be drawn to the same scale as every other one.
/// </param>
/// <param name="Pages">The slice, commonest first.</param>
public sealed record VisitPagesResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string Position,
    long TotalVisits,
    long TotalPaths,
    long MostVisits,
    IReadOnlyList<VisitPageRow> Pages);

/// <summary>
/// One page and how many visits began or ended on it.
/// </summary>
/// <param name="Path">
/// Path of the page, exactly as it was asked for. Written by whoever made the request, so it is
/// data everywhere it travels and never anything else.
/// </param>
/// <param name="Visits">Visits that began or ended there.</param>
public sealed record VisitPageRow(string Path, long Visits);

/// <summary>
/// What one visit did, in the order it did it.
/// </summary>
/// <param name="Visit">The visit this describes, as it was asked for.</param>
/// <param name="Steps">
/// The steps, oldest first. Empty where the identity names no visit on this website — including
/// where the activity behind it has passed out of retention.
/// </param>
public sealed record VisitJourneyResponse(string Visit, IReadOnlyList<VisitJourneyStep> Steps);

/// <summary>
/// One thing a visit did: arriving at a page, or operating a control on one.
/// </summary>
/// <remarks>
/// A page step is one arrival at one page, so a reader who comes back to an article later in the
/// same visit produces two steps rather than one with the readings added together. A press is a
/// step of its own, and carries the page it happened on.
/// </remarks>
/// <param name="At">When the step happened: the first report of an arrival, or the press itself.</param>
/// <param name="Path">
/// Path of the page, exactly as it was asked for. Written by whoever made the request, so it is
/// data everywhere it travels and never anything else.
/// </param>
/// <param name="StatusCode">
/// What the website answered with, where a reporter on the website's own server saw the request.
/// Absent where only the browser reported the page.
/// </param>
/// <param name="EngagedMs">
/// How long the page was genuinely in front of somebody, in milliseconds. Absent where no browser
/// watched the step, which is a different statement from a reader who left immediately.
/// </param>
/// <param name="DepthPercent">How far down the page the reader got, on the same terms.</param>
/// <param name="Press">
/// The control that was operated, where this step is a press rather than an arrival. Absent on an
/// arrival, which is what tells the two apart.
/// </param>
public sealed record VisitJourneyStep(
    DateTimeOffset At,
    string Path,
    int? StatusCode,
    int? EngagedMs,
    int? DepthPercent,
    VisitPressed? Press);

/// <summary>
/// One control a visitor operated, as it appears inside a visit.
/// </summary>
/// <param name="Name">
/// What the control said. Written by whoever wrote the page, so it is data everywhere it travels
/// and never anything else. Empty where the website gave the control no name.
/// </param>
/// <param name="Control">
/// What sort of thing it was, as an identifier the dashboard looks up in its own catalogue.
/// </param>
/// <param name="Target">
/// Where it pointed: a path on the same website, a host alone for anywhere else, and absent for an
/// address to write to or ring.
/// </param>
/// <param name="TargetKind">
/// What sort of place <paramref name="Target"/> describes, as an identifier the dashboard looks up
/// in its own catalogue.
/// </param>
public sealed record VisitPressed(string Name, string Control, string? Target, string TargetKind);
