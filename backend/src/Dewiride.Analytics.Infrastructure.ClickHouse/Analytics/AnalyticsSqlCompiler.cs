using System.Collections.Frozen;
using System.Collections.Immutable;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Classification.Identity;
using Dewiride.Analytics.Domain.Telemetry;

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
    private const string SiteIdsParameter = "site_ids";
    private const string FromParameter = "from_ms";
    private const string ToParameter = "to_ms";
    private const string TimeZoneParameter = "time_zone";
    private const string LimitParameter = "limit";
    private const string OffsetParameter = "offset";
    private const string IdleParameter = "idle_seconds";
    private const string SettledParameter = "settled_ms";
    private const string LongestVisitParameter = "longest_visit_seconds";
    private const string VisitorKeyParameter = "visitor_key";
    private const string SiteDomainParameter = "site_domain";
    private const string SuffixesParameter = "second_levels";
    private const string SourceKeysParameter = "source_keys";
    private const string SourceNamesParameter = "source_names";
    private const string SourceChannelsParameter = "source_channels";
    private const string HostingNumbersParameter = "hosting_numbers";
    private const string HostingNamesParameter = "hosting_names";
    private const string CategoriesParameter = "categories";
    private const string StrengthsParameter = "strengths";
    private const string LeastPagesParameter = "least_pages";
    private const string DevicesParameter = "devices";
    private const string SourceKindsParameter = "source_kinds";
    private const string BrowsersParameter = "browsers";
    private const string SystemsParameter = "systems";
    private const string CountriesParameter = "countries";
    private const string TownsParameter = "towns";
    private const string NetworksParameter = "networks";
    private const string SourcesParameter = "sources";
    private const string EntryPagesParameter = "entry_pages";
    private const string MostValuesParameter = "most_values";
    private const string MaxRequestsParameter = "max_requests";

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
    /// A fixed table of identifiers and approved fragments, each written in this assembly, which
    /// is what keeps the grouping a choice between two statements rather than a caller-supplied
    /// column name.
    /// </remarks>
    private static readonly FrozenDictionary<LocationGrouping, string> PlaceColumns =
        new Dictionary<LocationGrouping, string>
        {
            [LocationGrouping.Country] = "country_code",
            [LocationGrouping.Town] = "city",
            [LocationGrouping.Network] = NamedNetworks.From(8),
        }.ToFrozenDictionary();

    /// <summary>The catalogue of hosting networks, held as the arrays the store binds.</summary>
    private static readonly uint[] HostingNumbers = [.. HostingNetworks.Numbers];

    /// <summary>What each of those networks is called on a screen.</summary>
    private static readonly string[] HostingNames = [.. HostingNetworks.Operators];

    /// <summary>
    /// What a place list carries alongside each row as the country it is in.
    /// </summary>
    /// <remarks>
    /// Nothing, on a list of networks. A network is not a place, and carrying a country would
    /// divide one company's datacentres into a row per country — which is the answer this grouping
    /// exists to escape, since a rented server reports the country it is racked in and a hundred
    /// of them read as an audience there.
    /// </remarks>
    private static readonly FrozenDictionary<LocationGrouping, string> PlaceCountryColumns =
        new Dictionary<LocationGrouping, string>
        {
            [LocationGrouping.Country] = "country_code",
            [LocationGrouping.Town] = "country_code",
            [LocationGrouping.Network] = "''",
        }.ToFrozenDictionary();

    /// <summary>
    /// Which expression a source list groups on.
    /// </summary>
    /// <remarks>
    /// A fixed table of expressions written in this file, on the same terms as
    /// <see cref="PlaceColumns"/>. Neither reads anything a caller supplied: a page row is the
    /// sending site's address with the path of the sending page after it, and everything from the
    /// question mark onwards is cut — that part is somebody else's site carrying somebody else's
    /// state, and which article sent the readers is answered without it.
    /// </remarks>
    private static readonly FrozenDictionary<SourceGrouping, string> SourceExpressions =
        new Dictionary<SourceGrouping, string>
        {
            [SourceGrouping.Site] = "source_site",
            [SourceGrouping.Page] = "concat(source_site, path(source_address))",
            [SourceGrouping.Kind] = "source_channel",
        }.ToFrozenDictionary();

    /// <summary>
    /// What a source list carries alongside each row as the site it belongs to.
    /// </summary>
    /// <remarks>
    /// Nothing, on a list of kinds. A kind is not a site, and a row carrying one would divide
    /// "search engines" into as many rows as there were search engines — which is the answer the
    /// list beside it already gives.
    /// </remarks>
    private static readonly FrozenDictionary<SourceGrouping, string> SourceSiteExpressions =
        new Dictionary<SourceGrouping, string>
        {
            [SourceGrouping.Site] = "source_site",
            [SourceGrouping.Page] = "source_site",
            [SourceGrouping.Kind] = "''",
        }.ToFrozenDictionary();

    /// <summary>
    /// The catalogue of sending sites, held as the arrays the store binds.
    /// </summary>
    /// <remarks>
    /// Copied once rather than converted per question, and as plain arrays because that is what
    /// the driver knows how to send as an <c>Array(String)</c>. They are bound rather than written
    /// into the statement so that what a reviewer reads in an approved statement stays the shape
    /// of the question instead of a hundred hostnames.
    /// </remarks>
    private static readonly string[] SecondLevels = [.. TrafficSources.SecondLevelSuffixes];

    private static readonly string[] SourceKeys = [.. TrafficSources.Keys];

    private static readonly string[] SourceNames = [.. TrafficSources.Names];

    private static readonly string[] SourceChannels = [.. TrafficSources.Channels];

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
    /// Which pages a visit went to is settled in <see cref="VisitGrouping"/> as well, so the page a
    /// visit is recorded as beginning at is the page it arrived at, here and on the visit's own
    /// account of itself alike.
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
                    toInt64(countIf(opens_page)) AS page_count,
                    argMinIf(path, (server_ts, event_id), opens_page) AS entry_path,
                    argMaxIf(path, (server_ts, event_id), opens_page) AS exit_path
                FROM opened
                GROUP BY visitor_key, visit_ordinal
                HAVING started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                   AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                   AND ended_at < fromUnixTimestamp64Milli({settled_ms:Int64}, 'UTC')
            )
        """;

    /// <summary>
    /// A window's judged visits, reduced to one verdict each and narrowed to what a verdict holds.
    /// </summary>
    /// <remarks>
    /// Everything up to the slice, because both shapes of the visit list end the same way and only
    /// one of them has anything to say between the narrowing and the ordering.
    /// </remarks>
    private const string JudgedVisits = """
        SELECT
            session_key,
            started_at,
            ended_at,
            page_count,
            surfaces,
            category,
            strength,
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
        WHERE (empty({categories:Array(String)}) OR toString(category) IN {categories:Array(String)})
          AND (empty({strengths:Array(String)}) OR toString(strength) IN {strengths:Array(String)})
          AND page_count >= {least_pages:UInt32}
        """;

    /// <summary>Which of the narrowed visits a slice holds, in the order that makes slices agree.</summary>
    private const string JudgedSlice = """
        ORDER BY started_at DESC, session_key
        LIMIT {limit:UInt32} OFFSET {offset:UInt32}
        """;

    /// <summary>The whole statement for a narrowing the stored verdicts answer on their own.</summary>
    private const string JudgedVerdicts = $"{JudgedVisits}\n{JudgedSlice}";

    /// <summary>
    /// The same, with the rebuild in front of it and the nine conditions only the visits themselves
    /// can answer.
    /// </summary>
    /// <remarks>
    /// The further condition sits in the same outer selection as the other three, so it still
    /// applies after each visit has been reduced to one verdict and the count still describes the
    /// narrowed list rather than the whole window.
    /// </remarks>
    private static readonly string JudgedVerdictsByDetail = $$"""
        WITH
            {{JudgedVisitDetails.Fragment}},
            narrowed AS
            (
                SELECT session_key
                FROM described
                WHERE (empty({devices:Array(String)}) OR device IN {devices:Array(String)})
                  AND (empty({source_kinds:Array(String)}) OR from_kind IN {source_kinds:Array(String)})
                  AND (empty({browsers:Array(String)}) OR browser IN {browsers:Array(String)})
                  AND (empty({systems:Array(String)}) OR system_name IN {systems:Array(String)})
                  AND (empty({countries:Array(String)}) OR country IN {countries:Array(String)})
                  AND (empty({towns:Array(String)}) OR town IN {towns:Array(String)})
                  AND (empty({networks:Array(String)}) OR network IN {networks:Array(String)})
                  AND (empty({sources:Array(String)}) OR from_site IN {sources:Array(String)})
                  AND (empty({entry_pages:Array(String)}) OR entry_path IN {entry_pages:Array(String)})
            )
        {{JudgedVisits}}
          AND session_key IN (SELECT session_key FROM narrowed)
        {{JudgedSlice}}
        """;

    /// <summary>
    /// What each detail of a window's judged visits held, counted per visit.
    /// </summary>
    /// <remarks>
    /// The nine are unfolded from one array rather than laid end to end as nine selections, because
    /// the store writes a common table expression out again wherever it is named — so nine of them
    /// would rebuild every visit in the window nine times over.
    /// </remarks>
    private static readonly string VisitDetailCounts = $$"""
        WITH
            {{JudgedVisitDetails.Fragment}},
            judged AS
            (
                SELECT session_key
                FROM session_classifications
                WHERE site_id = {site_id:UUID}
                  AND started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                  AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
            ),
            detailed AS
            (
                SELECT
                    {{JudgedVisitDetails.EveryDetail(12)}} AS detail
                FROM described
                WHERE session_key IN (SELECT session_key FROM judged)
            )
        SELECT
            detail.1 AS kind,
            detail.2 AS value,
            toInt64(count()) AS visits
        FROM detailed
        GROUP BY kind, value
        ORDER BY kind, visits DESC, value
        LIMIT {most_values:UInt32} BY kind
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
            ?? CompileFromNow(scope, query)
            ?? throw new NotSupportedException($"No statement is defined for {query.GetType().Name}.");
    }

    /// <summary>
    /// Compiles the count of what a set of sites delivered over a window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one statement here that is not bound to an authorisation decision, because it answers
    /// nobody's question: the sites come from the control plane and the answer goes into the
    /// installation's own accounting. It is a separate entry point rather than another case in
    /// <see cref="Compile(TenantScope, AnalyticsQuery)"/> for exactly that reason — every statement
    /// reachable from a request takes its site from the scope, and a case that took one from the
    /// question instead would put an exception to that rule where somebody could reach it.
    /// </para>
    /// <para>
    /// It counts pages delivered through the same fragment the headline totals are built from, so
    /// a figure counted for accounting and a figure shown on a screen are the same arithmetic
    /// rather than two derivations that agree until they do not.
    /// </para>
    /// </remarks>
    /// <param name="window">Which sites, and which stretch of time.</param>
    /// <returns>The statement and its bound values.</returns>
    public static CompiledStatement CompileVolume(SiteVolumeWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var sql = $$"""
            WITH
                windowed AS
                (
                    SELECT site_id, kind, surface, path, visitor_key, correlation_id
                    FROM events
                    WHERE site_id IN {site_ids:Array(UUID)}
                      AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                      AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                ),
                {{ReconciledEvents.ReconciliationPerSite}}
            SELECT
                site_id,
                toInt64(sum(page_views)) AS page_views
            FROM
            (
                SELECT
                    site_id,
                    visitor_key,
                    {{ReconciledEvents.DeliveredPageViews(8)}}
                FROM identified
                GROUP BY site_id, visitor_key, path
            )
            GROUP BY site_id
            ORDER BY site_id
            """;

        return new CompiledStatement(
            sql,
            [
                new QueryParameter(SiteIdsParameter, window.SiteIds.ToArray()),
                new QueryParameter(FromParameter, window.Range.From.ToUnixTimeMilliseconds()),
                new QueryParameter(ToParameter, window.Range.To.ToUnixTimeMilliseconds()),
            ]);
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
            SiteSourcesQuery sources => CompileSiteSources(scope, sources),
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
    /// <para>
    /// These begin from stored verdicts rather than from activity, so they see only visits that
    /// have been judged and answer for a slightly older window than the rest — which the interface
    /// states rather than papers over.
    /// </para>
    /// <para>
    /// Two of them may then reach back into the activity behind those verdicts, because what a
    /// visit was is a different thing from what it was concluded to be, is stored nowhere, and has
    /// to be rebuilt before it can be asked about.
    /// </para>
    /// </remarks>
    private static CompiledStatement? CompileFromVerdicts(TenantScope scope, AnalyticsQuery query) =>
        query switch
        {
            TrafficBreakdownQuery breakdown => CompileTrafficBreakdown(scope, breakdown),
            TrafficSeriesQuery series => CompileTrafficSeries(scope, series),
            JudgedSessionsQuery judged => CompileJudgedSessions(scope, judged),
            SiteVisitFacetsQuery facets => CompileSiteVisitFacets(scope, facets),
            _ => null,
        };

    /// <summary>
    /// Compiles a question about the present moment, or nothing where it is not one.
    /// </summary>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The statement, or <see langword="null"/> where this is not one of these questions.</returns>
    /// <remarks>
    /// <para>
    /// These are the only statements here that read a stretch of activity and nothing on either
    /// side of it. Every other question about visitors rebuilds visits, and a visit has to be read
    /// from where it began, so those reach a full day back; these describe a few minutes, and a
    /// report outside them is not part of that description.
    /// </para>
    /// <para>
    /// They are asked again every few seconds while somebody is watching, which is what the narrow
    /// reading buys and what the time limit each of them carries protects.
    /// </para>
    /// </remarks>
    private static CompiledStatement? CompileFromNow(TenantScope scope, AnalyticsQuery query) =>
        query switch
        {
            SiteLiveVisitorsQuery visitors => CompileSiteLiveVisitors(scope, visitors),
            SiteLiveActivityQuery activity => CompileSiteLiveActivity(scope, activity),
            SiteLivePagesQuery pages => CompileSiteLivePages(scope, pages),
            SiteLiveTrailQuery trail => CompileSiteLiveTrail(scope, trail),
            _ => null,
        };

    /// <summary>
    /// Where each column of a reading about who is here sits, so the statement and the reader
    /// cannot drift.
    /// </summary>
    internal static class LiveVisitorColumn
    {
        /// <summary>The visitor's derived identity, after both halves were folded onto one key.</summary>
        public const int VisitorKey = 0;

        /// <summary>The first report from them inside the window.</summary>
        public const int FirstSeen = 1;

        /// <summary>The last.</summary>
        public const int LastSeen = 2;

        /// <summary>How many pages they were on during it.</summary>
        public const int PageCount = 3;

        /// <summary>The page they were on most recently.</summary>
        public const int CurrentPath = 4;

        /// <summary>The pages themselves, oldest first, capped at what the caller asked for.</summary>
        public const int Requests = 5;

        /// <summary>Which capture surfaces saw them.</summary>
        public const int Surfaces = 6;

        /// <summary>What they said they were.</summary>
        public const int UserAgent = 7;

        /// <summary>The language they asked for.</summary>
        public const int Language = 8;

        /// <summary>Widest viewport reported.</summary>
        public const int ViewportWidth = 9;

        /// <summary>Milliseconds the pages were in front of them, added up across the window.</summary>
        public const int EngagedMs = 10;

        /// <summary>Furthest any page was scrolled.</summary>
        public const int MaxScrollDepthPercent = 11;

        /// <summary>How many reports could see pointer activity.</summary>
        public const int PointerObserved = 12;

        /// <summary>How many of those saw some.</summary>
        public const int PointerSeen = 13;

        /// <summary>How many reports could see keyboard activity.</summary>
        public const int KeyboardObserved = 14;

        /// <summary>How many of those saw some.</summary>
        public const int KeyboardSeen = 15;

        /// <summary>How many reports could see an automation declaration.</summary>
        public const int WebDriverObserved = 16;

        /// <summary>How many of those carried one.</summary>
        public const int WebDriverSeen = 17;

        /// <summary>Routing number of the network they arrived over, or nought.</summary>
        public const int AutonomousSystem = 18;

        /// <summary>Who runs that network, as the routing registry names them.</summary>
        public const int NetworkOwner = 19;

        /// <summary>The company that vouches for the address they arrived from.</summary>
        public const int ConfirmedOperator = 20;

        /// <summary>The site that sent them.</summary>
        public const int SendingSite = 21;

        /// <summary>What kind of place that was.</summary>
        public const int SourceKind = 22;

        /// <summary>The country they arrived from.</summary>
        public const int Country = 23;

        /// <summary>The town within it.</summary>
        public const int Town = 24;

        /// <summary>Who runs the network, as this product names them.</summary>
        public const int Network = 25;

        /// <summary>The kind of device.</summary>
        public const int Device = 26;

        /// <summary>The browser.</summary>
        public const int Browser = 27;

        /// <summary>The operating system underneath it.</summary>
        public const int System = 28;

        /// <summary>How many visitors the window held altogether, before the list was cut short.</summary>
        public const int VisitorsSeen = 29;
    }

    /// <summary>
    /// Gathers everyone seen in the last stretch of minutes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One row per visitor and none per visit. What a reader is asking is who is on the site, and a
    /// visit is a poor answer to that: a departure reported when a tab is finally dismissed reopens
    /// a visit whose reader left hours ago, and a reader with an old tab open has two of them
    /// running at once. Neither is somebody who is here. Which visitors reported in the last few
    /// minutes has no such trouble, and needs no visit boundaries to settle.
    /// </para>
    /// <para>
    /// Every visitor is carried back with both accounts of themselves at once: the closed set the
    /// detection engine reasons about, and the separate set of facts a reader is shown. Asking twice
    /// would be two readings of the same minutes taken moments apart, which is how a screen comes to
    /// print a page count that disagrees with the pages beside it.
    /// </para>
    /// <para>
    /// The address the visitor arrived from is deliberately not among them. It is the one personal
    /// value on the row; the visit reconstruction carries it only to settle whose crawlers an
    /// address belongs to before the engine is asked anything, and there is no such question here —
    /// what a company publishes about its own machines was settled by the collector and travels as
    /// an answer.
    /// </para>
    /// <para>
    /// How many visitors there were is counted over every group the window produced rather than over
    /// the rows that fitted, so a busy site says how busy it is instead of describing the size of
    /// its own answer.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSiteLiveVisitors(TenantScope scope, SiteLiveVisitorsQuery query)
    {
        var sql = $$"""
            WITH
                {{LiveActivity.WithSources(
                    LiveActivity.EveryVisitor,
                    "event_id",
                    "surface",
                    "visitor_key",
                    "correlation_id",
                    "server_ts",
                    "kind",
                    "path",
                    "status_code",
                    "user_agent",
                    "language",
                    "viewport_width",
                    "engaged_ms",
                    "scroll_depth_percent",
                    "had_pointer_interaction",
                    "had_keyboard_interaction",
                    "declared_web_driver",
                    "country_code",
                    "city",
                    "autonomous_system",
                    "network_owner",
                    "confirmed_operator",
                    "device_class",
                    "browser_family",
                    "operating_system")}},
                gathered AS
                (
                    SELECT
                        visitor_key,
                        min(server_ts) AS first_seen,
                        max(server_ts) AS last_seen,
                        toUInt32(countIf(opens_page)) AS page_count,
                        argMax(path, (server_ts, event_id)) AS current_path,
                        groupArraySortedIf({max_requests:UInt32})(
                            (toUnixTimestamp64Milli(server_ts), path, status_code),
                            opens_page) AS requests,
                        groupUniqArray(toString(surface)) AS surfaces,
                        anyIf(user_agent, user_agent != '') AS user_agent,
                        anyIf(language, language != '') AS language,
                        max(viewport_width) AS viewport_width,
                        sumIf(page_engaged_ms, opens_page) AS engaged_ms,
                        max(scroll_depth_percent) AS max_scroll_depth_percent,
                        toUInt32(countIf(had_pointer_interaction != 'Unobserved')) AS pointer_observed,
                        toUInt32(countIf(had_pointer_interaction = 'Yes')) AS pointer_seen,
                        toUInt32(countIf(had_keyboard_interaction != 'Unobserved')) AS keyboard_observed,
                        toUInt32(countIf(had_keyboard_interaction = 'Yes')) AS keyboard_seen,
                        toUInt32(countIf(declared_web_driver != 'Unobserved')) AS web_driver_observed,
                        toUInt32(countIf(declared_web_driver = 'Yes')) AS web_driver_seen,
                        max(autonomous_system) AS autonomous_system,
                        anyIf(network_owner, network_owner != '') AS network_owner,
                        anyIf(confirmed_operator, confirmed_operator != '') AS confirmed_operator,
                        argMinIf(source_site, (server_ts, event_id), sending_host != '') AS from_site,
                        argMinIf(source_channel, (server_ts, event_id), sending_host != '') AS from_kind,
                        argMinIf(country_code, (server_ts, event_id), country_code != '') AS country,
                        argMinIf(city, (server_ts, event_id), city != '') AS town,
                        argMinIf(
                            toString(device_class),
                            (server_ts, event_id),
                            device_class != 'Unknown') AS device,
                        argMinIf(browser_family, (server_ts, event_id), browser_family != '') AS browser,
                        argMinIf(
                            operating_system,
                            (server_ts, event_id),
                            operating_system != '') AS system_name,
                        toUInt32(count() OVER ()) AS visitors_seen
                    FROM present
                    GROUP BY visitor_key
                )
            SELECT
                visitor_key,
                first_seen,
                last_seen,
                page_count,
                current_path,
                requests,
                surfaces,
                user_agent,
                language,
                viewport_width,
                engaged_ms,
                max_scroll_depth_percent,
                pointer_observed,
                pointer_seen,
                keyboard_observed,
                keyboard_seen,
                web_driver_observed,
                web_driver_seen,
                autonomous_system,
                network_owner,
                confirmed_operator,
                from_site,
                from_kind,
                country,
                town,
                {{NamedNetworks.From(4)}} AS network,
                device,
                browser,
                system_name,
                visitors_seen
            FROM gathered
            ORDER BY last_seen DESC, visitor_key
            LIMIT {limit:UInt32}
            {{LiveActivity.WithinTheBeat}}
            """;

        return new CompiledStatement(
            sql,
            [
                .. WindowParameters(scope, query.Range),
                .. CatalogueParameters(query.SiteDomain),
                .. NetworkNames(),
                new QueryParameter(MaxRequestsParameter, (uint)SiteLiveVisitorsQuery.MostRequests),
                new QueryParameter(LimitParameter, (uint)query.Limit),
            ]);
    }

    /// <summary>
    /// Counts how much of a site was read in each minute of the last stretch of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every minute the window covers comes back, including the ones nothing happened in. A store
    /// answers only about minutes that produced a row, and a drawing built from those alone would
    /// close the gaps up and show a busy half hour where there was a quiet one — so the empty
    /// minutes are filled here, where the window is known, rather than reconstructed by whoever
    /// draws them.
    /// </para>
    /// <para>
    /// Counted over the whole window rather than over the visitors an answer had room for, because
    /// this describes the site and not a hundred of its readers. A page reported by both halves of
    /// the measurement is the one delivery it was, on the same terms as every other count of pages
    /// in this file.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSiteLiveActivity(TenantScope scope, SiteLiveActivityQuery query)
    {
        var sql = $$"""
            WITH
                {{LiveActivity.Window(
                    "event_id",
                    "surface",
                    "visitor_key",
                    "correlation_id",
                    "server_ts",
                    "kind",
                    "path")}},
                {{ReconciledEvents.Reconciliation}},
                delivered AS
                (
                    SELECT
                        toStartOfMinute(server_ts) AS minute,
                        visitor_key,
                        path,
                        {{ReconciledEvents.DeliveredPageViews(12)}}
                    FROM identified
                    GROUP BY minute, visitor_key, path
                )
            SELECT
                minute,
                toInt64(sum(page_views)) AS page_views
            FROM delivered
            GROUP BY minute
            ORDER BY minute WITH FILL
                FROM toStartOfMinute(fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC'))
                TO toStartOfMinute(fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')) + INTERVAL 1 MINUTE
                STEP INTERVAL 1 MINUTE
            {{LiveActivity.WithinTheBeat}}
            """;

        return new CompiledStatement(sql, [.. WindowParameters(scope, query.Range)]);
    }

    /// <summary>
    /// Ranks the pages a site's visitors have been on in the last stretch of minutes.
    /// </summary>
    /// <remarks>
    /// How many visitors a page held is counted beside how often it was delivered, because a page
    /// one visitor reloaded twenty times and a page twenty visitors opened are the same number and
    /// not the same news. Both are counted the way the rest of the product counts a page, so this
    /// list and the busiest-pages list for a longer period are answering with the same arithmetic.
    /// </remarks>
    private static CompiledStatement CompileSiteLivePages(TenantScope scope, SiteLivePagesQuery query)
    {
        var sql = $$"""
            WITH
                {{LiveActivity.Window(
                    "event_id",
                    "surface",
                    "visitor_key",
                    "correlation_id",
                    "server_ts",
                    "kind",
                    "path")}},
                {{ReconciledEvents.Reconciliation}}
            SELECT
                path,
                toInt64(sum(page_views)) AS page_views,
                toInt64(uniqExactIf(visitor_key, visitor_key != '')) AS visitors
            FROM
            (
                SELECT
                    path,
                    visitor_key,
                    {{ReconciledEvents.DeliveredPageViews(8)}}
                FROM identified
                GROUP BY path, visitor_key
            )
            GROUP BY path
            HAVING page_views > 0
            ORDER BY page_views DESC, path
            LIMIT {limit:UInt32}
            {{LiveActivity.WithinTheBeat}}
            """;

        return new CompiledStatement(
            sql,
            [
                .. WindowParameters(scope, query.Range),
                new QueryParameter(LimitParameter, (uint)query.Limit),
            ]);
    }

    /// <summary>
    /// Reads back what one visitor has been doing in the last stretch of minutes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its own statement rather than the visit reconstruction narrowed to one visitor, and the
    /// reasons are three. That statement is read from where a visit began until as long as one may
    /// last, clamped to the instant a verdict was written — which for a visit still under way is no
    /// upper bound at all, until a verdict appears and snaps it back. It reads forward with no
    /// reach-back, so a departure arriving from a stale tab is folded into whichever visit last
    /// arrived at that page, and can print an hour of reading against a page opened three minutes
    /// ago. And the page count beside it is taken over a different window again, so a panel built on
    /// it would say "three of four pages" for a reason that is nothing but a difference in timing.
    /// </para>
    /// <para>
    /// This reads the same stretch of minutes the visitor was listed from, so the trail and the row
    /// it was opened from are one reading of one window. A step is a page the visitor was on during
    /// it, gathered once however many reports described it and whichever surfaces sent them — which
    /// is exactly what the list counted, so the number of steps and the number beside the row agree
    /// by construction. Somebody who leaves a page and comes back to it inside the window is on one
    /// page here, because there are no visit boundaries in this question for "again" to mean
    /// anything against.
    /// </para>
    /// <para>
    /// Presses are laid alongside the pages rather than folded into them, on the same terms as the
    /// visit reconstruction: a page is every report about it reduced to one row, while somebody who
    /// pressed the same control twice pressed it twice. Where the two share an instant the page
    /// comes first, since a control cannot be operated on a page nobody has reached. A page nothing
    /// but a press was seen on is still a page, which is what a visitor who arrived before the
    /// window opened looks like.
    /// </para>
    /// <para>
    /// Nothing is established about the visitor here, and no catalogue is consulted. Where they
    /// were, what they were reading on and who sent them travel on the row this was opened from,
    /// settled over these same minutes; asking again would be a second reading taken moments later
    /// and free to disagree with the first. What nothing could be measured on is carried as minus
    /// one, on the same terms as the visit reconstruction and read back by the same code.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSiteLiveTrail(TenantScope scope, SiteLiveTrailQuery query)
    {
        var sql = $$"""
            WITH
                {{LiveActivity.Window(
                    "event_id",
                    "surface",
                    "visitor_key",
                    "correlation_id",
                    "server_ts",
                    "kind",
                    "path",
                    "status_code",
                    "engaged_ms",
                    "scroll_depth_percent",
                    "action_control",
                    "action_label",
                    "action_target",
                    "action_target_kind")}},
                {{ReconciledEvents.Reconciliation}},
                theirs AS
                (
                    SELECT *
                    FROM identified
                    WHERE visitor_key = {visitor_key:String}
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
                    FROM theirs
                    GROUP BY path
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
                    FROM theirs
                    WHERE kind = 'Action'
                )
            SELECT
                at, press, path, status_code, engaged_ms, depth, label, control, target, target_kind
            FROM
            (
                SELECT * FROM pages
                UNION ALL
                SELECT * FROM pressed
            ) AS steps
            ORDER BY at, press, path
            LIMIT {limit:UInt32}
            {{LiveActivity.WithinTheBeat}}
            """;

        return new CompiledStatement(
            sql,
            [
                .. WindowParameters(scope, query.Range),
                new QueryParameter(VisitorKeyParameter, query.VisitorKey),
                new QueryParameter(LimitParameter, (uint)query.Limit),
            ]);
    }

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
    /// reading that never happened. Which arrival a report belongs to is settled in
    /// <see cref="VisitGrouping"/>, which is also what the visit's own page count is taken from.
    /// </para>
    /// <para>
    /// That alone would not make the two agree, and this is read up to the verdict's own last
    /// instant so that they do — see <see cref="SendingSites.ThePeriodUpToTheVerdict"/>. The engine
    /// judges a visit as soon as it is over and never returns to it, while activity keeps arriving,
    /// so the same expression over everything stored today answers a wider question than the one
    /// the verdict answered. What is shown here is the visit the verdict was about.
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
    /// <para>
    /// Every row carries the same account of who the visit was — where it was sent from, where it
    /// was, whose network it was on, and what it was read with. It is settled once over the whole
    /// visit and repeated rather than answered by a second question, because a visit is opened by
    /// somebody pressing a row and two questions where one will do doubles that cost for an answer
    /// nobody can act on until both arrive. Each fact is taken as the first report that carried
    /// one: geography and software are resolved per report and a visit watched by both halves has
    /// reports that resolved neither.
    /// </para>
    /// <para>
    /// The network is named from the hosting catalogue, which is what the card ranking a window's
    /// networks and the filter narrowing a list to one of them both name it from. A visit opened
    /// out of that list has to agree with the row it was opened from, and a registry's own
    /// description is a different string for the same company.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileSiteVisitJourney(TenantScope scope, SiteVisitJourneyQuery query)
    {
        var sql = $$"""
            WITH
                {{SendingSites.Of(
                    SendingSites.ThePeriodUpToTheVerdict,
                    "event_id",
                    "surface",
                    "visitor_key",
                    "correlation_id",
                    "server_ts",
                    "kind",
                    "path",
                    "status_code",
                    "engaged_ms",
                    "scroll_depth_percent",
                    "action_control",
                    "action_label",
                    "action_target",
                    "action_target_kind",
                    "country_code",
                    "city",
                    "autonomous_system",
                    "network_owner",
                    "device_class",
                    "browser_family",
                    "operating_system")}},
                {{ReconciledEvents.Reconciliation}},
                {{VisitGrouping.Of("visitor_key = {visitor_key:String}")}},
                stepped AS
                (
                    SELECT *
                    FROM opened
                    WHERE visit_ordinal = 0
                ),
                gathered AS
                (
                    SELECT
                        argMinIf(source_site, (server_ts, event_id), sending_host != '') AS from_site,
                        argMinIf(source_channel, (server_ts, event_id), sending_host != '') AS from_kind,
                        argMinIf(country_code, (server_ts, event_id), country_code != '') AS country,
                        argMinIf(city, (server_ts, event_id), city != '') AS town,
                        argMinIf(network_owner, (server_ts, event_id), network_owner != '') AS network_owner,
                        max(autonomous_system) AS autonomous_system,
                        argMinIf(
                            toString(device_class),
                            (server_ts, event_id),
                            device_class != 'Unknown') AS device,
                        argMinIf(browser_family, (server_ts, event_id), browser_family != '') AS browser,
                        argMinIf(
                            operating_system,
                            (server_ts, event_id),
                            operating_system != '') AS system_name
                    FROM stepped
                ),
                context AS
                (
                    SELECT
                        from_site,
                        from_kind,
                        country,
                        town,
                        {{NamedNetworks.From(12)}} AS network,
                        device,
                        browser,
                        system_name
                    FROM gathered
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
                    GROUP BY path, page_ordinal
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
            SELECT
                at, press, path, status_code, engaged_ms, depth, label, control, target, target_kind,
                from_site, from_kind, country, town, network, device, browser, system_name
            FROM
            (
                SELECT * FROM pages
                UNION ALL
                SELECT * FROM pressed
            ) AS steps
            CROSS JOIN context
            ORDER BY at, press, path
            LIMIT {limit:UInt32}
            """;

        return new CompiledStatement(
            sql,
            [
                .. WindowParameters(scope, query.Range),
                .. CatalogueParameters(query.SiteDomain),
                .. NetworkNames(),
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
    /// Counts judged visits by what generated them, bucket by bucket.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same reduction to one verdict per visit as <see cref="CompileTrafficBreakdown"/>, cut a
    /// second way. Every column is taken from that one ruleset's row, the bucket included, so a
    /// visit cannot be dated by one ruleset and counted by another.
    /// </para>
    /// <para>
    /// A visit falls whole into the bucket it began in, which is the column the store is
    /// partitioned and indexed on — so a window skips whole parts rather than reading and
    /// discarding them.
    /// </para>
    /// <para>
    /// The fill restarts for each category, because <c>category</c> is ordered ahead of the filled
    /// column and its value carries into the rows the fill invents. That is what makes every
    /// group's counts as long as the bucket list and in the same order, and it is also why a
    /// category the window never held is absent altogether rather than a run of zeroes.
    /// </para>
    /// </remarks>
    private static CompiledStatement CompileTrafficSeries(TenantScope scope, TrafficSeriesQuery query)
    {
        var bucket = BucketFunctions[query.Granularity];
        var step = StepIntervals[query.Granularity];

        // The upper fill bound is derived from one millisecond before the exclusive end of the
        // window, for the reason CompileTimeSeries gives: bounding it on the end instant itself
        // appends an empty bucket whenever a window ends exactly on a boundary, and truncates the
        // final partial bucket whenever it does not.
        var sql = $$"""
            SELECT
                category,
                bucket,
                toInt64(count()) AS sessions,
                toInt64(sum(page_count)) AS page_views
            FROM
            (
                SELECT
                    session_key,
                    argMax(category, (ruleset_major, ruleset_minor, classified_at)) AS category,
                    argMax(page_count, (ruleset_major, ruleset_minor, classified_at)) AS page_count,
                    {{bucket}}(
                        argMax(started_at, (ruleset_major, ruleset_minor, classified_at)),
                        {time_zone:String}) AS bucket
                FROM session_classifications
                WHERE site_id = {site_id:UUID}
                  AND started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                  AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                GROUP BY session_key
            )
            GROUP BY category, bucket
            ORDER BY category, bucket
            WITH FILL
                FROM {{bucket}}(fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC'), {time_zone:String})
                TO {{bucket}}(fromUnixTimestamp64Milli({to_ms:Int64} - 1, 'UTC'), {time_zone:String}) + {{step}}
                STEP {{step}}
            """;

        return new CompiledStatement(
            sql,
            [.. WindowParameters(scope, query.Range), new QueryParameter(TimeZoneParameter, scope.TimeZoneId)]);
    }
    /// <summary>
    /// Returns individual judged visits with the evidence behind each verdict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two statements rather than one, chosen by what the caller narrowed to. Everything a verdict
    /// itself holds is a condition on rows the store already has; anything about the activity behind
    /// a verdict has to be rebuilt from events before it can be compared against anything, and
    /// rebuilding unconditionally would charge every reader of the ordinary list for work nobody
    /// asked for.
    /// </para>
    /// <para>
    /// Within each shape the text does not vary, which is what keeps one plan for the store to reuse
    /// and one statement to approve rather than one per combination somebody might ask for.
    /// </para>
    /// </remarks>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The statement and its bound values.</returns>
    private static CompiledStatement CompileJudgedSessions(TenantScope scope, JudgedSessionsQuery query) =>
        query.Narrowing.ReadsActivity
            ? CompileJudgedByDetail(scope, query)
            : CompileJudgedByVerdict(scope, query);

    /// <summary>
    /// The shape for a narrowing the stored verdicts answer on their own.
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
    /// <para>
    /// What the caller narrowed to is applied <em>after</em> the reduction to one row per visit, so
    /// a visit is kept or dropped on the verdict a reader would be shown. Applied inside, a visit
    /// re-judged into a different category under newer rules would still be found by its old one.
    /// The count of the whole window is taken after the narrowing for the same reason: a list that
    /// says how far through it somebody is has to be counting the list they are looking at.
    /// </para>
    /// </remarks>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The statement and its bound values.</returns>
    private static CompiledStatement CompileJudgedByVerdict(TenantScope scope, JudgedSessionsQuery query) =>
        new(JudgedVerdicts, VerdictNarrowing(scope, query));

    /// <summary>
    /// The shape for a narrowing that also asks what the visits themselves were.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape above with the rebuild in front of it and one further condition on the same outer
    /// selection — so the narrowing still applies after each visit has been reduced to one verdict,
    /// and the count still describes the list the reader is looking at.
    /// </para>
    /// <para>
    /// The nine conditions are written out whatever was asked for, each empty set meaning "all of
    /// them". A reader who picked one country and a reader who picked a country, a browser and a
    /// landing page send the store the same statement with different values in it.
    /// </para>
    /// <para>
    /// It is joined on the identity the detection engine derived, which is reproduced from activity
    /// rather than stored a second time. <see cref="JudgedVisitDetails"/> is where that is done and
    /// why.
    /// </para>
    /// </remarks>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The statement and its bound values.</returns>
    private static CompiledStatement CompileJudgedByDetail(TenantScope scope, JudgedSessionsQuery query) =>
        new(
            JudgedVerdictsByDetail,
            [
                .. VerdictNarrowing(scope, query),
                .. CatalogueParameters(query.SiteDomain),
                .. NetworkNames(),
                new QueryParameter(IdleParameter, (long)query.IdleTimeout.TotalSeconds),
                LongestVisit(),
                .. DetailNarrowing(query.Narrowing),
            ]);

    /// <summary>
    /// Counts what each detail of a window's judged visits held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One pass over the rebuild rather than nine. The store writes a common table expression out
    /// again wherever it is named, so nine selections laid end to end would each rebuild every visit
    /// in the window; laying the nine details out as an array and unfolding it costs one.
    /// </para>
    /// <para>
    /// Only visits that have been judged take part, and only those the rebuild could describe. A
    /// visit whose activity has already aged out has no details left to offer, and offering it as
    /// "nothing established" would be a claim about the visitor rather than about the store.
    /// </para>
    /// <para>
    /// Each detail keeps its commonest values and stops there, which is what makes the answer a
    /// list somebody can read rather than every page a busy site has ever served.
    /// </para>
    /// </remarks>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The statement and its bound values.</returns>
    private static CompiledStatement CompileSiteVisitFacets(TenantScope scope, SiteVisitFacetsQuery query) =>
        new(
            VisitDetailCounts,
            [
                .. WindowParameters(scope, query.Range),
                .. CatalogueParameters(query.SiteDomain),
                .. NetworkNames(),
                new QueryParameter(IdleParameter, (long)query.IdleTimeout.TotalSeconds),
                LongestVisit(),
                new QueryParameter(MostValuesParameter, (uint)SiteVisitFacetsQuery.MostValues),
            ]);

    /// <summary>
    /// The window, the slice, and what was narrowed to among the things a verdict holds.
    /// </summary>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The values both shapes bind.</returns>
    private static QueryParameter[] VerdictNarrowing(TenantScope scope, JudgedSessionsQuery query) =>
    [
        .. WindowParameters(scope, query.Range),
        new QueryParameter(LimitParameter, (uint)query.Limit),
        new QueryParameter(OffsetParameter, (uint)query.Offset),
        new QueryParameter(CategoriesParameter, AsStored(query.Narrowing.Categories)),
        new QueryParameter(StrengthsParameter, AtLeast(query.Narrowing.LeastStrength)),
        new QueryParameter(LeastPagesParameter, (uint)query.Narrowing.LeastPages),
    ];

    /// <summary>
    /// What was narrowed to among the things the visit itself was, as the store spells them.
    /// </summary>
    /// <remarks>
    /// All nine, always, because the statement names all nine however few were asked for. An empty
    /// one is the question "all of them", and the statement says so rather than being assembled a
    /// second way.
    /// </remarks>
    /// <param name="narrowing">What the caller asked for.</param>
    /// <returns>The nine arrays.</returns>
    private static QueryParameter[] DetailNarrowing(VisitNarrowing narrowing) =>
    [
        new(DevicesParameter, AsStored(narrowing.Devices)),
        new(SourceKindsParameter, AsStored(narrowing.SourceKinds)),
        new(BrowsersParameter, narrowing.Browsers.ToArray()),
        new(SystemsParameter, narrowing.OperatingSystems.ToArray()),
        new(CountriesParameter, narrowing.Countries.ToArray()),
        new(TownsParameter, narrowing.Towns.ToArray()),
        new(NetworksParameter, narrowing.Networks.ToArray()),
        new(SourcesParameter, narrowing.Sources.ToArray()),
        new(EntryPagesParameter, narrowing.EntryPages.ToArray()),
    ];

    /// <summary>
    /// The conclusions asked for, named as the store spells them.
    /// </summary>
    /// <param name="categories">The conclusions.</param>
    /// <returns>The stored names.</returns>
    private static string[] AsStored(ImmutableArray<TrafficCategory> categories) =>
        [.. categories.Select(category => StoredNames.CategoryNames[category])];

    /// <summary>
    /// The kinds of device asked for, named as the rebuild holds them.
    /// </summary>
    /// <remarks>
    /// A device nothing established is held as an empty text rather than as the word the vocabulary
    /// spells it by. The rebuild carries the first report that said anything, and where none did
    /// there is nothing to carry — so asking for the visits nothing was established about is asking
    /// for the empty text, and reading a visit back performs the same mapping the other way round.
    /// </remarks>
    /// <param name="devices">The kinds of device.</param>
    /// <returns>The stored names.</returns>
    private static string[] AsStored(ImmutableArray<DeviceClass> devices) =>
        [.. devices.Select(device =>
            device == DeviceClass.Unknown ? string.Empty : StoredNames.DeviceClassNames[device])];

    /// <summary>
    /// The kinds of source asked for, named as the rebuild holds them.
    /// </summary>
    /// <remarks>
    /// A visit nothing sent is held as an empty text, on the same terms and for the same reason as
    /// a device nothing established. It is not "nobody sent them": it is "nothing said who did".
    /// </remarks>
    /// <param name="kinds">The kinds of source.</param>
    /// <returns>The stored names.</returns>
    private static string[] AsStored(ImmutableArray<SourceChannel> kinds) =>
        [.. kinds.Select(kind =>
            kind == SourceChannel.Direct ? string.Empty : TrafficSources.Spelling(kind))];

    /// <summary>
    /// The strengths that count as at least the one asked for, named as the store spells them.
    /// </summary>
    /// <remarks>
    /// Written out as a set rather than compared as a number, so the ordering of the bands stays a
    /// fact about this product's own enumeration and never a fact about which number the store
    /// happened to record each one under. Nothing asked for means every band, which is the same
    /// empty set the categories use.
    /// </remarks>
    /// <param name="least">The lowest band worth returning, or nothing for all of them.</param>
    /// <returns>The stored names, or an empty set.</returns>
    private static string[] AtLeast(EvidenceStrength? least) =>
        least is null
            ? []
            : [.. Enum.GetValues<EvidenceStrength>()
                .Where(strength => strength >= least.Value)
                .Select(strength => StoredNames.StrengthNames[strength])];

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
        var country = PlaceCountryColumns[query.Grouping];

        var sql = $$"""
            WITH
                windowed AS
                (
                    SELECT kind, surface, path, visitor_key, correlation_id, country_code, city, network_owner, autonomous_system
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
                        anyIf(network_owner, network_owner != '') AS network_owner,
                        max(autonomous_system) AS autonomous_system,
                        toInt64(sum(page_views)) AS page_views
                    FROM
                    (
                        SELECT
                            visitor_key,
                            path,
                            anyIf(country_code, country_code != '') AS country_code,
                            anyIf(city, city != '') AS city,
                            anyIf(network_owner, network_owner != '') AS network_owner,
                            max(autonomous_system) AS autonomous_system,
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
                    {{country}} AS country_code,
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
                .. CatalogueOfNetworks(query.Grouping),
                new QueryParameter(LimitParameter, (uint)query.Limit),
                new QueryParameter(OffsetParameter, (uint)query.Offset),
            ]);
    }

    /// <summary>
    /// Counts visitors, grouped by where they came from before they arrived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counted per visitor and settled once for the whole visitor, on exactly the terms a place
    /// list is. Only a visit's first page carries an address from anywhere else — every page after
    /// it was reached from the site itself — so taking each report's own answer would file one
    /// arrival from a search engine as one arrival from the search engine and a dozen from the
    /// site being measured.
    /// </para>
    /// <para>
    /// What counts as the site being measured is its registered address and anything below it,
    /// which is the rule the collector applies when it decides whose traffic a report is. A site
    /// reachable at two names, or spread across a documentation subdomain and a main one, would
    /// otherwise list itself as its own busiest source.
    /// </para>
    /// <para>
    /// That address is bound as a parameter rather than written into the statement. It is the
    /// only value in this file that comes from the control plane instead of from a fixed table of
    /// identifiers, and the two are kept apart on purpose.
    /// </para>
    /// <para>
    /// An arrival naming nowhere is the empty string, and is a row like any other. It is usually
    /// the largest row on the list, and dropping it would leave every share on the screen taken
    /// against a total that excluded most of the audience. The empty string means exactly that in
    /// all three groupings, which is what lets one aggregate settle a visitor's source whichever
    /// way the list is being read.
    /// </para>
    /// <para>
    /// <b>One site is one row whatever address it answered on.</b> A leading <c>www.</c> is cut,
    /// and the rest is reduced to the label in front of the public suffix — so <c>google.com</c>,
    /// <c>www.google.co.in</c> and <c>search.yahoo.co.jp</c> become <c>google</c>, <c>google</c>
    /// and <c>yahoo</c>. Without it the busiest source of a site's traffic is spread over a dozen
    /// rows and appears on none of them at its real size.
    /// </para>
    /// <para>
    /// That reduction is also what makes the lookup safe against the referrer being written by
    /// whoever visited the site. Matching any label would let somebody who registers
    /// <c>google.attacker.test</c> file their traffic under Google's name on a stranger's
    /// dashboard; taking the label in front of the suffix gives <c>attacker</c>.
    /// </para>
    /// <para>
    /// The catalogue itself is bound as three parallel arrays rather than written into the
    /// statement, so what a reviewer reads here stays the shape of the question rather than a list
    /// of a hundred hostnames. It is applied here rather than stored at ingest deliberately: a
    /// correction to it re-answers every period a site has ever recorded, where a stored column
    /// would leave the same visit classified two ways depending on when it happened to arrive.
    /// </para>
    /// </remarks>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The statement.</returns>
    private static CompiledStatement CompileSiteSources(TenantScope scope, SiteSourcesQuery query)
    {
        var source = SourceExpressions[query.Grouping];
        var sendingSite = SourceSiteExpressions[query.Grouping];

        var sql = $$"""
            WITH
                {{SendingSites.Of(SendingSites.ThePeriod, "kind", "surface", "path", "visitor_key", "correlation_id")}},
                {{ReconciledEvents.Reconciliation}},
                sourced AS
                (
                    SELECT
                        visitor_key,
                        anyIf(source, source != '') AS source,
                        anyIf(site, site != '') AS site,
                        toInt64(sum(page_views)) AS page_views
                    FROM
                    (
                        SELECT
                            visitor_key,
                            path,
                            anyIf({{source}}, source_site != '') AS source,
                            anyIf({{sendingSite}}, source_site != '') AS site,
                            {{ReconciledEvents.DeliveredPageViews(16)}}
                        FROM identified
                        WHERE visitor_key != ''
                        GROUP BY visitor_key, path
                    )
                    GROUP BY visitor_key
                )
            SELECT
                source,
                site,
                visitors,
                page_views,
                toInt64(sum(visitors) OVER ()) AS total_visitors,
                toInt64(count() OVER ()) AS total_sources,
                toInt64(max(visitors) OVER ()) AS most_visitors
            FROM
            (
                SELECT
                    source,
                    site,
                    toInt64(count()) AS visitors,
                    toInt64(sum(page_views)) AS page_views
                FROM sourced
                GROUP BY source, site
            )
            ORDER BY visitors DESC, source, site
            LIMIT {limit:UInt32} OFFSET {offset:UInt32}
            """;

        return new CompiledStatement(
            sql,
            [
                .. WindowParameters(scope, query.Range),
                .. CatalogueParameters(query.SiteDomain),
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
    /// What <see cref="SendingSites"/> needs bound: the measured site's own address, and the
    /// catalogue as three parallel arrays.
    /// </summary>
    /// <remarks>
    /// The catalogue is applied when the question is asked rather than resolved into a stored
    /// column when the traffic arrives, so correcting an entry re-answers every period a site has
    /// already recorded instead of leaving the same visit classified two ways depending on when it
    /// happened to arrive. Binding it also keeps a hostile referrer on the value side of the
    /// boundary, which is the rule the whole compiler is built on.
    /// </remarks>
    /// <param name="siteDomain">
    /// The measured site's own address, so it never appears as a source of its own traffic. Read
    /// from the site catalogue by the endpoint and never from the request, so a caller cannot
    /// decide whose traffic is left out of somebody else's answer.
    /// </param>
    /// <returns>The five values, in the order the statements bind them.</returns>
    private static QueryParameter[] CatalogueParameters(string siteDomain) =>
    [
        new(SiteDomainParameter, siteDomain),
        new(SuffixesParameter, SecondLevels),
        new(SourceKeysParameter, SourceKeys),
        new(SourceNamesParameter, SourceNames),
        new(SourceChannelsParameter, SourceChannels),
    ];

    /// <summary>
    /// The catalogue of hosting networks, bound only where a statement names it.
    /// </summary>
    /// <remarks>
    /// Conditional because the other two place groupings never read it, and a statement carrying
    /// bound values it does not mention is harder to review than one that does not.
    /// </remarks>
    /// <param name="grouping">What the place list is grouped by.</param>
    /// <returns>The two arrays, or nothing where the grouping does not read them.</returns>
    private static QueryParameter[] CatalogueOfNetworks(LocationGrouping grouping) =>
        grouping == LocationGrouping.Network ? NetworkNames() : [];

    /// <summary>
    /// The catalogue of hosting networks, as the two arrays a statement that names one binds.
    /// </summary>
    /// <remarks>
    /// Written once because three statements name a network: the list that ranks the networks a
    /// window's visitors arrived over, the rebuild that lets a reader narrow a list of visits to
    /// one of them, and the account a single opened visit gives of itself.
    /// </remarks>
    /// <returns>The two arrays.</returns>
    private static QueryParameter[] NetworkNames() =>
    [
        new(HostingNumbersParameter, HostingNumbers),
        new(HostingNamesParameter, HostingNames),
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
    private static QueryParameter LongestVisit() =>
        new(LongestVisitParameter, (long)VisitorKeys.LongestVisit.TotalSeconds);
}
