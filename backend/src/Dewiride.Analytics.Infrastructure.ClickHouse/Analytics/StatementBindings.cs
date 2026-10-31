using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// The values the statements of both compilers that count activity bind, written once.
/// </summary>
/// <remarks>
/// The dashboard's compiler and the one that counts each month for keeping write the same
/// arithmetic, and a count kept for a month is only the overview of that month if both bind the
/// same values to the same placeholders. What differs is only where the site comes from: an
/// authorisation decision for one, the control plane for the other.
/// </remarks>
internal static class StatementBindings
{
    /// <summary>Placeholder the site identifier binds to.</summary>
    public const string SiteId = "site_id";

    /// <summary>Placeholder the first instant of the window binds to.</summary>
    public const string From = "from_ms";

    /// <summary>Placeholder the instant after the window binds to.</summary>
    public const string To = "to_ms";

    /// <summary>Placeholder the website's reporting zone binds to.</summary>
    public const string TimeZone = "time_zone";

    /// <summary>Placeholder how far before a window verdicts are read binds to.</summary>
    public const string LongestVisitSeconds = "longest_visit_seconds";

    /// <summary>
    /// The site, and the window to count over.
    /// </summary>
    /// <param name="siteId">The site.</param>
    /// <param name="range">The window.</param>
    /// <returns>The three values, in the order the statements declare them.</returns>
    public static QueryParameter[] Window(Guid siteId, TimeRange range) =>
    [
        new(SiteId, siteId),
        new(From, range.From.ToUnixTimeMilliseconds()),
        new(To, range.To.ToUnixTimeMilliseconds()),
    ];

    /// <summary>
    /// How far either side of a period a statement has to read to see whole the visits that cross
    /// its edges.
    /// </summary>
    /// <remarks>
    /// Bound rather than written into the statement so that it reads as what it is — a length of
    /// time — beside the timeout it sits next to. Unlike that timeout it is not a setting: it
    /// follows from how a visitor key is built, and <see cref="VisitorKeys.LongestVisit"/> is where
    /// that is explained.
    /// </remarks>
    /// <returns>The value.</returns>
    public static QueryParameter LongestVisit() =>
        new(LongestVisitSeconds, (long)VisitorKeys.LongestVisit.TotalSeconds);

    /// <summary>
    /// How far before the window verdicts are read, bound only where a statement reads them.
    /// </summary>
    /// <remarks>
    /// Conditional because a statement asked about everybody names no verdict, and a value bound to
    /// a placeholder no statement names is a narrowing that silently does nothing.
    /// </remarks>
    /// <param name="population">Who the statement is asked about.</param>
    /// <returns>The value, or nothing.</returns>
    public static QueryParameter[] Population(Population population) =>
        population == Application.Analytics.Population.People ? [LongestVisit()] : [];
}
