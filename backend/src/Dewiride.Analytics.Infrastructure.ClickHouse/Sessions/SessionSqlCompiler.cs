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
/// The grouping is the standard one — a visitor's activity is one visit until they fall silent for
/// longer than the idle timeout — expressed as a running total of how many silences have been
/// crossed. There is no session function in the store, so this idiom is the implementation, and
/// keeping it in one statement means a visit is defined in exactly one place.
/// </para>
/// <para>
/// Activity is read a full idle timeout past the end of the window. That is what makes "this visit
/// is over" a fact rather than an artefact of where the reading stopped: a visit whose last
/// activity falls before the end of the window has been watched falling silent for long enough to
/// know nothing more is coming.
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

        /// <summary>Exact number of pages asked for.</summary>
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

        /// <summary>Milliseconds the pages were in front of somebody.</summary>
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
    private const string Sql = """
        WITH
            ordered AS
            (
                SELECT
                    visitor_key,
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
                    dateDiff('second', lagInFrame(server_ts, 1, server_ts) OVER visit, server_ts) AS since_previous
                FROM events
                WHERE site_id = {site_id:UUID}
                  AND visitor_key != ''
                  AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                  AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64} + {idle_seconds:Int64} * 1000, 'UTC')
                WINDOW visit AS (PARTITION BY visitor_key ORDER BY server_ts ROWS BETWEEN 1 PRECEDING AND CURRENT ROW)
            ),
            grouped AS
            (
                SELECT
                    *,
                    sum(toUInt8(since_previous > {idle_seconds:Int64})) OVER (
                        PARTITION BY visitor_key
                        ORDER BY server_ts
                        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS visit_ordinal
                FROM ordered
            )
        SELECT
            concat(visitor_key, ':', toString(toUnixTimestamp64Milli(min(server_ts)))) AS session_key,
            min(server_ts) AS started_at,
            max(server_ts) AS ended_at,
            toBool(max(server_ts) < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')) AS is_closed,
            toUInt32(countIf(kind = 'PageView')) AS page_count,
            groupArraySortedIf({max_requests:UInt32})(
                (toUnixTimestamp64Milli(server_ts), path, status_code), kind = 'PageView') AS requests,
            groupUniqArray(toString(surface)) AS surfaces,
            anyIf(user_agent, user_agent != '') AS user_agent,
            anyIf(language, language != '') AS language,
            max(viewport_width) AS viewport_width,
            sum(engaged_ms) AS engaged_ms,
            max(scroll_depth_percent) AS max_scroll_depth_percent,
            toUInt32(countIf(had_pointer_interaction != 'Unobserved')) AS pointer_observed,
            toUInt32(countIf(had_pointer_interaction = 'Yes')) AS pointer_seen,
            toUInt32(countIf(had_keyboard_interaction != 'Unobserved')) AS keyboard_observed,
            toUInt32(countIf(had_keyboard_interaction = 'Yes')) AS keyboard_seen,
            toUInt32(countIf(declared_web_driver != 'Unobserved')) AS web_driver_observed,
            toUInt32(countIf(declared_web_driver = 'Yes')) AS web_driver_seen
        FROM grouped
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
