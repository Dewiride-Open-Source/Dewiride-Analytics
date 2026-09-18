using Dewiride.Analytics.Application.Sessions;
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
        var sql = SessionSqlCompiler.Compile(Window()).Sql;

        sql.Should().Contain("HAVING started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')");
        sql.Should().Contain("AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')");
    }

    [Fact]
    public void Compiling_Refuses_A_Missing_Window()
    {
        var act = () => SessionSqlCompiler.Compile(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static SessionWindow Window() => new()
    {
        SiteId = SiteId,
        From = From,
        To = To,
        SettledBefore = To,
        IdleTimeout = TimeSpan.FromMinutes(30),
        MaxRequestsPerSession = 1000,
    };
}
