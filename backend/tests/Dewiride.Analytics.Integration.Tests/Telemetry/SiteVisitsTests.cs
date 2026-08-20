using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that a visit means the same thing wherever one is counted, and that a journey is exactly
/// what the events say.
/// </summary>
/// <remarks>
/// Visits are not stored. They are rebuilt from activity by a window function over a real store, so
/// this is the only place the properties that matter can be shown to hold: that a silence longer
/// than the idle timeout ends a visit and a shorter one does not, that a visit watched by both
/// halves of the measurement is one visit rather than two, and that a visit still under way takes
/// no part in an answer about how visits went.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SiteVisitsTests(AnalyticsStackFixture stack)
{
    private static readonly DateTimeOffset Midnight = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    [Fact]
    public async Task Visits_Are_Counted_With_The_Pages_They_Asked_For()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Asked(siteId, Midnight.AddHours(1), "visitor-a", "/"),
            Asked(siteId, Midnight.AddHours(1).AddMinutes(2), "visitor-a", "/pricing"),
            Asked(siteId, Midnight.AddHours(2), "visitor-b", "/"));

        var shape = await ShapeOf(siteId);

        shape.Visits.Should().Be(2);
        shape.PageViews.Should().Be(3);
        shape.SinglePageVisits.Should().Be(1);
    }

    /// <summary>
    /// The grouping is the whole definition of a visit, and it lives in one fragment both compilers
    /// build from — so proving it here proves it for the verdicts as well.
    /// </summary>
    [Fact]
    public async Task A_Silence_Longer_Than_The_Timeout_Begins_A_New_Visit()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Asked(siteId, Midnight.AddHours(1), "visitor-a", "/"),
            Asked(siteId, Midnight.AddHours(1).AddMinutes(31), "visitor-a", "/pricing"));

        var shape = await ShapeOf(siteId);

        shape.Visits.Should().Be(2);
        shape.SinglePageVisits.Should().Be(2);
    }

    [Fact]
    public async Task A_Silence_Shorter_Than_The_Timeout_Continues_The_Same_Visit()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Asked(siteId, Midnight.AddHours(1), "visitor-a", "/"),
            Asked(siteId, Midnight.AddHours(1).AddMinutes(29), "visitor-a", "/pricing"));

        var shape = await ShapeOf(siteId);

        shape.Visits.Should().Be(1);
        shape.SinglePageVisits.Should().Be(0);
    }

    /// <summary>
    /// A visit whose pages are still arriving would be reported as a reader who read one page and
    /// left. On a quiet website that alone would decide the single-page figure.
    /// </summary>
    [Fact]
    public async Task A_Visit_Still_Under_Way_Is_No_Part_Of_The_Count()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Asked(siteId, Midnight.AddHours(1), "visitor-a", "/"),
            Asked(siteId, Midnight.AddHours(20), "visitor-b", "/"));

        var shape = await ShapeOf(siteId, settledBefore: Midnight.AddHours(2));

        shape.Visits.Should().Be(1);
        shape.PageViews.Should().Be(1);
    }

    /// <summary>
    /// The intended arrangement on a measured site is both halves at once. Counting a linked visit
    /// twice would double every figure on the card for exactly the customers running the product
    /// properly.
    /// </summary>
    [Fact]
    public async Task A_Visit_Both_Halves_Watched_Is_One_Visit()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Reported(siteId, Midnight.AddHours(1), "server-a", "/", EventKind.PageView, Server, "pair-1", 200),
            Asked(siteId, Midnight.AddHours(1).AddSeconds(1), "browser-a", "/", correlationId: "pair-1"),
            Reported(
                siteId,
                Midnight.AddHours(1).AddMinutes(2),
                "server-a",
                "/pricing",
                EventKind.PageView,
                Server,
                "pair-2",
                200),
            Asked(
                siteId,
                Midnight.AddHours(1).AddMinutes(2).AddSeconds(1),
                "browser-a",
                "/pricing",
                correlationId: "pair-2"));

        var shape = await ShapeOf(siteId);

        shape.Visits.Should().Be(1);
        shape.PageViews.Should().Be(2);
    }

    /// <summary>
    /// A reader whose page view never arrived still reported which page they were reading, and the
    /// tracker only reports that from the page itself — so the page they named is the doorway.
    /// </summary>
    [Fact]
    public async Task A_Visit_Whose_Only_Report_Is_A_Reading_Enters_At_The_Page_It_Read()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Progressed(siteId, Midnight.AddHours(1), "visitor-a", "/", 12000, 40),
            Asked(siteId, Midnight.AddHours(2), "visitor-b", "/"));

        var shape = await ShapeOf(siteId);
        var entries = await FlowOf(siteId, VisitPosition.Entry);

        shape.Visits.Should().Be(2);
        entries.Pages.Should().ContainSingle().Which.Should().Be(new SiteVisitFlowRow("/", 2));
    }

    /// <summary>
    /// Every visit began somewhere and ended somewhere, so both lists have to add up to the visits
    /// beside them or a share taken against that total would be a share of something else.
    /// </summary>
    [Fact]
    public async Task Every_Visit_Is_On_Both_Lists_Exactly_Once()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Asked(siteId, Midnight.AddHours(1), "visitor-a", "/"),
            Asked(siteId, Midnight.AddHours(1).AddMinutes(1), "visitor-a", "/pricing"),
            Asked(siteId, Midnight.AddHours(2), "visitor-b", "/"),
            Asked(siteId, Midnight.AddHours(2).AddMinutes(1), "visitor-b", "/contact"),
            Asked(siteId, Midnight.AddHours(3), "visitor-c", "/blog"));

        var entries = await FlowOf(siteId, VisitPosition.Entry);
        var exits = await FlowOf(siteId, VisitPosition.Exit);

        entries.TotalVisits.Should().Be(3);
        entries.Pages.Sum(page => page.Visits).Should().Be(3);
        entries.Pages.Select(page => page.Path).Should().Equal("/", "/blog");

        exits.TotalVisits.Should().Be(3);
        exits.Pages.Sum(page => page.Visits).Should().Be(3);
        exits.Pages.Select(page => page.Path).Should().Equal("/blog", "/contact", "/pricing");
    }

    /// <summary>
    /// Arriving somewhere happens once. Counting per page view would rank a website's busiest page
    /// as its commonest doorway whether or not anybody arrived through it.
    /// </summary>
    [Fact]
    public async Task An_Arrival_Is_Counted_Once_However_Often_The_Page_Is_Read()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Asked(siteId, Midnight.AddHours(1), "visitor-a", "/"),
            Asked(siteId, Midnight.AddHours(1).AddMinutes(1), "visitor-a", "/pricing"),
            Asked(siteId, Midnight.AddHours(1).AddMinutes(2), "visitor-a", "/"),
            Asked(siteId, Midnight.AddHours(1).AddMinutes(3), "visitor-a", "/"));

        var entries = await FlowOf(siteId, VisitPosition.Entry);

        entries.Pages.Should().ContainSingle();
        entries.Pages[0].Visits.Should().Be(1);
    }

    /// <summary>
    /// The ordering is total, so somebody stepping through a list neither meets a page twice nor
    /// passes one that was never shown.
    /// </summary>
    [Fact]
    public async Task Slices_Of_The_List_Neither_Repeat_A_Page_Nor_Skip_One()
    {
        var siteId = Guid.NewGuid();
        var arrivals = Enumerable
            .Range(0, 6)
            .Select(index => Asked(
                siteId,
                Midnight.AddHours(1).AddMinutes(index * 45),
                $"visitor-{index}",
                $"/page-{index}"))
            .ToArray();

        await WriteAsync(arrivals);

        var first = await FlowOf(siteId, VisitPosition.Entry, limit: 4);
        var second = await FlowOf(siteId, VisitPosition.Entry, limit: 4, offset: 4);

        first.TotalPaths.Should().Be(6);
        first.Pages.Should().HaveCount(4);
        second.Pages.Should().HaveCount(2);

        var walked = first.Pages.Concat(second.Pages).Select(page => page.Path).ToArray();

        walked.Should().OnlyHaveUniqueItems();
        walked.Should().HaveCount(6);
    }

    /// <summary>
    /// The phase's whole point: a journey is exactly what the events say, in the order they say it.
    /// </summary>
    [Fact]
    public async Task A_Journey_Is_The_Visit_Pages_In_The_Order_They_Were_Asked_For()
    {
        var siteId = Guid.NewGuid();
        var began = Midnight.AddHours(1);

        await WriteAsync(
            Asked(siteId, began, "visitor-a", "/"),
            Asked(siteId, began.AddMinutes(1), "visitor-a", "/pricing"),
            Asked(siteId, began.AddMinutes(2), "visitor-a", "/contact"));

        var journey = await JourneyOf(siteId, "visitor-a", began);

        journey.Select(step => step.Path).Should().Equal("/", "/pricing", "/contact");
        journey.Select(step => step.At).Should().BeInAscendingOrder();
    }

    /// <summary>
    /// A reader who comes back to an article later in the same visit was there twice, and folding
    /// the two together would report one long reading that never happened.
    /// </summary>
    [Fact]
    public async Task A_Page_Read_Twice_In_One_Visit_Is_Two_Steps()
    {
        var siteId = Guid.NewGuid();
        var began = Midnight.AddHours(1);

        await WriteAsync(
            Asked(siteId, began, "visitor-a", "/article"),
            Progressed(siteId, began.AddSeconds(20), "visitor-a", "/article", 20000, 40),
            Asked(siteId, began.AddMinutes(1), "visitor-a", "/index"),
            Asked(siteId, began.AddMinutes(2), "visitor-a", "/article"),
            Progressed(siteId, began.AddMinutes(2).AddSeconds(5), "visitor-a", "/article", 5000, 90));

        var journey = await JourneyOf(siteId, "visitor-a", began);

        journey.Select(step => step.Path).Should().Equal("/article", "/index", "/article");
        journey[0].EngagedMs.Should().Be(20000);
        journey[2].EngagedMs.Should().Be(5000);
        journey[2].ScrollDepthPercent.Should().Be(90);
    }

    /// <summary>
    /// The distinction the whole product rests on, one step at a time: a page nobody was watching
    /// has no attention rather than none.
    /// </summary>
    [Fact]
    public async Task A_Step_Nobody_Watched_Has_No_Reading_Rather_Than_A_Reading_Of_Nought()
    {
        var siteId = Guid.NewGuid();
        var began = Midnight.AddHours(1);

        await WriteAsync(
            Reported(siteId, began, "visitor-a", "/.env", EventKind.PageView, Server, null, 404),
            Reported(
                siteId,
                began.AddSeconds(1),
                "visitor-a",
                "/wp-login.php",
                EventKind.PageView,
                Server,
                null,
                404));

        var journey = await JourneyOf(siteId, "visitor-a", began);

        journey.Select(step => step.Path).Should().Equal("/.env", "/wp-login.php");
        journey.Should().OnlyContain(step => step.EngagedMs == null && step.ScrollDepthPercent == null);
        journey.Select(step => step.StatusCode).Should().Equal(404, 404);
    }

    /// <summary>
    /// A tracker runs on a page that was delivered and has nothing to say about one that was not,
    /// so a step only the browser reported carries no status rather than a made-up success.
    /// </summary>
    [Fact]
    public async Task A_Step_Only_A_Browser_Reported_Carries_No_Status()
    {
        var siteId = Guid.NewGuid();
        var began = Midnight.AddHours(1);

        await WriteAsync(Asked(siteId, began, "visitor-a", "/"));

        var journey = await JourneyOf(siteId, "visitor-a", began);

        journey.Should().ContainSingle();
        journey[0].StatusCode.Should().BeNull();
    }

    /// <summary>
    /// Both halves watched one arrival. The step is when the page was delivered, and it carries
    /// what the server answered with and what the browser measured at once.
    /// </summary>
    [Fact]
    public async Task A_Step_Both_Halves_Watched_Is_One_Step_Carrying_Both_Accounts()
    {
        var siteId = Guid.NewGuid();
        var delivered = Midnight.AddHours(1);

        await WriteAsync(
            Reported(siteId, delivered, "server-a", "/", EventKind.PageView, Server, "pair-1", 200),
            Asked(siteId, delivered.AddSeconds(2), "browser-a", "/", correlationId: "pair-1"),
            Progressed(siteId, delivered.AddSeconds(30), "browser-a", "/", 28000, 65));

        var journey = await JourneyOf(siteId, "browser-a", delivered);

        journey.Should().ContainSingle();
        journey[0].At.Should().Be(delivered);
        journey[0].StatusCode.Should().Be(200);
        journey[0].EngagedMs.Should().Be(28000);
        journey[0].ScrollDepthPercent.Should().Be(65);
    }

    /// <summary>
    /// A journey is one visit's, so a later visit by the same reader is a different journey and
    /// another reader's activity is no part of this one.
    /// </summary>
    [Fact]
    public async Task A_Journey_Holds_Only_The_Visit_It_Was_Asked_For()
    {
        var siteId = Guid.NewGuid();
        var began = Midnight.AddHours(1);

        await WriteAsync(
            Asked(siteId, began, "visitor-a", "/first"),
            Asked(siteId, began.AddMinutes(45), "visitor-a", "/second-visit"),
            Asked(siteId, began.AddMinutes(1), "visitor-b", "/somebody-else"));

        var journey = await JourneyOf(siteId, "visitor-a", began);

        journey.Select(step => step.Path).Should().Equal("/first");
    }

    [Fact]
    public async Task An_Identity_Naming_No_Visit_Answers_With_Nothing()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(Asked(siteId, Midnight.AddHours(1), "visitor-a", "/"));

        var journey = await JourneyOf(siteId, "visitor-z", Midnight.AddHours(5));

        journey.Should().BeEmpty();
    }

    /// <summary>
    /// Tenant isolation is a property of the statement rather than of the caller, and every one of
    /// these questions has to hold it.
    /// </summary>
    [Fact]
    public async Task Another_Website_Traffic_Is_No_Part_Of_Any_Of_This()
    {
        var siteId = Guid.NewGuid();
        var neighbour = Guid.NewGuid();
        var began = Midnight.AddHours(1);

        await WriteAsync(
            Asked(siteId, began, "visitor-a", "/"),
            Asked(neighbour, began, "visitor-a", "/theirs"),
            Asked(neighbour, began.AddMinutes(1), "visitor-b", "/theirs"));

        var shape = await ShapeOf(siteId);
        var entries = await FlowOf(siteId, VisitPosition.Entry);
        var journey = await JourneyOf(siteId, "visitor-a", began);

        shape.Visits.Should().Be(1);
        entries.Pages.Select(page => page.Path).Should().Equal("/");
        journey.Select(step => step.Path).Should().Equal("/");
    }

    /// <summary>
    /// A path is written by whoever asked for it, and it is grouped on and read back rather than
    /// built into the statement, which is the whole of what a hostile one can do here.
    /// </summary>
    [Fact]
    public async Task A_Page_Named_To_Break_The_Statement_Is_Read_Like_Any_Other()
    {
        var siteId = Guid.NewGuid();
        var began = Midnight.AddHours(1);
        const string hostile = "/'); DROP TABLE events; --";

        await WriteAsync(Asked(siteId, began, "visitor-a", hostile));

        var entries = await FlowOf(siteId, VisitPosition.Entry);
        var journey = await JourneyOf(siteId, "visitor-a", began);

        entries.Pages.Select(page => page.Path).Should().Equal(hostile);
        journey.Select(step => step.Path).Should().Equal(hostile);
    }

    /// <summary>
    /// What makes a visit read as a story rather than as a list of addresses: what somebody pressed
    /// sits between the page they were on and the page it took them to.
    /// </summary>
    [Fact]
    public async Task A_Journey_Carries_What_Was_Pressed_Where_It_Was_Pressed()
    {
        var siteId = Guid.NewGuid();
        var began = Midnight.AddHours(1);

        await WriteAsync(
            Asked(siteId, began, "visitor-a", "/guide"),
            Operated(siteId, began.AddSeconds(20), "visitor-a", "/guide", "Copy the snippet"),
            Asked(siteId, began.AddMinutes(1), "visitor-a", "/pricing"));

        var journey = await JourneyOf(siteId, "visitor-a", began);

        journey.Select(step => step.Path).Should().Equal("/guide", "/guide", "/pricing");
        journey.Select(step => step.Press?.Name).Should().Equal(null, "Copy the snippet", null);
    }

    /// <summary>
    /// A control cannot be operated on a page nobody has arrived at, so where the two share an
    /// instant the arrival is the one that comes first.
    /// </summary>
    [Fact]
    public async Task An_Arrival_Comes_Before_A_Press_That_Shares_Its_Instant()
    {
        var siteId = Guid.NewGuid();
        var began = Midnight.AddHours(1);

        await WriteAsync(
            Operated(siteId, began, "visitor-a", "/guide", "Copy the snippet"),
            Asked(siteId, began, "visitor-a", "/guide"));

        var journey = await JourneyOf(siteId, "visitor-a", began);

        journey.Select(step => step.Press is null).Should().Equal(true, false);
    }

    /// <summary>
    /// A page is every report about one arrival folded into one row; a press is a row of its own,
    /// because somebody who pressed the same button twice pressed it twice.
    /// </summary>
    [Fact]
    public async Task The_Same_Control_Pressed_Twice_Is_Two_Steps()
    {
        var siteId = Guid.NewGuid();
        var began = Midnight.AddHours(1);

        await WriteAsync(
            Asked(siteId, began, "visitor-a", "/guide"),
            Operated(siteId, began.AddSeconds(10), "visitor-a", "/guide", "Copy the snippet"),
            Operated(siteId, began.AddSeconds(20), "visitor-a", "/guide", "Copy the snippet"));

        var journey = await JourneyOf(siteId, "visitor-a", began);

        journey.Count(step => step.Press?.Name == "Copy the snippet").Should().Be(2);
    }

    /// <summary>
    /// Where a press led is what makes it worth reading. An address to write to records that it was
    /// used and nothing else, because the address itself names a person.
    /// </summary>
    [Fact]
    public async Task A_Press_Says_Where_It_Led_Without_Naming_Anybody()
    {
        var siteId = Guid.NewGuid();
        var began = Midnight.AddHours(1);

        await WriteAsync(
            Asked(siteId, began, "visitor-a", "/guide"),
            Operated(siteId, began.AddSeconds(10), "visitor-a", "/guide", "Ask on GitHub") with
            {
                ActionControl = ControlKind.Link,
                ActionTarget = "github.com",
                ActionTargetKind = TargetKind.External,
            },
            Operated(siteId, began.AddSeconds(20), "visitor-a", "/guide", "Email me") with
            {
                ActionControl = ControlKind.Link,
                ActionTargetKind = TargetKind.Contact,
            });

        var journey = await JourneyOf(siteId, "visitor-a", began);
        var presses = journey.Where(step => step.Press is not null).Select(step => step.Press!.Value);

        presses.Select(press => press.TargetKind).Should().Equal(TargetKind.External, TargetKind.Contact);
        presses.Select(press => press.Target).Should().Equal("github.com", null);
        presses.Select(press => press.Control).Should().AllBeEquivalentTo(ControlKind.Link);
    }

    /// <summary>
    /// A press is not a page, and counting it as one would report a visit as having gone somewhere
    /// it never went.
    /// </summary>
    [Fact]
    public async Task Pressing_Something_Is_Never_Counted_As_A_Page()
    {
        var siteId = Guid.NewGuid();
        var began = Midnight.AddHours(1);

        await WriteAsync(
            Asked(siteId, began, "visitor-a", "/guide"),
            Operated(siteId, began.AddSeconds(10), "visitor-a", "/guide", "Copy the snippet"),
            Operated(siteId, began.AddSeconds(20), "visitor-a", "/guide", "Copy the snippet"));

        var shape = await ShapeOf(siteId);

        shape.PageViews.Should().Be(1);
        shape.Visits.Should().Be(1);
    }

    private const IngestSurface Server = IngestSurface.NextJsMiddleware;

    private Task<SiteVisitShape> ShapeOf(Guid siteId, DateTimeOffset? settledBefore = null) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteVisitShapeAsync(
            Scope(siteId),
            new SiteVisitShapeQuery(Window(), Boundaries(settledBefore)),
            Cancellation.Token);

    private Task<SiteVisitFlow> FlowOf(
        Guid siteId,
        VisitPosition position,
        int limit = 10,
        int offset = 0) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteVisitFlowAsync(
            Scope(siteId),
            new SiteVisitFlowQuery(Window(), Boundaries(null), position, limit, offset),
            Cancellation.Token);

    [Fact]
    public async Task A_Visit_Is_Named_By_The_Site_That_Sent_It()
    {
        var siteId = Guid.CreateVersion7();
        await WriteAsync(
            Asked(siteId, Midnight, "reader", "/posts/hello") with
            {
                Referrer = "https://www.google.co.in/search?q=analytics",
                ReferrerDomain = "www.google.co.in",
            });

        var visit = await VisitOf(siteId, "reader", Midnight);

        visit.Context.SendingSite.Should().Be("Google");
        visit.Context.Channel.Should().Be(SourceChannel.Search);
    }

    /// <summary>
    /// A referrer is written by whoever visited, so a name is taken from the label in front of the
    /// public suffix and never from any label that merely appears in the address.
    /// </summary>
    [Fact]
    public async Task A_Visit_Cannot_Be_Given_Another_Site_Name_By_Borrowing_Its_Word()
    {
        var siteId = Guid.CreateVersion7();
        await WriteAsync(
            Asked(siteId, Midnight, "reader", "/posts/hello") with
            {
                Referrer = "https://google.attacker.test/pretend",
                ReferrerDomain = "google.attacker.test",
            });

        var visit = await VisitOf(siteId, "reader", Midnight);

        visit.Context.SendingSite.Should().Be("google.attacker.test");
        visit.Context.SendingSite.Should().NotBe("Google");
        visit.Context.Channel.Should().Be(SourceChannel.Link);
    }

    /// <summary>
    /// Only a visit's first page names anywhere else; every page after it was reached from the site
    /// being measured, so the site must never head its own visit.
    /// </summary>
    [Fact]
    public async Task A_Visit_From_The_Measured_Site_Itself_Names_Nowhere()
    {
        var siteId = Guid.CreateVersion7();
        await WriteAsync(
            Asked(siteId, Midnight, "reader", "/posts/hello") with
            {
                Referrer = "https://example.com/index",
                ReferrerDomain = "example.com",
            });

        var visit = await VisitOf(siteId, "reader", Midnight);

        visit.Context.SendingSite.Should().BeEmpty();
        visit.Context.Channel.Should().Be(SourceChannel.Direct);
    }

    /// <summary>
    /// Settled once over the whole visit rather than per report, and from the earliest report that
    /// carried one — a panel that named a different browser on each reading would be a defect.
    /// </summary>
    [Fact]
    public async Task A_Visit_Takes_Its_Place_And_Software_From_The_Reports_That_Carried_Them()
    {
        var siteId = Guid.CreateVersion7();
        await WriteAsync(
            Asked(siteId, Midnight, "reader", "/posts/hello") with
            {
                CountryCode = "IN",
                City = "Pune",
                NetworkOwner = "Jio Platforms",
                DeviceClass = DeviceClass.Phone,
                BrowserFamily = "Chrome",
                OperatingSystem = "Android",
            },
            Asked(siteId, Midnight.AddMinutes(2), "reader", "/pricing"));

        var visit = await VisitOf(siteId, "reader", Midnight);

        visit.Context.CountryCode.Should().Be("IN");
        visit.Context.Town.Should().Be("Pune");
        visit.Context.NetworkOwner.Should().Be("Jio Platforms");
        visit.Context.Device.Should().Be(DeviceClass.Phone);
        visit.Context.Browser.Should().Be("Chrome");
        visit.Context.OperatingSystem.Should().Be("Android");
    }

    /// <summary>Nothing established is an answer rather than a gap, and is reported as one.</summary>
    [Fact]
    public async Task A_Visit_Nothing_Could_Be_Established_About_Says_So()
    {
        var siteId = Guid.CreateVersion7();
        await WriteAsync(Asked(siteId, Midnight, "reader", "/posts/hello"));

        var visit = await VisitOf(siteId, "reader", Midnight);

        visit.Context.Should().Be(VisitContext.Nothing);
    }

    [Fact]
    public async Task An_Identity_Naming_No_Visit_Establishes_Nothing_And_Lists_Nothing()
    {
        var siteId = Guid.CreateVersion7();
        await WriteAsync(Asked(siteId, Midnight, "reader", "/posts/hello"));

        var visit = await VisitOf(siteId, "stranger", Midnight);

        visit.Steps.Should().BeEmpty();
        visit.Context.Should().Be(VisitContext.Nothing);
    }

    private async Task<IReadOnlyList<VisitStep>> JourneyOf(
        Guid siteId,
        string visitorKey,
        DateTimeOffset began) =>
        (await VisitOf(siteId, visitorKey, began)).Steps;

    private Task<VisitJourney> VisitOf(Guid siteId, string visitorKey, DateTimeOffset began) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteVisitJourneyAsync(
            Scope(siteId),
            new SiteVisitJourneyQuery(
                new VisitKey(visitorKey, began),
                IdleTimeout,
                "example.com",
                SiteVisitJourneyQuery.MostSteps),
            Cancellation.Token);

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    private static TimeRange Window() => new(Midnight, Midnight.AddDays(1));

    /// <summary>
    /// What a visit is for these tests, with everything treated as finished unless a test says
    /// otherwise — which is what a window well in the past looks like to the endpoint.
    /// </summary>
    private static VisitBoundaries Boundaries(DateTimeOffset? settledBefore) =>
        new(IdleTimeout, settledBefore ?? Midnight.AddDays(2));

    private static TenantScope Scope(Guid siteId) =>
        new(siteId, Guid.NewGuid(), SiteRole.Viewer, "Etc/UTC");

    /// <summary>A page being delivered.</summary>
    private static RawEvent Asked(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        string path,
        string? correlationId = null) =>
        Reported(siteId, at, visitorKey, path, EventKind.PageView, IngestSurface.BrowserTracker, correlationId);

    /// <summary>A control the visitor operated on a page.</summary>
    private static RawEvent Operated(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        string path,
        string name) =>
        Reported(siteId, at, visitorKey, path, EventKind.Action, IngestSurface.BrowserTracker) with
        {
            ActionControl = ControlKind.Button,
            ActionLabel = name,
        };

    /// <summary>A page saying how the reading is going, part of the way through.</summary>
    private static RawEvent Progressed(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        string path,
        int engagedMs,
        byte depth) =>
        Reported(siteId, at, visitorKey, path, EventKind.Engagement, IngestSurface.BrowserTracker) with
        {
            EngagedMs = engagedMs,
            ScrollDepthPercent = depth,
        };

    private static RawEvent Reported(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        string path,
        EventKind kind,
        IngestSurface surface,
        string? correlationId = null,
        short? statusCode = null) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = kind,
            Surface = surface,
            ServerTimestamp = at,
            VisitorKey = visitorKey,
            Host = "example.com",
            Path = path,
            CorrelationId = correlationId,
            StatusCode = statusCode,
        };
}
