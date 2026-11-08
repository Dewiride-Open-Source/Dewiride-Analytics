using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves what a reader is shown on one post's own page: its views, every month it was read in, and its
/// recent weeks day by day — and that a path naming no post of the website is answered exactly as a
/// website that does not exist is.
/// </summary>
/// <remarks>
/// The posts are written as a reading of the sitemap leaves them; the views come from reports and from
/// months kept as an engine keeps them. The path a reader asks about is written by whoever asks, so it is
/// proved to be found exactly as written, never to be repeated in a refusal, and never to travel in a
/// trace of the request.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class InsightsPostReadTests(AnalyticsStackFixture stack)
{
    private const string Kolkata = "Asia/Kolkata";

    /// <summary>
    /// Paths that name no post of the website: never listed, a listed page that is not a post, a post no
    /// longer listed, another website's post, a listed post's path in another case or without its trailing
    /// slash, longer than any path a post may be listed at, and holding a character no listed path can hold.
    /// </summary>
    public static TheoryData<string> NoPostOfTheWebsite =>
    [
        "/posts/never-listed/",
        "/about/",
        "/posts/gone/",
        "/posts/elsewhere/",
        "/Posts/Hello/",
        "/posts/hello",
        $"/{new string('a', Site.MaxSitemapAddressLength)}",
        "/posts/hello/\0",
    ];

    /// <summary>
    /// Paths a website's sitemap may give a post: as long as any a post may be listed at, and holding what an
    /// address, a string literal, an escape or a placeholder is written with.
    /// </summary>
    public static TheoryData<string> AwkwardPaths =>
    [
        $"/{new string('a', Site.MaxSitemapAddressLength - 1)}",
        "/posts/it's-here/",
        "/posts/\"quoted\"/",
        "/posts/back\\slash/",
        "/posts/100%25/",
        "/posts/one+two/",
        "/posts/a&b=c?d#e/",
        "/{post_paths:Array(String)}/",
    ];

    /// <summary>
    /// The post with its title, address, day and views — at its own path and the path it was moved to — the
    /// month its views are counted from, the website's first day and the post's, each month it was read in
    /// with the days it covers from the day it was published, and every day of its recent weeks.
    /// </summary>
    [Fact]
    public async Task A_Posts_Page_Has_Its_Views_Its_Months_And_Its_Recent_Weeks()
    {
        var read = await PostReadAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var answer = await AskAsync(browser, read.Site.Id, "/posts/hello/");
        var today = read.Calendar.DateOf(answer.To);
        var weeksFrom = PostRecord.WeeksFrom(today);

        answer.Post.Should().Be(
            new InsightsPost("/posts/hello/", "https://example.com/posts/hello/", "Hello", Day(read.Published), 3));
        answer.ViewsFrom.Should().Be(Month(read.LastMonth));
        answer.FirstDay.Should().Be(Day(read.FirstDay));
        answer.From.Should().Be(Day(read.Published));
        answer.CompleteTo.Should().Be(answer.To);
        answer.Months.Should().Equal(
            new InsightsMonth(Month(read.LastMonth), 2, DateTime.DaysInMonth(read.LastMonth.Year, read.LastMonth.Month) - 9),
            new InsightsMonth(Month(ZoneCalendar.FirstOf(today)), 1, today.Day));
        answer.Days.Should().HaveCount(today.DayNumber - weeksFrom.DayNumber + 1);
        answer.Days[0].Should().Be(new InsightsPostDay(Day(weeksFrom), null));
        answer.Days[^1].Day.Should().Be(Day(today));
        Figure(answer, read.FirstDay.AddDays(-1)).Should().BeNull();
        Figure(answer, read.FirstDay).Should().Be(0);
        Figure(answer, read.Calendar.DateOf(read.Twelfth)).Should().Be(2);
        Figure(answer, read.Calendar.DateOf(read.Again)).Should().Be(1);
    }

    /// <summary>
    /// A post's views are the figure Insights gives it, for everybody and for people alike: its own path and
    /// the path it was moved to, with a server's report of it told whose it was by the browser that echoed
    /// another page of the same visit.
    /// </summary>
    /// <param name="only">Who the answer is about: nothing for everybody, or <c>people</c>.</param>
    [Theory]
    [InlineData("")]
    [InlineData("people")]
    public async Task A_Posts_Views_Are_The_Figure_Insights_Gives_It(string only)
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var lately = Lately(calendar, ZoneCalendar.FirstOf(calendar.DateOf(now)), now);
        var listedAt = SitemapSeed.Now(stack);
        var reader = $"reader-{site.Id:n}";
        var server = $"server-{site.Id:n}";
        var scraper = $"scraper-{site.Id:n}";

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(site.Id, "/posts/old-home/", listedAt, listedAt.AddDays(-3), landed: "/posts/new-home/"),
            KeptPosts.Post(site.Id, "/posts/other/", listedAt, listedAt.AddDays(-4)));
        await KeptRecord.WriteAsync(
            stack,
            [
                Report(site.Id, lately, "/landing/", server, IngestSurface.NextJsMiddleware, "delivery-1"),
                Report(site.Id, lately, "/landing/", reader, IngestSurface.BrowserTracker, "delivery-1"),
                Report(site.Id, lately.AddSeconds(1), "/posts/new-home/", server, IngestSurface.NextJsMiddleware),
                Report(site.Id, lately.AddSeconds(1), "/posts/new-home/", reader, IngestSurface.BrowserTracker),
                KeptRecord.Page(site.Id, lately.AddSeconds(2), "/posts/old-home/", reader),
                .. Enumerable.Range(0, 3).Select(page =>
                    KeptRecord.Page(site.Id, lately.AddSeconds(10 + page), "/posts/old-home/", scraper)),
            ]);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, lately, lately.AddSeconds(2), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, site.Id, scraper, lately.AddSeconds(10), lately.AddSeconds(12), TrafficCategory.ContentScraper);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var insights = await KeptRecord.GetAsync<InsightsPostsResponse>(
            browser,
            $"/api/sites/{site.Id}/insights/posts{(only.Length == 0 ? string.Empty : $"?only={only}")}");
        var answer = await AskAsync(browser, site.Id, "/posts/old-home/", only);

        answer.Post.PageViews.Should().Be(insights.Latest.Single(post => post.Path == "/posts/old-home/").PageViews);
        answer.Post.PageViews.Should().Be(only.Length == 0 ? 5 : 2);
    }

    /// <summary>
    /// A post is read at its own path and where its page landed, unless that is another listed post's own
    /// path, which is that post's alone; a page there that is not a post, or a post no longer listed, takes
    /// nothing from the post that landed on it. A reader read every path once and a scraper every path twice,
    /// and each post's views are the figure Insights gives it, for everybody and for people alike.
    /// </summary>
    /// <param name="only">Who the answer is about: nothing for everybody, or <c>people</c>.</param>
    [Theory]
    [InlineData("")]
    [InlineData("people")]
    public async Task A_Post_Is_Read_Where_It_Landed_As_Well_Unless_That_Is_Another_Listed_Posts_Own_Path(string only)
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var lately = Lately(calendar, ZoneCalendar.FirstOf(calendar.DateOf(now)), now);
        var listedAt = SitemapSeed.Now(stack);
        var reader = $"reader-{site.Id:n}";
        var scraper = $"scraper-{site.Id:n}";
        string[] paths = ["/posts/merged/", "/posts/other/", "/posts/retired/", "/archive/", "/posts/renamed/", "/posts/gone/"];

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(site.Id, "/posts/merged/", listedAt, listedAt.AddDays(-3), landed: "/posts/other/"),
            KeptPosts.Post(site.Id, "/posts/other/", listedAt),
            KeptPosts.Post(site.Id, "/posts/retired/", listedAt, listedAt.AddDays(-4), landed: "/archive/"),
            KeptPosts.NotAPost(site.Id, "/archive/", listedAt),
            KeptPosts.Post(site.Id, "/posts/renamed/", listedAt, listedAt.AddDays(-5), landed: "/posts/gone/"),
            KeptPosts.Gone(site.Id, "/posts/gone/", listedAt));
        await KeptRecord.WriteAsync(
            stack,
            [
                .. paths.Select((path, second) => KeptRecord.Page(site.Id, lately.AddSeconds(second), path, reader)),
                .. paths.Concat(paths).Select((path, second) =>
                    KeptRecord.Page(site.Id, lately.AddSeconds(10 + second), path, scraper)),
            ]);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, lately, lately.AddSeconds(5), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, site.Id, scraper, lately.AddSeconds(10), lately.AddSeconds(21), TrafficCategory.ContentScraper);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var insights = await KeptRecord.GetAsync<InsightsPostsResponse>(
            browser,
            $"/api/sites/{site.Id}/insights/posts{(only.Length == 0 ? string.Empty : $"?only={only}")}");
        var shown = insights.Latest.ToDictionary(post => post.Path, post => post.PageViews, StringComparer.Ordinal);
        var merged = await AskAsync(browser, site.Id, "/posts/merged/", only);
        var retired = await AskAsync(browser, site.Id, "/posts/retired/", only);
        var renamed = await AskAsync(browser, site.Id, "/posts/renamed/", only);

        merged.Post.PageViews.Should().Be(shown["/posts/merged/"]);
        merged.Post.PageViews.Should().Be(only.Length == 0 ? 3 : 1);
        retired.Post.PageViews.Should().Be(shown["/posts/retired/"]);
        retired.Post.PageViews.Should().Be(only.Length == 0 ? 6 : 2);
        renamed.Post.PageViews.Should().Be(shown["/posts/renamed/"]);
        renamed.Post.PageViews.Should().Be(only.Length == 0 ? 6 : 2);
    }

    /// <summary>
    /// A post's views are what was kept of the months whose reports have gone, added to what the reports say
    /// of the months since; its months begin with the first kept, everybody's figures and the people's alike.
    /// </summary>
    [Fact]
    public async Task A_Posts_Views_Are_Its_Kept_Months_And_The_Reports_Since_Added_Up()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(now));
        var longAgo = running.AddMonths(-15);
        var lately = Lately(calendar, running, now);
        var reader = $"reader-{site.Id:n}";
        var scraper = $"scraper-{site.Id:n}";

        await KeptRecord.KeepAsync(stack, KeptRecord.Counted(site, calendar, longAgo, 40));
        await KeptPosts.KeepAsync(
            stack,
            KeptPosts.Counted(site, calendar, longAgo, PostFingerprint.Of(["/posts/a/"]), Counted(calendar, longAgo), new PostMonthViews("/posts/a/", 40, 25)));
        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(site.Id, "/posts/a/", SitemapSeed.Now(stack), lately));
        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, lately, "/posts/a/", reader),
                KeptRecord.Page(site.Id, lately.AddSeconds(1), "/posts/a/", scraper),
                KeptRecord.Page(site.Id, lately.AddSeconds(2), "/posts/a/", scraper),
            ]);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, lately, lately, TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, site.Id, scraper, lately.AddSeconds(1), lately.AddSeconds(2), TrafficCategory.ContentScraper);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var everybody = await AskAsync(browser, site.Id, "/posts/a/");
        var people = await AskAsync(browser, site.Id, "/posts/a/", "people");

        everybody.ViewsFrom.Should().Be(Month(longAgo));
        everybody.Post.PageViews.Should().Be(43);
        everybody.Months[0].Should().Be(new InsightsMonth(Month(longAgo), 40, DateTime.DaysInMonth(longAgo.Year, longAgo.Month)));
        everybody.Months[^1].PageViews.Should().Be(3);
        people.ViewsFrom.Should().Be(Month(longAgo));
        people.Post.PageViews.Should().Be(26);
        people.Months[0].PageViews.Should().Be(25);
        people.Months[^1].PageViews.Should().Be(1);
    }

    /// <summary>
    /// A month whose posts were never kept and can no longer be counted, between two that were, has no
    /// figure and covers no days: nothing says how much the post was read in it.
    /// </summary>
    [Fact]
    public async Task A_Month_Whose_Posts_Were_Never_Kept_Is_A_Gap_Rather_Than_Nought()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(await KeptRecord.NowInsideTheMonthAsync(stack, calendar)));
        var fifteen = running.AddMonths(-15);
        var thirteen = running.AddMonths(-13);
        var fingerprint = PostFingerprint.Of(["/posts/a/"]);

        await KeptRecord.KeepAsync(stack, KeptRecord.Counted(site, calendar, fifteen, 40));
        await KeptRecord.KeepAsync(stack, KeptRecord.Counted(site, calendar, thirteen, 30));
        await KeptPosts.KeepAsync(
            stack,
            KeptPosts.Counted(site, calendar, fifteen, fingerprint, Counted(calendar, fifteen), new PostMonthViews("/posts/a/", 4, 2)));
        await KeptPosts.KeepAsync(
            stack,
            KeptPosts.Counted(site, calendar, thirteen, fingerprint, Counted(calendar, thirteen), new PostMonthViews("/posts/a/", 6, 3)));
        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(site.Id, "/posts/a/", SitemapSeed.Now(stack), calendar.MidnightOn(fifteen.AddDays(-20))));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var answer = await AskAsync(browser, site.Id, "/posts/a/");

        answer.Months.Take(3).Should().Equal(
            new InsightsMonth(Month(fifteen), 4, DateTime.DaysInMonth(fifteen.Year, fifteen.Month)),
            new InsightsMonth(Month(fifteen.AddMonths(1)), null, 0),
            new InsightsMonth(Month(thirteen), 6, DateTime.DaysInMonth(thirteen.Year, thirteen.Month)));
        answer.Post.PageViews.Should().Be(10);
    }

    /// <summary>
    /// Kept to people, a post's page counts the reader and leaves the scraper out of everything; and it says
    /// it is complete only as far as judging has reached across what it was read from, which on a website
    /// judging has not started on is the first of the recent weeks.
    /// </summary>
    [Fact]
    public async Task A_Posts_Page_Of_People_Counts_The_Reader_Alone_And_Says_How_Far_It_Is_Complete()
    {
        var read = await PostReadAsync();
        var scraper = $"scraper-{read.Site.Id:n}";

        await KeptRecord.WriteAsync(
            stack,
            [
                .. Enumerable.Range(0, 3).Select(page =>
                    KeptRecord.Page(read.Site.Id, read.Again.AddSeconds(10 + page), "/posts/hello/", scraper)),
            ]);
        await KeptRecord.JudgedAsync(stack, read.Site.Id, read.Reader, read.Twelfth, read.Twelfth, TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, read.Site.Id, read.Reader, read.Again, read.Again, TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(
            stack,
            read.Site.Id,
            scraper,
            read.Again.AddSeconds(10),
            read.Again.AddSeconds(12),
            TrafficCategory.ContentScraper);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var everybody = await AskAsync(browser, read.Site.Id, "/posts/hello/");
        var people = await AskAsync(browser, read.Site.Id, "/posts/hello/", "people");

        everybody.Post.PageViews.Should().Be(6);
        everybody.CompleteTo.Should().Be(everybody.To);
        people.Post.PageViews.Should().Be(2);
        people.Months.Select(month => month.PageViews).Should().Equal(1, 1);
        people.CompleteTo.Should().Be(read.Calendar.MidnightOn(PostRecord.WeeksFrom(read.Calendar.DateOf(people.To))));
    }

    /// <summary>
    /// A path that names no post of the website is answered exactly as a website that does not exist is —
    /// the same status, and the same problem — so nothing about the website's pages, its other posts or
    /// another website's can be told from it, and the answer never repeats what was asked.
    /// </summary>
    /// <param name="path">The path asked about.</param>
    [Theory]
    [MemberData(nameof(NoPostOfTheWebsite))]
    public async Task A_Path_That_Is_No_Listed_Post_Is_Not_Found_As_A_Website_That_Does_Not_Exist_Is_Not(string path)
    {
        var listedAt = SitemapSeed.Now(stack);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var elsewhere = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(site.Id, "/posts/hello/", listedAt),
            KeptPosts.NotAPost(site.Id, "/about/", listedAt),
            KeptPosts.Gone(site.Id, "/posts/gone/", listedAt),
            KeptPosts.Post(elsewhere.Id, "/posts/elsewhere/", listedAt));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id, elsewhere.Id);
        var asked = await RefusalAsync(browser, site.Id, path);
        var invented = await RefusalAsync(browser, Guid.NewGuid(), "/posts/hello/");

        asked.Status.Should().Be(HttpStatusCode.NotFound);
        asked.Should().Be(invented);
    }

    /// <summary>
    /// A post of a website somebody has no role on is answered exactly as a website that does not exist is,
    /// so another account's posts can neither be read nor found to be there.
    /// </summary>
    [Fact]
    public async Task A_Post_Of_A_Website_Somebody_Cannot_See_Is_Not_Found()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(theirs.Id, "/posts/hello/", SitemapSeed.Now(stack)));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, mine.Id);
        var existing = await RefusalAsync(browser, theirs.Id, "/posts/hello/");
        var invented = await RefusalAsync(browser, Guid.NewGuid(), "/posts/hello/");

        existing.Status.Should().Be(HttpStatusCode.NotFound);
        existing.Should().Be(invented);
    }

    /// <summary>
    /// A path that names no post of the website is answered from the control plane alone: here the
    /// telemetry store refuses every question, and nothing asks it one.
    /// </summary>
    [Fact]
    public async Task A_Path_That_Is_No_Listed_Post_Is_Answered_Without_Asking_The_Telemetry_Store()
    {
        var listedAt = SitemapSeed.Now(stack);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var refusing = DispatchProxy.Create<ITelemetryQueries, RefusingTelemetry>();

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(site.Id, "/posts/hello/", listedAt),
            KeptPosts.NotAPost(site.Id, "/about/", listedAt));

        using var host = stack.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(refusing)));
        using var browser = await SignedIn.AsAsync(stack, host, SiteRole.Viewer, site.Id);

        foreach (var path in new[] { "/posts/never-listed/", "/about/", $"/{new string('a', Site.MaxSitemapAddressLength)}" })
        {
            var response = await browser.GetAsync(Address(site.Id, path));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        ((RefusingTelemetry)refusing).Asked.Should().Be(0);
    }

    /// <summary>
    /// A question naming no post, or asked about anybody but everybody or people, is refused before the
    /// website is looked for: the website here does not exist, and the refusal comes back regardless.
    /// </summary>
    /// <param name="question">What follows the address.</param>
    /// <param name="detail">What the refusal says.</param>
    [Theory]
    [InlineData("", "Ask about a post by the path Insights lists it at.")]
    [InlineData("?path=", "Ask about a post by the path Insights lists it at.")]
    [InlineData("?only=people", "Ask about a post by the path Insights lists it at.")]
    [InlineData("?path=%2Fposts%2Fhello%2F&only=robots", "Ask about everybody, or only about people.")]
    public async Task A_Posts_Page_Refuses_A_Missing_Path_And_Anybody_But_Everybody_Or_People_Before_The_Website_Is_Looked_For(
        string question,
        string detail)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var response = await browser.GetAsync($"/api/sites/{Guid.NewGuid()}/insights/post{question}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var refusal = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation.Token));

        refusal.RootElement.GetProperty("detail").GetString().Should().Be(detail);
    }

    /// <summary>
    /// However a post's path is spelled, escaped into the address it finds its post, exactly as it is listed.
    /// </summary>
    /// <param name="path">The post's path.</param>
    [Theory]
    [MemberData(nameof(AwkwardPaths))]
    public async Task A_Long_Awkward_Path_Finds_Its_Post_Through_The_Address(string path)
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);

        await SitemapSeed.WriteAsync(
            stack,
            new SitePost
            {
                SiteId = site.Id,
                Path = path,
                Address = "https://example.com/posts/awkward/",
                ListedInPostsPart = true,
                FirstListedAt = SitemapSeed.Now(stack),
            });
        await KeptRecord.WriteAsync(
            stack,
            [KeptRecord.Page(site.Id, Lately(calendar, ZoneCalendar.FirstOf(calendar.DateOf(now)), now), path, $"reader-{site.Id:n}")]);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var answer = await AskAsync(browser, site.Id, path);

        answer.Post.Path.Should().Be(path);
        answer.Post.PageViews.Should().Be(1);
    }

    [Fact]
    public async Task A_Posts_Page_Is_Not_Read_By_Anybody_Signed_Out()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(site.Id, "/posts/hello/", SitemapSeed.Now(stack)));

        using var browser = await Browser.OpenAsync(stack);
        var response = await browser.GetAsync(Address(site.Id, "/posts/hello/"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The path a reader asks about is written by whoever asks, and it travels in no trace of the request,
    /// on the post's page or its chart, whether it names a post or not: what the request's query held is
    /// recorded as withheld.
    /// </summary>
    /// <param name="question">The address asked, beneath the website's.</param>
    /// <param name="rest">What else the query asks.</param>
    /// <param name="withheld">The query, as the trace records it.</param>
    [Theory]
    [InlineData("insights/post", "", "?path=Redacted")]
    [InlineData("insights/post/series", "&granularity=day", "?path=Redacted&granularity=Redacted")]
    public async Task The_Path_Asked_About_Travels_In_No_Trace_Of_The_Request(string question, string rest, string withheld)
    {
        var marker = Guid.NewGuid().ToString("n");
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var address = $"/api/sites/{site.Id}/{question}";
        var traced = new ConcurrentQueue<Activity>();

        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(site.Id, $"/posts/listed-{marker}/", SitemapSeed.Now(stack)));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = traced.Enqueue,
        };

        ActivitySource.AddActivityListener(listener);

        var found = await browser.GetAsync($"{address}?path={Uri.EscapeDataString($"/posts/listed-{marker}/")}{rest}");
        var missing = await browser.GetAsync($"{address}?path={Uri.EscapeDataString($"/posts/unlisted-{marker}/")}{rest}");
        var requests = await TracedAsync(traced, address, 2);

        found.StatusCode.Should().Be(HttpStatusCode.OK);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        requests.Should().HaveCount(2);

        foreach (var request in requests)
        {
            Recorded(request).Should().NotContain(text => text.Contains(marker, StringComparison.Ordinal));
            request.GetTagItem("url.query").Should().Be(withheld);
        }
    }

    /// <summary>
    /// The names on the wire are what the dashboard's own check of this answer is written against, and this
    /// is the only place that sees them as they are sent.
    /// </summary>
    [Fact]
    public async Task The_Answer_Is_Written_Under_The_Names_The_Dashboard_Reads()
    {
        var read = await PostReadAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var response = await browser.GetAsync(Address(read.Site.Id, "/posts/hello/"));

        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation.Token));
        var root = answer.RootElement;

        Names(root).Should().Equal("post", "viewsFrom", "firstDay", "from", "to", "completeTo", "months", "days");
        Names(root.GetProperty("post")).Should().Equal("path", "address", "title", "published", "pageViews");
        Names(root.GetProperty("months")[0]).Should().Equal("month", "pageViews", "daysMeasured");
        Names(root.GetProperty("days")[0]).Should().Equal("day", "pageViews");
        root.GetProperty("days")[0].GetProperty("pageViews").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// A website in Kolkata, first measured on the fifth of last month, listing a post published on the tenth
    /// and moved to a new path: on the twelfth one reader read it at its own path and another at the new
    /// one, and the first read it again this month, no more than forty minutes ago.
    /// </summary>
    private async Task<PostRead> PostReadAsync()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(now));
        var lastMonth = running.AddMonths(-1);
        var firstDay = lastMonth.AddDays(4);
        var published = lastMonth.AddDays(9);
        var twelfth = calendar.MidnightOn(lastMonth.AddDays(11)).AddHours(10);
        var lately = Lately(calendar, running, now);
        var reader = $"reader-{site.Id:n}";

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(
                site.Id,
                "/posts/hello/",
                SitemapSeed.Now(stack),
                calendar.MidnightOn(published).AddHours(9),
                landed: "/posts/hello-again/",
                title: "Hello"));
        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, calendar.MidnightOn(firstDay).AddHours(11), "/about/", reader),
                KeptRecord.Page(site.Id, twelfth, "/posts/hello/", reader),
                KeptRecord.Page(site.Id, twelfth.AddMinutes(5), "/posts/hello-again/", $"another-{site.Id:n}"),
                KeptRecord.Page(site.Id, lately, "/posts/hello/", reader),
            ]);

        return new PostRead(site, calendar, reader, lastMonth, firstDay, published, twelfth, lately);
    }

    /// <summary>Asks for a post's page and insists it was answered.</summary>
    private static Task<InsightsPostResponse> AskAsync(Browser browser, Guid siteId, string path, string only = "") =>
        KeptRecord.GetAsync<InsightsPostResponse>(browser, Address(siteId, path, only));

    /// <summary>The address a post's page is asked for at, with its path escaped as the dashboard escapes it.</summary>
    private static string Address(Guid siteId, string path, string only = "") =>
        $"/api/sites/{siteId}/insights/post?path={Uri.EscapeDataString(path)}{(only.Length == 0 ? string.Empty : $"&only={only}")}";

    /// <summary>Everything the refusal of a post's page says, and whether it repeats the path asked about.</summary>
    private static async Task<Refusal.Said> RefusalAsync(Browser browser, Guid siteId, string path) =>
        await Refusal.WholeAsync(await browser.GetAsync(Address(siteId, path)), path);

    /// <summary>The traces of the requests to an address, once as many as were made have finished.</summary>
    private static async Task<List<Activity>> TracedAsync(ConcurrentQueue<Activity> traced, string address, int requests)
    {
        var began = TimeProvider.System.GetTimestamp();

        while (true)
        {
            List<Activity> finished = [.. traced.Where(activity => Equals(activity.GetTagItem("url.path"), address))];

            if (finished.Count >= requests || TimeProvider.System.GetElapsedTime(began) > TimeSpan.FromSeconds(10))
            {
                return finished;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), Cancellation.Token);
        }
    }

    /// <summary>Everything a trace of a request records: its names, its tags, its baggage and its events.</summary>
    private static IEnumerable<string> Recorded(Activity activity) =>
    [
        activity.OperationName,
        activity.DisplayName,
        activity.StatusDescription ?? string.Empty,
        .. activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"),
        .. activity.Baggage.Select(item => $"{item.Key}={item.Value}"),
        .. activity.Events.SelectMany(happened =>
            happened.Tags.Select(tag => $"{happened.Name}: {tag.Key}={tag.Value}").Append(happened.Name)),
    ];

    /// <summary>One report of a page delivered, from the given half of the measurement.</summary>
    private static RawEvent Report(
        Guid siteId,
        DateTimeOffset at,
        string path,
        string visitorKey,
        IngestSurface surface,
        string? correlationId = null) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = EventKind.PageView,
            Surface = surface,
            ServerTimestamp = at,
            VisitorKey = visitorKey,
            Host = "example.com",
            Path = path,
            UserAgent = KeptRecord.Agent,
            StatusCode = 200,
            CorrelationId = correlationId,
        };

    /// <summary>When a month of posts is kept as counted: the day after it ended.</summary>
    private static DateTimeOffset Counted(ZoneCalendar calendar, DateOnly month) =>
        calendar.MidnightOn(month.AddMonths(1)).AddDays(1);

    /// <summary>A moment this month, no more than forty minutes ago.</summary>
    private static DateTimeOffset Lately(ZoneCalendar calendar, DateOnly running, DateTimeOffset now) =>
        calendar.MidnightOn(running) > now.AddMinutes(-40) ? calendar.MidnightOn(running) : now.AddMinutes(-40);

    private static long? Figure(InsightsPostResponse answer, DateOnly day) =>
        answer.Days.Single(held => held.Day == Day(day)).PageViews;

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Month(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string Domain() => $"insights-post-{Guid.NewGuid():n}.example";

    /// <summary>What was written, and when.</summary>
    /// <param name="Site">The website.</param>
    /// <param name="Calendar">Its calendar.</param>
    /// <param name="Reader">Who read the post twice.</param>
    /// <param name="LastMonth">The first of the month before the one running now.</param>
    /// <param name="FirstDay">The day the website was first measured on.</param>
    /// <param name="Published">The day the post was published on.</param>
    /// <param name="Twelfth">When the post was read on the twelfth of last month.</param>
    /// <param name="Again">When it was read again this month.</param>
    private sealed record PostRead(
        Site Site,
        ZoneCalendar Calendar,
        string Reader,
        DateOnly LastMonth,
        DateOnly FirstDay,
        DateOnly Published,
        DateTimeOffset Twelfth,
        DateTimeOffset Again);
}
