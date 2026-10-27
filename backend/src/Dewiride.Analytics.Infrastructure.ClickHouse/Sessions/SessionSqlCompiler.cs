using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Sessions;

/// <summary>
/// Writes the statement that groups stored activity into visits.
/// </summary>
/// <remarks>
/// <para>
/// The other half of the pair that produces every statement this product sends. The analytics
/// compiler answers questions a signed-in person asked and is bound to an authorisation decision;
/// this one answers a question the engine asks about a site it was handed, and is reached only by
/// background work. Both follow the same rule and for the same reason: the text is written here,
/// and every value is bound.
/// </para>
/// <para>
/// The grouping into visits and into the pages they went to, and the marking of a page's second
/// reporter, are written in <see cref="VisitGrouping"/> rather than here, because more than one
/// statement now needs them and a visit — and a page within one — has to mean the same thing in
/// all of them. That is what keeps the pages the engine judged and the pages a reader is shown for
/// the same visit from being two different answers. What belongs to this statement alone is what
/// it makes of a visit once it has one: the evidence the detection engine is allowed to reason
/// about, and nothing else.
/// </para>
/// <para>
/// Activity is read a full day either side of the window, and the window decides only which visits
/// this reading is answerable for. A visit belongs to the window its first report falls in; whether
/// it is over is asked of the moment nothing more can arrive, which the caller passes in. Keeping
/// those two apart is what stops a verdict depending on where a boundary happened to fall: a
/// backlog is walked in stretches of a few hours, and a visit lying across the end of one of them
/// is judged on all of itself rather than on the part that fits.
/// </para>
/// <para>
/// The reach back is what stops a visit being counted twice. The caller works forward through a
/// site, and its bookmark stops at the earliest visit still in progress rather than at the end of
/// the stretch it just read — so the next window routinely opens part-way through a visit that has
/// already been judged. Read from the window's own start, the remainder of that visit looks like a
/// whole one beginning at whichever report happened to fall first inside it, and it is judged a
/// second time under a second identity. The reach back gives that remainder its true beginning,
/// which then falls before the window and is dropped by the filter below.
/// </para>
/// <para>
/// A day is exactly enough in both directions, and it is derived rather than estimated — see
/// <see cref="VisitorKeys.LongestVisit"/>. The idle timeout is no bound on it, close to hand as it
/// is: a report about a page belongs to the visit that page was arrived at in however long the
/// silence before it, so a departure sent when a tab is finally closed joins a visit it is hours
/// adrift of, and a visit is a chain that can have hours between its links.
/// </para>
/// <para>
/// A visit already judged under the caller's rules is left out, for as long as it is the visit
/// that was judged. The caller's reading starts where its judging waits, at the earliest visit
/// still in progress, and that can be most of a day behind the present when somebody leaves a page
/// open; without this, every visit judged since would be judged again on every run until the page
/// was closed. The match is on the visit's name and the instant it ended, so a visit that a later
/// report has made longer is read again and judged on all of itself.
/// </para>
/// <para>
/// Which kind of site sent a visit is settled on the visit list's own terms: the reduction in
/// <see cref="SendingSites"/> reads each report's referring address against the site's own address
/// and the catalogue of sending sites, and the visit takes the kind its first report naming
/// somebody else gives. The kind the engine judges a visit with is therefore the kind a reader is
/// shown beside it. The referring address and the site it names stay inside the statement; only
/// the kind comes out.
/// </para>
/// <para>
/// One thing about a visit is read from beside the activity rather than out of it: whose crawler
/// it was, where an earlier pass settled that from the name its address answers to. The address is
/// cleared after three days, so the answer is kept under the visit's own name instead, and it is
/// read for the window's own visits — by site and by when each began, and never by the visitor
/// key, which on a rented network can stand for a whole network's day.
/// </para>
/// <para>
/// Two things are read about how a page's time was kept, both page by page and both from the
/// tracker's reports alone. How much of the time a page reported was said as it was being left,
/// because a page let go of says so as it goes and one a program opened, kept waiting and switched
/// off never does. And how soon a page sent its first report of how it was going, on whichever of
/// the two clocks that report carries says longer: the time the page says it was in front of
/// somebody, and the page's own time of day read against the time of day its page view gave. The
/// tracker waits on the browser's own clock before sending that report, so a browser whose timers
/// keep to its clocks never sends it sooner on both.
/// </para>
/// <para>
/// A page is a stretch between two arrivals at the same address, cut where the collector received
/// them, so two things can put a page's reports in the wrong stretch. A report sent as a page is
/// left can reach the collector after the next arrival at the same address — a reload, a jump to
/// another section of one page, a second tab — and land in the next stretch; so only the last
/// stretch of an address in a visit is ever taken for a page left open, and only the first is read
/// for how soon its wait was over, because nothing earlier can spill into it. And a visitor's key
/// is renewed at midnight, so whatever a page still open then reports afterwards is filed under a
/// key that never arrived at it and is not read here; a page that reported within a progress
/// report's interval of midnight is therefore taken as having been let go of.
/// </para>
/// </remarks>
public static class SessionSqlCompiler
{
    private const string SiteIdParameter = "site_id";
    private const string FromParameter = "from_ms";
    private const string ToParameter = "to_ms";
    private const string SettledParameter = "settled_ms";
    private const string IdleParameter = "idle_seconds";
    private const string LongestVisitParameter = "longest_visit_seconds";
    private const string MaxRequestsParameter = "max_requests";
    private const string RulesetMajorParameter = "ruleset_major";
    private const string RulesetMinorParameter = "ruleset_minor";
    private const string ProgressIntervalParameter = "progress_interval_seconds";

    /// <summary>
    /// The longest a page still open in front of somebody goes without reporting, with time for
    /// the report to arrive.
    /// </summary>
    /// <remarks>
    /// The tracker reports how a page is going at least once a minute while it stays in view; a
    /// quarter of a minute more covers the report on its way. A page whose last report reached the
    /// collector within this of midnight may have gone on reporting under the next day's key.
    /// </remarks>
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(75);

    /// <summary>
    /// Column positions in the statement below, so the reader and the statement cannot drift.
    /// </summary>
    internal static class Column
    {
        /// <summary>Derived identity of the visit.</summary>
        public const int SessionKey = 0;

        /// <summary>When the first activity was received.</summary>
        public const int StartedAt = 1;

        /// <summary>When the last was.</summary>
        public const int EndedAt = 2;

        /// <summary>Whether the visit is over.</summary>
        public const int IsClosed = 3;

        /// <summary>Exact number of pages the visit went to.</summary>
        public const int PageCount = 4;

        /// <summary>
        /// The pages themselves, oldest first, capped at the number the caller asked for.
        /// </summary>
        /// <remarks>
        /// The cap keeps the earliest pages rather than an arbitrary sample of them, and keeps the
        /// same ones on every run. Both matter: a sweep is recognised by the shape of its opening,
        /// and a verdict that changed between two runs over the same activity would not be a
        /// verdict.
        /// </remarks>
        public const int Requests = 5;

        /// <summary>Which capture surfaces saw the visit.</summary>
        public const int Surfaces = 6;

        /// <summary>What the visitor said it was.</summary>
        public const int UserAgent = 7;

        /// <summary>The language it asked for.</summary>
        public const int Language = 8;

        /// <summary>Widest viewport reported.</summary>
        public const int ViewportWidth = 9;

        /// <summary>
        /// Milliseconds the pages were in front of somebody, added up across the visit.
        /// </summary>
        /// <remarks>
        /// One reading per page rather than one per report. A tracker reports the time a page has
        /// held somebody so far, over and over while it holds them, so each report restates the
        /// last one with more on the end; adding the reports together would count the first minute
        /// of a long read once for every report that mentioned it and hand the engine an afternoon
        /// where there was a quarter of an hour.
        /// </remarks>
        public const int EngagedMs = 10;

        /// <summary>Deepest point of any page that came into view, first screen included.</summary>
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

        /// <summary>Routing number of the network the visit arrived over, or nought.</summary>
        /// <remarks>
        /// Taken as the largest seen rather than the first, so a visit whose reports arrived over
        /// several addresses is judged on a network that carried it rather than on nothing. Nought
        /// is what an unresolved address reads as, and the largest of nought and a real number is
        /// the real one.
        /// </remarks>
        public const int AutonomousSystem = 18;

        /// <summary>Who runs that network, for the reader rather than for the rules.</summary>
        public const int NetworkOwner = 19;

        /// <summary>The company that vouches for the address the visit arrived from.</summary>
        /// <remarks>
        /// <para>
        /// Any one report establishing it settles the visit. A crawler asks for several pages and
        /// the check is made against each request as it arrives, so a visit where one report
        /// resolved and the rest did not is a visit whose address was published by then; there is
        /// no reading of that under which the visit was somebody else.
        /// </para>
        /// <para>
        /// Where no report says, the answer an earlier pass settled from the name the visit's
        /// address answers to is read instead, which is what lets a visit judged again after its
        /// address has been cleared keep the identity it was given. What the activity says comes
        /// first, as it does when a visit is first judged. It was settled report by report as the
        /// activity arrived — from a list the company publishes, or from an address a name check
        /// had already settled — so it speaks for the reports themselves, where the answer kept
        /// beside the visit speaks for the one address the visit was asked about.
        /// </para>
        /// </remarks>
        public const int ConfirmedOperator = 20;

        /// <summary>What kind of site sent the visit, or empty where nothing named one.</summary>
        /// <remarks>
        /// Settled by the same expression the visit list uses — the first report whose referring
        /// address names a site other than this one, reduced through the catalogue of sending sites
        /// — so the kind a visit is judged with is the kind a reader is shown. Only the kind leaves
        /// the statement: the referring address and the site it names stay inside it.
        /// </remarks>
        public const int SentBy = 21;

        /// <summary>One of the addresses the visit arrived from, while one is still kept.</summary>
        /// <remarks>
        /// <para>
        /// The only personal data this statement carries, and the only reason it does is to settle
        /// whose crawlers an address belongs to against the name its operator documents — a
        /// question the collector cannot ask, because asking it means waiting on a name server
        /// while a visitor is waiting on a page. It goes no further than the pass that reads it: it
        /// is never handed to the engine, never stored on a verdict, and never logged.
        /// </para>
        /// <para>
        /// Empty for anything older than three days, which is when the column is cleared. Any one
        /// of a visit's addresses will do — a crawler fetching a dozen pages from a dozen machines
        /// in one fleet is one operator whichever of them is picked, and a person who changed
        /// network mid-visit is a person under either answer.
        /// </para>
        /// </remarks>
        public const int Address = 22;

        /// <summary>
        /// Of the milliseconds in <see cref="EngagedMs"/>, those the pages said as they were being
        /// left.
        /// </summary>
        /// <remarks>
        /// Page by page: what the page reported the last time it said it was being left — a report of
        /// a page being left carries the whole of that page's time so far, so the last one stands for
        /// every earlier one — and nothing where it never said so. A page that cannot be taken for one
        /// left open counts whole: one the same address was arrived at again after in the visit, and
        /// one that reported within a progress report's interval of midnight. Nought where nothing
        /// measured any time at all, which <see cref="EngagedMs"/> says by being null.
        /// </remarks>
        public const int ReadMs = 23;

        /// <summary>
        /// How soon, in milliseconds, a page sent its first report of how it was going, on whichever
        /// of that report's two clocks says longer, the soonest across the visit's pages.
        /// </summary>
        /// <remarks>
        /// Read only for the first page at each address in the visit, and only where the tracker's
        /// own page view gave the time of day the page opened at, its report gave both readings and
        /// was sent after the page opened — so nothing missing is ever read as nought, and nothing
        /// another tab sent is read against this one's opening. A page whose opening only a server
        /// saw has no time of day to read the report against. Null where no page qualifies.
        /// </remarks>
        public const int SoonestProgressMs = 24;
    }

    /// <summary>
    /// Tests whether a report came from the tracker, spelled from the stored name so the surface
    /// tested is the one the collector writes.
    /// </summary>
    /// <remarks>
    /// The tracker alone sends reports of how a page is going, and its page view alone gives the
    /// time of day by the same clock those reports do. A reporter on the site's own server keeps a
    /// different clock, so its page view is never read against a tracker's report.
    /// </remarks>
    private static readonly string FromTheTracker =
        $"surface = '{StoredNames.SurfaceNames[IngestSurface.BrowserTracker]}'";

    /// <summary>
    /// The statement, with the same shape whatever the window, so the store can reuse its plan.
    /// </summary>
    /// <remarks>
    /// Only activity carrying a visitor key takes part. A report without one has not told us that
    /// an anonymous visitor was there; it has told us nothing about who was there, and gathering
    /// all of those under one empty key would build a single impossibly busy visitor out of
    /// everybody the product could not identify, and then judge it.
    /// </remarks>
    private static readonly string Sql = $$"""
        WITH
            {{SendingSites.Of(
                SendingSites.ThePeriodAndTheVisitsAcrossIt,
                "event_id",
                "visitor_key",
                "correlation_id",
                "server_ts",
                "client_ts",
                "kind",
                "path",
                "status_code",
                "surface",
                "user_agent",
                "language",
                "viewport_width",
                "engaged_ms",
                "scroll_depth_percent",
                "had_pointer_interaction",
                "had_keyboard_interaction",
                "declared_web_driver",
                "autonomous_system",
                "network_owner",
                "confirmed_operator",
                "ip_address")}},
            {{ReconciledEvents.Reconciliation}},
            {{VisitGrouping.Of(VisitGrouping.EveryVisitor)}},
            attended AS
            (
                SELECT
                    *,
                    max(engaged_ms) OVER page AS page_engaged_ms,
                    maxIf(engaged_ms, kind = 'Exit' AND {{FromTheTracker}}) OVER page AS page_left_ms,
                    max(server_ts) OVER page AS page_last_at,
                    max(page_ordinal) OVER (PARTITION BY visitor_key, visit_ordinal, path) AS last_page_ordinal,
                    minIf(client_ts, kind = 'PageView' AND {{FromTheTracker}}) OVER page AS page_opened_on_client,
                    argMinIf(
                        (client_ts, engaged_ms),
                        (server_ts, event_id),
                        kind = 'Engagement' AND {{FromTheTracker}}) OVER page AS first_progress
                FROM opened
                WINDOW page AS (PARTITION BY visitor_key, visit_ordinal, path, page_ordinal)
            ),
            visits AS
            (
                SELECT
                    concat(visitor_key, ':', toString(toUnixTimestamp64Milli(min(server_ts)))) AS session_key,
                    min(server_ts) AS started_at,
                    max(server_ts) AS ended_at,
                    toBool(max(server_ts) < fromUnixTimestamp64Milli({settled_ms:Int64}, 'UTC')) AS is_closed,
                    toUInt32(countIf(opens_page)) AS page_count,
                    groupArraySortedIf({max_requests:UInt32})(
                        (toUnixTimestamp64Milli(server_ts), path, status_code),
                        opens_page) AS requests,
                    groupUniqArray(toString(surface)) AS surfaces,
                    anyIf(user_agent, user_agent != '') AS user_agent,
                    anyIf(language, language != '') AS language,
                    max(viewport_width) AS viewport_width,
                    sumIf(page_engaged_ms, opens_page) AS engaged_ms,
                    sumIf(
                        if(
                            page_ordinal < last_page_ordinal
                            OR toDate(page_last_at + toIntervalSecond({progress_interval_seconds:Int64})) != toDate(page_last_at),
                            page_engaged_ms,
                            least(ifNull(page_left_ms, 0), page_engaged_ms)),
                        opens_page) AS read_ms,
                    minIf(
                        greatest(
                            toUnixTimestamp64Milli(first_progress.1) - toUnixTimestamp64Milli(page_opened_on_client),
                            first_progress.2),
                        opens_page
                        AND page_ordinal = 1
                        AND page_opened_on_client IS NOT NULL
                        AND first_progress.1 IS NOT NULL
                        AND first_progress.2 IS NOT NULL
                        AND first_progress.1 >= page_opened_on_client) AS soonest_progress_ms,
                    max(scroll_depth_percent) AS max_scroll_depth_percent,
                    toUInt32(countIf(had_pointer_interaction != 'Unobserved')) AS pointer_observed,
                    toUInt32(countIf(had_pointer_interaction = 'Yes')) AS pointer_seen,
                    toUInt32(countIf(had_keyboard_interaction != 'Unobserved')) AS keyboard_observed,
                    toUInt32(countIf(had_keyboard_interaction = 'Yes')) AS keyboard_seen,
                    toUInt32(countIf(declared_web_driver != 'Unobserved')) AS web_driver_observed,
                    toUInt32(countIf(declared_web_driver = 'Yes')) AS web_driver_seen,
                    max(autonomous_system) AS autonomous_system,
                    anyIf(network_owner, network_owner != '') AS network_owner,
                    anyIf(confirmed_operator, confirmed_operator != '') AS stamped_operator,
                    argMinIf(source_channel, (server_ts, event_id), sending_host != '') AS from_kind,
                    anyIf(ip_address, ip_address != '') AS ip_address
                FROM attended
                GROUP BY visitor_key, visit_ordinal
                HAVING started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                   AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                   AND (session_key, ended_at) NOT IN
                   (
                       SELECT session_key, ended_at
                       FROM session_classifications
                       WHERE site_id = {site_id:UUID}
                         AND ruleset_major = {ruleset_major:UInt16}
                         AND ruleset_minor = {ruleset_minor:UInt16}
                         AND started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                         AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                   )
            ),
            confirmations AS
            (
                SELECT
                    session_key,
                    argMax(operator, settled_at) AS operator
                FROM name_confirmations
                WHERE site_id = {site_id:UUID}
                  AND started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                  AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                GROUP BY session_key
            )
        SELECT
            session_key,
            started_at,
            ended_at,
            is_closed,
            page_count,
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
            if(stamped_operator != '', stamped_operator, ifNull(operator, '')) AS confirmed_operator,
            from_kind,
            ip_address,
            read_ms,
            soonest_progress_ms
        FROM visits
        LEFT JOIN confirmations USING (session_key)
        ORDER BY started_at, session_key
        """;

    /// <summary>
    /// Compiles the reconstruction.
    /// </summary>
    /// <param name="window">Which site, which stretch of time, and what counts as one visit.</param>
    /// <returns>The statement and its bound values.</returns>
    /// <exception cref="ArgumentException">
    /// The window names no address for the site, which would make every page a reader opened after
    /// the first look like an arrival from another site.
    /// </exception>
    public static CompiledStatement Compile(SessionWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(window.SiteDomain);

        return new CompiledStatement(
            Sql,
            [
                new QueryParameter(SiteIdParameter, window.SiteId),
                new QueryParameter(FromParameter, window.From.ToUnixTimeMilliseconds()),
                new QueryParameter(ToParameter, window.To.ToUnixTimeMilliseconds()),
                new QueryParameter(SettledParameter, window.SettledBefore.ToUnixTimeMilliseconds()),
                new QueryParameter(IdleParameter, (long)window.IdleTimeout.TotalSeconds),
                new QueryParameter(LongestVisitParameter, (long)VisitorKeys.LongestVisit.TotalSeconds),
                new QueryParameter(MaxRequestsParameter, (uint)window.MaxRequestsPerSession),
                new QueryParameter(RulesetMajorParameter, (ushort)window.UnjudgedUnder.Major),
                new QueryParameter(RulesetMinorParameter, (ushort)window.UnjudgedUnder.Minor),
                new QueryParameter(ProgressIntervalParameter, (long)ProgressInterval.TotalSeconds),
                .. SendingSites.Parameters(window.SiteDomain),
            ]);
    }
}
