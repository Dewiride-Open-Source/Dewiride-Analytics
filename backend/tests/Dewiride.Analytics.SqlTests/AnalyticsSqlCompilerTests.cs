using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;
using Dewiride.Analytics.Infrastructure.ClickHouse.Sessions;

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
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>A visit identity of the shape the engine derives: a hexadecimal key and an instant.</summary>
    private static readonly VisitKey Visit = new("2f8a1c0b4d6e7f905a1b2c3d4e5f6071", From.AddHours(9));

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
    public Task Site_Pages()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SitePagesQuery(Window(), 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Countries()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteLocationsQuery(Window(), LocationGrouping.Country, 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Towns()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteLocationsQuery(Window(), LocationGrouping.Town, 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Devices()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteDeviceKindsQuery(Window()));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Browsers()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSoftwareQuery(Window(), SoftwareGrouping.Browser, 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Systems()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSoftwareQuery(Window(), SoftwareGrouping.OperatingSystem, 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Controls()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteActionsQuery(Window(), ActionGrouping.Control, 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Control_Destinations()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteActionsQuery(Window(), ActionGrouping.Destination, 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    /// <summary>
    /// Only a press may be counted as a press. Without this the list would rank page views by
    /// their address, which is a different question that already has its own answer.
    /// </summary>
    [Theory]
    [InlineData(ActionGrouping.Control)]
    [InlineData(ActionGrouping.Destination)]
    public void Only_Reports_Of_An_Operated_Control_Are_Counted(ActionGrouping grouping)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteActionsQuery(Window(), grouping, 10));

        statement.Sql.Should().Contain("kind = 'Action'");
    }

    /// <summary>
    /// Where a press led on the site is answered by the pages themselves, so the destination list
    /// would otherwise rank a site against itself and bury the places it actually sends people to.
    /// </summary>
    [Fact]
    public void Only_Presses_That_Led_Off_The_Site_Are_Counted_As_Destinations()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteActionsQuery(Window(), ActionGrouping.Destination, 10));

        statement.Sql.Should().Contain("action_target_kind = 'External'");
    }

    /// <summary>
    /// A press can only be seen by something running in the visitor's own browser, so there is one
    /// account of each and nothing to fold together. Reconciling could only lose presses.
    /// </summary>
    [Fact]
    public void Counting_Presses_Reconciles_Nothing()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteActionsQuery(Window(), ActionGrouping.Control, 10));

        statement.Sql.Should().NotContain("identified");
        statement.Sql.Should().NotContain("correlation_id");
    }

    /// <summary>
    /// A name is written by whoever wrote the page, and a page may carry writing somebody else put
    /// there. It is grouped on and read back, and never becomes part of the statement.
    /// </summary>
    [Fact]
    public void A_Controls_Own_Name_Never_Enters_The_Statement()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteActionsQuery(Window(), ActionGrouping.Control, 10, 40));

        statement.Sql.Should().Contain("GROUP BY name, control");
        statement.Parameters.Select(parameter => parameter.Name)
            .Should()
            .BeEquivalentTo("site_id", "from_ms", "to_ms", "limit", "offset");
    }

    /// <summary>
    /// Each figure describes the whole window rather than the slice returned, so a share and a bar
    /// mean the same thing on every screenful.
    /// </summary>
    [Theory]
    [InlineData("sum(presses) OVER ()")]
    [InlineData("count() OVER ()")]
    [InlineData("max(presses) OVER ()")]
    public void The_Control_List_Describes_The_Whole_Window_Rather_Than_The_Slice(string figure)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteActionsQuery(Window(), ActionGrouping.Control, 10, 40));

        statement.Sql.Should().Contain(figure);
    }

    /// <summary>
    /// Two controls pressed equally often could otherwise swap places between one slice and the
    /// next, which would show one of them twice and never show the other at all.
    /// </summary>
    [Fact]
    public void The_Control_List_Is_Ordered_So_That_Slices_Neither_Repeat_Nor_Skip()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteActionsQuery(Window(), ActionGrouping.Control, 10));

        statement.Sql.Should().Contain("ORDER BY presses DESC, name, control");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SiteActionsQuery.MostControls + 1)]
    public void Asking_For_An_Impossible_Number_Of_Controls_Is_Refused(int limit)
    {
        var act = () => new SiteActionsQuery(Window(), ActionGrouping.Control, limit);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Starting_The_Control_List_Before_Its_Beginning_Is_Refused()
    {
        var act = () => new SiteActionsQuery(Window(), ActionGrouping.Control, 10, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// The grouping chooses a fragment from a closed set. A member outside it has no fragment to
    /// choose, and is refused where it is asked for rather than reaching the compiler.
    /// </summary>
    [Fact]
    public void Gathering_Presses_A_Way_This_Product_Does_Not_Define_Is_Refused()
    {
        var act = () => new SiteActionsQuery(Window(), (ActionGrouping)99, 10);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public Task Site_Engagement()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteEngagementQuery(Window()));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Pages_By_Attention()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SitePageEngagementQuery(Window(), EngagementRanking.Attention, 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Pages_By_Depth()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SitePageEngagementQuery(Window(), EngagementRanking.Depth, 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Visit_Totals()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteVisitShapeQuery(Window(), Visits()));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Entry_Pages()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteVisitFlowQuery(Window(), Visits(), VisitPosition.Entry, 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Exit_Pages()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteVisitFlowQuery(Window(), Visits(), VisitPosition.Exit, 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Visit_Journey()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteVisitJourneyQuery(Visit, IdleTimeout, 200));

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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SitePagesQuery.MostPages + 1)]
    public void Asking_For_An_Impossible_Number_Of_Pages_Is_Refused(int limit)
    {
        var act = () => new SitePagesQuery(Window(), limit);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Starting_The_Page_List_Before_Its_Beginning_Is_Refused()
    {
        var act = () => new SitePagesQuery(Window(), 10, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Each figure describes the whole window rather than the slice returned, so it stays still
    /// while somebody moves through the list. Worked out from the rows returned instead, the
    /// busiest page of a large site would be reported at several times the share it has, and
    /// every slice would begin with a full-length bar.
    /// </summary>
    [Theory]
    [InlineData("sum(page_views) OVER ()")]
    [InlineData("count() OVER ()")]
    [InlineData("max(page_views) OVER ()")]
    public void The_Page_List_Describes_The_Whole_Window_Rather_Than_The_Slice(string figure)
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SitePagesQuery(Window(), 10, 40));

        statement.Sql.Should().Contain(figure);
    }

    /// <summary>
    /// Two addresses with equal traffic could otherwise swap places between one slice and the
    /// next, which would show one of them twice and never show the other at all.
    /// </summary>
    [Fact]
    public void The_Page_List_Orders_Totally_So_Slices_Neither_Repeat_Nor_Skip()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SitePagesQuery(Window(), 10, 40));

        statement.Sql.Should().Contain("ORDER BY page_views DESC, path");
        statement.Sql.Should().Contain("LIMIT {limit:UInt32} OFFSET {offset:UInt32}");
        statement.Parameters.Select(parameter => parameter.Name)
            .Should().Equal("site_id", "from_ms", "to_ms", "limit", "offset");
    }

    /// <summary>
    /// The list and the headline are the same arithmetic, so a share taken against one is a share
    /// of the other. Counting reports here while the headline counts deliveries would put a page
    /// on the list at twice the traffic the site is told it had.
    /// </summary>
    [Theory]
    [InlineData("greatest(")]
    [InlineData("countIf(kind = 'PageView' AND surface IN ('BrowserTracker', 'NoScriptPixel'))")]
    [InlineData("countIf(kind = 'PageView' AND surface NOT IN ('BrowserTracker', 'NoScriptPixel'))")]
    [InlineData("if(visitor_key = '', countIf(kind = 'PageView'), delivered) AS page_views")]
    public void The_Page_List_Counts_Deliveries_On_The_Same_Terms_As_The_Headline(string arithmetic)
    {
        var pages = AnalyticsSqlCompiler.Compile(Scope(), new SitePagesQuery(Window(), 10));
        var overview = AnalyticsSqlCompiler.Compile(Scope(), new OverviewQuery(Window()));

        pages.Sql.Should().Contain(arithmetic);
        overview.Sql.Should().Contain(arithmetic);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SiteLocationsQuery.MostPlaces + 1)]
    public void Asking_For_An_Impossible_Number_Of_Places_Is_Refused(int limit)
    {
        var act = () => new SiteLocationsQuery(Window(), LocationGrouping.Country, limit);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Starting_The_Place_List_Before_Its_Beginning_Is_Refused()
    {
        var act = () => new SiteLocationsQuery(Window(), LocationGrouping.Country, 10, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// The same rule the page list follows, for the same reason: a share taken against the rows
    /// on screen would report the busiest country of a widely-read site at several times the
    /// share it has, and a bar drawn against whatever led one slice would start every slice full.
    /// </summary>
    [Theory]
    [InlineData("sum(visitors) OVER ()")]
    [InlineData("count() OVER ()")]
    [InlineData("max(visitors) OVER ()")]
    public void The_Place_List_Describes_The_Whole_Window_Rather_Than_The_Slice(string figure)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteLocationsQuery(Window(), LocationGrouping.Country, 10, 40));

        statement.Sql.Should().Contain(figure);
    }

    /// <summary>
    /// Two towns of the same name in different countries would otherwise be one row, and two
    /// countries with equal audiences could swap places between slices.
    /// </summary>
    [Fact]
    public void The_Place_List_Orders_Totally_So_Slices_Neither_Repeat_Nor_Skip()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteLocationsQuery(Window(), LocationGrouping.Town, 10, 40));

        statement.Sql.Should().Contain("ORDER BY visitors DESC, place, country_code");
        statement.Sql.Should().Contain("LIMIT {limit:UInt32} OFFSET {offset:UInt32}");
        statement.Parameters.Select(parameter => parameter.Name)
            .Should().Equal("site_id", "from_ms", "to_ms", "limit", "offset");
    }

    /// <summary>
    /// Both halves of the measurement resolve the visitor's address independently and one of them
    /// may have resolved nothing, so the place is settled once for the whole visitor. Taking each
    /// report's own answer would split one reader into a reader somewhere and a reader nowhere.
    /// </summary>
    [Theory]
    [InlineData(LocationGrouping.Country)]
    [InlineData(LocationGrouping.Town)]
    public void A_Visitor_Is_Placed_Once_However_Many_Halves_Reported_Them(LocationGrouping grouping)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteLocationsQuery(Window(), grouping, 10));

        statement.Sql.Should().Contain("anyIf(country_code, country_code != '')");
        statement.Sql.Should().Contain("anyIf(city, city != '')");
        statement.Sql.Should().Contain("GROUP BY visitor_key");
    }

    /// <summary>
    /// A place list counts people rather than pages. Ranked by pages read, whichever country
    /// browses most would head a list that claims to say where an audience is.
    /// </summary>
    [Fact]
    public void The_Place_List_Ranks_By_People_Rather_Than_By_Pages()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteLocationsQuery(Window(), LocationGrouping.Country, 10));

        statement.Sql.Should().Contain("toInt64(count()) AS visitors");
        statement.Sql.Should().Contain("ORDER BY visitors DESC");
    }

    /// <summary>
    /// Which column a place list groups on comes from a fixed table in the compiler. Neither
    /// spelling is a caller's word, and no other spelling can reach the statement at all.
    /// </summary>
    [Theory]
    [InlineData(LocationGrouping.Country, "country_code AS place")]
    [InlineData(LocationGrouping.Town, "city AS place")]
    public void A_Place_List_Groups_On_A_Column_Named_In_The_Compiler(LocationGrouping grouping, string expected)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteLocationsQuery(Window(), grouping, 10));

        statement.Sql.Should().Contain(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SiteSoftwareQuery.MostNames + 1)]
    public void Asking_For_An_Impossible_Number_Of_Names_Is_Refused(int limit)
    {
        var act = () => new SiteSoftwareQuery(Window(), SoftwareGrouping.Browser, limit);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Starting_The_Software_List_Before_Its_Beginning_Is_Refused()
    {
        var act = () => new SiteSoftwareQuery(Window(), SoftwareGrouping.Browser, 10, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// The rule every sliced list here follows, for the reason all of them follow it.
    /// </summary>
    [Theory]
    [InlineData("sum(visitors) OVER ()")]
    [InlineData("count() OVER ()")]
    [InlineData("max(visitors) OVER ()")]
    public void The_Software_List_Describes_The_Whole_Window_Rather_Than_The_Slice(string figure)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSoftwareQuery(Window(), SoftwareGrouping.Browser, 10, 40));

        statement.Sql.Should().Contain(figure);
    }

    [Fact]
    public void The_Software_List_Orders_Totally_So_Slices_Neither_Repeat_Nor_Skip()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSoftwareQuery(Window(), SoftwareGrouping.OperatingSystem, 10, 40));

        statement.Sql.Should().Contain("ORDER BY visitors DESC, name");
        statement.Sql.Should().Contain("LIMIT {limit:UInt32} OFFSET {offset:UInt32}");
        statement.Parameters.Select(parameter => parameter.Name)
            .Should().Equal("site_id", "from_ms", "to_ms", "limit", "offset");
    }

    /// <summary>
    /// Which column a software list groups on comes from a fixed table in the compiler, on the
    /// same terms as a place list's.
    /// </summary>
    [Theory]
    [InlineData(SoftwareGrouping.Browser, "browser_family")]
    [InlineData(SoftwareGrouping.OperatingSystem, "operating_system")]
    public void A_Software_List_Groups_On_A_Column_Named_In_The_Compiler(
        SoftwareGrouping grouping,
        string expected)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSoftwareQuery(Window(), grouping, 10));

        statement.Sql.Should().Contain($"anyIf({expected}, {expected} != '') AS name");
    }

    /// <summary>
    /// Both halves of the measurement read the device from a user agent each, and a report
    /// forwarded by a site's own server frequently carries none — so what somebody was reading on
    /// is settled once for the whole visitor rather than once per report.
    /// </summary>
    [Fact]
    public void A_Visitor_Is_Given_One_Device_However_Many_Halves_Reported_Them()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteDeviceKindsQuery(Window()));

        statement.Sql.Should().Contain("anyIf(device, device != '') AS device");
        statement.Sql.Should().Contain("GROUP BY visitor_key");
    }

    /// <summary>
    /// The kind is read out as text, so a visitor nothing could be established about carries the
    /// empty string as they do everywhere else in the store — rather than the word 'Unknown',
    /// which would sit in the same list as the kinds that were actually established.
    /// </summary>
    [Fact]
    public void The_Device_Split_Leaves_What_It_Could_Not_Establish_Empty()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteDeviceKindsQuery(Window()));

        statement.Sql.Should().Contain("anyIf(toString(device_class), device_class != 'Unknown')");
    }

    /// <summary>
    /// A device split counts people. Ranked by pages, one busy crawler would decide what a site's
    /// readers are said to be reading on.
    /// </summary>
    [Fact]
    public void The_Device_Split_Counts_People_Rather_Than_Pages()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteDeviceKindsQuery(Window()));

        statement.Sql.Should().Contain("toInt64(count()) AS visitors");
        statement.Sql.Should().Contain("ORDER BY visitors DESC, device");
    }

    /// <summary>
    /// Nothing to page through, so nothing that would need bounding: the kinds are a closed set
    /// of five and the whole answer is always a whole answer.
    /// </summary>
    [Fact]
    public void The_Device_Split_Asks_For_The_Window_And_Nothing_Else()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteDeviceKindsQuery(Window()));

        statement.Parameters.Select(parameter => parameter.Name)
            .Should().Equal("site_id", "from_ms", "to_ms");
        statement.Sql.Should().NotContain("LIMIT");
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
    [Theory]
    [InlineData(0)]
    [InlineData(SitePageEngagementQuery.MostPages + 1)]
    public void Asking_For_An_Impossible_Number_Of_Read_Pages_Is_Refused(int limit)
    {
        var act = () => new SitePageEngagementQuery(Window(), EngagementRanking.Attention, limit);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Starting_The_Read_Pages_List_Before_Its_Beginning_Is_Refused()
    {
        var act = () => new SitePageEngagementQuery(Window(), EngagementRanking.Depth, 10, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// A page reports its progress several times over and every report carries a running total, so
    /// the largest report is what that reading came to. Summing them would multiply one reading by
    /// however many times it announced itself.
    /// </summary>
    [Fact]
    public void A_Reading_Is_Worth_Its_Largest_Report_Rather_Than_All_Of_Them()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteEngagementQuery(Window()));

        statement.Sql.Should().Contain("max(engaged_ms)");
        statement.Sql.Should().Contain("max(scroll_depth_percent)");
        statement.Sql.Should().Contain("GROUP BY visitor_key, path");
        statement.Sql.Should().NotContain("sum(engaged_ms)");
    }

    /// <summary>
    /// Only the browser half of the measurement observes any of this. A reading nobody was
    /// watching has to stay countable and stay out of every average, which is what carrying it as
    /// a figure outside either measurement's legal range achieves.
    /// </summary>
    [Fact]
    public void A_Reading_Nothing_Watched_Is_Counted_But_Never_Averaged()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteEngagementQuery(Window()));

        statement.Sql.Should().Contain("toInt32(ifNull(max(engaged_ms), -1)) AS engaged_ms");
        statement.Sql.Should().Contain("toInt64(count()) AS total_readings");
        statement.Sql.Should().Contain("toInt64(countIf(engaged_ms >= 0)) AS measured_readings");
        statement.Sql.Should().Contain("quantileExactIf(0.5)(engaged_ms, engaged_ms >= 0)");
    }

    /// <summary>
    /// The middle reading rather than the mean one: attention has a long tail, and a mean drags
    /// towards it until it describes an audience nobody in it resembles.
    /// </summary>
    [Fact]
    public void Attention_Is_Reported_As_The_Middle_Reading_Rather_Than_The_Average()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteEngagementQuery(Window()));

        statement.Sql.Should().Contain("quantileExactIf(0.5)");
        statement.Sql.Should().NotContain("avg(");
    }

    /// <summary>
    /// The four bands have to account for every measured reading exactly once, or the bar drawn
    /// from them says a site had more or fewer readers than it had.
    /// </summary>
    [Fact]
    public void The_Depth_Bands_Divide_The_Measured_Readings_Without_Gap_Or_Overlap()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteEngagementQuery(Window()));

        statement.Sql.Should().Contain("countIf(depth BETWEEN 0 AND 24)");
        statement.Sql.Should().Contain("countIf(depth BETWEEN 25 AND 49)");
        statement.Sql.Should().Contain("countIf(depth BETWEEN 50 AND 74)");
        statement.Sql.Should().Contain("countIf(depth >= 75)");
    }

    /// <summary>
    /// Nothing to page through: it is one answer about one window.
    /// </summary>
    [Fact]
    public void The_Reading_Summary_Asks_For_The_Window_And_Nothing_Else()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteEngagementQuery(Window()));

        statement.Parameters.Select(parameter => parameter.Name)
            .Should().Equal("site_id", "from_ms", "to_ms");
        statement.Sql.Should().NotContain("LIMIT");
    }

    /// <summary>
    /// A page seen solely by a reporter on a site's own server has nothing to say about how it
    /// was read. Listed with a nought beside it, it would say something quite different.
    /// </summary>
    [Fact]
    public void A_Page_Nothing_Could_Be_Measured_On_Is_Left_Off_The_Reading_List()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SitePageEngagementQuery(Window(), EngagementRanking.Attention, 10));

        statement.Sql.Should().Contain("HAVING measured > 0");
    }

    /// <summary>
    /// Which figure a reading list is ordered by comes from a fixed table in the compiler, on the
    /// same terms as a place list's column.
    /// </summary>
    [Theory]
    [InlineData(EngagementRanking.Attention, "ORDER BY median_engaged_ms DESC, path")]
    [InlineData(EngagementRanking.Depth, "ORDER BY median_depth DESC, path")]
    public void A_Reading_List_Orders_By_A_Figure_Named_In_The_Compiler(
        EngagementRanking ranking,
        string expected)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SitePageEngagementQuery(Window(), ranking, 10));

        statement.Sql.Should().Contain(expected);
        statement.Sql.Should().Contain("LIMIT {limit:UInt32} OFFSET {offset:UInt32}");
    }

    /// <summary>
    /// The figures beside the rows describe the whole window rather than the slice, so they stay
    /// still while somebody moves through the list.
    /// </summary>
    [Theory]
    [InlineData("toInt64(count() OVER ()) AS total_pages")]
    [InlineData("toInt32(max(median_engaged_ms) OVER ()) AS longest_median_engaged_ms")]
    public void The_Reading_List_Describes_The_Whole_Window_Rather_Than_The_Slice(string figure)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SitePageEngagementQuery(Window(), EngagementRanking.Attention, 10, 40));

        statement.Sql.Should().Contain(figure);
    }

    /// <summary>
    /// A reading is a fact about a reader on a page, so activity that never established who was
    /// there takes no part, which is the rule a place list keeps as well.
    /// </summary>
    [Fact]
    public void Activity_That_Named_Nobody_Is_No_Part_Of_A_Reading()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteEngagementQuery(Window()));

        statement.Sql.Should().Contain("WHERE visitor_key != ''");
    }

    /// <summary>
    /// A visit still under way has an unfinished page count, so counting one would report a reader
    /// two pages into a long article as somebody who read one page and left. On a quiet website a
    /// handful of those would decide the answer on their own.
    /// </summary>
    [Fact]
    public void Only_Visits_That_Have_Finished_Are_Counted()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteVisitShapeQuery(Window(), Visits()));

        statement.Sql.Should().Contain("AND ended_at < fromUnixTimestamp64Milli({settled_ms:Int64}, 'UTC')");
    }

    /// <summary>
    /// Activity is read a full idle timeout past the end of the window, so "this visit is over" is
    /// an observation rather than an artefact of where the reading stopped.
    /// </summary>
    [Fact]
    public void Activity_Is_Read_Past_The_Window_So_A_Visit_Is_Watched_Falling_Silent()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteVisitShapeQuery(Window(), Visits()));

        statement.Sql.Should().Contain("{to_ms:Int64} + {idle_seconds:Int64} * 1000");
    }

    /// <summary>
    /// A visit is kept by when it began, so it belongs to exactly one window however long it ran
    /// for and consecutive windows do not both claim it.
    /// </summary>
    [Theory]
    [InlineData("HAVING started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')")]
    [InlineData("AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')")]
    public void A_Visit_Belongs_To_The_Window_It_Began_In(string expected)
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteVisitShapeQuery(Window(), Visits()));

        statement.Sql.Should().Contain(expected);
    }

    /// <summary>
    /// A visit whose page view never arrived says where somebody was but not what they arrived at,
    /// so it is no part of a list of doorways.
    /// </summary>
    [Fact]
    public void A_Visit_That_Asked_For_No_Page_Is_No_Part_Of_The_Count()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new SiteVisitShapeQuery(Window(), Visits()));

        statement.Sql.Should().Contain("AND page_count > 0");
    }

    /// <summary>
    /// Which end of a visit a list counts comes from a table in the compiler, never from the
    /// caller, so the column can only ever be one of two the compiler wrote itself.
    /// </summary>
    [Theory]
    [InlineData(VisitPosition.Entry, "entry_path AS path")]
    [InlineData(VisitPosition.Exit, "exit_path AS path")]
    public void An_Arrival_List_Counts_A_Column_Named_In_The_Compiler(VisitPosition position, string expected)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteVisitFlowQuery(Window(), Visits(), position, 10));

        statement.Sql.Should().Contain(expected);
        statement.Sql.Should().Contain("LIMIT {limit:UInt32} OFFSET {offset:UInt32}");
    }

    /// <summary>
    /// The figures beside the rows describe the whole window rather than the slice, so they stay
    /// still while somebody moves through the list.
    /// </summary>
    [Theory]
    [InlineData("toInt64(sum(visits) OVER ()) AS total_visits")]
    [InlineData("toInt64(count() OVER ()) AS total_paths")]
    [InlineData("toInt64(max(visits) OVER ()) AS most_visits")]
    public void An_Arrival_List_Describes_The_Whole_Window_Rather_Than_The_Slice(string figure)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteVisitFlowQuery(Window(), Visits(), VisitPosition.Entry, 10, 40));

        statement.Sql.Should().Contain(figure);
    }

    /// <summary>
    /// A visit's identity reaches the engine from an address somebody typed. Both halves of it
    /// travel as bound values, so neither can reach the statement as text.
    /// </summary>
    [Fact]
    public void A_Journey_Names_Its_Visitor_As_A_Bound_Value()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteVisitJourneyQuery(Visit, IdleTimeout, 200));

        statement.Sql.Should().Contain("WHERE visitor_key = {visitor_key:String}");
        statement.Sql.Should().NotContain(Visit.VisitorKey);
        statement.Parameters.Should().ContainSingle(parameter =>
            parameter.Name == "visitor_key" && (string)parameter.Value == Visit.VisitorKey);
    }

    /// <summary>
    /// A journey is found rather than looked up: activity is read forward from the instant the
    /// visit began, and the first visit that grouping produces is by construction the one asked
    /// for.
    /// </summary>
    [Fact]
    public void A_Journey_Reads_Forward_From_Where_The_Visit_Began()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteVisitJourneyQuery(Visit, IdleTimeout, 200));

        statement.Sql.Should().Contain("WHERE visit_ordinal = 0");
        statement.Parameters.Should().ContainSingle(parameter =>
            parameter.Name == "from_ms" && (long)parameter.Value == Visit.StartedAt.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// A reader who comes back to an article later in the same visit was there twice, and folding
    /// the two together would report one long reading that never happened.
    /// </summary>
    [Fact]
    public void A_Journey_Is_One_Row_Per_Arrival_Rather_Than_One_Per_Page()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteVisitJourneyQuery(Visit, IdleTimeout, 200));

        statement.Sql.Should().Contain("GROUP BY path, step");
        statement.Sql.Should().Contain("ORDER BY at, press, path");
    }

    /// <summary>
    /// A visit reads as what it did rather than as where it went. Presses are gathered apart from
    /// arrivals, because an arrival is every report about it folded into one row while a press is a
    /// row of its own — somebody who pressed the same button twice pressed it twice.
    /// </summary>
    [Fact]
    public void A_Journey_Carries_What_Was_Operated_Beside_Where_It_Was_Operated()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteVisitJourneyQuery(Visit, IdleTimeout, 200));

        statement.Sql.Should().Contain("WHERE kind != 'Action'");
        statement.Sql.Should().Contain("WHERE kind = 'Action'");
        statement.Sql.Should().Contain("UNION ALL");
    }

    /// <summary>
    /// A control cannot be operated on a page nobody has arrived at, so where the two share an
    /// instant the arrival is the one that comes first.
    /// </summary>
    [Fact]
    public void An_Arrival_Comes_Before_A_Press_That_Shares_Its_Instant()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteVisitJourneyQuery(Visit, IdleTimeout, 200));

        statement.Sql.Should().Contain("toUInt8(0) AS press");
        statement.Sql.Should().Contain("toUInt8(1) AS press");
        statement.Sql.Should().Contain("ORDER BY at, press, path");
    }

    /// <summary>
    /// What nothing could be measured on is carried as a figure outside the range any of the three
    /// can legally take, so "not observed" is never mistaken for an observation.
    /// </summary>
    [Theory]
    [InlineData("toInt16(ifNull(max(status_code), -1)) AS status_code")]
    [InlineData("toInt32(ifNull(max(engaged_ms), -1)) AS engaged_ms")]
    [InlineData("toInt16(ifNull(max(scroll_depth_percent), -1)) AS depth")]
    public void A_Step_Nothing_Watched_Is_Carried_As_Not_Observed(string expected)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteVisitJourneyQuery(Visit, IdleTimeout, 200));

        statement.Sql.Should().Contain(expected);
    }

    /// <summary>
    /// A visit means the same thing wherever one is counted. Both compilers build the grouping from
    /// the same fragment, so the idiom that decides where one visit ends and the next begins cannot
    /// drift between what the dashboard reports and what the engine judged.
    /// </summary>
    [Fact]
    public void Every_Statement_That_Counts_A_Visit_Groups_Them_The_Same_Way()
    {
        var dashboard = AnalyticsSqlCompiler.Compile(Scope(), new SiteVisitShapeQuery(Window(), Visits()));

        var engine = SessionSqlCompiler.Compile(new SessionWindow
        {
            SiteId = SiteId,
            From = From,
            To = To,
            IdleTimeout = IdleTimeout,
            MaxRequestsPerSession = 1000,
        });

        const string grouping = "sum(toUInt8(since_previous > {idle_seconds:Int64})) OVER (";

        dashboard.Sql.Should().Contain(grouping);
        engine.Sql.Should().Contain(grouping);
    }

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

    /// <summary>What a visit is, and which of them the compiler may treat as finished.</summary>
    private static VisitBoundaries Visits() => new(IdleTimeout, To - IdleTimeout);

    /// <summary>
    /// A question from outside the vocabulary, built the only way one can be.
    /// </summary>
    /// <param name="Original">An existing question to take the window from.</param>
    private sealed record UntaughtQuery(AnalyticsQuery Original) : AnalyticsQuery(Original);
}
