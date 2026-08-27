using System.Text.RegularExpressions;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
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
public sealed partial class AnalyticsSqlCompilerTests
{
    private static readonly Guid SiteId = Guid.Parse("0197c0de-0000-7000-8000-000000000001");
    private static readonly Guid SecondSiteId = Guid.Parse("0197c0de-0000-7000-8000-000000000002");
    private static readonly Guid OrganizationId = Guid.Parse("0197c0de-0000-7000-8000-0000000000ff");
    private static readonly DateTimeOffset From = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 5, 8, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>A visit identity of the shape the engine derives: a hexadecimal key and an instant.</summary>
    private static readonly VisitKey Visit = new("2f8a1c0b4d6e7f905a1b2c3d4e5f6071", From.AddHours(9));

    /// <summary>Two conclusions as the store spells them, for the narrowing a visit list allows.</summary>
    private static readonly string[] NarrowedCategories = ["LikelyHuman", "KnownAiCrawler"];

    /// <summary>Every band at or above strong, which is what a floor on the evidence means.</summary>
    private static readonly string[] StrongEvidenceAndAbove = ["Strong", "Verified"];

    /// <summary>The empty text, which is what the rebuild holds where nothing could be established.</summary>
    private static readonly string[] NothingEstablished = [""];

    /// <summary>The free-text narrowings a hostile value is proved to reach the store as a value.</summary>
    private static readonly string[] FreeTextNarrowings = ["towns", "browsers", "entry_pages"];

    [Fact]
    public Task Overview()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new OverviewQuery(Window()));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Volume_Across_Sites()
    {
        var statement = AnalyticsSqlCompiler.CompileVolume(Volume());

        return Verify(CompiledStatementReport.Render(statement));
    }

    /// <summary>
    /// The two statements that count pages delivered count them the same way. One answers a
    /// dashboard and the other answers an installation's own accounting, and a customer whose
    /// screen and whose allowance disagreed would have no way to tell which figure was wrong.
    /// </summary>
    [Fact]
    public void Counting_A_Window_And_Metering_It_Share_Their_Arithmetic()
    {
        var dashboard = AnalyticsSqlCompiler.Compile(Scope(), new OverviewQuery(Window()));
        var metered = AnalyticsSqlCompiler.CompileVolume(Volume());

        const string delivered = "greatest(\n            countIf(kind = 'PageView' AND surface IN (";

        dashboard.Sql.Should().Contain(delivered);
        metered.Sql.Should().Contain(delivered);
    }

    /// <summary>
    /// Identity is settled inside each site when several are counted at once. A correlation
    /// identifier is minted by the reporting site's own server, so two sites can mint the same one,
    /// and folding them together would move one site's activity onto the other's key.
    /// </summary>
    [Fact]
    public void Metering_Several_Sites_Settles_Identity_Inside_Each_Of_Them()
    {
        var statement = AnalyticsSqlCompiler.CompileVolume(Volume());

        statement.Sql.Should().Contain("GROUP BY site_id, correlation_id");
        statement.Sql.Should().Contain("LEFT JOIN linked ON windowed.site_id = linked.site_id AND");
    }

    [Fact]
    public void Metering_Refuses_A_Missing_Window()
    {
        var act = () => AnalyticsSqlCompiler.CompileVolume(null!);

        act.Should().Throw<ArgumentNullException>();
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

    /// <summary>
    /// A network is not a place, so a row carries no country. Carrying one would divide a company's
    /// datacentres into a row per country, which is the answer this grouping exists to escape.
    /// </summary>
    [Fact]
    public Task Site_Networks()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteLocationsQuery(Window(), LocationGrouping.Network, 10));

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
    public Task Site_Source_Kinds()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSourcesQuery(Window(), SourceGrouping.Kind, "example.com", 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Sending_Sites()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSourcesQuery(Window(), SourceGrouping.Site, "example.com", 10));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Sending_Pages()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSourcesQuery(Window(), SourceGrouping.Page, "example.com", 10));

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
            new SiteVisitJourneyQuery(Visit, IdleTimeout, "example.com", 200));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Traffic_Breakdown()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new TrafficBreakdownQuery(Window()));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Traffic_Series_By_Day()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new TrafficSeriesQuery(Window(), TimeGranularity.Day));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Traffic_Series_By_Hour()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new TrafficSeriesQuery(Window(), TimeGranularity.Hour));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Judged_Sessions()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), Judged(50, 100));

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Judged_Sessions_By_Detail()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), JudgedByDetail());

        return Verify(CompiledStatementReport.Render(statement));
    }

    [Fact]
    public Task Site_Visit_Details()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), Facets());

        return Verify(CompiledStatementReport.Render(statement));
    }

    /// <summary>
    /// A filter never offers something the list beneath it cannot show, so only visits that have
    /// been judged offer a value. Only those the rebuild could still describe, too: a visit whose
    /// activity has aged out has no details left, and offering it as "nothing established" would be
    /// a claim about the visitor rather than about the store.
    /// </summary>
    [Fact]
    public void A_Detail_Is_Offered_Only_From_Visits_That_Were_Judged()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), Facets());

        statement.Sql.Should().Contain("WHERE session_key IN (SELECT session_key FROM judged)");
    }

    /// <summary>
    /// A busy site's pages run into thousands and a list nobody can reach the bottom of is not a
    /// choice, so each detail keeps its commonest values and stops there.
    /// </summary>
    [Fact]
    public void Each_Detail_Offers_Only_Its_Commonest_Values()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), Facets());

        statement.Sql.Should().Contain("ORDER BY kind, visits DESC, value");
        statement.Sql.Should().Contain("LIMIT {most_values:UInt32} BY kind");
    }

    /// <summary>
    /// The store writes a common table expression out again wherever it is named, so nine
    /// selections laid end to end would rebuild every visit in the period nine times over. The nine
    /// details are unfolded from one array instead, over a single pass.
    /// </summary>
    [Fact]
    public void Every_Detail_Is_Counted_In_One_Pass_Over_The_Rebuild()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), Facets());

        statement.Sql.Should().Contain("arrayJoin([");
        statement.Sql.Should().NotContain("UNION ALL");
    }

    /// <summary>
    /// Every detail a reader can narrow to is one the panel can offer them. The two statements name
    /// the nine independently — one as a condition, the other as a value it reports — and a detail
    /// present in one and absent from the other is either a filter with nothing to offer or an
    /// option that narrows nothing.
    /// </summary>
    [Fact]
    public void Every_Detail_A_Reader_Can_Narrow_To_Is_One_The_Answer_Offers()
    {
        var offered = AnalyticsSqlCompiler.Compile(Scope(), Facets());
        var narrowed = AnalyticsSqlCompiler.Compile(Scope(), JudgedByDetail());

        var details = OfferedDetail().Matches(offered.Sql)
            .Select(match => match.Groups[1].Value)
            .ToArray();

        details.Should().HaveCount(9);

        foreach (var detail in details)
        {
            narrowed.Sql.Should().Contain($"OR {detail} IN ");
        }
    }

    /// <summary>
    /// A detail is offered under the address of the site that sent the visits, and a measured site
    /// is never one of its own sources — so a question that arrived without the site's own address
    /// would offer a reader their own site as somewhere their readers came from.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Describing_A_Period_Without_The_Site_Own_Address_Is_Refused(string domain)
    {
        var act = () => new SiteVisitFacetsQuery(Window(), IdleTimeout, domain);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The count describes the whole window rather than the slice returned, so a list can say how
    /// far through it somebody is and stop when there is genuinely nothing left. Counted from the
    /// rows returned instead, a period would be reported as holding whatever a screenful happens
    /// to be, and the list would stop without admitting there is more behind it.
    /// </summary>
    [Fact]
    public void The_Visit_List_Counts_The_Whole_Window_Rather_Than_The_Slice()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), Judged(10, 20));

        statement.Sql.Should().Contain("count() OVER ()");
    }

    /// <summary>
    /// Two visits beginning in the same millisecond are ordinary on a busy site. Without the visit's
    /// own key breaking the tie they could swap places between one slice and the next, and one of
    /// them would be shown twice while the other was never seen at all.
    /// </summary>
    [Fact]
    public void The_Visit_List_Is_Ordered_Totally_So_Slices_Neither_Repeat_Nor_Skip()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), Judged(10, 20));

        statement.Sql.Should().Contain("ORDER BY started_at DESC, session_key");
    }

    [Fact]
    public void Starting_The_Visit_List_Before_Its_Beginning_Is_Refused()
    {
        var act = () => new JudgedSessionsQuery(Window(), IdleTimeout, "example.com", 10, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// What the reader narrowed to is bound like every other value, so a list of conclusions never
    /// reaches the statement as text.
    /// </summary>
    [Fact]
    public void Narrowing_The_Visit_List_Binds_What_Was_Asked_For()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            Judged(new VisitNarrowing
            {
                Categories = [TrafficCategory.LikelyHuman, TrafficCategory.KnownAiCrawler],
                LeastStrength = EvidenceStrength.Moderate,
                LeastPages = 1,
            }));

        statement.Sql.Should().NotContain("LikelyHuman").And.NotContain("Moderate");

        statement.Parameters.Should().ContainSingle(parameter => parameter.Name == "categories")
            .Which.Value.Should().BeEquivalentTo(NarrowedCategories);

        statement.Parameters.Should().ContainSingle(parameter => parameter.Name == "least_pages")
            .Which.Value.Should().Be(1U);
    }

    /// <summary>
    /// A floor on the evidence means every band at or above it, written out as the set the store
    /// spells them by rather than compared as a number the store happens to have recorded.
    /// </summary>
    [Fact]
    public void Asking_For_Strong_Evidence_Includes_Everything_Above_It()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            Judged(new VisitNarrowing { LeastStrength = EvidenceStrength.Strong }));

        statement.Parameters.Should().ContainSingle(parameter => parameter.Name == "strengths")
            .Which.Value.Should().BeEquivalentTo(StrongEvidenceAndAbove);
    }

    /// <summary>
    /// Nothing asked for is every visit, and the statement says so rather than being assembled a
    /// second way: one shape of statement is one plan for the store and one text to approve.
    /// </summary>
    [Fact]
    public void Narrowing_By_Nothing_Leaves_The_Statement_Unchanged()
    {
        var narrowed = AnalyticsSqlCompiler.Compile(
            Scope(),
            Judged(new VisitNarrowing { Categories = [TrafficCategory.LikelyHuman] }));

        var whole = AnalyticsSqlCompiler.Compile(Scope(), Judged(10));

        whole.Sql.Should().Be(narrowed.Sql);

        whole.Parameters.Should().ContainSingle(parameter => parameter.Name == "categories")
            .Which.Value.Should().BeEquivalentTo(Array.Empty<string>());
    }

    /// <summary>
    /// A visit is kept or dropped on the verdict a reader would be shown. Narrowing before the
    /// reduction to one row per visit would find a visit by a conclusion newer rules have already
    /// replaced.
    /// </summary>
    [Fact]
    public void Narrowing_Happens_After_Each_Visit_Is_Reduced_To_One_Verdict()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            Judged(new VisitNarrowing { Categories = [TrafficCategory.LikelyHuman] }));

        statement.Sql.IndexOf("LIMIT 1 BY session_key", StringComparison.Ordinal).Should()
            .BeLessThan(statement.Sql.IndexOf("toString(category) IN", StringComparison.Ordinal));
    }

    /// <summary>
    /// A narrowing that asks what a visit itself was cannot be answered from the verdicts, so the
    /// period's visits are rebuilt from activity and joined on the identity the engine derived.
    /// Reading a stored copy instead would freeze both catalogues at the moment each visit was
    /// judged, and leave the same visit filed one way on a card and another way in this list.
    /// </summary>
    [Theory]
    [InlineData("described AS")]
    [InlineData("AND session_key IN (SELECT session_key FROM narrowed)")]
    public void Narrowing_The_Visit_List_By_What_A_Visit_Was_Rebuilds_The_Period(string expected)
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), JudgedByDetail());

        statement.Sql.Should().Contain(expected);
    }

    /// <summary>
    /// The ordinary list pays nothing for the nine narrowings it was not given. A rebuild is a
    /// second reading of every event in the period rather than a condition on rows the store
    /// already has, and doing it unconditionally would charge every reader for work nobody asked
    /// for.
    /// </summary>
    [Fact]
    public void Narrowing_By_Nothing_A_Visit_Did_Rebuilds_Nothing()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), Judged(10));

        statement.Sql.Should().NotContain("described");
    }

    /// <summary>
    /// One shape whatever combination was named, on the same terms as the narrowing the verdicts
    /// answer: an empty set is the question "all of them", so the store gets one plan to reuse and
    /// there is one statement to read rather than one per combination somebody might ask for.
    /// </summary>
    [Fact]
    public void Narrowing_By_Any_Combination_Of_Details_Leaves_The_Statement_Unchanged()
    {
        var one = AnalyticsSqlCompiler.Compile(
            Scope(),
            Judged(new VisitNarrowing { Countries = ["IN"] }));

        var everything = AnalyticsSqlCompiler.Compile(
            Scope(),
            Judged(new VisitNarrowing
            {
                Categories = [TrafficCategory.LikelyHuman],
                LeastStrength = EvidenceStrength.Strong,
                LeastPages = 2,
                Devices = [DeviceClass.Phone],
                SourceKinds = [SourceChannel.Search],
                Browsers = ["Firefox"],
                OperatingSystems = ["Android"],
                Countries = ["IN"],
                Towns = ["Jaipur"],
                Networks = ["Hetzner Online"],
                Sources = ["Google"],
                EntryPages = ["/pricing"],
            }));

        everything.Sql.Should().Be(one.Sql);
    }

    /// <summary>
    /// What nothing established is held as an empty text rather than as the word the vocabulary
    /// spells it by, because that is what the rebuild produces where no report said anything at
    /// all. So asking to see the visits nobody could place has to ask for the empty text, and the
    /// two closed sets are the only narrowings where that mapping has to be performed.
    /// </summary>
    [Theory]
    [InlineData("devices")]
    [InlineData("source_kinds")]
    public void Asking_For_The_Visits_Nothing_Was_Established_About_Asks_For_An_Empty_Value(string bound)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            Judged(new VisitNarrowing
            {
                Devices = [DeviceClass.Unknown],
                SourceKinds = [SourceChannel.Direct],
            }));

        statement.Parameters.Should().ContainSingle(parameter => parameter.Name == bound)
            .Which.Value.Should().BeEquivalentTo(NothingEstablished);
    }

    /// <summary>
    /// A visit is still kept or dropped on the verdict a reader would be shown, and the count still
    /// describes the narrowed list — so the further condition sits in the same outer selection as
    /// the other three rather than inside the rebuild.
    /// </summary>
    [Fact]
    public void Narrowing_By_What_A_Visit_Was_Still_Counts_The_Narrowed_List()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), JudgedByDetail());

        statement.Sql.IndexOf("LIMIT 1 BY session_key", StringComparison.Ordinal).Should()
            .BeLessThan(statement.Sql.IndexOf("FROM narrowed", StringComparison.Ordinal));

        statement.Sql.Should().Contain("count() OVER ()");
    }

    /// <summary>
    /// A narrowing may ask which site sent a visit, and the measured site is never one of its own
    /// sources — so the statement needs the site's own address, and a question that arrived without
    /// one would quietly report a site as its own busiest source.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Visit_List_Without_The_Site_Own_Address_Is_Refused(string domain)
    {
        var act = () => new JudgedSessionsQuery(Window(), IdleTimeout, domain, 10);

        act.Should().Throw<ArgumentException>();
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
    /// The series reduces each visit to one verdict on exactly the terms the breakdown does, and
    /// takes the instant it buckets on from that same row. A visit dated by one ruleset and counted
    /// by another would drift between the two answers without either of them being wrong.
    /// </summary>
    [Theory]
    [InlineData("argMax(started_at, (ruleset_major, ruleset_minor, classified_at))")]
    [InlineData("GROUP BY session_key")]
    public void The_Series_Counts_Each_Visit_Once(string expected)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new TrafficSeriesQuery(Window(), TimeGranularity.Day));

        statement.Sql.Should().Contain(expected);
    }

    /// <summary>
    /// Every category the window held covers the whole of it, because the fill restarts inside each
    /// one. Ordered the other way round the answer comes back ragged — a category's counts as long
    /// as its own busiest run rather than as long as the bucket list — and a reader lining the two
    /// up against each other would read every count against the wrong day.
    /// </summary>
    [Fact]
    public void Every_Category_In_The_Series_Covers_The_Whole_Window()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new TrafficSeriesQuery(Window(), TimeGranularity.Hour));

        statement.Sql.Should().Contain("ORDER BY category, bucket");
        statement.Sql.Should().Contain("WITH FILL");
        statement.Sql.Should().Contain("STEP INTERVAL 1 HOUR");
    }

    /// <summary>
    /// How finely to cut a period is the only thing a caller chooses about this statement, and
    /// their word for it never reaches the text: it is looked up in a closed set on the way in and
    /// the statement is assembled from identifiers this codebase owns. What the caller does supply
    /// — the site and both ends of the window — is bound rather than written in.
    /// </summary>
    [Theory]
    [InlineData(TimeGranularity.Day, "toStartOfDay")]
    [InlineData(TimeGranularity.Hour, "toStartOfHour")]
    public void A_Series_Names_The_Site_And_The_Window_Rather_Than_Writing_Them_In(
        TimeGranularity granularity,
        string bucket)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new TrafficSeriesQuery(Window(), granularity));

        statement.Sql.Should().Contain(bucket);
        statement.Sql.Should().NotContain(SiteId.ToString());
        statement.Sql.Should().NotContain(From.ToUnixTimeMilliseconds().ToString(null as IFormatProvider));
        statement.Parameters.Select(parameter => parameter.Name)
            .Should().Equal("site_id", "from_ms", "to_ms", "time_zone");
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
        var act = () => new JudgedSessionsQuery(Window(), IdleTimeout, "example.com", limit);

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
    [InlineData("toUInt64(countIf(kind != 'PageView' AND surface IN ('BrowserTracker', 'NoScriptPixel')) > 0)")]
    [InlineData("if(visitor_key = '', countIf(kind = 'PageView'), delivered) AS page_views")]
    public void The_Page_List_Counts_Deliveries_On_The_Same_Terms_As_The_Headline(string arithmetic)
    {
        var pages = AnalyticsSqlCompiler.Compile(Scope(), new SitePagesQuery(Window(), 10));
        var overview = AnalyticsSqlCompiler.Compile(Scope(), new OverviewQuery(Window()));

        pages.Sql.Should().Contain(arithmetic);
        overview.Sql.Should().Contain(arithmetic);
    }

    /// <summary>
    /// A tracker only reports how a page is being read from the page itself, so a progress report
    /// is evidence the page was delivered even when the report announcing the delivery was lost.
    /// One delivery is credited however many progress reports arrived, because a page read for
    /// half an hour and reported on thirty times was still delivered once.
    /// </summary>
    [Fact]
    public void A_Page_Only_Ever_Reported_As_Read_Still_Counts_As_Delivered_Once()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), new OverviewQuery(Window()));

        statement.Sql.Should().Contain(
            "toUInt64(countIf(kind != 'PageView' AND surface IN ('BrowserTracker', 'NoScriptPixel')) > 0)");
        statement.Sql.Should().NotContain(
            "countIf(kind != 'PageView' AND surface IN ('BrowserTracker', 'NoScriptPixel')),");
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
    [InlineData(SiteSourcesQuery.MostSources + 1)]
    public void Asking_For_An_Impossible_Number_Of_Sources_Is_Refused(int limit)
    {
        var act = () => new SiteSourcesQuery(Window(), SourceGrouping.Site, "example.com", limit);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Starting_The_Source_List_Before_Its_Beginning_Is_Refused()
    {
        var act = () => new SiteSourcesQuery(Window(), SourceGrouping.Site, "example.com", 10, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Without an address to exclude, the site being measured is its own busiest source: every
    /// page after the first in a visit was reached from it. A question that cannot say which site
    /// it is about cannot be asked at all.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Source_List_Without_The_Site_Own_Address_Is_Refused(string domain)
    {
        var act = () => new SiteSourcesQuery(Window(), SourceGrouping.Site, domain, 10);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The same rule the place list follows and for the same reason: shares taken against a
    /// screenful would overstate every row, and a bar drawn against whatever led one slice would
    /// start every slice full.
    /// </summary>
    [Theory]
    [InlineData("sum(visitors) OVER ()")]
    [InlineData("count() OVER ()")]
    [InlineData("max(visitors) OVER ()")]
    public void The_Source_List_Describes_The_Whole_Window_Rather_Than_The_Slice(string figure)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSourcesQuery(Window(), SourceGrouping.Site, "example.com", 10, 40));

        statement.Sql.Should().Contain(figure);
    }

    [Fact]
    public void The_Source_List_Orders_Totally_So_Slices_Neither_Repeat_Nor_Skip()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSourcesQuery(Window(), SourceGrouping.Page, "example.com", 10, 40));

        statement.Sql.Should().Contain("ORDER BY visitors DESC, source, site");
        statement.Sql.Should().Contain("LIMIT {limit:UInt32} OFFSET {offset:UInt32}");
        statement.Parameters.Select(parameter => parameter.Name)
            .Should().Equal(
                "site_id",
                "from_ms",
                "to_ms",
                "site_domain",
                "second_levels",
                "source_keys",
                "source_names",
                "source_channels",
                "limit",
                "offset");
    }

    /// <summary>
    /// The measured site is not one of its own sources, and neither is anything below it: a site
    /// reachable at two names, or spread across a documentation subdomain and a main one, would
    /// otherwise head its own list. This is the rule the collector applies when it decides whose
    /// traffic a report is, and the address is bound rather than written into the statement.
    /// </summary>
    [Theory]
    [InlineData(SourceGrouping.Site)]
    [InlineData(SourceGrouping.Page)]
    public void A_Site_Is_Never_Its_Own_Source(SourceGrouping grouping)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSourcesQuery(Window(), grouping, "example.com", 10));

        statement.Sql.Should().Contain("referrer_domain != {site_domain:String}");
        statement.Sql.Should().Contain("endsWith(referrer_domain, concat('.', {site_domain:String}))");
        statement.Sql.Should().NotContain("example.com");

        statement.Parameters.Should()
            .ContainSingle(parameter => parameter.Name == "site_domain")
            .Which.Value.Should().Be("example.com");
    }

    /// <summary>
    /// Only a visit's first page names anywhere else, so the source is settled once for the whole
    /// visitor. Taking each report's own answer would file one arrival from a search engine as one
    /// arrival from the search engine and a dozen from the site being measured.
    /// </summary>
    [Theory]
    [InlineData(SourceGrouping.Site)]
    [InlineData(SourceGrouping.Page)]
    public void A_Visitor_Is_Credited_To_One_Source_However_Many_Pages_They_Read(SourceGrouping grouping)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSourcesQuery(Window(), grouping, "example.com", 10));

        statement.Sql.Should().Contain("anyIf(source, source != '')");
        statement.Sql.Should().Contain("GROUP BY visitor_key");
    }

    /// <summary>
    /// Which expression a source list groups on comes from a fixed table in the compiler, and a
    /// page row carries nothing after the question mark: that part is somebody else's site
    /// carrying somebody else's state, and which article sent the readers is answered without it.
    /// </summary>
    [Theory]
    [InlineData(SourceGrouping.Site, "anyIf(source_site, source_site != '') AS source")]
    [InlineData(SourceGrouping.Page, "anyIf(concat(source_site, path(source_address)), source_site != '') AS source")]
    [InlineData(SourceGrouping.Kind, "anyIf(source_channel, source_site != '') AS source")]
    public void A_Source_List_Groups_On_An_Expression_Named_In_The_Compiler(
        SourceGrouping grouping,
        string expected)
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSourcesQuery(Window(), grouping, "example.com", 10));

        statement.Sql.Should().Contain(expected);
    }

    /// <summary>
    /// One site answers on many addresses, and a list that treats each as its own row reports the
    /// busiest source of a site's traffic at a fraction of its size on each of a dozen rows.
    /// </summary>
    [Fact]
    public void A_Leading_Www_Is_Not_A_Different_Site()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSourcesQuery(Window(), SourceGrouping.Site, "example.com", 10));

        statement.Sql.Should().Contain("startsWith(referrer_domain, 'www.')");
        statement.Sql.Should().Contain("substring(referrer_domain, 5)");
    }

    /// <summary>
    /// A site is recognised by the label in front of its public suffix, so one search engine's
    /// country addresses are one row. That is also what keeps the lookup safe: a referrer is
    /// written by whoever visited the site, and matching any label would let somebody who
    /// registers <c>google.attacker.test</c> file their traffic under Google's name on a
    /// stranger's dashboard.
    /// </summary>
    [Fact]
    public void A_Site_Is_Recognised_By_The_Label_In_Front_Of_Its_Suffix()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSourcesQuery(Window(), SourceGrouping.Site, "example.com", 10));

        statement.Sql.Should().Contain("has({second_levels:Array(String)}, arrayElement(labels, -2))");
        statement.Sql.Should().Contain("arrayElement(labels, -3)");
        statement.Sql.Should().Contain("arrayElement(labels, -2)) AS sending_name");
    }

    /// <summary>
    /// The catalogue is bound rather than written into the statement, so an approved statement
    /// stays the shape of the question instead of a hundred hostnames — and so that nothing in it
    /// can be mistaken for text a caller supplied.
    /// </summary>
    [Fact]
    public void The_Catalogue_Of_Sending_Sites_Is_Bound_Rather_Than_Written_In()
    {
        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            new SiteSourcesQuery(Window(), SourceGrouping.Kind, "example.com", 10));

        statement.Sql.Should().NotContain("google").And.NotContain("duckduckgo");

        var keys = statement.Parameters.Single(parameter => parameter.Name == "source_keys");

        keys.Value.Should().BeOfType<string[]>().Which.Should().Contain("google");
    }

    /// <summary>
    /// Three arrays read in step, so an entry added to one without the others would name a site
    /// nothing can look up or give it a kind that belongs to a different site.
    /// </summary>
    [Fact]
    public void Every_Catalogued_Site_Has_A_Name_And_A_Kind()
    {
        TrafficSources.Keys.Should().HaveCount(TrafficSources.Names.Length);
        TrafficSources.Keys.Should().HaveCount(TrafficSources.Channels.Length);
        TrafficSources.Keys.Should().OnlyHaveUniqueItems();
        TrafficSources.Names.Should().AllSatisfy(name => name.Should().NotBeNullOrWhiteSpace());
    }

    /// <summary>
    /// Every key reaches the store inside a bound array, but a key carrying a quotation mark would
    /// still be a mistake nobody would notice until a customer's list went blank.
    /// </summary>
    [Fact]
    public void No_Catalogued_Key_Holds_Anything_But_A_Hostname()
    {
        TrafficSources.Keys.Should().AllSatisfy(key =>
            key.Should().MatchRegex("^[a-z0-9.-]+$"));

        TrafficSources.SecondLevelSuffixes.Should().AllSatisfy(suffix =>
            suffix.Should().MatchRegex("^[a-z]+$"));
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
    /// A narrowing carries a town, a browser's name and a page's address, and every one of them was
    /// typed into a link — by the reader, or by whoever sent them the link. They are bound like
    /// everything else, so text written to end a string literal reaches the store as the town
    /// nobody lives in.
    /// </summary>
    [Fact]
    public void A_Narrowing_Is_Bound_Rather_Than_Written_Into_The_Statement()
    {
        const string hostile = "') OR 1=1 --";

        var statement = AnalyticsSqlCompiler.Compile(
            Scope(),
            Judged(new VisitNarrowing
            {
                Towns = [hostile],
                Browsers = [hostile],
                EntryPages = [hostile],
            }));

        statement.Sql.Should().NotContain(hostile);

        foreach (var name in FreeTextNarrowings)
        {
            statement.Parameters.Should().ContainSingle(parameter => parameter.Name == name)
                .Which.Value.Should().BeEquivalentTo(new[] { hostile });
        }
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

    /// <summary>
    /// A statement and its values are written apart and have to meet exactly. A placeholder nothing
    /// is bound to is a statement the store refuses outright; a value bound to a placeholder no
    /// statement names is worse, because it is a narrowing that silently does nothing.
    /// </summary>
    [Fact]
    public void Every_Placeholder_In_A_Statement_Has_A_Bound_Value()
    {
        foreach (var statement in EveryShapeOfStatement())
        {
            var bound = statement.Parameters.Select(parameter => parameter.Name).ToArray();

            var named = Placeholder().Matches(statement.Sql)
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            named.Should().BeEquivalentTo(bound);
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
            new SiteVisitJourneyQuery(Visit, IdleTimeout, "example.com", 200));

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
            new SiteVisitJourneyQuery(Visit, IdleTimeout, "example.com", 200));

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
            new SiteVisitJourneyQuery(Visit, IdleTimeout, "example.com", 200));

        statement.Sql.Should().Contain("GROUP BY path, page_ordinal");
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
            new SiteVisitJourneyQuery(Visit, IdleTimeout, "example.com", 200));

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
            new SiteVisitJourneyQuery(Visit, IdleTimeout, "example.com", 200));

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
            new SiteVisitJourneyQuery(Visit, IdleTimeout, "example.com", 200));

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

        var engine = SessionSqlCompiler.Compile(Judging());

        const string grouping = "sum(toUInt8(since_previous > {idle_seconds:Int64})) OVER (";

        dashboard.Sql.Should().Contain(grouping);
        engine.Sql.Should().Contain(grouping);
    }

    /// <summary>
    /// A visit is named by its visitor and the instant it began, and that name is derived rather
    /// than stored — so a list narrowed by what a visit was and the engine that judged it have to
    /// derive it identically. They do not meet in a foreign key that would fail loudly: they meet in
    /// a text, and a text that differs matches nothing and hands back an empty list with no error.
    /// </summary>
    [Fact]
    public void Every_Statement_That_Names_A_Visit_Derives_The_Name_The_Same_Way()
    {
        var dashboard = AnalyticsSqlCompiler.Compile(Scope(), JudgedByDetail());

        var engine = SessionSqlCompiler.Compile(Judging());

        const string identity = "concat(visitor_key, ':', toString(toUnixTimestamp64Milli(min(server_ts))))";

        dashboard.Sql.Should().Contain(identity);
        engine.Sql.Should().Contain(identity);
    }

    /// <summary>
    /// A visit already under way when the period opened has to keep its own beginning, because its
    /// beginning is half its name. Read from the period's own start it would be handed an invented
    /// one, and every visit that crossed the edge would quietly disappear from a narrowed list.
    /// Reading past the far end is what makes the visits at that edge whole in the same way.
    /// </summary>
    [Theory]
    [InlineData("server_ts >= fromUnixTimestamp64Milli({from_ms:Int64} - {idle_seconds:Int64} * 1000, 'UTC')")]
    [InlineData("server_ts < fromUnixTimestamp64Milli({to_ms:Int64} + {idle_seconds:Int64} * 1000, 'UTC')")]
    public void Rebuilding_A_Visit_Reads_Past_Both_Ends_Of_The_Period(string expected)
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), JudgedByDetail());

        statement.Sql.Should().Contain(expected);
    }

    /// <summary>
    /// A visit belongs to the period it began in, on the same terms as the verdict it is joined to.
    /// Without that the rebuild would describe visits from either side of the period as well, which
    /// costs work and could only ever match a verdict from another period's list.
    /// </summary>
    [Fact]
    public void A_Rebuilt_Visit_Belongs_To_The_Period_It_Began_In()
    {
        var statement = AnalyticsSqlCompiler.Compile(Scope(), Facets());

        statement.Sql.Should().Contain("HAVING started_at >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')");
        statement.Sql.Should().Contain("AND started_at < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')");
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

    /// <summary>Two sites of one organisation, which is what a usage figure is summed across.</summary>
    private static SiteVolumeWindow Volume() =>
        new() { Range = Window(), SiteIds = [SiteId, SecondSiteId] };

    /// <summary>What a visit is, and which of them the compiler may treat as finished.</summary>
    private static VisitBoundaries Visits() => new(IdleTimeout, To - IdleTimeout);

    /// <summary>A slice of the standard window's judged visits, narrowed to nothing.</summary>
    private static JudgedSessionsQuery Judged(int limit, int offset = 0) =>
        new(Window(), IdleTimeout, "example.com", limit, offset);

    /// <summary>A slice of the standard window's judged visits, narrowed as given.</summary>
    private static JudgedSessionsQuery Judged(VisitNarrowing narrowing) =>
        Judged(10) with { Narrowing = narrowing };

    /// <summary>A slice of the standard window's judged visits, narrowed by what a visit itself was.</summary>
    private static JudgedSessionsQuery JudgedByDetail() =>
        Judged(new VisitNarrowing { Devices = [DeviceClass.Phone] });

    /// <summary>What each detail of the standard window's judged visits held.</summary>
    private static SiteVisitFacetsQuery Facets() => new(Window(), IdleTimeout, "example.com");

    /// <summary>What the engine was asked when it judged the standard window.</summary>
    private static SessionWindow Judging() =>
        new()
        {
            SiteId = SiteId,
            From = From,
            To = To,
            IdleTimeout = IdleTimeout,
            MaxRequestsPerSession = 1000,
        };

    /// <summary>
    /// One statement of every shape that binds more than a window.
    /// </summary>
    /// <remarks>
    /// Held together so that a property every statement has to have is asserted over all of them
    /// rather than over whichever one happened to be in mind when the assertion was written.
    /// </remarks>
    /// <returns>The statements.</returns>
    private static CompiledStatement[] EveryShapeOfStatement() =>
    [
        Compile(TimeGranularity.Hour, TimeSeriesMetric.Visitors),
        AnalyticsSqlCompiler.Compile(Scope(), new TrafficSeriesQuery(Window(), TimeGranularity.Day)),
        AnalyticsSqlCompiler.Compile(Scope(), Judged(50, 100)),
        AnalyticsSqlCompiler.Compile(Scope(), JudgedByDetail()),
        AnalyticsSqlCompiler.Compile(Scope(), Facets()),
    ];

    /// <summary>Finds every placeholder a statement names.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"\{(\w+):")]
    private static partial Regex Placeholder();

    /// <summary>
    /// Finds every detail an answer offers.
    /// </summary>
    /// <remarks>
    /// The back-reference is the assertion: a detail is reported under the name of the column it
    /// was read from, so a value can be sorted back into the list it belongs to.
    /// </remarks>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"\('(\w+)', \1\)")]
    private static partial Regex OfferedDetail();

    /// <summary>
    /// A question from outside the vocabulary, built the only way one can be.
    /// </summary>
    /// <param name="Original">An existing question to take the window from.</param>
    private sealed record UntaughtQuery(AnalyticsQuery Original) : AnalyticsQuery(Original);
}
