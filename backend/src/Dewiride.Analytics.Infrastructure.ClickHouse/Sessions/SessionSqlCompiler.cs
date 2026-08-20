using Dewiride.Analytics.Application.Sessions;
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
/// Activity is read a full idle timeout past the end of the window. That is what makes "this visit
/// is over" a fact rather than an artefact of where the reading stopped: a visit whose last
/// activity falls before the end of the window has been watched falling silent for long enough to
/// know nothing more is coming.
/// </para>
/// <para>
/// It is read a full idle timeout before the start of the window as well, and that is what stops a
/// visit being counted twice. The caller works forward through a site in windows, and its bookmark
/// stops at the earliest visit still in progress rather than at the end of the window it just
/// read — so the next window routinely opens part-way through a visit that has already been judged.
/// Read from the window's own start, the remainder of that visit looks like a whole one that began
/// at whichever report happened to fall first inside it, and it is judged a second time under a
/// second identity, usually with too little left in it to say anything. The reach back is what
/// gives that remainder its true beginning, which then falls before the window and is dropped by
/// the filter below.
/// </para>
/// <para>
/// One idle timeout is exactly enough, and not by estimation. A visit is a chain of reports each
/// less than an idle timeout apart, so a visit with a report on both sides of the window's start
/// must have one within an idle timeout before it — which is the report that carries the whole
/// chain back and puts the reconstructed beginning outside the window.
/// </para>
/// </remarks>
public static class SessionSqlCompiler
{
    private const string SiteIdParameter = "site_id";
    private const string FromParameter = "from_ms";
    private const string ToParameter = "to_ms";
    private const string IdleParameter = "idle_seconds";
    private const string MaxRequestsParameter = "max_requests";

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
    }

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
            windowed AS
            (
                SELECT
                    event_id,
                    visitor_key,
                    correlation_id,
                    server_ts,
                    kind,
                    path,
                    status_code,
                    surface,
                    user_agent,
                    language,
                    viewport_width,
                    engaged_ms,
                    scroll_depth_percent,
                    had_pointer_interaction,
                    had_keyboard_interaction,
                    declared_web_driver,
                    autonomous_system,
                    network_owner
                FROM events
                WHERE site_id = {site_id:UUID}
                  AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64} - {idle_seconds:Int64} * 1000, 'UTC')
                  AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64} + {idle_seconds:Int64} * 1000, 'UTC')
            ),
            {{ReconciledEvents.Reconciliation}},
            {{VisitGrouping.Of(VisitGrouping.EveryVisitor)}},
            attended AS
            (
                SELECT
                    *,
                    max(engaged_ms) OVER (
                        PARTITION BY visitor_key, visit_ordinal, path, page_ordinal) AS page_engaged_ms
                FROM opened
            )
        SELECT
            concat(visitor_key, ':', toString(toUnixTimestamp64Milli(min(server_ts)))) AS session_key,
            min(server_ts) AS started_at,
            max(server_ts) AS ended_at,
            toBool(max(server_ts) < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')) AS is_closed,
            toUInt32(countIf(opens_page)) AS page_count,
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
            anyIf(network_owner, network_owner != '') AS network_owner
        FROM attended
        GROUP BY visitor_key, visit_ordinal
        HAVING started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
           AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
        ORDER BY started_at, session_key
        """;

    /// <summary>
    /// Compiles the reconstruction.
    /// </summary>
    /// <param name="window">Which site, which stretch of time, and what counts as one visit.</param>
    /// <returns>The statement and its bound values.</returns>
    public static CompiledStatement Compile(SessionWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new CompiledStatement(
            Sql,
            [
                new QueryParameter(SiteIdParameter, window.SiteId),
                new QueryParameter(FromParameter, window.From.ToUnixTimeMilliseconds()),
                new QueryParameter(ToParameter, window.To.ToUnixTimeMilliseconds()),
                new QueryParameter(IdleParameter, (long)window.IdleTimeout.TotalSeconds),
                new QueryParameter(MaxRequestsParameter, (uint)window.MaxRequestsPerSession),
            ]);
    }
}
