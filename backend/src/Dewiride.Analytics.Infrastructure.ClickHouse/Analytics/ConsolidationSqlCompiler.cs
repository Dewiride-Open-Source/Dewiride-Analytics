using System.Globalization;
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
/// day is the busiest bucket of the daily series, its best hour the busiest bucket of the hourly
/// series, and its hours are the hourly series folded. A test holds the version of the arithmetic,
/// <see cref="CountingRules.Current"/>, to the text of these statements.
/// </para>
/// <para>
/// The counting statements end by asking the store for one thread and ten minutes at most. They run
/// in the background beside the screens people are reading, which matter more than how soon a
/// month is kept.
/// </para>
/// <para>
/// A month's posts and its busiest pages are counted by statements of their own, each held to a version of
/// its own, <see cref="CountingRules.Posts"/> and <see cref="CountingRules.Pages"/>, so a change to how one
/// of the three is counted recounts the months of the others not at all.
/// </para>
/// </remarks>
public static class ConsolidationSqlCompiler
{
    /// <summary>Placeholder the instant the calendar is read at binds to.</summary>
    private const string AsOfParameter = StatementBindings.To;

    /// <summary>What the counting statements ask of the store, ahead of anything a person is reading.</summary>
    private const string Restraint = "SETTINGS max_threads = 1, max_execution_time = 600";

    /// <summary>Where a month's posts are kept.</summary>
    private const string PostTable = "consolidated_post_months";

    /// <summary>Where a month's busiest pages are kept.</summary>
    private const string PageTable = "consolidated_page_months";

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
    /// gives the best hour and the hours. A cut is never derived from a finer one, because pages
    /// delivered are the larger of what the two halves of the measurement saw, and the larger of two
    /// sums is not the sum of the larger.
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

    /// <summary>
    /// Compiles the reading of how each month whose posts were kept for a website was counted.
    /// </summary>
    /// <remarks>
    /// Each month as its newest count, on the terms the calendar reads a month's totals by. Only how a
    /// month was counted is read, and which list of posts it was counted for, never the posts it holds:
    /// that is all choosing what to count again needs, and a month of fifty thousand posts is not read on
    /// every run to decide nothing.
    /// </remarks>
    /// <param name="siteId">The website.</param>
    /// <returns>The statement and its bound value.</returns>
    public static CompiledStatement CompilePostSurvey(Guid siteId) =>
        new(KeptDescriptions(PostTable, ",\n    posts_fingerprint"), [new QueryParameter(StatementBindings.SiteId, siteId)]);

    /// <summary>
    /// Compiles the reading of how each month whose pages were kept for a website was counted.
    /// </summary>
    /// <remarks>
    /// On the terms <see cref="CompilePostSurvey"/> reads the months of posts by: how each month was
    /// counted, as its newest count, and nothing of the pages it holds.
    /// </remarks>
    /// <param name="siteId">The website.</param>
    /// <returns>The statement and its bound value.</returns>
    public static CompiledStatement CompilePageSurvey(Guid siteId) =>
        new(KeptDescriptions(PageTable, string.Empty), [new QueryParameter(StatementBindings.SiteId, siteId)]);

    /// <summary>
    /// Compiles the count of the pages delivered at every path of a website in one month, for everybody
    /// or for the visits judged to be people, which the month's posts are read out of.
    /// </summary>
    /// <remarks>
    /// The statement the dashboard counts the same posts' views by, from <see cref="PostViewsStatement"/>,
    /// ending as every counting statement here ends. It names no post, so a month is counted by this one
    /// statement however many posts the website lists, and the posts are kept from its answer as it is
    /// read.
    /// </remarks>
    /// <param name="month">The month.</param>
    /// <param name="population">Who to count.</param>
    /// <returns>The statement and its bound values.</returns>
    public static CompiledStatement CompilePostCount(MonthToCount month, Population population)
    {
        ArgumentNullException.ThrowIfNull(month);

        return new CompiledStatement(
            $"{PostViewsStatement.Text(population)}\n{Restraint}",
            PostViewsStatement.Bound(month.SiteId, month.Window, population));
    }

    /// <summary>
    /// Compiles the count of the pages delivered at every path of a website in one month, for everybody
    /// and for the visits judged to be people, and of which of them the month keeps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One statement for both populations: the month's reports settled as the list of pages settles them,
    /// everybody's pages counted from the reports with identity settled and the people's from the reports
    /// the verdicts on people cover — each by the arithmetic of the list of pages for that population, from
    /// <see cref="PostViewsStatement"/> — and both brought together path by path.
    /// </para>
    /// <para>
    /// A path can be kept when at least <see cref="KeptPages.FewestVisitors"/> different visitor keys were
    /// delivered a page at it and it is no longer than <see cref="KeptPages.LongestPath"/> characters. Of
    /// those, the month keeps the <see cref="KeptPages.MostPerPopulation"/> most viewed by everybody and as
    /// many of the most viewed by people — of the paths a person was delivered a page at — ties broken by
    /// the path's bytes, and keeps every other path only as what they add up to. The busiest of each kind
    /// are gathered in the same pass over the paths as the totals, each holding no more than it keeps,
    /// whatever was asked for that month; ranking every path instead would hold every path at once, and a
    /// count of a month never spills past the memory a statement is allowed.
    /// </para>
    /// <para>
    /// The figures <see cref="KeptPages"/> sets are written into the statement as they are, so the test that
    /// holds <see cref="CountingRules.Pages"/> to its text fails when one of them changes. Nothing a path
    /// holds is part of the statement: every path is grouped on and read back.
    /// </para>
    /// </remarks>
    /// <param name="month">The month.</param>
    /// <returns>
    /// The statement and its bound values. It answers one row: the paths kept, everybody's and the
    /// people's pages delivered at each of them, and how many other paths there were and the pages
    /// delivered at them.
    /// </returns>
    public static CompiledStatement CompilePageCount(MonthToCount month)
    {
        ArgumentNullException.ThrowIfNull(month);

        var most = Literal(KeptPages.MostPerPopulation);

        var sql = $$"""
            {{PostViewsStatement.Reported(Population.People)}},
                viewed AS
                (
                    SELECT
                        path,
                        toInt64(sum(everybody)) AS page_views,
                        toInt64(sum(of_people)) AS people_views,
                        toInt64(sum(seen_by)) AS visitors
                    FROM
                    (
                        SELECT
                            path,
                            page_views AS everybody,
                            toInt64(0) AS of_people,
                            visitors AS seen_by
                        FROM
                        (
                            {{PostViewsStatement.ByPathWithVisitors(ReconciledEvents.Identified, 16)}}
                        )
                        UNION ALL
                        SELECT
                            path,
                            toInt64(0) AS everybody,
                            page_views AS of_people,
                            toInt64(0) AS seen_by
                        FROM
                        (
                            {{PostViewsStatement.ByPath(JudgedPeople.Attributed, 16)}}
                        )
                    )
                    GROUP BY path
                    HAVING page_views > 0
                )
            SELECT
                arrayMap(entry -> entry.1, kept) AS paths,
                arrayMap(entry -> entry.2, kept) AS page_views,
                arrayMap(entry -> entry.3, kept) AS people_page_views,
                toInt64(counted_paths - length(kept)) AS left_out_paths,
                toInt64(counted_page_views - arraySum(page_views)) AS left_out_page_views,
                toInt64(counted_people_views - arraySum(people_page_views)) AS left_out_people_page_views
            FROM
            (
                SELECT
                    counted_paths,
                    counted_page_views,
                    counted_people_views,
                    arrayDistinct(arrayConcat(
                        arrayMap(busy -> (busy.2, -busy.1, busy.3), busiest),
                        arrayMap(busy -> (busy.2, busy.3, -busy.1), busiest_for_people))) AS kept
                FROM
                (
                    SELECT
                        toInt64(count()) AS counted_paths,
                        toInt64(sum(page_views)) AS counted_page_views,
                        toInt64(sum(people_views)) AS counted_people_views,
                        groupArraySortedIf({{most}})((-page_views, path, people_views), keepable) AS busiest,
                        groupArraySortedIf({{most}})((-people_views, path, page_views), keepable AND people_views > 0) AS busiest_for_people
                    FROM
                    (
                        SELECT
                            path,
                            page_views,
                            people_views,
                            lengthUTF8(path) <= {{Literal(KeptPages.LongestPath)}} AND visitors >= {{Literal(KeptPages.FewestVisitors)}} AS keepable
                        FROM viewed
                    )
                )
            )
            {{Restraint}}
            """;

        return new CompiledStatement(
            sql,
            [
                .. StatementBindings.Window(month.SiteId, month.Window),
                StatementBindings.LongestVisit(),
            ]);
    }

    /// <summary>
    /// Writes the reading of how each month kept in a table of months of paths was counted, as its newest
    /// count, with anything that table says of its months besides.
    /// </summary>
    /// <param name="table">The table. Written in this file and never supplied by a caller.</param>
    /// <param name="also">The columns read after how the month was counted, each beginning a line of its own.</param>
    /// <returns>The statement, which binds <c>site_id</c>.</returns>
    private static string KeptDescriptions(string table, string also) => $$"""
        SELECT
            month,
            time_zone,
            ruleset_major,
            ruleset_minor,
            ruleset_judged_to,
            counting_rules,
            counted_to,
            judged_to,
            counted_at{{also}}
        FROM {{table}}
        WHERE site_id = {site_id:UUID}
        ORDER BY month, counted_at DESC
        LIMIT 1 BY month
        """;

    /// <summary>Writes a whole number as a statement holds it.</summary>
    private static string Literal(int value) => value.ToString(CultureInfo.InvariantCulture);
}
