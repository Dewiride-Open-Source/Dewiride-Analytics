using System.Collections.Frozen;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Tenancy;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Turns a question from the analytics vocabulary into a statement for the telemetry store.
/// </summary>
/// <remarks>
/// <para>
/// Every statement it produces comes from two sources and no others: text written here, and the
/// fixed tables below that map each closed enumeration onto an approved fragment. Nothing a caller
/// supplies is ever concatenated — values are bound, and the site identifier and time zone come
/// from an authorisation decision rather than from the request.
/// </para>
/// <para>
/// One of the two compilers in this project, and the only one reachable from a request. The other
/// rebuilds visits for the detection engine, which works on a site it was handed rather than on
/// one a caller named; between them they write all the SQL this product sends, under the same
/// rule.
/// </para>
/// <para>
/// That matters more here than in most applications. This product's own data is written by
/// whoever is crawling the customer's site, so user agents, referrers and requested paths are
/// hostile text by default. Keeping them permanently on the value side of the boundary is what
/// lets the rest of the system treat them as ordinary data.
/// </para>
/// </remarks>
public static class AnalyticsSqlCompiler
{
    private const string SiteIdParameter = "site_id";
    private const string FromParameter = "from_ms";
    private const string ToParameter = "to_ms";
    private const string TimeZoneParameter = "time_zone";
    private const string LimitParameter = "limit";
    private const string OffsetParameter = "offset";
    private const string IdleParameter = "idle_seconds";
    private const string SettledParameter = "settled_ms";
    private const string VisitorKeyParameter = "visitor_key";

    /// <summary>
    /// Bucket function per granularity. Bucketing runs in the site's own time zone, so the
    /// boundaries are the ones its owner experiences rather than UTC's.
    /// </summary>
    private static readonly FrozenDictionary<TimeGranularity, string> BucketFunctions =
        new Dictionary<TimeGranularity, string>
        {
            [TimeGranularity.Hour] = "toStartOfHour",
            [TimeGranularity.Day] = "toStartOfDay",
        }.ToFrozenDictionary();

    /// <summary>
    /// Gap-filling step per granularity. Expressed as a calendar interval rather than a fixed
    /// number of seconds so that a day which is not twenty-four hours long still produces
    /// exactly one bucket.
    /// </summary>
    private static readonly FrozenDictionary<TimeGranularity, string> StepIntervals =
        new Dictionary<TimeGranularity, string>
        {
            [TimeGranularity.Hour] = "INTERVAL 1 HOUR",
            [TimeGranularity.Day] = "INTERVAL 1 DAY",
        }.ToFrozenDictionary();

    /// <summary>
    /// Aggregate per metric, over one visitor's activity on one page. Visitors excludes activity
    /// carrying no visitor key: a surface that could not derive one has not observed an anonymous
    /// visitor, it has observed nothing about who was there, and counting those together would
    /// invent a single busy phantom.
    /// </summary>
    private static readonly FrozenDictionary<TimeSeriesMetric, string> MetricExpressions =
        new Dictionary<TimeSeriesMetric, string>
        {
            [TimeSeriesMetric.PageViews] = "toInt64(sum(page_views))",
            [TimeSeriesMetric.Visitors] = "toInt64(uniqExactIf(visitor_key, visitor_key != ''))",
        }.ToFrozenDictionary();

    /// <summary>
    /// Which resolved column a place list groups on.
    /// </summary>
    /// <remarks>
    /// A fixed table of identifiers written in this file, which is what keeps the grouping a
    /// choice between two statements rather than a caller-supplied column name.
    /// </remarks>
    private static readonly FrozenDictionary<LocationGrouping, string> PlaceColumns =
        new Dictionary<LocationGrouping, string>
        {
            [LocationGrouping.Country] = "country_code",
            [LocationGrouping.Town] = "city",
        }.ToFrozenDictionary();

    /// <summary>
    /// Which resolved column a software list groups on.
    /// </summary>
    /// <remarks>
    /// A fixed table of identifiers written in this file, on the same terms as
    /// <see cref="PlaceColumns"/>.
    /// </remarks>
    private static readonly FrozenDictionary<SoftwareGrouping, string> SoftwareColumns =
        new Dictionary<SoftwareGrouping, string>
        {
            [SoftwareGrouping.Browser] = "browser_family",
            [SoftwareGrouping.OperatingSystem] = "operating_system",
        }.ToFrozenDictionary();

    /// <summary>
    /// Which measured figure a page-engagement list is ordered by.
    /// </summary>
    /// <remarks>
    /// A fixed table of identifiers written in this file, on the same terms as
    /// <see cref="PlaceColumns"/>. Both name a figure the statement below computes rather than a
    /// column of the store, so a ranking can never reach anything the statement did not choose to
    /// expose.
    /// </remarks>
    private static readonly FrozenDictionary<EngagementRanking, string> RankingExpressions =
        new Dictionary<EngagementRanking, string>
        {
            [EngagementRanking.Attention] = "median_engaged_ms",
            [EngagementRanking.Depth] = "median_depth",
        }.ToFrozenDictionary();

    /// <summary>
    /// Which end of a visit a page list counts.
    /// </summary>
    /// <remarks>
    /// A fixed table of identifiers written in this file, on the same terms as
    /// <see cref="PlaceColumns"/>. Both name a figure the statement below works out rather than a
    /// column of the store.
    /// </remarks>
    private static readonly FrozenDictionary<VisitPosition, string> PositionColumns =
        new Dictionary<VisitPosition, string>
        {
            [VisitPosition.Entry] = "entry_path",
            [VisitPosition.Exit] = "exit_path",
        }.ToFrozenDictionary();

    /// <summary>
    /// Everything about a window reduced to one row per reading.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reading is one visitor on one page. A page reports its progress repeatedly while it is
    /// open and every report carries a running total rather than an instalment, so the largest
    /// report is what that reading came to — which is also what makes the two halves of the
    /// measurement fold together here without arithmetic of their own.
    /// </para>
    /// <para>
    /// What nothing could be measured on is carried as minus one rather than as nothing. The store
    /// refuses a condition that might be nothing inside the counting functions below, and a
    /// figure outside the range either measurement can legally take says "not observed" without
    /// being mistaken for an observation: attention is never negative and a depth is a percentage.
    /// </para>
    /// <para>
    /// Activity carrying no visitor key takes no part, on the same terms as a place list: it has
    /// not told us that somebody unidentifiable read a page, it has told us nothing about who was
    /// there, and a reading is a fact about a reader.
    /// </para>
    /// <para>
    /// Every report about a page counts towards the reading, whether or not the report announcing
    /// the delivery itself arrived. Reports travel by a transport that acknowledges nothing, from
    /// pages that are frequently in the act of being closed, and the whole arrangement is built so
    /// that a lost report costs nothing — insisting on the first one would break that for exactly
    /// the readings it was meant to protect.
    /// </para>
    /// </remarks>
    /// <summary>
    /// How one grouping reads a press: what names the row, what sort of control it was, and which
    /// presses take part at all.
    /// </summary>
    /// <param name="Name">Expression naming the row.</param>
    /// <param name="Control">Expression giving the kind of control, as text.</param>
    /// <param name="Presses">Predicate deciding which activity is counted.</param>
    private readonly record struct ActionShape(string Name, string Control, string Presses);

    /// <summary>
    /// What each way of gathering presses selects and counts.
    /// </summary>
    /// <remarks>
    /// Every fragment is a literal from this file, chosen by a member of a closed set. A caller
    /// picks the member; it never contributes a character of the statement.
    /// </remarks>
    private static readonly FrozenDictionary<ActionGrouping, ActionShape> ActionShapes =
        new Dictionary<ActionGrouping, ActionShape>
        {
            [ActionGrouping.Control] = new(
                "action_label",
                "toString(action_control)",
                "kind = 'Action'"),
            [ActionGrouping.Destination] = new(
                "action_target",
                "'Unknown'",
                "kind = 'Action' AND action_target_kind = 'External'"),
        }.ToFrozenDictionary();

    private static readonly string ReadingsPrefix = $$"""
        WITH
            windowed AS
            (
                SELECT
                    surface,
                    path,
                    visitor_key,
                    correlation_id,
                    engaged_ms,
                    scroll_depth_percent,
                    had_pointer_interaction,
                    had_keyboard_interaction
                FROM events
                WHERE site_id = {site_id:UUID}
                  AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                  AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
            ),
            {{ReconciledEvents.Reconciliation}},
            readings AS
            (
                SELECT
                    path,
                    toInt32(ifNull(max(engaged_ms), -1)) AS engaged_ms,
                    toInt16(ifNull(max(scroll_depth_percent), -1)) AS depth,
                    max(had_pointer_interaction = 'Yes' OR had_keyboard_interaction = 'Yes') AS interacted
                FROM identified
                WHERE visitor_key != ''
                GROUP BY visitor_key, path
            )
        """;

    /// <summary>
    /// A window's activity rebuilt into the visits that finished inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What counts as one visit is written once, in <see cref="VisitGrouping"/>, and is the same
    /// definition the detection engine is judging against — so a page named here as where visits
    /// began is where the visits on the list below it began.
    /// </para>
    /// <para>
    /// Activity is read a full idle timeout past the end of the window, which is what makes "this
    /// visit is over" an observation rather than an artefact of where the reading stopped. Visits
    /// are then kept by when they began, so each belongs to exactly one window however long it ran
    /// for, and a visit whose last activity has not yet been left alone for a full idle timeout is
    /// dropped: its pages are still arriving, and counting one would report a reader two pages into
    /// a long article as somebody who read one page and left.
    /// </para>
    /// <para>
    /// A visit that asked for no page at all is no part of this. It is a reader whose page view
    /// never arrived and whose progress reports did, which says where somebody was but not what
    /// they arrived at.
    /// </para>
    /// </remarks>
    private static readonly string ReconstructedVisits = $$"""
        WITH
            windowed AS
            (
                SELECT
                    event_id,
                    surface,
                    visitor_key,
                    correlation_id,
                    server_ts,
                    kind,
                    path
                FROM events
                WHERE site_id = {site_id:UUID}
                  AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                  AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64} + {idle_seconds:Int64} * 1000, 'UTC')
            ),
            {{ReconciledEvents.Reconciliation}},
            {{VisitGrouping.Of(VisitGrouping.EveryVisitor)}},
            reconstructed AS
            (
                SELECT
                    min(server_ts) AS started_at,
                    max(server_ts) AS ended_at,
                    toInt64(countIf(is_page)) AS page_count,
                    argMinIf(path, (server_ts, event_id), is_page) AS entry_path,
                    argMaxIf(path, (server_ts, event_id), is_page) AS exit_path
                FROM
                (
                    SELECT
                        *,
                        kind = 'PageView' AND NOT is_second_sighting AS is_page
                    FROM counted
                )
                GROUP BY visitor_key, visit_ordinal
                HAVING started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                   AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                   AND ended_at < fromUnixTimestamp64Milli({settled_ms:Int64}, 'UTC')
                   AND page_count > 0
            )
        """;

    /// <summary>
    /// Compiles a question into a statement.
    /// </summary>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The statement and its bound values.</returns>
    /// <exception cref="NotSupportedException">The vocabulary has a case this compiler has not been taught.</exception>
    public static CompiledStatement Compile(TenantScope scope, AnalyticsQuery query)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        return CompileFromActivity(scope, query)
            ?? CompileFromReadings(scope, query)
            ?? CompileFromVisits(scope, query)
            ?? CompileFromVerdicts(scope, query)
            ?? throw new NotSupportedException($"No statement is defined for {query.GetType().Name}.");
    }

    /// <summary>
    /// Compiles a question about what a window's traffic did, or nothing where it is not one.
    /// </summary>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The statement, or <see langword="null"/> where this is not one of these questions.</returns>
    /// <remarks>
    /// Every one of these reads raw activity and reconciles the two halves of the measurement
    /// before counting anything, which is why they are grouped together rather than by what they
    /// happen to return.
    /// </remarks>
    private static CompiledStatement? CompileFromActivity(TenantScope scope, AnalyticsQuery query) =>
        query switch
        {
            OverviewQuery overview => CompileOverview(scope, overview),
            TimeSeriesQuery series => CompileTimeSeries(scope, series),
            SitePagesQuery pages => CompileSitePages(scope, pages),
            SiteActionsQuery actions => CompileSiteActions(scope, actions),
            SiteLocationsQuery places => CompileSiteLocations(scope, places),
            SiteDeviceKindsQuery devices => CompileSiteDeviceKinds(scope, devices),
            SiteSoftwareQuery software => CompileSiteSoftware(scope, software),
            _ => null,
        };

    /// <summary>
    /// Compiles a question about how a window's pages were read, or nothing where it is not one.
    /// </summary>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The statement, or <see langword="null"/> where this is not one of these questions.</returns>
    /// <remarks>
    /// These reduce a window to one row per reading first, and only the browser half of the
    /// measurement can answer them at all — so each carries how much of the window it could be
    /// taken from alongside what it found.
    /// </remarks>
    private static CompiledStatement? CompileFromReadings(TenantScope scope, AnalyticsQuery query) =>
        query switch
        {
            SiteEngagementQuery engagement => CompileSiteEngagement(scope, engagement),
            SitePageEngagementQuery reading => CompileSitePageEngagement(scope, reading),
            _ => null,
        };

    /// <summary>
    /// Compiles a question about the visits a window held, or nothing where it is not one.
    /// </summary>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The statement, or <see langword="null"/> where this is not one of these questions.</returns>
    /// <remarks>
    /// Each of these rebuilds visits from raw activity before it can count anything, which is what
    /// separates them from the questions answered out of stored verdicts: these keep step with the
    /// headline totals, and those wait for a visit to be judged.
    /// </remarks>
    private static CompiledStatement? CompileFromVisits(TenantScope scope, AnalyticsQuery query) =>
        query switch
        {
            SiteVisitShapeQuery shape => CompileSiteVisitShape(scope, shape),
            SiteVisitFlowQuery flow => CompileSiteVisitFlow(scope, flow),
            SiteVisitJourneyQuery journey => CompileSiteVisitJourney(scope, journey),
            _ => null,
        };

    /// <summary>
    /// Compiles a question about what the engine concluded, or nothing where it is not one.
    /// </summary>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The statement, or <see langword="null"/> where this is not one of these questions.</returns>
    /// <remarks>
    /// These read stored verdicts rather than activity, so they see only visits that have been
    /// judged and answer for a slightly older window than the rest — which the interface states
    /// rather than papers over.
    /// </remarks>
    private static CompiledStatement? CompileFromVerdicts(TenantScope scope, AnalyticsQuery query) =>
        query switch
        {
            TrafficBreakdownQuery breakdown => CompileTrafficBreakdown(scope, breakdown),
            JudgedSessionsQuery judged => CompileJudgedSessions(scope, judged),
            _ => null,
        };


    /// <summary>
    /// Reduces a window to how its pages were actually read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The middle reading rather than the mean one. Attention has a long tail — a page left in
    /// front of somebody while they do something else, a reader who stops half way through and
    /// comes back — and a mean drags towards those until it describes an audience nobody in it
    /// resembles. The middle reading is one somebody actually had.
    /// </para>
    /// <para>
    /// How many readings could be measured is answered beside every figure, because only the
    /// browser half of the measurement observes any of this. A site measured solely from its own
    /// server has nothing to say here, and that is a different statement from a site whose readers
    /// did nothing — which is the distinction the whole product exists to keep.
    /// </para>
    /// <para>
    /// Depth is counted in quarters. It is measured against the document's height at the moment
    /// the reader stopped, and a page whose images arrive late is a different height a second
    /// later, so four bands say what a finer division could not honestly claim.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSiteEngagement(TenantScope scope, SiteEngagementQuery query)
    {
        var sql = $$"""
            {{ReadingsPrefix}}
            SELECT
                toInt64(count()) AS total_readings,
                toInt64(countIf(engaged_ms >= 0)) AS measured_readings,
                toInt32(quantileExactIf(0.5)(engaged_ms, engaged_ms >= 0)) AS median_engaged_ms,
                toInt64(countIf(interacted)) AS interacted_readings,
                toInt64(countIf(depth BETWEEN 0 AND 24)) AS reached_top,
                toInt64(countIf(depth BETWEEN 25 AND 49)) AS reached_quarter,
                toInt64(countIf(depth BETWEEN 50 AND 74)) AS reached_half,
                toInt64(countIf(depth >= 75)) AS reached_whole
            FROM readings
            """;

        return new CompiledStatement(sql, WindowParameters(scope, query.Range));
    }

    /// <summary>
    /// Ranks a window's pages by how they were read, one slice at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only pages at least one reading could be measured on are on the list. A page seen solely by
    /// a reporter on the site's own server would otherwise sit among the rest wearing a nought,
    /// which reads as nobody staying rather than as nobody watching.
    /// </para>
    /// <para>
    /// The two figures about the whole window ride on every row as window functions, so they are
    /// worked out across every page before the slice is taken and stay still while somebody moves
    /// through the list — the same arrangement, and for the same reasons, as the busiest-pages
    /// list.
    /// </para>
    /// <para>
    /// The ordering is total: the chosen figure first, and the address breaks a tie, so successive
    /// slices neither repeat a row nor skip one.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSitePageEngagement(TenantScope scope, SitePageEngagementQuery query)
    {
        var ranked = RankingExpressions[query.Ranking];

        var sql = $$"""
            {{ReadingsPrefix}}
            SELECT
                path,
                measured,
                median_engaged_ms,
                median_depth,
                interacted,
                toInt64(count() OVER ()) AS total_pages,
                toInt32(max(median_engaged_ms) OVER ()) AS longest_median_engaged_ms
            FROM
            (
                SELECT
                    path,
                    toInt64(countIf(engaged_ms >= 0)) AS measured,
                    toInt32(quantileExactIf(0.5)(engaged_ms, engaged_ms >= 0)) AS median_engaged_ms,
                    toInt32(quantileExactIf(0.5)(depth, depth >= 0)) AS median_depth,
                    toInt64(countIf(interacted)) AS interacted
                FROM readings
                GROUP BY path
                HAVING measured > 0
            )
            ORDER BY {{ranked}} DESC, path
            LIMIT {limit:UInt32} OFFSET {offset:UInt32}
            """;

        return new CompiledStatement(
            sql,
            [
                .. WindowParameters(scope, query.Range),
                new QueryParameter(LimitParameter, (uint)query.Limit),
                new QueryParameter(OffsetParameter, (uint)query.Offset),
            ]);
    }

    /// <summary>
    /// Counts a window's finished visits, and how many of them were a single page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single-page visit is counted rather than turned into a rate here, so that the share and
    /// the count on the screen are the same arithmetic. Pages per visit is left as the two figures
    /// it is made of for the same reason: one number is reported once and divided where it is
    /// shown, rather than derived twice and given two chances to disagree.
    /// </para>
    /// <para>
    /// Whether a single-page visit is a reader who found nothing or a reader who found exactly what
    /// they came for is not knowable from a page count, and this statement does not pretend
    /// otherwise. It is how long they stayed that separates the two, which is a different question
    /// and has its own answer.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSiteVisitShape(TenantScope scope, SiteVisitShapeQuery query)
    {
        var sql = $$"""
            {{ReconstructedVisits}}
            SELECT
                toInt64(count()) AS visits,
                toInt64(countIf(page_count = 1)) AS single_page_visits,
                toInt64(sum(page_count)) AS page_views
            FROM reconstructed
            """;

        return new CompiledStatement(sql, VisitParameters(scope, query.Range, query.Boundaries));
    }

    /// <summary>
    /// Counts a window's finished visits, grouped by the page they began or ended on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counted per visit rather than per page view: arriving somewhere is something that happens
    /// once, and counting it per view would rank a site's busiest page as its commonest doorway
    /// whether or not anybody arrived through it.
    /// </para>
    /// <para>
    /// The three figures about the whole window ride on every row as window functions, so they are
    /// worked out across every page before the slice is taken — the same arrangement, and for the
    /// same reasons, as the busiest-pages list.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSiteVisitFlow(TenantScope scope, SiteVisitFlowQuery query)
    {
        var position = PositionColumns[query.Position];

        var sql = $$"""
            {{ReconstructedVisits}}
            SELECT
                path,
                visits,
                toInt64(sum(visits) OVER ()) AS total_visits,
                toInt64(count() OVER ()) AS total_paths,
                toInt64(max(visits) OVER ()) AS most_visits
            FROM
            (
                SELECT
                    {{position}} AS path,
                    toInt64(count()) AS visits
                FROM reconstructed
                GROUP BY path
            )
            ORDER BY visits DESC, path
            LIMIT {limit:UInt32} OFFSET {offset:UInt32}
            """;

        return new CompiledStatement(
            sql,
            [
                .. VisitParameters(scope, query.Range, query.Boundaries),
                new QueryParameter(LimitParameter, (uint)query.Limit),
                new QueryParameter(OffsetParameter, (uint)query.Offset),
            ]);
    }

    /// <summary>
    /// Reads back what one visit did, in the order it did it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The visit is found rather than looked up. Its identity says whose activity it is and when it
    /// began, so activity is read forward from that instant for one visitor and regrouped under the
    /// same definition of a visit as everything else — and the first visit that grouping produces
    /// is by construction the one asked for. Both parts of the identity travel as bound values.
    /// </para>
    /// <para>
    /// A step is one arrival at one page, not one page. A reader who comes back to an article later
    /// in the same visit was there twice, and folding the two together would report one long
    /// reading that never happened. Which arrival a progress report belongs to is settled by
    /// counting the page views of that address before it, so a report still counts towards its step
    /// when the page view announcing it was lost.
    /// </para>
    /// <para>
    /// What nothing could be measured on is carried as minus one, on the same terms as a reading
    /// list: it is outside the range any of these three can legally take, so it says "not observed"
    /// without being mistaken for an observation. Attention is never negative, a depth is a
    /// percentage, and no site answers a request with a status of minus one.
    /// </para>
    /// <para>
    /// Pages and presses are gathered separately and then laid end to end in time, because they are
    /// counted on different terms: a page is every report about one arrival folded into one row,
    /// while a press is a row of its own — somebody who pressed the same button twice pressed it
    /// twice. Where the two share an instant the page comes first, since a control cannot be
    /// operated on a page nobody has arrived at.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSiteVisitJourney(TenantScope scope, SiteVisitJourneyQuery query)
    {
        var sql = $$"""
            WITH
                windowed AS
                (
                    SELECT
                        event_id,
                        surface,
                        visitor_key,
                        correlation_id,
                        server_ts,
                        kind,
                        path,
                        status_code,
                        engaged_ms,
                        scroll_depth_percent,
                        action_control,
                        action_label,
                        action_target,
                        action_target_kind
                    FROM events
                    WHERE site_id = {site_id:UUID}
                      AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                      AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                ),
                {{ReconciledEvents.Reconciliation}},
                {{VisitGrouping.Of("visitor_key = {visitor_key:String}")}},
                stepped AS
                (
                    SELECT
                        *,
                        sum(toUInt8(kind = 'PageView' AND NOT is_second_sighting)) OVER (
                            PARTITION BY path
                            ORDER BY server_ts, event_id
                            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS step
                    FROM counted
                    WHERE visit_ordinal = 0
                ),
                pages AS
                (
                    SELECT
                        min(server_ts) AS at,
                        toUInt8(0) AS press,
                        path,
                        toInt16(ifNull(max(status_code), -1)) AS status_code,
                        toInt32(ifNull(max(engaged_ms), -1)) AS engaged_ms,
                        toInt16(ifNull(max(scroll_depth_percent), -1)) AS depth,
                        '' AS label,
                        'Unknown' AS control,
                        '' AS target,
                        'None' AS target_kind
                    FROM stepped
                    WHERE kind != 'Action'
                    GROUP BY path, step
                ),
                pressed AS
                (
                    SELECT
                        server_ts AS at,
                        toUInt8(1) AS press,
                        path,
                        toInt16(-1) AS status_code,
                        toInt32(-1) AS engaged_ms,
                        toInt16(-1) AS depth,
                        action_label AS label,
                        toString(action_control) AS control,
                        action_target AS target,
                        toString(action_target_kind) AS target_kind
                    FROM stepped
                    WHERE kind = 'Action'
                )
            SELECT at, press, path, status_code, engaged_ms, depth, label, control, target, target_kind
            FROM
            (
                SELECT * FROM pages
                UNION ALL
                SELECT * FROM pressed
            )
            ORDER BY at, press, path
            LIMIT {limit:UInt32}
            """;

        return new CompiledStatement(
            sql,
            [
                new QueryParameter(SiteIdParameter, scope.SiteId),
                new QueryParameter(FromParameter, query.Range.From.ToUnixTimeMilliseconds()),
                new QueryParameter(ToParameter, query.Range.To.ToUnixTimeMilliseconds()),
                new QueryParameter(IdleParameter, (long)query.IdleTimeout.TotalSeconds),
                new QueryParameter(VisitorKeyParameter, query.Visit.VisitorKey),
                new QueryParameter(LimitParameter, (uint)query.Limit),
            ]);
    }

    /// <summary>
    /// Counts judged visits by what generated them.
    /// </summary>
    /// <remarks>
    /// Verdicts are kept per ruleset, so each visit is first reduced to the newest ruleset that
    /// has an opinion about it. Without that, improving the rules would double every number, and
    /// the same visit would be counted once as a person and once as a crawler. Reducing on the
    /// ruleset rather than on when the row was written also means judging an older ruleset again
    /// does not overturn what the current one concluded.
    /// </remarks>
    private static CompiledStatement CompileTrafficBreakdown(TenantScope scope, TrafficBreakdownQuery query)
    {
        const string sql = """
            SELECT
                category,
                strength,
                toInt64(count()) AS sessions,
                toInt64(sum(page_count)) AS page_views
            FROM
            (
                SELECT
                    session_key,
                    argMax(category, (ruleset_major, ruleset_minor, classified_at)) AS category,
                    argMax(strength, (ruleset_major, ruleset_minor, classified_at)) AS strength,
                    argMax(page_count, (ruleset_major, ruleset_minor, classified_at)) AS page_count
                FROM session_classifications
                WHERE site_id = {site_id:UUID}
                  AND started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                  AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                GROUP BY session_key
            )
            GROUP BY category, strength
            ORDER BY sessions DESC, category, strength
            """;

        return new CompiledStatement(sql, WindowParameters(scope, query.Range));
    }

    /// <summary>
    /// Returns individual judged visits with the evidence behind each verdict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reduced to one row per visit on the same terms as the breakdown, so a visit judged under
    /// two rulesets appears once, under the newer of them.
    /// </para>
    /// <para>
    /// How many visits the window holds altogether rides on every row as a window function, worked
    /// out across the whole deduplicated set before the slice is taken. Counting the rows returned
    /// instead would say a period held whatever a screenful happens to be, which is the figure that
    /// makes a list stop without admitting there is more behind it.
    /// </para>
    /// <para>
    /// The ordering is total — newest first, and the visit's own key breaks a tie — so successive
    /// slices neither repeat a visit nor skip one. Two visits beginning in the same millisecond are
    /// ordinary on a busy site, and without the tie-break they could swap places between one slice
    /// and the next and one of them would never be seen.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileJudgedSessions(TenantScope scope, JudgedSessionsQuery query)
    {
        const string sql = """
            SELECT
                session_key,
                started_at,
                ended_at,
                page_count,
                surfaces,
                category,
                strength,
                is_provisional,
                ruleset_major,
                ruleset_minor,
                signal_codes,
                signal_directions,
                signal_weights,
                signal_supporting,
                signal_parameters,
                toInt64(count() OVER ()) AS total_visits
            FROM
            (
                SELECT *
                FROM session_classifications
                WHERE site_id = {site_id:UUID}
                  AND started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                  AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                ORDER BY ruleset_major DESC, ruleset_minor DESC, classified_at DESC
                LIMIT 1 BY session_key
            )
            ORDER BY started_at DESC, session_key
            LIMIT {limit:UInt32} OFFSET {offset:UInt32}
            """;

        return new CompiledStatement(
            sql,
            [
                .. WindowParameters(scope, query.Range),
                new QueryParameter(LimitParameter, (uint)query.Limit),
                new QueryParameter(OffsetParameter, (uint)query.Offset),
            ]);
    }

    /// <summary>
    /// Counts the headline totals, reading pages delivered rather than reports received.
    /// </summary>
    /// <remarks>
    /// Reports are still totalled as they arrive, because that figure answers a different question
    /// — how much the site is being watched — and is not a claim about how much traffic there was.
    /// </remarks>
    private static CompiledStatement CompileOverview(TenantScope scope, OverviewQuery query)
    {
        var sql = $$"""
            WITH
                windowed AS
                (
                    SELECT kind, surface, path, visitor_key, correlation_id
                    FROM events
                    WHERE site_id = {site_id:UUID}
                      AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                      AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                ),
                {{ReconciledEvents.Reconciliation}}
            SELECT
                toInt64(sum(page_views)) AS page_views,
                toInt64(uniqExactIf(visitor_key, visitor_key != '')) AS visitors,
                toInt64(sum(reports)) AS events
            FROM
            (
                SELECT
                    visitor_key,
                    count() AS reports,
                    {{ReconciledEvents.DeliveredPageViews(8)}}
                FROM identified
                GROUP BY visitor_key, path
            )
            """;

        return new CompiledStatement(sql, WindowParameters(scope, query.Range));
    }

    /// <summary>
    /// Counts pages delivered, grouped by which page was delivered, one slice at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three figures about the whole window ride on every row: what was delivered altogether, how
    /// many addresses there were, and how much the busiest of them had. All three are window
    /// functions, so they are worked out across every address before the slice is taken and stay
    /// still while somebody moves through the list. Summing the rows instead would report the
    /// busiest page of a large site at several times the share it has, and measuring a bar against
    /// whatever led one slice would start every slice with a full one.
    /// </para>
    /// <para>
    /// Working them out takes a level of its own: the store refuses an aggregate inside a window
    /// function, so the per-page counts have to be finished before anything can be measured across
    /// them.
    /// </para>
    /// <para>
    /// The ordering is total — busiest first, and the address breaks a tie — so successive slices
    /// neither repeat a row nor skip one. Without the tie-break, two addresses with equal traffic
    /// could swap places between one slice and the next and one of them would never be seen.
    /// </para>
    /// <para>
    /// A path never enters this statement. It is grouped on and read back, which is the whole of
    /// what a hostile one can do here.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSitePages(TenantScope scope, SitePagesQuery query)
    {
        var sql = $$"""
            WITH
                windowed AS
                (
                    SELECT kind, surface, path, visitor_key, correlation_id
                    FROM events
                    WHERE site_id = {site_id:UUID}
                      AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                      AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                ),
                {{ReconciledEvents.Reconciliation}}
            SELECT
                path,
                page_views,
                visitors,
                toInt64(sum(page_views) OVER ()) AS total_page_views,
                toInt64(count() OVER ()) AS total_paths,
                toInt64(max(page_views) OVER ()) AS most_page_views
            FROM
            (
                SELECT
                    path,
                    toInt64(sum(page_views)) AS page_views,
                    toInt64(uniqExactIf(visitor_key, visitor_key != '')) AS visitors
                FROM
                (
                    SELECT
                        path,
                        visitor_key,
                        {{ReconciledEvents.DeliveredPageViews(12)}}
                    FROM identified
                    GROUP BY path, visitor_key
                )
                GROUP BY path
                HAVING page_views > 0
            )
            ORDER BY page_views DESC, path
            LIMIT {limit:UInt32} OFFSET {offset:UInt32}
            """;

        return new CompiledStatement(
            sql,
            [
                .. WindowParameters(scope, query.Range),
                new QueryParameter(LimitParameter, (uint)query.Limit),
                new QueryParameter(OffsetParameter, (uint)query.Offset),
            ]);
    }

    /// <summary>
    /// Counts presses, grouped by what was operated or by where it led, one slice at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No reconciliation. Only something running in the visitor's own browser can see a press, so
    /// there is exactly one account of each and nothing to fold together — which is why this reads
    /// activity directly rather than through the shared identity fragment the page counts need.
    /// </para>
    /// <para>
    /// The kind of control is read out as text so that both groupings answer with the same column
    /// type. Grouping by where a press led has no control to report, and says so in the same
    /// vocabulary rather than by leaving the column out of one of the two answers.
    /// </para>
    /// <para>
    /// The whole-window figures are carried past the slice by window functions over the finished
    /// per-row counts, because the store refuses an aggregate inside a window function. The
    /// ordering is total — most pressed first, then the name, then the kind — so successive slices
    /// neither repeat a row nor skip one.
    /// </para>
    /// <para>
    /// A control's name is written by whoever wrote the page, and a page may carry writing that
    /// somebody else put there. It is grouped on and read back, which is the whole of what a
    /// hostile one can do here.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSiteActions(TenantScope scope, SiteActionsQuery query)
    {
        var shape = ActionShapes[query.Grouping];

        var sql = $$"""
            WITH
                pressed AS
                (
                    SELECT action_label, action_control, action_target, visitor_key
                    FROM events
                    WHERE site_id = {site_id:UUID}
                      AND {{shape.Presses}}
                      AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                      AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                )
            SELECT
                name,
                control,
                presses,
                visitors,
                toInt64(sum(presses) OVER ()) AS total_presses,
                toInt64(count() OVER ()) AS total_controls,
                toInt64(max(presses) OVER ()) AS most_presses
            FROM
            (
                SELECT
                    {{shape.Name}} AS name,
                    {{shape.Control}} AS control,
                    toInt64(count()) AS presses,
                    toInt64(uniqExactIf(visitor_key, visitor_key != '')) AS visitors
                FROM pressed
                GROUP BY name, control
            )
            ORDER BY presses DESC, name, control
            LIMIT {limit:UInt32} OFFSET {offset:UInt32}
            """;

        return new CompiledStatement(
            sql,
            [
                .. WindowParameters(scope, query.Range),
                new QueryParameter(LimitParameter, (uint)query.Limit),
                new QueryParameter(OffsetParameter, (uint)query.Offset),
            ]);
    }

    /// <summary>
    /// Counts visitors, grouped by where they were, one slice at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counted per visitor rather than per page. A place is a fact about an audience, and ranking
    /// places by pages read would put whichever country browses most at the top of a list that
    /// claims to say where the readers are.
    /// </para>
    /// <para>
    /// Where a visitor was is settled once for the whole visitor, before anything is grouped. The
    /// two halves of the measurement resolve the address independently and one of them may have
    /// resolved nothing — a report forwarded by a site's own server carries whatever address that
    /// server passed on — so taking each report's own answer would split one reader in Pune into a
    /// reader in Pune and a reader nowhere. Any non-empty answer settles it for all their activity.
    /// </para>
    /// <para>
    /// Activity carrying no visitor key takes no part. It has not told us that somebody
    /// unidentifiable was somewhere; it has told us nothing about who was there, and a place list
    /// is a list of who was where.
    /// </para>
    /// <para>
    /// A place that resolved to nothing is a row rather than an omission. An install behind a
    /// proxy that does not pass the visitor's address through resolves nothing at all, and that
    /// has to be visible on the screen rather than showing as an empty list.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSiteLocations(TenantScope scope, SiteLocationsQuery query)
    {
        var place = PlaceColumns[query.Grouping];

        var sql = $$"""
            WITH
                windowed AS
                (
                    SELECT kind, surface, path, visitor_key, correlation_id, country_code, city
                    FROM events
                    WHERE site_id = {site_id:UUID}
                      AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                      AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                ),
                {{ReconciledEvents.Reconciliation}},
                located AS
                (
                    SELECT
                        visitor_key,
                        anyIf(country_code, country_code != '') AS country_code,
                        anyIf(city, city != '') AS city,
                        toInt64(sum(page_views)) AS page_views
                    FROM
                    (
                        SELECT
                            visitor_key,
                            path,
                            anyIf(country_code, country_code != '') AS country_code,
                            anyIf(city, city != '') AS city,
                            {{ReconciledEvents.DeliveredPageViews(16)}}
                        FROM identified
                        WHERE visitor_key != ''
                        GROUP BY visitor_key, path
                    )
                    GROUP BY visitor_key
                )
            SELECT
                place,
                country_code,
                visitors,
                page_views,
                toInt64(sum(visitors) OVER ()) AS total_visitors,
                toInt64(count() OVER ()) AS total_places,
                toInt64(max(visitors) OVER ()) AS most_visitors
            FROM
            (
                SELECT
                    {{place}} AS place,
                    country_code,
                    toInt64(count()) AS visitors,
                    toInt64(sum(page_views)) AS page_views
                FROM located
                GROUP BY place, country_code
            )
            ORDER BY visitors DESC, place, country_code
            LIMIT {limit:UInt32} OFFSET {offset:UInt32}
            """;

        return new CompiledStatement(
            sql,
            [
                .. WindowParameters(scope, query.Range),
                new QueryParameter(LimitParameter, (uint)query.Limit),
                new QueryParameter(OffsetParameter, (uint)query.Offset),
            ]);
    }

    /// <summary>
    /// Counts visitors, grouped by the kind of device they were on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counted per visitor and settled once for the whole visitor, on exactly the terms a place
    /// list is: the two halves of the measurement read the device from a user agent each, and a
    /// report forwarded by a site's own server frequently carries none. Taking each report's own
    /// answer would split one reader on a phone into a reader on a phone and a reader on nothing.
    /// </para>
    /// <para>
    /// The kind is read out as text so that "not established" is the empty string here as it is
    /// everywhere else in the store, rather than a sixth name sitting in the same list as the five
    /// real ones.
    /// </para>
    /// <para>
    /// Unpaged, and deliberately: the answer is a closed set of five, so there is nothing to page
    /// through and no window function is needed to describe a window that is entirely on screen.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSiteDeviceKinds(TenantScope scope, SiteDeviceKindsQuery query)
    {
        var sql = $$"""
            WITH
                windowed AS
                (
                    SELECT kind, surface, path, visitor_key, correlation_id, device_class
                    FROM events
                    WHERE site_id = {site_id:UUID}
                      AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                      AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                ),
                {{ReconciledEvents.Reconciliation}},
                equipped AS
                (
                    SELECT
                        visitor_key,
                        anyIf(device, device != '') AS device,
                        toInt64(sum(page_views)) AS page_views
                    FROM
                    (
                        SELECT
                            visitor_key,
                            path,
                            anyIf(toString(device_class), device_class != 'Unknown') AS device,
                            {{ReconciledEvents.DeliveredPageViews(16)}}
                        FROM identified
                        WHERE visitor_key != ''
                        GROUP BY visitor_key, path
                    )
                    GROUP BY visitor_key
                )
            SELECT
                device,
                toInt64(count()) AS visitors,
                toInt64(sum(page_views)) AS page_views
            FROM equipped
            GROUP BY device
            ORDER BY visitors DESC, device
            """;

        return new CompiledStatement(sql, WindowParameters(scope, query.Range));
    }

    /// <summary>
    /// Counts visitors, grouped by the software they were using, one slice at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Settled once per visitor and counted per visitor, for the reasons the device list and the
    /// place list are, and carrying the whole window's figures past the slice for the reason the
    /// page list does.
    /// </para>
    /// <para>
    /// Nothing in these columns was written by a client. A browser names itself in a string
    /// anybody can invent, and what is stored is the word the engine's own catalogue answered
    /// with — so the set of values here is closed however many browsers are invented, and this
    /// statement groups on a column the way any other statement does.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSiteSoftware(TenantScope scope, SiteSoftwareQuery query)
    {
        var column = SoftwareColumns[query.Grouping];

        var sql = $$"""
            WITH
                windowed AS
                (
                    SELECT kind, surface, path, visitor_key, correlation_id, {{column}}
                    FROM events
                    WHERE site_id = {site_id:UUID}
                      AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                      AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                ),
                {{ReconciledEvents.Reconciliation}},
                equipped AS
                (
                    SELECT
                        visitor_key,
                        anyIf({{column}}, {{column}} != '') AS name,
                        toInt64(sum(page_views)) AS page_views
                    FROM
                    (
                        SELECT
                            visitor_key,
                            path,
                            anyIf({{column}}, {{column}} != '') AS {{column}},
                            {{ReconciledEvents.DeliveredPageViews(16)}}
                        FROM identified
                        WHERE visitor_key != ''
                        GROUP BY visitor_key, path
                    )
                    GROUP BY visitor_key
                )
            SELECT
                name,
                visitors,
                page_views,
                toInt64(sum(visitors) OVER ()) AS total_visitors,
                toInt64(count() OVER ()) AS total_names,
                toInt64(max(visitors) OVER ()) AS most_visitors
            FROM
            (
                SELECT
                    name,
                    toInt64(count()) AS visitors,
                    toInt64(sum(page_views)) AS page_views
                FROM equipped
                GROUP BY name
            )
            ORDER BY visitors DESC, name
            LIMIT {limit:UInt32} OFFSET {offset:UInt32}
            """;

        return new CompiledStatement(
            sql,
            [
                .. WindowParameters(scope, query.Range),
                new QueryParameter(LimitParameter, (uint)query.Limit),
                new QueryParameter(OffsetParameter, (uint)query.Offset),
            ]);
    }

    private static CompiledStatement CompileTimeSeries(TenantScope scope, TimeSeriesQuery query)
    {
        var bucket = BucketFunctions[query.Granularity];
        var step = StepIntervals[query.Granularity];
        var metric = MetricExpressions[query.Metric];

        // The upper fill bound is derived from one millisecond before the exclusive end of the
        // window, so the series stops at the last bucket that could hold data. Bounding it on the
        // end instant itself would append an empty bucket whenever a window ends exactly on a
        // boundary, and truncate the final partial bucket whenever it does not.
        var sql = $$"""
            WITH
                windowed AS
                (
                    SELECT kind, surface, path, visitor_key, correlation_id, server_ts
                    FROM events
                    WHERE site_id = {site_id:UUID}
                      AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                      AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                ),
                {{ReconciledEvents.Reconciliation}}
            SELECT
                bucket,
                {{metric}} AS value
            FROM
            (
                SELECT
                    {{bucket}}(server_ts, {time_zone:String}) AS bucket,
                    visitor_key,
                    {{ReconciledEvents.DeliveredPageViews(8)}}
                FROM identified
                GROUP BY bucket, visitor_key, path
            )
            GROUP BY bucket
            ORDER BY bucket
            WITH FILL
                FROM {{bucket}}(fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC'), {time_zone:String})
                TO {{bucket}}(fromUnixTimestamp64Milli({to_ms:Int64} - 1, 'UTC'), {time_zone:String}) + {{step}}
                STEP {{step}}
            """;

        return new CompiledStatement(
            sql,
            [.. WindowParameters(scope, query.Range), new QueryParameter(TimeZoneParameter, scope.TimeZoneId)]);
    }

    private static QueryParameter[] WindowParameters(TenantScope scope, TimeRange range) =>
    [
        new(SiteIdParameter, scope.SiteId),
        new(FromParameter, range.From.ToUnixTimeMilliseconds()),
        new(ToParameter, range.To.ToUnixTimeMilliseconds()),
    ];

    /// <summary>
    /// The window, plus what turns activity inside it into visits.
    /// </summary>
    /// <remarks>
    /// The idle timeout is bound rather than written into the statement, because it is a setting a
    /// self-hoster may change and every answer that mentions a visit has to be counting the same
    /// thing. Together with the instant a visit is treated as finished, it is the whole of what
    /// distinguishes these statements from the ones that only count reports.
    /// </remarks>
    private static QueryParameter[] VisitParameters(
        TenantScope scope,
        TimeRange range,
        VisitBoundaries boundaries) =>
    [
        .. WindowParameters(scope, range),
        new QueryParameter(IdleParameter, (long)boundaries.IdleTimeout.TotalSeconds),
        new QueryParameter(SettledParameter, boundaries.SettledBefore.ToUnixTimeMilliseconds()),
    ];
}
