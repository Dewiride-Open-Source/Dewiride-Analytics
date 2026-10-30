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
/// wrong in front of somebody watching. Six properties matter more than the rest and each has a
/// test here: one visitor is one row however many surfaces saw them, somebody who has gone is gone,
/// a page left open keeps nobody here, how busy a site is does not shrink to the length of the list
/// an answer had room for, every visitor counted stands on exactly one of the pages listed, and
/// nobody is said to have come straight here unless the window saw them arrive.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class LiveTrafficTests(AnalyticsStackFixture stack)
{
    /// <summary>The address of the site these readings are about, and of every page written here.</summary>
    private const string SiteDomain = "example.com";

    /// <summary>A page of that site, as a referrer names it.</summary>
    private const string OwnPage = $"https://{SiteDomain}/posts/hello";

    /// <summary>A search engine's results, as a referrer names them.</summary>
    private const string SearchResults = "https://www.google.com/";

    /// <summary>The host of those results, as the collector records it.</summary>
    private const string SearchHost = "www.google.com";

    /// <summary>The instant <see cref="Now"/> settled on, once a test first asked for it.</summary>
    private DateTimeOffset? _now;

    /// <summary>
    /// The moment these readings are taken at: the stack's clock when the test first asks, taken
    /// back to a whole minute and held there for the rest of the test, so a reading's near end falls
    /// on one and an instant written to the store reads back as itself.
    /// </summary>
    private DateTimeOffset Now =>
        _now ??= OnTheMinute(stack.Services.GetRequiredService<TimeProvider>().GetUtcNow());

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

    /// <summary>
    /// A visitor a search sent inside the window was seen arriving, and the search is named as what
    /// sent them. The pages they reached from it afterwards name the site itself and change neither.
    /// </summary>
    [Fact]
    public async Task A_Visitor_A_Search_Sent_Inside_The_Window_Is_Seen_Arriving_From_It()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Referred(FromBrowser(siteId, Now.AddMinutes(-6), "searcher", "/posts/hello"), SearchResults, SearchHost),
            Referred(FromBrowser(siteId, Now.AddMinutes(-3), "searcher", "/pricing"), OwnPage, SiteDomain));

        var here = await WhoIsHere(siteId);

        here.Visitors.Should().ContainSingle();
        here.Visitors[0].SeenArriving.Should().BeTrue();
        here.Visitors[0].Context.SendingSite.Should().Be("Google");
        here.Visitors[0].Context.Channel.Should().Be(SourceChannel.Search);
        here.Visitors[0].Evidence.SentBy.Should().Be(
            SourceChannel.Search,
            "what the visitor is judged on and what the screen says sent them are one answer");
    }

    /// <summary>
    /// A first page that names nowhere it was reached from is an arrival with nothing said about
    /// who sent it, which is what coming straight here means and all it means.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Whose_First_Page_Named_No_Sender_Is_Seen_Arriving()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromBrowser(siteId, Now.AddMinutes(-6), "typed-it", "/"),
            Referred(FromBrowser(siteId, Now.AddMinutes(-3), "typed-it", "/pricing"), OwnPage, SiteDomain));

        var here = await WhoIsHere(siteId);

        here.Visitors[0].SeenArriving.Should().BeTrue();
        here.Visitors[0].Context.SendingSite.Should().BeEmpty();
        here.Visitors[0].Context.Channel.Should().Be(SourceChannel.Direct);
        here.Visitors[0].Evidence.SentBy.Should().Be(SourceChannel.Direct);
    }

    /// <summary>
    /// The window is a stretch of minutes rather than a visit, so somebody who arrived before it
    /// opened reaches it moving from one page of the site to the next. Their first page in it names
    /// the site itself, which is a visitor who was already here, and saying they came straight in
    /// would be a claim the window cannot carry.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Whose_First_Page_In_The_Window_Was_Reached_From_The_Site_Was_Already_Here()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Referred(FromBrowser(siteId, Now.AddMinutes(-6), "already-here", "/pricing"), OwnPage, SiteDomain));

        var here = await WhoIsHere(siteId);

        here.Visitors[0].SeenArriving.Should().BeFalse();
        here.Visitors[0].Context.SendingSite.Should().BeEmpty();
    }

    /// <summary>
    /// A page left on a screen reports every minute for as long as it stays there. Somebody whose
    /// only news inside the window is that opened their page before it, and a list that counted them
    /// would hold them at the top, active a moment ago, all night — and on into the next day under a
    /// key no visit is ever made of. They are not here until they open another page.
    /// </summary>
    [Fact]
    public async Task A_Page_Left_Open_Keeps_Nobody_Here_However_Recently_It_Reported()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
        [
            FromBrowser(siteId, Now.AddMinutes(-45), "left-open", "/long-read"),
            .. Enumerable.Range(1, 44).Select(minute =>
                Engaged(siteId, Now.AddMinutes(-45 + minute), "left-open", "/long-read")),
        ]);

        var here = await WhoIsHere(siteId);
        var pages = await WhatIsBeingRead(siteId);

        here.VisitorsSeen.Should().Be(0);
        here.Visitors.Should().BeEmpty();
        pages.Should().BeEmpty();
    }

    /// <summary>
    /// A departure is sent from the page being left, so it is news of a page somebody already had.
    /// Leaving a page opened before the window — a tab dismissed at last, or put behind another — is
    /// not somebody arriving, and lists nobody.
    /// </summary>
    [Fact]
    public async Task Leaving_A_Page_Opened_Before_The_Window_Lists_Nobody()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromBrowser(siteId, Now.AddMinutes(-50), "dismissed", "/long-read"),
            Left(siteId, Now.AddMinutes(-2), "dismissed", "/long-read"));

        var here = await WhoIsHere(siteId);

        here.VisitorsSeen.Should().Be(0);
        here.Visitors.Should().BeEmpty();
    }

    /// <summary>
    /// Somebody who opened a page is here however long ago inside the window they did it, and a page
    /// left open beside them is counted nowhere: not in the figure, not on a page, and not at the top
    /// of the list for having reported last.
    /// </summary>
    [Fact]
    public async Task Somebody_Who_Opened_A_Page_Is_Counted_And_A_Page_Left_Open_Beside_Them_Is_Not()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-20), "reader", "/posts/hello"),
            Engaged(siteId, Now.AddMinutes(-1), "left-open", "/long-read"));

        var here = await WhoIsHere(siteId);
        var pages = await WhatIsBeingRead(siteId);

        here.VisitorsSeen.Should().Be(1);
        here.Visitors.Select(visitor => visitor.VisitorKey).Should().Equal("reader");
        pages.Select(page => page.Path).Should().Equal("/posts/hello");
        pages.Sum(page => page.Visitors).Should().Be(here.VisitorsSeen);
    }

    /// <summary>
    /// A visitor already on the site who left and came back through a search was sent by the
    /// search, and is named as sent by it; the window still did not see them arrive the first time,
    /// and says so. Both answers travel, and which one a screen leads with is the screen's to say.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Already_Here_Who_Came_Back_From_A_Search_Is_Sent_By_It_Without_Being_Seen_Arriving()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Referred(FromBrowser(siteId, Now.AddMinutes(-8), "returning", "/pricing"), OwnPage, SiteDomain),
            Referred(FromBrowser(siteId, Now.AddMinutes(-3), "returning", "/posts/hello"), SearchResults, SearchHost));

        var here = await WhoIsHere(siteId);

        here.Visitors[0].Context.SendingSite.Should().Be("Google");
        here.Visitors[0].SeenArriving.Should().BeFalse();
    }

    /// <summary>
    /// A site reported only from its own server is watched from the request path, and a request
    /// that named no referrer is as much an arrival there as a tracker's report of one. The pages
    /// reached from it afterwards name the site, and change nothing about how the visitor arrived.
    /// </summary>
    [Fact]
    public async Task A_Server_Seen_Direct_Arrival_Followed_By_Internal_Pages_Is_Seen_Arriving()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-9), "server-seen", "/"),
            Referred(FromServer(siteId, Now.AddMinutes(-6), "server-seen", "/pricing"), OwnPage, SiteDomain),
            Referred(FromServer(siteId, Now.AddMinutes(-2), "server-seen", "/docs"), OwnPage, SiteDomain));

        var here = await WhoIsHere(siteId);

        here.Visitors.Should().ContainSingle();
        here.Visitors[0].SeenArriving.Should().BeTrue();
    }

    /// <summary>
    /// A referrer is written by whoever sent the request, and one naming no host anybody could have
    /// been on says nothing about where they were before. It reads as nobody having named a sender,
    /// which is what the finished visit makes of it, and never as a visitor who was already here.
    /// </summary>
    [Fact]
    public async Task A_Referrer_Naming_No_Host_Says_Nothing_About_When_Somebody_Arrived()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Referred(FromBrowser(siteId, Now.AddMinutes(-5), "nameless", "/"), "about:blank", string.Empty));

        var here = await WhoIsHere(siteId);

        here.Visitors[0].SeenArriving.Should().BeTrue();
        here.Visitors[0].Context.SendingSite.Should().BeEmpty();
    }

    /// <summary>
    /// A reporter on the site's own server need not pass on where a page was reached from, and the
    /// tracker on the same page says whenever the browser does. The tracker's account is taken
    /// first, so a visitor clicking through from a page they were already on is not read as an
    /// arrival because the server's report of the same page reached the store a moment earlier with
    /// nothing in it.
    /// </summary>
    [Fact]
    public async Task The_Tracker_Is_Taken_At_Its_Word_Ahead_Of_A_Server_That_Named_No_Referrer()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-5), "as-the-server-saw-them", "/pricing", "delivery-1"),
            Referred(
                FromBrowser(siteId, Now.AddMinutes(-5).AddSeconds(1), "as-the-browser-saw-them", "/pricing", "delivery-1"),
                OwnPage,
                SiteDomain));

        var here = await WhoIsHere(siteId);

        here.Visitors.Should().ContainSingle();
        here.Visitors[0].SeenArriving.Should().BeFalse();
    }

    /// <summary>
    /// The image a page with no script running asks for never says where its page was reached
    /// from. A reporter on the site's own server can send its report of the same page seconds
    /// later, in a batch, and that report does say: the visitor came from another page of the site.
    /// The image's silence is read last, so it cannot speak over it.
    /// </summary>
    [Fact]
    public async Task An_Image_That_Named_Nothing_Does_Not_Speak_Over_A_Server_That_Named_The_Site()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromImage(siteId, Now.AddMinutes(-5), "scripts-off", "/pricing"),
            Referred(FromServer(siteId, Now.AddMinutes(-5).AddSeconds(15), "scripts-off", "/pricing"), OwnPage, SiteDomain));

        var here = await WhoIsHere(siteId);

        here.Visitors.Should().ContainSingle();
        here.Visitors[0].SeenArriving.Should().BeFalse();
    }

    /// <summary>
    /// Where the image is the only thing that reported a page, its page view is the arrival the
    /// window saw. It names no sender, which is what the finished visit makes of the same report.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Only_The_Image_Saw_Is_Seen_Arriving()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(FromImage(siteId, Now.AddMinutes(-5), "image-only", "/"));

        var here = await WhoIsHere(siteId);

        here.Visitors.Should().ContainSingle();
        here.Visitors[0].SeenArriving.Should().BeTrue();
        here.Visitors[0].Context.SendingSite.Should().BeEmpty();
    }

    /// <summary>
    /// Somebody reading a page they opened before the window began, who then opens another page
    /// that says nothing about where it was reached from — a link marked to send no referrer, a
    /// site that sends none — was already here: their own reading is in the window ahead of that
    /// page view, and it says so.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Already_Reading_Whose_Next_Page_Named_Nothing_Was_Not_Seen_Arriving()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Engaged(siteId, Now.AddMinutes(-8), "kept-reading", "/long-read"),
            FromBrowser(siteId, Now.AddMinutes(-3), "kept-reading", "/pricing"));

        var here = await WhoIsHere(siteId);

        here.Visitors.Should().ContainSingle();
        here.Visitors[0].SeenArriving.Should().BeFalse();
        here.Visitors[0].Context.SendingSite.Should().BeEmpty();
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
    /// Somebody who opened a page inside the window and went back to one they had open before it is
    /// here, and stands on the page they reported from last in both lists — or the two would
    /// disagree by one for every reader with two tabs.
    /// </summary>
    [Fact]
    public async Task Somebody_Back_On_A_Page_They_Opened_Earlier_Is_On_It_In_Both_Lists()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromBrowser(siteId, Now.AddMinutes(-9), "reader", "/pricing"),
            Engaged(siteId, Now.AddMinutes(-2), "reader", "/long-read"));

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
                SiteDomain,
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
            new SiteLiveVisitorsQuery(Window(), SiteDomain, limit),
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
    private TimeRange Window() => new(Now - HalfHour, Now);

    private static DateTimeOffset OnTheMinute(DateTimeOffset at) =>
        at.AddTicks(-(at.Ticks % TimeSpan.TicksPerMinute));

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

    /// <summary>
    /// A report naming the page its own page was reached from, with the host the collector reads
    /// out of it — empty for an address that names none.
    /// </summary>
    private static RawEvent Referred(RawEvent observed, string address, string host) =>
        observed with { Referrer = address, ReferrerDomain = host };

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

    private static RawEvent Left(Guid siteId, DateTimeOffset at, string? visitorKey, string path) =>
        Observed(siteId, at, visitorKey, path, EventKind.Exit, IngestSurface.BrowserTracker, null);

    /// <summary>The image a page with no script running asks for, which names no referrer.</summary>
    private static RawEvent FromImage(Guid siteId, DateTimeOffset at, string? visitorKey, string path) =>
        Observed(siteId, at, visitorKey, path, EventKind.PageView, IngestSurface.NoScriptPixel, null);

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
            Host = SiteDomain,
            Path = path,
            CorrelationId = correlationId,
        };
}
