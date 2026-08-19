namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// The complete vocabulary of questions that may be asked of the telemetry store.
/// </summary>
/// <remarks>
/// <para>
/// The constructor is <c>private protected</c>, so the cases live here and are added here. The
/// SQL compiler in the ClickHouse infrastructure project pattern-matches over them and builds
/// statements from a static identifier allow-list with every value bound as a parameter, which
/// means there is no code path anywhere that turns caller-supplied text into SQL. A question the
/// compiler has not been taught produces no statement at all: it throws.
/// </para>
/// <para>
/// That property carries more weight here than in an ordinary application, because this
/// product's own dataset is attacker-controlled text — user agents, referrers, and the URL
/// paths crawlers ask for. It is also what makes the natural-language layer safe to build
/// later: the model chooses <em>which</em> of these questions to ask and never how to
/// execute one.
/// </para>
/// </remarks>
public abstract record AnalyticsQuery
{
    private protected AnalyticsQuery(TimeRange range)
    {
        Range = range;
    }

    /// <summary>The window the question is asked over.</summary>
    public TimeRange Range { get; }
}

/// <summary>
/// Headline totals for a site over a window.
/// </summary>
/// <param name="Range">The window to summarise.</param>
public sealed record OverviewQuery(TimeRange Range) : AnalyticsQuery(Range);

/// <summary>
/// A single metric bucketed over time.
/// </summary>
/// <param name="Range">The window to cover.</param>
/// <param name="Granularity">Bucket size.</param>
/// <param name="Metric">Which metric to bucket.</param>
public sealed record TimeSeriesQuery(TimeRange Range, TimeGranularity Granularity, TimeSeriesMetric Metric)
    : AnalyticsQuery(Range);

/// <summary>Bucket size for a time series.</summary>
public enum TimeGranularity
{
    /// <summary>One bucket per hour.</summary>
    Hour = 1,

    /// <summary>One bucket per day, in the site's configured time zone.</summary>
    Day = 2,
}

/// <summary>Which metric a time series reports.</summary>
public enum TimeSeriesMetric
{
    /// <summary>Number of page views.</summary>
    PageViews = 1,

    /// <summary>Number of distinct visitor keys observed.</summary>
    Visitors = 2,
}

/// <summary>
/// One slice of the pages a site's traffic went to, busiest first.
/// </summary>
/// <remarks>
/// <para>
/// Counted as pages delivered rather than as reports received, on the same terms as
/// <see cref="OverviewQuery"/>, so a share taken against the headline total is a share of the
/// same arithmetic rather than of a second, differently-derived number.
/// </para>
/// <para>
/// Every address the window holds is reachable by asking for successive slices. The ordering is
/// total — busiest first, and the address itself breaks a tie — so a page of results does not
/// shuffle beneath somebody moving through them.
/// </para>
/// </remarks>
public sealed record SitePagesQuery : AnalyticsQuery
{
    /// <summary>
    /// Most pages any one question may ask for.
    /// </summary>
    /// <remarks>
    /// A documentation site has thousands of addresses and every one of them is a group in the
    /// store. This bounds one answer, not the work behind it, which is why the window is bounded
    /// separately and why the whole list is reached a slice at a time rather than at once.
    /// </remarks>
    public const int MostPages = 100;

    /// <summary>Asks for a slice of the pages in a window.</summary>
    /// <param name="range">The window to count over.</param>
    /// <param name="limit">How many pages to return, at most <see cref="MostPages"/>.</param>
    /// <param name="offset">How many of the busiest pages to pass over first.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds, or the offset is negative.</exception>
    public SitePagesQuery(TimeRange range, int limit, int offset = 0)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostPages);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Limit = limit;
        Offset = offset;
    }

    /// <summary>How many pages to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the busiest pages to pass over first.</summary>
    public int Offset { get; }
}

/// <summary>
/// Visits grouped by what the engine concluded generated them.
/// </summary>
/// <remarks>
/// Answered from stored verdicts rather than from raw activity, because what generated a visit is
/// a property of the whole visit and is not knowable one request at a time. Only visits that have
/// been judged appear, so a window that reaches into the last half-hour reports less than the
/// headline totals do — which the interface states rather than papers over.
/// </remarks>
/// <param name="Range">The window to group over, by when each visit began.</param>
public sealed record TrafficBreakdownQuery(TimeRange Range) : AnalyticsQuery(Range);

/// <summary>
/// Individual visits with the evidence behind each verdict, newest first.
/// </summary>
public sealed record JudgedSessionsQuery : AnalyticsQuery
{
    /// <summary>
    /// Most visits any one question may ask for.
    /// </summary>
    /// <remarks>
    /// Each visit carries its whole evidence list, so this bounds the answer's size rather than
    /// merely its length. Nobody reads five hundred visits at once either.
    /// </remarks>
    public const int MostSessions = 500;

    /// <summary>Asks for the most recent judged visits in a window.</summary>
    /// <param name="range">The window to look in, by when each visit began.</param>
    /// <param name="limit">How many visits to return, at most <see cref="MostSessions"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is not between one and the maximum.</exception>
    public JudgedSessionsQuery(TimeRange range, int limit)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostSessions);

        Limit = limit;
    }

    /// <summary>How many visits to return.</summary>
    public int Limit { get; }
}
