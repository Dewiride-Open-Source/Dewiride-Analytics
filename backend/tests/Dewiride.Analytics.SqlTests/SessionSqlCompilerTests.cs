using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Infrastructure.ClickHouse.Sessions;
using Dewiride.Analytics.Testing;

namespace Dewiride.Analytics.SqlTests;

/// <summary>
/// Approves the statement that groups stored activity into visits.
/// </summary>
/// <remarks>
/// This statement decides what counts as one visit, and every verdict in the product rests on
/// that. Approving it beside the test is what makes a change to it something a person read and
/// agreed to rather than something that quietly re-cut a customer's history.
/// </remarks>
public sealed class SessionSqlCompilerTests
{
    private static readonly Guid SiteId = Guid.Parse("0197c0de-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset From = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 5, 1, 6, 0, 0, TimeSpan.Zero);

    /// <summary>The address the site is known by.</summary>
    private const string SiteDomain = "blog.example.com";

    /// <summary>
    /// Rules other than the ones compiled into the build, so the approved statement does not change
    /// every time the rules do.
    /// </summary>
    private static readonly RulesetVersion Rules = new(4, 2);

    [Fact]
    public void Reconstructing_Visits()
    {
        var statement = SessionSqlCompiler.Compile(Window());

        Snapshot.Matches(CompiledStatementReport.Render(statement));
    }

    /// <summary>
    /// The activity being grouped was written by whoever visited the site, and the window comes
    /// from the engine's own bookmark. Neither is text in the statement.
    /// </summary>
    [Fact]
    public void No_Value_Appears_In_The_Statement_Text()
    {
        var statement = SessionSqlCompiler.Compile(Window());

        statement.Sql.Should().NotContain(SiteId.ToString());
        statement.Sql.Should().NotContain(From.ToUnixTimeMilliseconds().ToString(null as IFormatProvider));
    }

    [Fact]
    public void Every_Placeholder_Has_A_Bound_Value()
    {
        var statement = SessionSqlCompiler.Compile(Window());

        foreach (var parameter in statement.Parameters)
        {
            statement.Sql.Should().Contain($"{{{parameter.Name}:");
        }
    }

    /// <summary>
    /// Activity carrying no visitor key takes no part. A report without one has not told us an
    /// anonymous visitor was there; it has told us nothing about who was there, and gathering all
    /// of those under one empty key would build a single impossibly busy visitor and judge it.
    /// </summary>
    [Fact]
    public void Activity_That_Could_Not_Be_Attributed_Is_Left_Out()
    {
        SessionSqlCompiler.Compile(Window()).Sql.Should().Contain("visitor_key != ''");
    }

    /// <summary>
    /// A page is every report about one arrival at it folded into one, so a page read for half an
    /// hour and reported on thirty times is the single delivery it was, and a page two surfaces
    /// both watched being delivered is not two.
    /// </summary>
    [Fact]
    public void Pages_Are_Counted_From_Every_Report_About_Them()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;

        sql.Should().Contain("toUInt32(countIf(opens_page)) AS page_count");
        sql.Should().NotContain("countIf(kind = 'PageView' AND NOT is_second_sighting)) AS page_count");
    }

    /// <summary>
    /// Only an arrival begins a visit. A tracker reports how a page is going, and reports it being
    /// left, from the page itself, so a report of either kind is an account of a page somebody was
    /// already on — and a departure that reaches the collector an hour after the reader stopped
    /// touching the page is the end of a visit rather than the whole of a new one.
    /// </summary>
    [Fact]
    public void Only_An_Arrival_Begins_A_Visit()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;

        sql.Should().Contain(
            "sum(toUInt8(kind = 'PageView' AND since_previous > {idle_seconds:Int64})) OVER (");
        sql.Should().NotContain("sum(toUInt8(since_previous > {idle_seconds:Int64})) OVER");
    }

    /// <summary>
    /// A report is filed under the arrival it is an account of, which is knowable from the report
    /// itself: the visitor and the page are both on it. Filing it under whichever visit happened to
    /// be nearest would hand a reading to a visit that never went to the page it was measured on.
    /// </summary>
    [Fact]
    public void A_Report_Is_Filed_Under_The_Arrival_It_Is_About()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;

        sql.Should().Contain("max(if(kind = 'PageView', arrival_ordinal, 0)) OVER (");
        sql.Should().Contain("ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS visit_ordinal");
    }

    /// <summary>
    /// A report naming a page its visitor was never seen arriving at takes no part. The page was
    /// delivered to somebody, but nothing on the report says which visit it belonged to — and a
    /// visitor's key changes when the network does and again at midnight, so the tail of a visit
    /// routinely arrives under a key that announced nothing.
    /// </summary>
    [Fact]
    public void A_Report_Belonging_To_No_Arrival_Takes_No_Part()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;

        sql.Should().Contain("max(if(kind = 'PageView', toUnixTimestamp64Milli(server_ts), 0)) OVER (");
        sql.Should().Contain("ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS arrival_ms");
        sql.Should().Contain("WHERE arrival_ms > 0");
    }

    /// <summary>
    /// A tracker restates how long a page has held somebody every time it reports, so the readings
    /// are one per page rather than one per report. Adding the reports up instead would hand the
    /// engine an afternoon where there was a quarter of an hour.
    /// </summary>
    [Fact]
    public void Reading_Time_Is_Counted_Once_Per_Page()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;

        sql.Should().Contain("sumIf(page_engaged_ms, opens_page) AS engaged_ms");
        sql.Should().NotContain("sum(engaged_ms) AS engaged_ms");
    }

    /// <summary>
    /// A visit already under way when the window opens is reconstructed from its own beginning
    /// rather than from wherever the window starts, so what is left of it is recognised as an
    /// earlier visit and left out instead of being returned as a second, shorter one.
    /// </summary>
    [Fact]
    public void Activity_Is_Read_A_Whole_Visit_Before_The_Window()
    {
        var statement = SessionSqlCompiler.Compile(Window());

        statement.Sql.Should().Contain("{from_ms:Int64} - {longest_visit_seconds:Int64} * 1000");
        statement.Sql.Should().Contain("HAVING started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')");
        statement.Parameters.Should().Contain(parameter =>
            parameter.Name == "longest_visit_seconds" && (long)parameter.Value == 86400);
    }

    /// <summary>
    /// A backlog is walked in stretches of a few hours, and where one ends is an accident of when
    /// the engine was last run. What decides whether a visit is over is the moment nothing more can
    /// arrive, which the caller passes in, and never the edge of the stretch the visit happened to
    /// be attributed to.
    /// </summary>
    [Fact]
    public void A_Visit_Is_Over_When_Nothing_More_Can_Arrive_Rather_Than_At_The_Window_Edge()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;

        sql.Should().Contain("toBool(max(server_ts) < fromUnixTimestamp64Milli({settled_ms:Int64}, 'UTC')) AS is_closed");
        sql.Should().NotContain("toBool(max(server_ts) < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC'))");
    }

    /// <summary>
    /// Which visits the window is answerable for is still decided by where each one began, so a
    /// caller working forward covers a site once however long any one visit ran.
    /// </summary>
    [Fact]
    public void A_Visit_Belongs_To_The_Window_It_Began_In()
    {
        var bounds = Between(
            SessionSqlCompiler.Compile(Window()).Sql,
            "HAVING started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')",
            "NOT IN");

        bounds.Should().Contain("AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')");
    }

    /// <summary>
    /// The reading starts where judging waits, at the earliest visit still in progress, and that
    /// can be most of a day back. A visit already judged under the same rules is left out, so the
    /// visits after an open one are judged once rather than on every run until it closes — and the
    /// match includes when the visit ended, so one a later report has made longer is read again.
    /// </summary>
    [Fact]
    public void A_Visit_Already_Judged_Under_The_Same_Rules_Is_Left_Out_Until_More_Of_It_Arrives()
    {
        var statement = SessionSqlCompiler.Compile(Window());

        statement.Sql.Should().Contain("AND (session_key, ended_at) NOT IN");
        statement.Sql.Should().Contain("SELECT session_key, ended_at");
        statement.Sql.Should().Contain("FROM session_classifications");
        statement.Sql.Should().Contain("AND ruleset_major = {ruleset_major:UInt16}");
        statement.Sql.Should().Contain("AND ruleset_minor = {ruleset_minor:UInt16}");
        statement.Parameters.Should().Contain(parameter =>
            parameter.Name == "ruleset_major" && (ushort)parameter.Value == Rules.Major);
        statement.Parameters.Should().Contain(parameter =>
            parameter.Name == "ruleset_minor" && (ushort)parameter.Value == Rules.Minor);
    }

    /// <summary>
    /// The verdicts looked up are this site's, inside the same window: the bounds keep a reading to
    /// the month the window falls in, which is how the table is partitioned, rather than to a site's
    /// whole history.
    /// </summary>
    [Fact]
    public void The_Verdicts_Looked_Up_Are_This_Sites_And_Inside_The_Window()
    {
        var lookup = Between(
            SessionSqlCompiler.Compile(Window()).Sql,
            "FROM session_classifications",
            "confirmations AS");

        lookup.Should().Contain("site_id = {site_id:UUID}");
        lookup.Should().Contain("started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')");
        lookup.Should().Contain("started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')");
    }

    /// <summary>
    /// An answer kept for a visit is read for this site and for the window's own visits — the
    /// bounds are the window's, because an answer is filed under when its visit began — and found
    /// by the visit's name alone. On a rented network one visitor key can stand for a whole
    /// network's day, so an answer looked up by visitor would vouch for activity nobody checked.
    /// </summary>
    [Fact]
    public void An_Answer_Kept_For_A_Visit_Is_Read_For_This_Site_And_Found_By_The_Visit()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;
        var kept = Between(sql, "confirmations AS", "GROUP BY session_key");

        kept.Should().Contain("FROM name_confirmations");
        kept.Should().Contain("WHERE site_id = {site_id:UUID}");
        kept.Should().Contain("AND started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')");
        kept.Should().Contain("AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')");
        kept.Should().Contain("argMax(operator, settled_at) AS operator");
        kept.Should().NotContain("visitor_key");
        kept.Should().NotContain("ip_address");
        sql.Should().Contain("LEFT JOIN confirmations USING (session_key)");
    }

    /// <summary>
    /// What the activity itself says comes first. A list a company publishes about all of its
    /// machines is the stronger statement, and a visit nobody vouched for reads as nobody's — a
    /// visit with no answer kept is not a visit somebody denied.
    /// </summary>
    [Fact]
    public void What_The_Activity_Says_About_Whose_Crawler_A_Visit_Was_Is_Read_Before_What_Was_Kept()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;

        sql.Should().Contain("anyIf(confirmed_operator, confirmed_operator != '') AS stamped_operator");
        sql.Should().Contain(
            "if(stamped_operator != '', stamped_operator, ifNull(operator, '')) AS confirmed_operator");
    }

    [Fact]
    public void Compiling_Refuses_A_Missing_Window()
    {
        var act = () => SessionSqlCompiler.Compile(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Without the site's own address every page a reader opened after the first would name the
    /// site as where they came from, and read as a link from somewhere else.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Compiling_Refuses_A_Window_That_Names_No_Address_For_The_Site(string domain)
    {
        var act = () => SessionSqlCompiler.Compile(Window() with { SiteDomain = domain });

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The site's own address is bound like every other value, never written into the text.
    /// </summary>
    [Fact]
    public void The_Sites_Own_Address_Is_Bound_And_Never_Written_Into_The_Statement()
    {
        var statement = SessionSqlCompiler.Compile(Window());

        statement.Sql.Should().NotContain(SiteDomain);
        statement.Parameters.Should().Contain(parameter =>
            parameter.Name == "site_domain" && (string)parameter.Value == SiteDomain);
    }

    /// <summary>
    /// Which kind of site sent a visit is settled the way the visit list settles it — the first
    /// report naming a site other than this one — so the kind a visit is judged with is the kind a
    /// reader is shown beside it.
    /// </summary>
    [Fact]
    public void Which_Kind_Of_Site_Sent_A_Visit_Is_Settled_As_The_Visit_List_Settles_It()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;

        sql.Should().Contain("argMinIf(source_channel, (server_ts, event_id), sending_host != '') AS from_kind");
    }

    /// <summary>
    /// A reader moving between the site's own pages, or onto one of its own subdomains, names the
    /// site as where they came from, and that is no arrival at all.
    /// </summary>
    [Fact]
    public void The_Sites_Own_Pages_Never_Send_It_A_Visit()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;

        sql.Should().Contain("AND referrer_domain != {site_domain:String}");
        sql.Should().Contain("AND NOT endsWith(referrer_domain, concat('.', {site_domain:String}))");
    }

    /// <summary>
    /// The referring address is text the visitor's browser wrote, and the site it names is no rule's
    /// business: the kind of site is all that leaves the statement.
    /// </summary>
    [Fact]
    public void Neither_The_Referring_Address_Nor_The_Site_It_Names_Is_Handed_Back()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;
        var answer = sql[sql.LastIndexOf("SELECT", StringComparison.Ordinal)..];

        answer.Should().Contain("from_kind");
        answer.Should().NotContain("source_address");
        answer.Should().NotContain("sending_host");
        answer.Should().NotContain("source_site");
    }

    /// <summary>
    /// A report of a page being left carries the whole of that page's time so far, so what the
    /// pages said as they were left is the last such report of each — worked out page by page, on
    /// the same pages the reading is counted over, and from the tracker's reports alone.
    /// </summary>
    [Fact]
    public void What_A_Page_Said_As_It_Was_Left_Is_The_Last_Report_Of_It_Being_Left()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;

        sql.Should().Contain("maxIf(engaged_ms, kind = 'Exit' AND surface = 'BrowserTracker') OVER page AS page_left_ms");
        sql.Should().Contain("WINDOW page AS (PARTITION BY visitor_key, visit_ordinal, path, page_ordinal)");
        Between(sql, "sumIf(", "AS read_ms").Should().Contain("least(ifNull(page_left_ms, 0), page_engaged_ms)");
    }

    /// <summary>
    /// Two pages are never taken for pages left open: one whose address was arrived at again later
    /// in the visit, whose leaving can be filed with the next arrival, and one that reported within
    /// a progress report's interval of midnight, whose later reports go under the next day's key.
    /// Both count whole.
    /// </summary>
    [Fact]
    public void A_Page_Whose_Leaving_May_Have_Been_Filed_Elsewhere_Counts_Whole()
    {
        var statement = SessionSqlCompiler.Compile(Window());
        var read = Between(statement.Sql, "sumIf(", "AS read_ms");

        statement.Sql.Should().Contain(
            "max(page_ordinal) OVER (PARTITION BY visitor_key, visit_ordinal, path) AS last_page_ordinal");
        statement.Sql.Should().Contain("max(server_ts) OVER page AS page_last_at");
        read.Should().Contain("page_ordinal < last_page_ordinal");
        read.Should().Contain(
            "toDate(page_last_at + toIntervalSecond({progress_interval_seconds:Int64})) != toDate(page_last_at)");
        statement.Parameters.Should().Contain(parameter =>
            parameter.Name == "progress_interval_seconds"
            && (long)parameter.Value == (long)SessionSqlCompiler.ProgressInterval.TotalSeconds);
    }

    /// <summary>
    /// The first report of how a page was going is read against the time of day the tracker's own
    /// page view gave, because a reporter on the site's server keeps a different clock — and only
    /// where all three readings exist, so nothing missing is ever read as nought.
    /// </summary>
    [Fact]
    public void A_Pages_First_Report_Is_Read_Against_The_Time_Of_Day_The_Trackers_Own_Page_View_Gave()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;
        var soonest = Between(sql, "AS read_ms", "AS soonest_progress_ms");

        sql.Should().Contain("minIf(client_ts, kind = 'PageView' AND surface = 'BrowserTracker') OVER page AS page_opened_on_client");
        Between(sql, "argMinIf(", "AS first_progress")
            .Should().Contain("(client_ts, engaged_ms)")
            .And.Contain("(server_ts, event_id)")
            .And.Contain("kind = 'Engagement' AND surface = 'BrowserTracker'");
        soonest.Should().Contain("greatest(");
        soonest.Should().Contain("toUnixTimestamp64Milli(first_progress.1) - toUnixTimestamp64Milli(page_opened_on_client)");
        soonest.Should().Contain("AND page_opened_on_client IS NOT NULL");
        soonest.Should().Contain("AND first_progress.1 IS NOT NULL");
        soonest.Should().Contain("AND first_progress.2 IS NOT NULL");
    }

    /// <summary>
    /// Only the first page at an address in a visit is read for how soon its wait was over, and only
    /// a report sent after that page opened: a report another tab or an earlier page at the same
    /// address sent cannot be read against this one's opening.
    /// </summary>
    [Fact]
    public void A_Wait_Is_Read_Only_Where_Nothing_Earlier_At_The_Same_Address_Can_Have_Spilled_Into_It()
    {
        var soonest = Between(SessionSqlCompiler.Compile(Window()).Sql, "AS read_ms", "AS soonest_progress_ms");

        soonest.Should().Contain("AND page_ordinal = 1");
        soonest.Should().Contain("AND first_progress.1 >= page_opened_on_client");
    }

    /// <summary>
    /// What the two readings are worked out from — the page's own time of day — stays inside the
    /// statement; only the lengths of time leave it.
    /// </summary>
    [Fact]
    public void Only_Lengths_Of_Time_Leave_The_Statement_And_Never_A_Time_Of_Day()
    {
        var sql = SessionSqlCompiler.Compile(Window()).Sql;
        var answer = sql[sql.LastIndexOf("SELECT", StringComparison.Ordinal)..];

        answer.Should().Contain("read_ms");
        answer.Should().Contain("soonest_progress_ms");
        answer.Should().NotContain("client_ts");
        answer.Should().NotContain("first_progress");
        answer.Should().NotContain("page_last_at");
    }

    /// <summary>The text from one marker to the first occurrence of another after it.</summary>
    /// <param name="sql">The statement.</param>
    /// <param name="from">Where the slice starts.</param>
    /// <param name="to">What ends it.</param>
    /// <returns>The slice, including the opening marker.</returns>
    private static string Between(string sql, string from, string to)
    {
        var start = sql.IndexOf(from, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, "the statement holds {0}", from);

        var end = sql.IndexOf(to, start + from.Length, StringComparison.Ordinal);

        end.Should().BeGreaterThan(start, "{0} is closed by {1}", from, to);

        return sql[start..end];
    }

    private static SessionWindow Window() => new()
    {
        SiteId = SiteId,
        SiteDomain = SiteDomain,
        From = From,
        To = To,
        SettledBefore = To,
        UnjudgedUnder = Rules,
        IdleTimeout = TimeSpan.FromMinutes(30),
        MaxRequestsPerSession = 1000,
    };
}
