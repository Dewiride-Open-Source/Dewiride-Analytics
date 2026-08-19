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
/// One slice of what a site's visitors operated, most pressed first.
/// </summary>
/// <remarks>
/// <para>
/// Answered from presses alone, with none of the reconciliation the page counts need. A press can
/// only be seen by something running in the visitor's own browser, so there is no second sighting
/// of it to recognise and nothing to fold together.
/// </para>
/// <para>
/// The ordering is total — most pressed first, then the name, then the kind — so successive slices
/// neither repeat a row nor skip one.
/// </para>
/// </remarks>
public sealed record SiteActionsQuery : AnalyticsQuery
{
    /// <summary>Most controls any one question may ask for.</summary>
    public const int MostControls = 100;

    /// <summary>Asks for a slice of what was operated in a window.</summary>
    /// <param name="range">The window to count over.</param>
    /// <param name="grouping">What the presses are gathered by.</param>
    /// <param name="limit">How many rows to return, at most <see cref="MostControls"/>.</param>
    /// <param name="offset">How many of the most pressed to pass over first.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The grouping is not one this product defines, the limit is outside its bounds, or the
    /// offset is negative.
    /// </exception>
    public SiteActionsQuery(TimeRange range, ActionGrouping grouping, int limit, int offset = 0)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostControls);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        if (!Enum.IsDefined(grouping))
        {
            throw new ArgumentOutOfRangeException(nameof(grouping));
        }

        Grouping = grouping;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>What the presses are gathered by.</summary>
    public ActionGrouping Grouping { get; }

    /// <summary>How many rows to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the most pressed to pass over first.</summary>
    public int Offset { get; }
}

/// <summary>
/// What a window's presses are gathered by.
/// </summary>
public enum ActionGrouping
{
    /// <summary>By the control itself: what it said, and what sort of thing it was.</summary>
    Control = 1,

    /// <summary>
    /// By the host a press led to, counting only the presses that led off the site. Where a press
    /// led on the site is answered by the pages themselves.
    /// </summary>
    Destination = 2,
}

/// <summary>
/// One slice of the places a site's traffic came from, busiest first.
/// </summary>
/// <remarks>
/// <para>
/// Counted per visitor rather than per page, because a place is a fact about people rather than
/// about pages: one reader in Pune who works through forty pages is one reader in Pune, and
/// ranking places by pages read would put whoever browses most at the top of a list that claims
/// to be about where an audience is.
/// </para>
/// <para>
/// Read a slice at a time on the same terms as <see cref="SitePagesQuery"/>, with the same total
/// ordering, so successive slices neither repeat a place nor skip one.
/// </para>
/// </remarks>
public sealed record SiteLocationsQuery : AnalyticsQuery
{
    /// <summary>Most places any one question may ask for.</summary>
    public const int MostPlaces = 100;

    /// <summary>Asks for a slice of the places in a window.</summary>
    /// <param name="range">The window to count over.</param>
    /// <param name="grouping">Whether to group by country or by town.</param>
    /// <param name="limit">How many places to return, at most <see cref="MostPlaces"/>.</param>
    /// <param name="offset">How many of the busiest places to pass over first.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds, or the offset is negative.</exception>
    public SiteLocationsQuery(TimeRange range, LocationGrouping grouping, int limit, int offset = 0)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostPlaces);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Grouping = grouping;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>What each row stands for.</summary>
    public LocationGrouping Grouping { get; }

    /// <summary>How many places to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the busiest places to pass over first.</summary>
    public int Offset { get; }
}

/// <summary>What one row of a place list stands for.</summary>
public enum LocationGrouping
{
    /// <summary>One row per country.</summary>
    Country = 1,

    /// <summary>
    /// One row per town.
    /// </summary>
    /// <remarks>
    /// Towns are an estimate: address ranges are allocated to networks rather than to streets, so
    /// the answer is frequently the nearest sizeable town rather than where the visitor is.
    /// </remarks>
    Town = 2,
}

/// <summary>
/// How many of a site's audience were on each kind of device.
/// </summary>
/// <remarks>
/// <para>
/// Unpaged, because the answer is a closed set of five and there is nothing to page through. That
/// is the whole difference between this question and <see cref="SiteSoftwareQuery"/>: a browser is
/// one of an open and growing list, and a device is one of a handful the engine can name.
/// </para>
/// <para>
/// Counted per visitor, on the same terms as <see cref="SiteLocationsQuery"/> and for the same
/// reason.
/// </para>
/// </remarks>
/// <param name="Range">The window to count over.</param>
public sealed record SiteDeviceKindsQuery(TimeRange Range) : AnalyticsQuery(Range);

/// <summary>
/// One slice of the browsers or the operating systems a site's audience used, commonest first.
/// </summary>
/// <remarks>
/// Read a slice at a time on the same terms as <see cref="SitePagesQuery"/>, with the same total
/// ordering, so successive slices neither repeat a name nor skip one. Open-ended in a way the
/// device kinds are not: browsers are released, renamed and forked, and the engine's catalogue
/// grows with them.
/// </remarks>
public sealed record SiteSoftwareQuery : AnalyticsQuery
{
    /// <summary>Most names any one question may ask for.</summary>
    public const int MostNames = 100;

    /// <summary>Asks for a slice of the software in a window.</summary>
    /// <param name="range">The window to count over.</param>
    /// <param name="grouping">Whether to group by browser or by operating system.</param>
    /// <param name="limit">How many names to return, at most <see cref="MostNames"/>.</param>
    /// <param name="offset">How many of the commonest names to pass over first.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds, or the offset is negative.</exception>
    public SiteSoftwareQuery(TimeRange range, SoftwareGrouping grouping, int limit, int offset = 0)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostNames);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Grouping = grouping;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>What each row stands for.</summary>
    public SoftwareGrouping Grouping { get; }

    /// <summary>How many names to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the commonest names to pass over first.</summary>
    public int Offset { get; }
}

/// <summary>What one row of a software list stands for.</summary>
public enum SoftwareGrouping
{
    /// <summary>One row per browser family, without its version.</summary>
    Browser = 1,

    /// <summary>One row per operating system, without its version.</summary>
    OperatingSystem = 2,
}

/// <summary>
/// How a window's pages were actually read, across the whole site.
/// </summary>
/// <remarks>
/// <para>
/// Unpaged, because it is one answer about one window rather than a list.
/// </para>
/// <para>
/// Counted per reading — one reader on one page — rather than per report, because a page reports
/// its progress several times over as it is read and every one of those reports carries a running
/// total rather than an instalment. The largest of them is what that reading was worth.
/// </para>
/// <para>
/// Only the browser half of the measurement can observe any of this: a reporter on a site's own
/// server sees a page leave the building and nothing after that. So the answer carries how many
/// readings could be measured alongside how many there were, and the two are never conflated. A
/// site measured only from its server has nothing to say here, which is a different statement from
/// a site whose readers did nothing.
/// </para>
/// </remarks>
/// <param name="Range">The window to count over.</param>
public sealed record SiteEngagementQuery(TimeRange Range) : AnalyticsQuery(Range);

/// <summary>
/// One slice of a site's pages ranked by how they were read rather than by how often.
/// </summary>
/// <remarks>
/// Read a slice at a time on the same terms as <see cref="SitePagesQuery"/>, with the same total
/// ordering. Only pages at least one reading could be measured on appear: a page seen solely by a
/// reporter on the site's own server has nothing to say about how it was read, and a row of noughts
/// beside it would say something quite different from nothing.
/// </remarks>
public sealed record SitePageEngagementQuery : AnalyticsQuery
{
    /// <summary>Most pages any one question may ask for.</summary>
    public const int MostPages = 100;

    /// <summary>Asks for a slice of the pages in a window.</summary>
    /// <param name="range">The window to count over.</param>
    /// <param name="ranking">What the list is ordered by.</param>
    /// <param name="limit">How many pages to return, at most <see cref="MostPages"/>.</param>
    /// <param name="offset">How many of the leading pages to pass over first.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds, or the offset is negative.</exception>
    public SitePageEngagementQuery(TimeRange range, EngagementRanking ranking, int limit, int offset = 0)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostPages);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Ranking = ranking;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>What the list is ordered by.</summary>
    public EngagementRanking Ranking { get; }

    /// <summary>How many pages to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the leading pages to pass over first.</summary>
    public int Offset { get; }
}

/// <summary>What a page-engagement list is ordered by.</summary>
public enum EngagementRanking
{
    /// <summary>Longest typical attention first.</summary>
    Attention = 1,

    /// <summary>Furthest typical scroll depth first.</summary>
    Depth = 2,
}

/// <summary>
/// What a visit is, for the questions that have to rebuild them.
/// </summary>
/// <remarks>
/// <para>
/// Visits are not stored. They are the standard grouping — one visitor's activity up to the first
/// silence longer than the idle timeout — worked out on the way out, so what counts as one is part
/// of the question rather than a property of a table.
/// </para>
/// <para>
/// A visit still under way is left out of every answer these produce. Its pages are still
/// arriving, so counting it would report a reader who is two pages into a long article as somebody
/// who read one page and left — and on a quiet site that alone would decide the number.
/// </para>
/// </remarks>
/// <param name="IdleTimeout">How long a visitor may be quiet before their next activity is a new visit.</param>
/// <param name="SettledBefore">
/// A visit whose last activity falls at or after this instant is still under way. The caller sets
/// it an idle timeout behind the present moment, which is the point at which falling silent has
/// been observed rather than assumed.
/// </param>
public readonly record struct VisitBoundaries(TimeSpan IdleTimeout, DateTimeOffset SettledBefore);

/// <summary>
/// How a window's finished visits were shaped.
/// </summary>
/// <remarks>
/// Unpaged: three figures about one window rather than a list. Answered from activity rather than
/// from stored verdicts, so it keeps step with the headline totals instead of trailing them the
/// way <see cref="TrafficBreakdownQuery"/> does — what a visit was is a slower question than how
/// many there were.
/// </remarks>
/// <param name="Range">The window to count over, by when each visit began.</param>
/// <param name="Boundaries">What counts as one visit, and which ones have finished.</param>
public sealed record SiteVisitShapeQuery(TimeRange Range, VisitBoundaries Boundaries) : AnalyticsQuery(Range);

/// <summary>
/// One slice of the pages a window's visits began or ended on, commonest first.
/// </summary>
/// <remarks>
/// Counted per visit rather than per page view: an arrival is a thing that happens once however
/// many times the page is read afterwards. Read a slice at a time on the same terms as
/// <see cref="SitePagesQuery"/>, with the same total ordering.
/// </remarks>
public sealed record SiteVisitFlowQuery : AnalyticsQuery
{
    /// <summary>Most pages any one question may ask for.</summary>
    public const int MostPages = 100;

    /// <summary>Asks for a slice of the pages in a window.</summary>
    /// <param name="range">The window to count over, by when each visit began.</param>
    /// <param name="boundaries">What counts as one visit, and which ones have finished.</param>
    /// <param name="position">Whether to count where visits began or where they ended.</param>
    /// <param name="limit">How many pages to return, at most <see cref="MostPages"/>.</param>
    /// <param name="offset">How many of the commonest pages to pass over first.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds, or the offset is negative.</exception>
    public SiteVisitFlowQuery(
        TimeRange range,
        VisitBoundaries boundaries,
        VisitPosition position,
        int limit,
        int offset = 0)
        : base(range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostPages);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Boundaries = boundaries;
        Position = position;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>What counts as one visit, and which ones have finished.</summary>
    public VisitBoundaries Boundaries { get; }

    /// <summary>Which end of a visit each row stands for.</summary>
    public VisitPosition Position { get; }

    /// <summary>How many pages to return.</summary>
    public int Limit { get; }

    /// <summary>How many of the commonest pages to pass over first.</summary>
    public int Offset { get; }
}

/// <summary>Which end of a visit a page list stands for.</summary>
public enum VisitPosition
{
    /// <summary>The page each visit began on.</summary>
    Entry = 1,

    /// <summary>The last page each visit reached.</summary>
    Exit = 2,
}

/// <summary>
/// The pages one visit went through, in the order it went through them.
/// </summary>
/// <remarks>
/// <para>
/// Answered from activity on demand rather than stored beside the verdict. A verdict is the one
/// thing that cannot be worked out again, and a journey is the opposite: it is exactly what the
/// events say, so keeping a second copy of it would only create something that could disagree with
/// them. It also means the whole journey is available rather than the opening the classifier was
/// handed, which is capped because one sweep can ask for tens of thousands of pages.
/// </para>
/// <para>
/// Activity is read forward from where the visit began, for as long as one visit may be followed.
/// A visit that outlasts that has already gone far past the number of steps any one answer
/// carries.
/// </para>
/// </remarks>
public sealed record SiteVisitJourneyQuery : AnalyticsQuery
{
    /// <summary>Most steps any one question may ask for.</summary>
    /// <remarks>
    /// A sweep's journey is thousands of pages long and nobody reads it; what the reader wants
    /// from one is its shape, which the opening gives them. The visit's own page count is exact
    /// and is reported beside it, so a journey that was cut short says so.
    /// </remarks>
    public const int MostSteps = 200;

    /// <summary>How far forward a visit is followed from where it began.</summary>
    public static readonly TimeSpan LongestVisit = TimeSpan.FromHours(24);

    /// <summary>Asks for the pages one visit went through.</summary>
    /// <param name="visit">Which visit.</param>
    /// <param name="idleTimeout">How long a visitor may be quiet before their next activity is a new visit.</param>
    /// <param name="limit">How many steps to return, at most <see cref="MostSteps"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside its bounds.</exception>
    public SiteVisitJourneyQuery(VisitKey visit, TimeSpan idleTimeout, int limit)
        : base(new TimeRange(visit.StartedAt, visit.StartedAt + LongestVisit))
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MostSteps);

        Visit = visit;
        IdleTimeout = idleTimeout;
        Limit = limit;
    }

    /// <summary>Which visit.</summary>
    public VisitKey Visit { get; }

    /// <summary>How long a visitor may be quiet before their next activity is a new visit.</summary>
    public TimeSpan IdleTimeout { get; }

    /// <summary>How many steps to return.</summary>
    public int Limit { get; }
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
