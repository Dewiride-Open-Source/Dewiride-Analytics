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
