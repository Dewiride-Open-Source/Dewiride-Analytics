using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

namespace Dewiride.Analytics.SqlTests;

/// <summary>
/// Approves the statements the analytics compiler produces.
/// </summary>
/// <remarks>
/// One of the two compilers that between them write every statement this product sends, and this
/// suite is what makes a change to it visible: the approved statement sits beside the test, and
/// altering the compiler fails the build until somebody has read the new statement and moved the
/// received file over the approved one.
/// </remarks>
public sealed class AnalyticsSqlCompilerTests
{
    private static readonly Guid SiteId = Guid.Parse("0197c0de-0000-7000-8000-000000000001");
    private static readonly Guid OrganizationId = Guid.Parse("0197c0de-0000-7000-8000-0000000000ff");
    private static readonly DateTimeOffset From = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 5, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task Overview()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new OverviewQuery(Window()));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Page_Views_By_Hour()
    {
        var statement = Compile(TimeGranularity.Hour, TimeSeriesMetric.PageViews);

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Page_Views_By_Day()
    {
        var statement = Compile(TimeGranularity.Day, TimeSeriesMetric.PageViews);

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Visitors_By_Hour()
    {
        var statement = Compile(TimeGranularity.Hour, TimeSeriesMetric.Visitors);

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Visitors_By_Day()
    {
        var statement = Compile(TimeGranularity.Day, TimeSeriesMetric.Visitors);

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Traffic_Breakdown()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new TrafficBreakdownQuery(Window()));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Judged_Sessions()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new JudgedSessionsQuery(Window(), 50));

        return Verify(CompiledStatementReport.Render(statement));
    }

    /// <summary>
    /// Verdicts are kept per ruleset, so a visit judged under two of them exists twice. Both
    /// statements reduce to the newest ruleset that has an opinion about a visit; without that,
    /// improving the rules would double every number on the screen and count one visit as both a
    /// person and a crawler.
    /// </summary>
    [Theory]
    [InlineData("ruleset_major, ruleset_minor, classified_at")]
    [InlineData("session_key")]
    public void The_Breakdown_Counts_Each_Visit_Once(string expected)
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new TrafficBreakdownQuery(Window()));

        statement.Sql.Should().Contain(expected);
    }

    /// <summary>
    /// How much evidence stood behind a conclusion is reported beside it rather than folded into
    /// it. A hundred visits called a crawler on weak evidence is a different statement from a
    /// hundred called one on strong evidence, and collapsing them would hide the distinction the
    /// product exists to make.
    /// </summary>
    [Fact]
    public void The_Breakdown_Reports_Strength_Alongside_Category()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new TrafficBreakdownQuery(Window()));

        statement.Sql.Should().Contain("GROUP BY category, strength");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(JudgedSessionsQuery.MostSessions + 1)]
    public void Asking_For_An_Impossible_Number_Of_Visits_Is_Refused(int limit)
    {
        var act = () => new JudgedSessionsQuery(Window(), limit);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// The property the whole design rests on: nothing a caller supplies is concatenated. The
    /// site being read and the zone its days are cut in come from an authorisation decision, and
    /// both arrive on the value side of the boundary.
    /// </summary>
    [Fact]
    public void No_Caller_Supplied_Value_Appears_In_The_Statement_Text()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new OverviewQuery(Window()));

        statement.Sql.Should().NotContain(SiteId.ToString());
        statement.Sql.Should().NotContain(From.ToUnixTimeMilliseconds().ToString(null as IFormatProvider));
    }

    /// <summary>
    /// A time zone identifier reaches the compiler from the site record, and a site record is
    /// edited by a customer. It is bound like every other value, so text that would end a string
    /// literal cannot reach the statement.
    /// </summary>
    [Fact]
    public void A_Time_Zone_Is_Bound_Rather_Than_Written_Into_The_Statement()
    {
        const string hostile = "Etc/UTC') OR 1=1 --";
        var scope = new TenantScope(SiteId, OrganizationId, SiteRole.Viewer, hostile);

        var statement = AnalyticsSqlCompiler.Compile(
            scope,
            new TimeSeriesQuery(Window(), TimeGranularity.Day, TimeSeriesMetric.PageViews));

        statement.Sql.Should().NotContain(hostile);
        statement.Parameters.Should().Contain(parameter => parameter.Name == "time_zone");
    }

    [Fact]
    public void Every_Placeholder_In_A_Statement_Has_A_Bound_Value()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new TimeSeriesQuery(Window(), TimeGranularity.Hour, TimeSeriesMetric.Visitors));

        foreach (var parameter in statement.Parameters)
        {
            statement.Sql.Should().Contain($"{{{parameter.Name}:");
        }
    }

    [Fact]
    public void An_Overview_Binds_The_Site_And_Both_Ends_Of_The_Window()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new OverviewQuery(Window()));

        statement.Parameters.Select(parameter => parameter.Name)
            .Should().Equal("site_id", "from_ms", "to_ms");
    }

    /// <summary>
    /// The compiler produces statements for the questions it was taught and nothing else. This is
    /// what makes the vocabulary safe rather than merely tidy: a case it does not recognise gets
    /// no statement at all, so there is no default path that could improvise one.
    /// </summary>
    [Fact]
    public void Compiling_Refuses_A_Question_It_Was_Never_Taught()
    {
        var unknown = new UntaughtQuery(new OverviewQuery(Window()));

        var act = () => AnalyticsSqlCompiler.Compile(Scope(), unknown);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Compiling_Refuses_A_Missing_Question()
    {
        var act = () => AnalyticsSqlCompiler.Compile(Scope(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Compiling_Refuses_A_Missing_Authorisation()
    {
        var act = () => AnalyticsSqlCompiler.Compile(null!, new OverviewQuery(Window()));

        act.Should().Throw<ArgumentNullException>();
    }

    private static CompiledStatement Compile(TimeGranularity granularity, TimeSeriesMetric metric) =>
        AnalyticsSqlCompiler.Compile(Scope(), new TimeSeriesQuery(Window(), granularity, metric));

    private static TenantScope Scope() =>
        new(SiteId, OrganizationId, SiteRole.Viewer, "Europe/London");

    private static TimeRange Window() => new(From, To);

    /// <summary>
    /// A question from outside the vocabulary, built the only way one can be.
    /// </summary>
    /// <param name="Original">An existing question to take the window from.</param>
    private sealed record UntaughtQuery(AnalyticsQuery Original) : AnalyticsQuery(Original);
}
