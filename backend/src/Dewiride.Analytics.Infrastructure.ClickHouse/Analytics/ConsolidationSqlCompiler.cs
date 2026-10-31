using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Writes the statements that count a website's finished months for keeping.
/// </summary>
/// <remarks>
/// <para>
/// The third compiler in this project, and like the one that rebuilds visits for the detection
/// engine it works on a website it was handed rather than one a caller named: the website comes
/// from the control plane, so nothing here takes an authorisation decision and nothing reachable
/// from a request may use it. Every statement a person can cause is still written by
/// <see cref="AnalyticsSqlCompiler"/> from a <see cref="Application.Tenancy.TenantScope"/>.
/// </para>
/// <para>
/// A month is counted from exactly the fragments the dashboard counts from — the reconciliation of
/// the two halves of the measurement, pages delivered, the people the verdicts cover, and the
/// buckets of the website's calendar — so a kept month is the overview of that month, its busiest
/// day is the busiest bucket of the daily series, and its hours are the hourly series folded. A
/// test holds the version of the arithmetic, <see cref="CountingRules.Current"/>, to the text of
/// these statements.
/// </para>
/// <para>
/// The counting statements end by asking the store for one thread and ten minutes at most. They run
/// in the background beside the screens people are reading, which matter more than how soon a
/// month is kept.
/// </para>
/// </remarks>
public static class ConsolidationSqlCompiler
{
    /// <summary>Placeholder the instant the calendar is read at binds to.</summary>
    private const string AsOfParameter = StatementBindings.To;

    /// <summary>What the counting statements ask of the store, ahead of anything a person is reading.</summary>
    private const string Restraint = "SETTINGS max_threads = 1, max_execution_time = 600";

    /// <summary>What the calendar reads of each kept month: how it was counted.</summary>
    private static readonly KeptColumn[] Described =
    [
        new("time_zone", "time_zone"),
        new("first_day", "first_day"),
        new("ruleset_major", "ruleset_major"),
        new("ruleset_minor", "ruleset_minor"),
        new("ruleset_judged_to", "ruleset_judged_to"),
        new("counting_rules", "counting_rules"),
        new("counted_to", "counted_to"),
        new("judged_to", "judged_to"),
        new("counted_at", "counted_at"),
    ];

    /// <summary>
    /// Compiles the reading of a website's calendar: every month of its record, and how each that
    /// was kept was counted.
    /// </summary>
    /// <param name="siteId">The website.</param>
    /// <param name="timeZoneId">The zone its calendar is read in.</param>
    /// <param name="now">The moment the calendar is read at.</param>
    /// <returns>The statement and its bound values.</returns>
    public static CompiledStatement CompileSurvey(Guid siteId, string timeZoneId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        return new CompiledStatement(
            KeptCalendar.Statement(Described),
            [
                new QueryParameter(StatementBindings.SiteId, siteId),
                new QueryParameter(AsOfParameter, now.ToUnixTimeMilliseconds()),
                new QueryParameter(StatementBindings.TimeZone, timeZoneId),
            ]);
    }

    /// <summary>
    /// Compiles the count of one month's totals, for everybody or for the visits judged to be people.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three cuts of the month in one statement, each grouped exactly as the dashboard groups it: the
    /// month whole, as the overview counts a period; day by day, as the daily series does, which
    /// gives the busiest day and the weekdays; and hour by hour, as the hourly series does, which
    /// gives the hours. A cut is never derived from a finer one, because pages delivered are the
    /// larger of what the two halves of the measurement saw, and the larger of two sums is not the
    /// sum of the larger.
    /// </para>
    /// <para>
    /// The three are gathered inside a selection of their own, because the store does not say
    /// whether a common table expression reaches every branch of a union written directly beneath it.
    /// </para>
    /// </remarks>
    /// <param name="month">The month.</param>
    /// <param name="population">Who to count.</param>
    /// <returns>The statement and its bound values.</returns>
    public static CompiledStatement CompileCount(MonthToCount month, Population population)
    {
        ArgumentNullException.ThrowIfNull(month);

        var kept = JudgedPeople.Among(ReconciledEvents.Identified, population);
        var day = CalendarBuckets.Holding(TimeGranularity.Day, "server_ts");
        var hour = CalendarBuckets.Holding(TimeGranularity.Hour, "server_ts");

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
                {{ReconciledEvents.Reconciliation}}{{kept.Following}}
            SELECT
                cut,
                day,
                slot,
                page_views,
                visitors
            FROM
            (
                SELECT
                    'month' AS cut,
                    toDate(0) AS day,
                    toUInt8(0) AS slot,
                    toInt64(sum(page_views)) AS page_views,
                    {{ReconciledEvents.Visitors}} AS visitors
                FROM
                (
                    SELECT
                        visitor_key,
                        {{ReconciledEvents.DeliveredPageViews(12)}}
                    FROM {{kept.Source}}
                    GROUP BY visitor_key, path
                )
                UNION ALL
                SELECT
                    'day' AS cut,
                    calendar_day AS day,
                    toUInt8(toDayOfWeek(calendar_day)) AS slot,
                    toInt64(sum(page_views)) AS page_views,
                    toInt64(0) AS visitors
                FROM
                (
                    SELECT
                        {{day}} AS calendar_day,
                        visitor_key,
                        {{ReconciledEvents.DeliveredPageViews(12)}}
                    FROM {{kept.Source}}
                    GROUP BY calendar_day, visitor_key, path
                )
                GROUP BY calendar_day
                UNION ALL
                SELECT
                    'hour' AS cut,
                    toDate(calendar_hour, {time_zone:String}) AS day,
                    toUInt8(toHour(calendar_hour, {time_zone:String})) AS slot,
                    toInt64(sum(page_views)) AS page_views,
                    toInt64(0) AS visitors
                FROM
                (
                    SELECT
                        {{hour}} AS calendar_hour,
                        visitor_key,
                        {{ReconciledEvents.DeliveredPageViews(12)}}
                    FROM {{kept.Source}}
                    GROUP BY calendar_hour, visitor_key, path
                )
                GROUP BY calendar_hour
            )
            {{Restraint}}
            """;

        return new CompiledStatement(
            sql,
            [
                .. StatementBindings.Window(month.SiteId, month.Window),
                new QueryParameter(StatementBindings.TimeZone, month.TimeZoneId),
                .. StatementBindings.Population(population),
            ]);
    }

    /// <summary>
    /// Compiles the count of how many visits of each kind began in one month.
    /// </summary>
    /// <remarks>
    /// The reduction to one verdict per visit the dashboard's own count of who came uses, so a kept
    /// month's visits of each kind are that month's bar on the dashboard.
    /// </remarks>
    /// <param name="month">The month.</param>
    /// <returns>The statement and its bound values.</returns>
    public static CompiledStatement CompileVerdicts(MonthToCount month)
    {
        ArgumentNullException.ThrowIfNull(month);

        const string sql = $$"""
            SELECT
                category,
                toInt64(count()) AS visits,
                toInt64(sum(page_count)) AS page_views
            FROM
            (
                SELECT
                    session_key,
                    argMax(category, (ruleset_major, ruleset_minor, classified_at)) AS category,
                    argMax(page_count, (ruleset_major, ruleset_minor, classified_at)) AS page_count
                FROM session_classifications
                WHERE site_id = {site_id:UUID}
                  AND started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
                  AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                GROUP BY session_key
            )
            GROUP BY category
            ORDER BY category
            {{Restraint}}
            """;

        return new CompiledStatement(sql, StatementBindings.Window(month.SiteId, month.Window));
    }
}
