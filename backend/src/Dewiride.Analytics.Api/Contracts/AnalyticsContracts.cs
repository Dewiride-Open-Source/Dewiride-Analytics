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
/// One slice of where a website's visitors came from over a window, busiest first.
/// </summary>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Grouping">What each row stands for: <c>site</c> or <c>page</c>.</param>
/// <param name="Visitors">Visitors across every source in the window, not only this slice.</param>
/// <param name="TotalSources">How many sources the window holds, so a slice can say what it is part of.</param>
/// <param name="MostVisitors">The busiest source's count, which every bar on the list is drawn against.</param>
/// <param name="Sources">The slice itself.</param>
public sealed record SourcesResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string Grouping,
    long Visitors,
    long TotalSources,
    long MostVisitors,
    IReadOnlyList<SourceRow> Sources);

/// <summary>
/// One source and how many of a window's visitors it sent.
/// </summary>
/// <param name="Source">
/// The sending site's address, or that address followed by the path of the sending page. Empty
/// when the arrival named nowhere at all — which is reported rather than dropped, because on most
/// websites it is the largest share of the audience.
/// </param>
/// <param name="Site">
/// The sending site's address on its own, so a page row can be shown as belonging somewhere
/// without the screen having to take the address apart. Empty on the row for arrivals that named
/// nowhere.
/// </param>
/// <param name="Visitors">Distinct visitors, counted on the same daily terms as the headline.</param>
/// <param name="PageViews">Pages those visitors went on to be delivered.</param>
public sealed record SourceRow(string Source, string Site, long Visitors, long PageViews);

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
/// Judged visits over a window, counted by what generated them, bucket by bucket.
/// </summary>
/// <remarks>
/// A table rather than a list of its own cells: every entry in <paramref name="Groups"/> holds one
/// count per bucket, in the order <paramref name="Buckets"/> names them. A category the window never
/// held is left out altogether rather than reported as a run of zeroes.
/// </remarks>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Granularity">How wide each bucket is: <c>hour</c> or <c>day</c>.</param>
/// <param name="CompleteTo">
/// The instant judging has finished up to. A visit is judged once it has been silent long enough to
/// have ended, so buckets reaching past this hold fewer visits than they eventually will — which a
/// reader has to be told rather than left to infer from a line that falls away at the end.
/// </param>
/// <param name="Buckets">Where each bucket begins, oldest first.</param>
/// <param name="Groups">One entry per category the window held.</param>
public sealed record TrafficSeriesResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string Granularity,
    DateTimeOffset CompleteTo,
    IReadOnlyList<DateTimeOffset> Buckets,
    IReadOnlyList<TrafficCategorySeries> Groups);

/// <summary>
/// One category's counts across every bucket of a window.
/// </summary>
/// <param name="Category">What generated these visits.</param>
/// <param name="Sessions">How many visits began in each bucket.</param>
/// <param name="PageViews">How many pages those visits asked for, bucket by bucket.</param>
public sealed record TrafficCategorySeries(
    string Category,
    IReadOnlyList<long> Sessions,
    IReadOnlyList<long> PageViews);

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
/// How many judged visits the window holds, across every slice, and after anything the caller
/// narrowed to. Counted over the whole window, so the rows returned need not add up to it — and
/// counted over the narrowed set, so a list can say how far through the visits it is showing
/// somebody has read.
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
/// What each detail of a period's judged visits held, so a reader is offered what is there.
/// </summary>
/// <remarks>
/// <para>
/// Every list here describes the whole period rather than whatever else the caller has narrowed
/// to, so one question has one answer however a visit list is being read. The commonest values
/// first, and only as many of each as somebody could reach the bottom of.
/// </para>
/// <para>
/// Among the free-text details an empty value is a value rather than a gap: it counts the visits
/// nothing could be established about, which is a fair thing to ask to see and a different thing
/// from asking to see all of them.
/// </para>
/// </remarks>
/// <param name="From">Inclusive start of the period, by when each visit began.</param>
/// <param name="To">Exclusive end of the period.</param>
/// <param name="Devices">Kinds of device the visits were made on.</param>
/// <param name="SourceKinds">Kinds of place the visits were sent from.</param>
/// <param name="Browsers">Browsers the visits were made with.</param>
/// <param name="Systems">The systems those browsers were running on.</param>
/// <param name="Countries">Countries the visits arrived from, as two-letter codes.</param>
/// <param name="Towns">Towns and cities within them.</param>
/// <param name="Networks">Who runs the networks the visits arrived over.</param>
/// <param name="Sources">The sites that sent the visits.</param>
/// <param name="EntryPages">The pages the visits began on.</param>
public sealed record VisitFacetsResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<VisitDetailRow> Devices,
    IReadOnlyList<VisitDetailRow> SourceKinds,
    IReadOnlyList<VisitDetailRow> Browsers,
    IReadOnlyList<VisitDetailRow> Systems,
    IReadOnlyList<VisitDetailRow> Countries,
    IReadOnlyList<VisitDetailRow> Towns,
    IReadOnlyList<VisitDetailRow> Networks,
    IReadOnlyList<VisitDetailRow> Sources,
    IReadOnlyList<VisitDetailRow> EntryPages);

/// <summary>
/// One value a detail held, and how many of the period's judged visits held it.
/// </summary>
/// <remarks>
/// Counted per visit rather than per report, because a visit is what the list this narrows is made
/// of: a value offered as four hundred that handed back ninety would be a promise the product
/// could not keep.
/// </remarks>
/// <param name="Value">The value, spelled exactly as it must be spelled to narrow the list to it.</param>
/// <param name="Visits">How many of the period's judged visits held it.</param>
public sealed record VisitDetailRow(string Value, long Visits);

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
/// One visit: who it was, and what it did in the order it did it.
/// </summary>
/// <param name="Visit">The visit this describes, as it was asked for.</param>
/// <param name="Context">What could be established about the visitor, which may be nothing.</param>
/// <param name="Steps">
/// The steps, oldest first. Empty where the identity names no visit on this website — including
/// where the activity behind it has passed out of retention.
/// </param>
public sealed record VisitJourneyResponse(
    string Visit,
    VisitContextResponse Context,
    IReadOnlyList<VisitJourneyStep> Steps);

/// <summary>
/// What could be established about the visitor behind one visit.
/// </summary>
/// <remarks>
/// Every field is empty rather than absent where nothing established it. A site behind something
/// that does not pass the visitor's address along places nobody, and a visit only its own server
/// saw carries no browser — both are answers, and the dashboard says so in words rather than
/// leaving a gap the reader has to interpret.
/// </remarks>
/// <param name="Source">
/// The site that sent them, named where it is catalogued and left as its own address where it is
/// not. Empty where the browser named nowhere.
/// </param>
/// <param name="Kind">What sort of thing that was: a search engine, an assistant, a social network, or a link.</param>
/// <param name="CountryCode">Two-letter country code, or empty.</param>
/// <param name="Town">
/// Town or city, spelled as the free geolocation data spells it. An estimate from the visitor's
/// network rather than a position from their device.
/// </param>
/// <param name="Network">Who runs the network the visit came over, or empty.</param>
/// <param name="Device">Phone, tablet, computer, something else, or not known.</param>
/// <param name="Browser">What it was read with, or empty.</param>
/// <param name="System">What that was running on, or empty.</param>
public sealed record VisitContextResponse(
    string Source,
    string Kind,
    string CountryCode,
    string Town,
    string Network,
    string Device,
    string Browser,
    string System);

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

/// <summary>
/// What is happening on a site at this moment.
/// </summary>
/// <remarks>
/// <para>
/// The one answer in this product that carries no window a caller chose. What "now" is comes from
/// the engine's own clock, and how far back it reaches is what counts as a visitor still being here
/// — so there is nothing about the present moment for a caller to name, and a screen that renews
/// itself asks the same question every time.
/// </para>
/// <para>
/// It says how many visitors there have been and lists as many of them as one answer carries. The
/// two are separate figures on purpose: a sweep putting three hundred visitors on a site in ten
/// minutes must be reported as three hundred rather than as however long the list was allowed to be.
/// </para>
/// </remarks>
/// <param name="At">
/// The moment the reading was taken, by the engine's clock. Everything about how long ago something
/// happened is measured against this rather than against the reader's own clock, which on a machine
/// that is an hour out would otherwise report visitors arriving in the future.
/// </param>
/// <param name="From">Where the stretch of minutes it covers begins.</param>
/// <param name="VisitorsSeen">How many visitors reported in that stretch, counted over all of them.</param>
/// <param name="Visitors">Those this answer carried, the one most recently active first.</param>
/// <param name="Minutes">Every minute the stretch covers, oldest first, including the empty ones.</param>
/// <param name="Pages">The pages being read, busiest first.</param>
public sealed record LiveResponse(
    DateTimeOffset At,
    DateTimeOffset From,
    int VisitorsSeen,
    IReadOnlyList<LiveVisitorSummary> Visitors,
    IReadOnlyList<LiveMinuteRow> Minutes,
    IReadOnlyList<LivePageRow> Pages);

/// <summary>
/// One visitor who has been on the site in the last stretch of minutes.
/// </summary>
/// <remarks>
/// A visitor rather than a visit. A visit ends when it has been quiet long enough, and a report
/// about a page belongs to the visit that page was arrived at in however long the silence before
/// it — so a tab dismissed the next morning reopens a visit whose reader left hours ago, and a
/// reader with an old tab open has two visits running at once. Neither is somebody who is here.
/// </remarks>
/// <param name="Visitor">
/// Handle for asking a second question about the same visitor. Derived, rotates daily, and names a
/// visitor rather than a person; it is never shown to anybody.
/// </param>
/// <param name="FirstSeen">The first report from them inside the stretch.</param>
/// <param name="LastSeen">The last.</param>
/// <param name="PageCount">How many pages they have been on during it.</param>
/// <param name="CurrentPath">
/// The page they were on most recently. Written by whoever is visiting the site, so it reaches a
/// screen as text and is never followed.
/// </param>
/// <param name="Category">
/// What they are, where that can be settled from evidence nothing they do next can withdraw, and
/// <see langword="null"/> otherwise — which is the answer for most people and is not a gap.
/// </param>
/// <param name="Strength">How much weight stands behind that, or nothing where there is no verdict.</param>
/// <param name="Ruleset">Which set of detection rules reached it, or nothing.</param>
/// <param name="Supporting">The evidence behind it, or nothing.</param>
/// <param name="Contradicting">The evidence that pointed the other way, kept and shown.</param>
/// <param name="Operator">
/// The company that vouches for the address they arrived from, or empty where none does.
/// </param>
/// <param name="Network">The routing number of the network they arrived over, or nought.</param>
/// <param name="Context">What can be said about them to a reader, which may be nothing.</param>
public sealed record LiveVisitorSummary(
    string Visitor,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    int PageCount,
    string CurrentPath,
    string? Category,
    string? Strength,
    string? Ruleset,
    IReadOnlyList<VisitReason> Supporting,
    IReadOnlyList<VisitReason> Contradicting,
    string Operator,
    long Network,
    VisitContextResponse Context);

/// <summary>
/// One minute of a site's reading.
/// </summary>
/// <param name="Start">The minute began here.</param>
/// <param name="PageViews">How many pages were delivered in it.</param>
public sealed record LiveMinuteRow(DateTimeOffset Start, long PageViews);

/// <summary>
/// One page being read.
/// </summary>
/// <param name="Path">
/// The address, as it was asked for. Written by whoever is visiting the site, so it reaches a screen
/// as text and is never followed.
/// </param>
/// <param name="PageViews">How many times it was delivered during the stretch.</param>
/// <param name="Visitors">How many separate visitors were on it.</param>
public sealed record LivePageRow(string Path, long PageViews, long Visitors);

/// <summary>
/// What one visitor who is on the site now has been doing, in the order they did it.
/// </summary>
/// <remarks>
/// It carries no account of who the visitor is, and that is deliberate rather than an omission.
/// Where they are, what they are reading on and who sent them travel on the row this was opened
/// from, settled over the same minutes; answering the question a second time moments later would be
/// a second answer free to disagree with the first.
/// </remarks>
/// <param name="Visitor">The visitor this describes, as the reading of who is here named them.</param>
/// <param name="At">
/// The moment the reading was taken, by the engine's clock, so how long ago each step happened is
/// measured against the same clock the steps were measured against.
/// </param>
/// <param name="Steps">
/// The steps, oldest first. Empty where the key names nobody the last stretch of minutes holds —
/// which is what a visitor who has gone looks like, rather than a failure.
/// </param>
public sealed record LiveTrailResponse(
    string Visitor,
    DateTimeOffset At,
    IReadOnlyList<VisitJourneyStep> Steps);
