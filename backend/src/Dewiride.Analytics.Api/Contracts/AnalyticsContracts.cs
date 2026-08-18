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
/// Individual judged visits over a window.
/// </summary>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Exclusive end of the window.</param>
/// <param name="Visits">The visits, newest first.</param>
public sealed record VisitsResponse(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<VisitSummary> Visits);

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
