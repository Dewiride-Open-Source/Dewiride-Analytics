using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves what a reading of the present moment carries, against a real store.
/// </summary>
/// <remarks>
/// These readings answer a screen that renews itself every few seconds, so what they get wrong is
/// wrong in front of somebody watching. Four properties matter more than the rest and each has a
/// test here: one visitor is one row however many surfaces saw them, somebody who has gone is gone,
/// how busy a site is does not shrink to the length of the list an answer had room for, and every
/// visitor counted stands on exactly one of the pages listed.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class LiveTrafficTests(AnalyticsStackFixture stack)
{
    /// <summary>The moment these readings are taken at, held still.</summary>
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>As long as a visitor may be quiet before their visit is over.</summary>
    private static readonly TimeSpan HalfHour = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A site running both a reporter on its own server and the tracker in the browser sees every
    /// page twice. Shown as two visitors, a live screen would report double the traffic there is,
    /// and would do it on the arrangement this product recommends.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Both_Halves_Saw_Is_One_Visitor()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-5), "as-the-server-saw-them", "/posts/hello", "delivery-1"),
            FromBrowser(siteId, Now.AddMinutes(-5), "as-the-browser-saw-them", "/posts/hello", "delivery-1"));

        var here = await WhoIsHere(siteId);

        here.VisitorsSeen.Should().Be(1);
        here.Visitors.Should().ContainSingle();
        here.Visitors[0].Evidence.PageCount.Should().Be(1);
    }

    [Fact]
    public async Task Somebody_Whose_Last_Report_Is_Older_Than_The_Window_Is_Not_Here()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-45), "left-already", "/posts/hello"),
            FromServer(siteId, Now.AddMinutes(-2), "still-reading", "/posts/hello"));

        var here = await WhoIsHere(siteId);

        here.Visitors.Select(visitor => visitor.VisitorKey).Should().Equal("still-reading");
    }

    /// <summary>
    /// What somebody is reading now is the last thing they asked for, not the first — which is the
    /// difference between a live screen and a list of arrivals.
    /// </summary>
    [Fact]
    public async Task What_Somebody_Is_Reading_Is_The_Last_Page_They_Asked_For()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-6), "reader", "/"),
            FromServer(siteId, Now.AddMinutes(-4), "reader", "/posts/hello"),
            FromServer(siteId, Now.AddMinutes(-1), "reader", "/pricing"));

        var here = await WhoIsHere(siteId);

        here.Visitors[0].CurrentPath.Should().Be("/pricing");
        here.Visitors[0].Evidence.PageCount.Should().Be(3);
    }

    /// <summary>
    /// A page read for a while is reported on over and over while it is being read, and a visitor
    /// who comes back to a page they were already on inside the window is still on one page: there
    /// are no visit boundaries in a reading of the last half hour for "again" to mean anything
    /// against.
    /// </summary>
    [Fact]
    public async Task Every_Report_About_One_Page_Is_Still_One_Page()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-8), "reader", "/long-read"),
            Engaged(siteId, Now.AddMinutes(-7), "reader", "/long-read"),
            Engaged(siteId, Now.AddMinutes(-6), "reader", "/long-read"),
            FromServer(siteId, Now.AddMinutes(-3), "reader", "/long-read"));

        var here = await WhoIsHere(siteId);

        here.Visitors[0].Evidence.PageCount.Should().Be(1);
    }

    /// <summary>
    /// A sweep can put hundreds of visitors on a site in a few minutes. The list stops; the figure
    /// beside it must not, or the screen reports a sweep as exactly as busy as it has room to draw.
    /// </summary>
    [Fact]
    public async Task How_Busy_The_Site_Is_Does_Not_Shrink_To_The_Length_Of_The_List()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
        [
            .. Enumerable.Range(1, 12).Select(index =>
                FromServer(siteId, Now.AddMinutes(-index), $"visitor-{index:00}", $"/page-{index:00}")),
        ]);

        var here = await WhoIsHere(siteId, limit: 4);

        here.Visitors.Should().HaveCount(4);
        here.VisitorsSeen.Should().Be(12);
    }

    [Fact]
    public async Task Visitors_Are_Listed_With_The_One_Seen_Most_Recently_First()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-20), "earliest", "/"),
            FromServer(siteId, Now.AddMinutes(-2), "latest", "/"),
            FromServer(siteId, Now.AddMinutes(-11), "middle", "/"));

        var here = await WhoIsHere(siteId);

        here.Visitors.Select(visitor => visitor.VisitorKey)
            .Should().Equal("latest", "middle", "earliest");
    }

    /// <summary>
    /// What was settled about the address a visitor arrived from travels with them, because it is
    /// the one thing this product can say about somebody who is still reading and stand behind.
    /// </summary>
    [Fact]
    public async Task What_A_Company_Vouches_For_Travels_With_The_Visitor()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Vouched(FromServer(siteId, Now.AddMinutes(-3), "crawler", "/robots.txt"), "Google"));

        var here = await WhoIsHere(siteId);

        here.Visitors[0].ConfirmedOperator.Should().Be("Google");
        here.Visitors[0].Evidence.ConfirmedOperator.Should().Be("Google");
    }

    /// <summary>
    /// The address a visitor arrived from is the one personal value on an event, and nothing about
    /// the present moment has a use for it. It is asserted against the statement in the compiler
    /// suite; this proves the same thing from the other end, where a column added later would show
    /// up.
    /// </summary>
    [Fact]
    public async Task A_Reading_Of_Who_Is_Here_Carries_No_Address()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Arriving(FromServer(siteId, Now.AddMinutes(-3), "visitor", "/"), "203.0.113.7"));

        var here = await WhoIsHere(siteId);

        here.Visitors.Should().ContainSingle();
        here.Visitors[0].Evidence.Requests.Should().NotBeEmpty();
        here.Visitors[0].Context.NetworkOwner.Should().NotContain("203.0.113.7");
    }

    [Fact]
    public async Task Another_Site_Is_Not_Reported_As_This_One()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-3), "mine", "/mine"),
            FromServer(Guid.NewGuid(), Now.AddMinutes(-3), "theirs", "/theirs"));

        var here = await WhoIsHere(siteId);

        here.Visitors.Select(visitor => visitor.VisitorKey).Should().Equal("mine");
    }

    /// <summary>
    /// A path is written by whoever is visiting the site, so a value that looks like part of a
    /// statement has to reach the store as a value. A live screen reads the freshest activity there
    /// is, which is the activity somebody has just chosen.
    /// </summary>
    [Fact]
    public async Task A_Path_Written_By_A_Visitor_Reaches_The_Store_As_A_Value()
    {
        var siteId = Guid.NewGuid();
        const string Hostile = "/'; DROP TABLE events; --";

        await WriteAsync(FromServer(siteId, Now.AddMinutes(-3), "visitor", Hostile));

        var here = await WhoIsHere(siteId);

        here.Visitors[0].CurrentPath.Should().Be(Hostile);
    }

    /// <summary>
    /// The drawing of the last half hour has a column for every minute in it. A store answers only
    /// about minutes that produced a row, and a drawing built from those alone closes the gaps up
    /// and shows a busy half hour where there was a quiet one.
    /// </summary>
    [Fact]
    public async Task Every_Minute_Comes_Back_Including_The_Ones_Nothing_Happened_In()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-20), "visitor-a", "/"),
            FromServer(siteId, Now.AddMinutes(-2), "visitor-b", "/"));

        var minutes = await ByTheMinute(siteId);

        minutes.Should().HaveCount(31);
        minutes.Select(minute => minute.PageViews).Sum().Should().Be(2);
        minutes.Where(minute => minute.PageViews == 0).Should().HaveCount(29);
        minutes.Should().BeInAscendingOrder(minute => minute.Start);
    }

    [Fact]
    public async Task A_Site_Nobody_Is_On_Still_Draws_Its_Half_Hour()
    {
        var minutes = await ByTheMinute(Guid.NewGuid());

        minutes.Should().HaveCount(31);
        minutes.Should().OnlyContain(minute => minute.PageViews == 0);
    }

    [Fact]
    public async Task The_Pages_Being_Read_Are_Ranked_Busiest_First()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-9), "visitor-a", "/popular"),
            FromServer(siteId, Now.AddMinutes(-8), "visitor-b", "/popular"),
            FromServer(siteId, Now.AddMinutes(-7), "visitor-c", "/quiet"));

        var pages = await WhatIsBeingRead(siteId);

        pages.Select(page => page.Path).Should().Equal("/popular", "/quiet");
        pages.Select(page => page.Visitors).Should().Equal(2, 1);
    }

    /// <summary>
    /// A page one visitor reloaded twenty times holds one visitor. How often a page was delivered
    /// is the minute-by-minute drawing's question, and a list of pages that answered it too would
    /// put a page one person kept reloading above a page ten people are reading.
    /// </summary>
    [Fact]
    public async Task A_Page_One_Visitor_Reloaded_Holds_One_Visitor()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-9), "visitor-a", "/reloaded"),
            FromServer(siteId, Now.AddMinutes(-8), "visitor-a", "/reloaded"),
            FromServer(siteId, Now.AddMinutes(-7), "visitor-a", "/reloaded"));

        var pages = await WhatIsBeingRead(siteId);

        pages.Should().ContainSingle();
        pages[0].Visitors.Should().Be(1);
    }

    /// <summary>
    /// The property the pages list exists to hold: every visitor the reading of who is here counts
    /// stands on exactly one page, so the pages add up to the headline rather than to something
    /// near it, and the page beside every row is one of the pages listed.
    /// </summary>
    [Fact]
    public async Task Every_Visitor_Seen_Stands_On_Exactly_One_Page_Being_Read()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-9), "visitor-a", "/a"),
            FromServer(siteId, Now.AddMinutes(-8), "visitor-b", "/a"),
            FromServer(siteId, Now.AddMinutes(-7), "visitor-c", "/b"),
            FromServer(siteId, Now.AddMinutes(-6), "visitor-d", "/a"),
            FromServer(siteId, Now.AddMinutes(-2), "visitor-d", "/c"));

        var here = await WhoIsHere(siteId);
        var pages = await WhatIsBeingRead(siteId);

        pages.Sum(page => page.Visitors).Should().Be(here.VisitorsSeen);
        here.Visitors.Select(visitor => visitor.CurrentPath).Should().BeSubsetOf(pages.Select(page => page.Path));
        pages.Select(page => page.Path).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Somebody who arrived before the window opened and is still reading has sent nothing but
    /// progress reports inside it. They are counted by the reading of who is here, so they stand on
    /// a page in the list of pages too, or the two would disagree by one for every long read.
    /// </summary>
    [Fact]
    public async Task Somebody_Only_Reading_A_Page_Is_On_It_In_Both_Lists()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(Engaged(siteId, Now.AddMinutes(-5), "reader", "/long-read"));

        var here = await WhoIsHere(siteId);
        var pages = await WhatIsBeingRead(siteId);

        here.VisitorsSeen.Should().Be(1);
        here.Visitors[0].CurrentPath.Should().Be("/long-read");
        pages.Should().ContainSingle();
        pages[0].Path.Should().Be("/long-read");
        pages[0].Visitors.Should().Be(1);
    }

    /// <summary>
    /// A report carrying no visitor key names nobody, and nobody cannot be placed on a page. The
    /// reading of who is here leaves such reports out for the same reason, so leaving them in here
    /// would put more visitors on the pages than the headline says there are.
    /// </summary>
    [Fact]
    public async Task A_Report_That_Named_Nobody_Puts_Nobody_On_A_Page()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-3), null, "/anonymous"),
            FromServer(siteId, Now.AddMinutes(-2), "reader", "/"));

        var here = await WhoIsHere(siteId);
        var pages = await WhatIsBeingRead(siteId);

        pages.Select(page => page.Path).Should().Equal("/");
        pages.Sum(page => page.Visitors).Should().Be(here.VisitorsSeen);
        here.VisitorsSeen.Should().Be(1);
    }

    [Fact]
    public async Task A_Visitor_Who_Moved_On_Is_Counted_Where_They_Are_Now()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-6), "reader", "/"),
            FromServer(siteId, Now.AddMinutes(-1), "reader", "/pricing"));

        var pages = await WhatIsBeingRead(siteId);

        pages.Select(page => page.Path).Should().Equal("/pricing");
    }

    [Fact]
    public async Task A_Visitor_Both_Halves_Saw_Stands_On_One_Page()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-5), "as-the-server-saw-them", "/posts/hello", "delivery-1"),
            FromBrowser(siteId, Now.AddMinutes(-5), "as-the-browser-saw-them", "/posts/hello", "delivery-1"));

        var here = await WhoIsHere(siteId);
        var pages = await WhatIsBeingRead(siteId);

        here.VisitorsSeen.Should().Be(1);
        pages.Should().ContainSingle();
        pages[0].Visitors.Should().Be(1);
    }

    /// <summary>
    /// What the site answered with is only ever seen by the reporter on its own server, and the
    /// browser's sighting of the same page carries nothing. The live reading reads the status from
    /// the server's sighting, as the finished visit does, so a rule that turns on whether a page was
    /// served reaches the same conclusion about a visitor while they are here as it will once they
    /// have gone.
    /// </summary>
    [Fact]
    public async Task A_Page_Both_Halves_Saw_Carries_The_Status_The_Site_Answered_With()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromBrowser(siteId, Now.AddMinutes(-5), "as-the-browser-saw-them", "/wp-admin/", "delivery-1"),
            Answered(FromServer(siteId, Now.AddMinutes(-5).AddSeconds(1), "as-the-server-saw-them", "/wp-admin/", "delivery-1"), 302));

        var here = await WhoIsHere(siteId);

        here.Visitors.Should().ContainSingle();
        here.Visitors[0].Evidence.Requests.Should().ContainSingle();
        here.Visitors[0].Evidence.Requests[0].StatusCode.Should().Be(302);
    }

    /// <summary>
    /// The number of steps a trail shows and the number of pages the row it was opened from printed
    /// are the same number, because both are one visitor's activity over the same minutes gathered
    /// by the page. A trail that disagreed with its own row would say "three of four pages" for a
    /// reason that is nothing but a difference in timing.
    /// </summary>
    [Fact]
    public async Task A_Trail_Shows_As_Many_Pages_As_The_Row_It_Was_Opened_From_Counted()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-9), "reader", "/"),
            FromServer(siteId, Now.AddMinutes(-6), "reader", "/posts/hello"),
            FromServer(siteId, Now.AddMinutes(-2), "reader", "/pricing"));

        var here = await WhoIsHere(siteId);
        var trail = await TrailOf(siteId, "reader");

        trail.Count(step => step.Press is null).Should().Be(here.Visitors[0].Evidence.PageCount);
        trail.Select(step => step.Path).Should().Equal("/", "/posts/hello", "/pricing");
    }

    /// <summary>
    /// A site running both a reporter on its own server and the tracker in the browser reports every
    /// page twice under two keys until the browser echoes what it was given. A trail opened on the
    /// key the reading gave has to hold both halves, or a visitor watched properly would show fewer
    /// pages than a visitor watched from one side only.
    /// </summary>
    [Fact]
    public async Task A_Trail_Holds_What_Both_Halves_Of_The_Measurement_Saw()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-8), "as-the-server-saw-them", "/", "delivery-1"),
            FromBrowser(siteId, Now.AddMinutes(-8), "as-the-browser-saw-them", "/", "delivery-1"),
            FromServer(siteId, Now.AddMinutes(-3), "as-the-server-saw-them", "/pricing", "delivery-2"));

        var here = await WhoIsHere(siteId);
        var trail = await TrailOf(siteId, here.Visitors[0].VisitorKey);

        here.Visitors[0].VisitorKey.Should().Be("as-the-browser-saw-them");
        trail.Select(step => step.Path).Should().Equal("/", "/pricing");
    }

    /// <summary>
    /// A page reads once however many reports described it. Every report of a reading carries a
    /// running total rather than an instalment, so the largest of them is what the reading came to.
    /// </summary>
    [Fact]
    public async Task A_Page_Read_Over_Several_Reports_Is_One_Step_Carrying_The_Whole_Reading()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromBrowser(siteId, Now.AddMinutes(-7), "reader", "/posts/hello"),
            Read(Engaged(siteId, Now.AddMinutes(-6), "reader", "/posts/hello"), 4_000, 25),
            Read(Engaged(siteId, Now.AddMinutes(-4), "reader", "/posts/hello"), 31_000, 90));

        var trail = await TrailOf(siteId, "reader");

        trail.Should().ContainSingle();
        trail[0].EngagedMs.Should().Be(31_000);
        trail[0].ScrollDepthPercent.Should().Be(90);
        trail[0].At.Should().Be(Now.AddMinutes(-7));
    }

    /// <summary>
    /// A press is a step of its own, because somebody who pressed the same control twice pressed it
    /// twice. It carries the page it happened on, so it can be read against the arrival above it.
    /// </summary>
    [Fact]
    public async Task A_Control_Somebody_Operated_Is_Its_Own_Step_On_The_Page_It_Was_On()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromBrowser(siteId, Now.AddMinutes(-5), "reader", "/pricing"),
            Pressed(siteId, Now.AddMinutes(-4), "reader", "/pricing", "Start free trial"),
            Pressed(siteId, Now.AddMinutes(-3), "reader", "/pricing", "Start free trial"));

        var trail = await TrailOf(siteId, "reader");

        trail.Should().HaveCount(3);
        trail[0].Press.Should().BeNull();
        trail[1].Press!.Value.Name.Should().Be("Start free trial");
        trail[1].Press!.Value.Control.Should().Be(ControlKind.Button);
        trail[2].Press!.Value.Name.Should().Be("Start free trial");
    }

    /// <summary>
    /// What the site answered with is only ever seen by a reporter on the site's own server. A page
    /// nobody was there to answer for carries nothing rather than a nought, which is the difference
    /// between not knowing and knowing it was fine.
    /// </summary>
    [Fact]
    public async Task A_Page_The_Site_Refused_Says_So_And_One_Only_The_Browser_Saw_Says_Nothing()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Answered(FromServer(siteId, Now.AddMinutes(-6), "scanner", "/wp-admin"), 404),
            FromBrowser(siteId, Now.AddMinutes(-3), "scanner", "/posts/hello"));

        var trail = await TrailOf(siteId, "scanner");

        trail.Should().HaveCount(2);
        trail[0].StatusCode.Should().Be(404);
        trail[1].StatusCode.Should().BeNull();
        trail[1].EngagedMs.Should().BeNull();
        trail[1].ScrollDepthPercent.Should().BeNull();
    }

    /// <summary>
    /// A visitor who has fallen out of the stretch since their row was drawn is an empty trail, and
    /// so is a key that never named anybody. A visitor leaving is what happens to every visitor, so
    /// the two answer alike and neither can be used to find out which keys are real.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Who_Has_Gone_And_A_Visitor_Who_Never_Was_Answer_Alike()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(FromServer(siteId, Now.AddMinutes(-45), "left-already", "/posts/hello"));

        var gone = await TrailOf(siteId, "left-already");
        var neverWas = await TrailOf(siteId, "nobody-at-all");

        gone.Should().BeEmpty();
        neverWas.Should().BeEmpty();
    }

    /// <summary>
    /// A trail describes one visitor on one site. A key another site happens to hold the same
    /// spelling of takes no part, because the two readings are scoped separately and a coincidence
    /// must not become one visitor seen twice.
    /// </summary>
    [Fact]
    public async Task A_Trail_Holds_Nothing_Another_Site_Reported()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        await WriteAsync(
            FromServer(mine, Now.AddMinutes(-5), "same-spelling", "/mine"),
            FromServer(theirs, Now.AddMinutes(-4), "same-spelling", "/theirs"));

        var trail = await TrailOf(mine, "same-spelling");

        trail.Select(step => step.Path).Should().Equal("/mine");
    }

    /// <summary>
    /// The one place a trail and the same visit once it has finished say different things, pinned
    /// down so it stays the only one.
    /// </summary>
    /// <remarks>
    /// A finished visit knows where each arrival was, so somebody who leaves a page and comes back
    /// to it was there twice. A trail has no visit boundaries in it at all — that is what makes it
    /// answerable about a moment rather than about a visit — so it has nothing for "again" to mean
    /// against, and shows one page the visitor was on. Both are honest answers to the questions
    /// they were asked, and the trail agrees with the count printed beside it, which is what a
    /// reader compares it to.
    /// </remarks>
    [Fact]
    public async Task A_Trail_Shows_A_Page_Somebody_Returned_To_Once_Where_A_Finished_Visit_Shows_It_Twice()
    {
        var siteId = Guid.NewGuid();
        var began = Now.AddMinutes(-9);

        await WriteAsync(
            FromServer(siteId, began, "reader", "/"),
            FromServer(siteId, Now.AddMinutes(-6), "reader", "/pricing"),
            FromServer(siteId, Now.AddMinutes(-2), "reader", "/"));

        var trail = await TrailOf(siteId, "reader");
        var journey = await JourneyOf(siteId, "reader", began);

        trail.Select(step => step.Path).Should().Equal("/", "/pricing");
        journey.Steps.Select(step => step.Path).Should().Equal("/", "/pricing", "/");
    }

    private Task<VisitJourney> JourneyOf(Guid siteId, string visitorKey, DateTimeOffset began) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteVisitJourneyAsync(
            Scope(siteId),
            new SiteVisitJourneyQuery(
                new VisitKey(visitorKey, began),
                HalfHour,
                "example.com",
                SiteVisitJourneyQuery.MostSteps),
            Cancellation.Token);

    private Task<IReadOnlyList<VisitStep>> TrailOf(Guid siteId, string visitorKey) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteLiveTrailAsync(
            Scope(siteId),
            new SiteLiveTrailQuery(Window(), visitorKey, SiteLiveTrailQuery.MostSteps),
            Cancellation.Token);

    private Task<LiveVisitors> WhoIsHere(Guid siteId, int limit = 50) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteLiveVisitorsAsync(
            Scope(siteId),
            new SiteLiveVisitorsQuery(Window(), "example.com", limit),
            Cancellation.Token);

    private Task<IReadOnlyList<LiveMinute>> ByTheMinute(Guid siteId) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteLiveActivityAsync(
            Scope(siteId),
            new SiteLiveActivityQuery(Window()),
            Cancellation.Token);

    private Task<IReadOnlyList<LivePage>> WhatIsBeingRead(Guid siteId) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteLivePagesAsync(
            Scope(siteId),
            new SiteLivePagesQuery(Window(), 10),
            Cancellation.Token);

    /// <summary>
    /// The stretch a reading covers, with its near end on a whole minute so that the drawing of it
    /// has whole minutes to draw.
    /// </summary>
    private static TimeRange Window() => new(Now - HalfHour, Now);

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    private static TenantScope Scope(Guid siteId) =>
        new(siteId, Guid.NewGuid(), SiteRole.Viewer, "Etc/UTC");

    private static RawEvent Vouched(RawEvent observed, string operatorName) =>
        observed with { ConfirmedOperator = operatorName };

    private static RawEvent Answered(RawEvent observed, short statusCode) =>
        observed with { StatusCode = statusCode };

    private static RawEvent Read(RawEvent observed, int engagedMs, byte depthPercent) =>
        observed with { EngagedMs = engagedMs, ScrollDepthPercent = depthPercent };

    private static RawEvent Pressed(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        string path,
        string label) =>
        Observed(siteId, at, visitorKey, path, EventKind.Action, IngestSurface.BrowserTracker, null)
            with
        {
            ActionControl = ControlKind.Button,
            ActionLabel = label,
            ActionTargetKind = TargetKind.None,
        };

    private static RawEvent Arriving(RawEvent observed, string address) =>
        observed with { IpAddress = address };

    private static RawEvent FromBrowser(
        Guid siteId,
        DateTimeOffset at,
        string? visitorKey,
        string path,
        string? correlationId = null) =>
        Observed(siteId, at, visitorKey, path, EventKind.PageView, IngestSurface.BrowserTracker, correlationId);

    private static RawEvent FromServer(
        Guid siteId,
        DateTimeOffset at,
        string? visitorKey,
        string path,
        string? correlationId = null) =>
        Observed(siteId, at, visitorKey, path, EventKind.PageView, IngestSurface.NextJsMiddleware, correlationId);

    private static RawEvent Engaged(Guid siteId, DateTimeOffset at, string? visitorKey, string path) =>
        Observed(siteId, at, visitorKey, path, EventKind.Engagement, IngestSurface.BrowserTracker, null);

    private static RawEvent Observed(
        Guid siteId,
        DateTimeOffset at,
        string? visitorKey,
        string path,
        EventKind kind,
        IngestSurface surface,
        string? correlationId) =>
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
        };
}
