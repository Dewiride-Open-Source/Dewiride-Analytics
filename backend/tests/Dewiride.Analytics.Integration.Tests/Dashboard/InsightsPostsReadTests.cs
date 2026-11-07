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
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves what a reader is shown of a website's posts on the Insights screen: how many there are, the
/// newest and the most read with their views, and when the last twelve months' were published.
/// </summary>
/// <remarks>
/// The posts are written as a reading of the sitemap leaves them; the views come from reports and from
/// months of posts kept as an engine keeps them.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class InsightsPostsReadTests(AnalyticsStackFixture stack)
{
    private const string Kolkata = "Asia/Kolkata";

    /// <summary>
    /// The newest posts with a date, newest first, each with its title, address, day and views — a post
    /// redirected elsewhere read where it landed — the most read, and the days of the last twelve months
    /// a post was published on. A post with no date counts among the posts and is on neither list.
    /// </summary>
    [Fact]
    public async Task Posts_Are_Listed_Newest_First_With_What_Each_Was_Read_And_When_It_Was_Published()
    {
        var read = await PostsReadAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var posts = await AskAsync(browser, read.Site.Id);
        var today = read.Calendar.DateOf(read.Now);

        posts.Posts.Should().Be(4);
        posts.Dated.Should().Be(3);
        posts.Latest.Should().Equal(
            new InsightsPost("/posts/newest/", "https://example.com/posts/newest/", "Newest post", Day(read.Calendar.DateOf(read.Newest)), 4),
            new InsightsPost("/posts/older/", "https://example.com/posts/older/", null, Day(read.Calendar.DateOf(read.Older)), 1),
            new InsightsPost("/posts/old-home/", "https://example.com/posts/old-home/", "Moved", Day(read.Calendar.DateOf(read.LongAgo)), 2));
        posts.MostRead.Should().Be(posts.Latest[0]);
        posts.CalendarFrom.Should().Be(Day(ZoneCalendar.FirstOf(today).AddMonths(-11)));
        posts.CalendarTo.Should().Be(Day(today));
        posts.Calendar.Should().Equal(
            new PostingDayResponse(Day(read.Calendar.DateOf(read.Older)), 1),
            new PostingDayResponse(Day(read.Calendar.DateOf(read.Newest)), 1));
    }

    /// <summary>Kept to people, a post's views are the reader's alone, and the most read follows them.</summary>
    [Fact]
    public async Task Posts_Of_People_Count_The_Reader_Alone()
    {
        var read = await PostsReadAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var posts = await AskAsync(browser, read.Site.Id, "?only=people");

        posts.Latest.Select(post => (post.Path, post.PageViews)).Should().Equal(
            ("/posts/newest/", 1L),
            ("/posts/older/", 1L),
            ("/posts/old-home/", 2L));
        posts.MostRead!.Path.Should().Be("/posts/old-home/");
    }

    /// <summary>
    /// A post's views are what was kept of the months whose reports have gone, added to what the reports
    /// say of the months since — and the answer says which month they are counted from.
    /// </summary>
    [Fact]
    public async Task A_Posts_Views_Are_The_Months_Kept_And_The_Reports_Since_Added_Up()
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
            KeptPosts.Counted(
                site,
                calendar,
                longAgo,
                PostFingerprint.Of(["/posts/a/"]),
                calendar.MidnightOn(longAgo.AddMonths(1)).AddDays(1),
                new PostMonthViews("/posts/a/", 40, 25)));
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
        var everybody = await AskAsync(browser, site.Id);
        var people = await AskAsync(browser, site.Id, "?only=people");

        everybody.ViewsFrom.Should().Be(Month(longAgo));
        everybody.MostRead!.PageViews.Should().Be(43);
        people.ViewsFrom.Should().Be(Month(longAgo));
        people.MostRead!.PageViews.Should().Be(26);
    }

    [Fact]
    public async Task Posts_Refuse_Anybody_But_Everybody_Or_People()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var response = await browser.GetAsync($"/api/sites/{site.Id}/insights/posts?only=robots");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A website somebody has no role on answers exactly as one that does not exist, so the posts of
    /// another account's website can neither be read nor found to be there.
    /// </summary>
    [Fact]
    public async Task Posts_Of_A_Website_Somebody_Cannot_See_Are_Not_Found()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var theirs = await PostsReadAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, mine.Id);
        var existing = await browser.GetAsync($"/api/sites/{theirs.Site.Id}/insights/posts");
        var invented = await browser.GetAsync($"/api/sites/{Guid.NewGuid()}/insights/posts");

        existing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        invented.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A website that lists no posts has nothing to count, and is answered from the control plane alone:
    /// here the telemetry store refuses every question, and the answer comes back regardless.
    /// </summary>
    [Fact]
    public async Task A_Website_That_Lists_No_Posts_Is_Answered_Without_Asking_The_Telemetry_Store()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var emailAddress = SignedIn.Address();
        var (created, account) = await ControlPlaneSeed.AddAccountAsync(stack, emailAddress, Passwords.Acceptable);
        var refusing = DispatchProxy.Create<ITelemetryQueries, RefusingTelemetry>();

        created.Succeeded.Should().BeTrue();
        await ControlPlaneSeed.GrantAsync(stack, site.Id, account.Id, SiteRole.Viewer);

        using var host = stack.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(refusing)));
        using var browser = await SignedIn.AsAccountAsync(host, emailAddress);

        var posts = await KeptRecord.GetAsync<InsightsPostsResponse>(browser, $"/api/sites/{site.Id}/insights/posts");

        ((RefusingTelemetry)refusing).Asked.Should().Be(0);
        posts.Posts.Should().Be(0);
        posts.Dated.Should().Be(0);
        posts.ViewsFrom.Should().BeNull();
        posts.Latest.Should().BeEmpty();
        posts.MostRead.Should().BeNull();
        posts.Calendar.Should().BeEmpty();
        posts.CalendarTo.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// The names on the wire are what the dashboard's own check of this answer is written against, and
    /// this is the only place that sees them as they are sent.
    /// </summary>
    [Fact]
    public async Task The_Answer_Is_Written_Under_The_Names_The_Dashboard_Reads()
    {
        var read = await PostsReadAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, read.Site.Id);
        var response = await browser.GetAsync($"/api/sites/{read.Site.Id}/insights/posts");

        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation.Token));
        var root = answer.RootElement;

        Names(root).Should().Equal("posts", "dated", "viewsFrom", "latest", "mostRead", "calendarFrom", "calendarTo", "calendar");
        Names(root.GetProperty("latest")[0]).Should().Equal("path", "address", "title", "published", "pageViews");
        Names(root.GetProperty("mostRead")).Should().Equal("path", "address", "title", "published", "pageViews");
        Names(root.GetProperty("calendar")[0]).Should().Equal("day", "posts");
        root.GetProperty("latest")[1].GetProperty("title").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// A website in Kolkata listing four posts: the newest two days old, another forty, one four hundred
    /// days old that was moved to a new path, and one with no date. This month a reader judged a person
    /// read the newest once, the moved post twice at its new path, and the older one once; a scraper took
    /// the newest three times.
    /// </summary>
    private async Task<PostsRead> PostsReadAsync()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(now));
        var lately = Lately(calendar, running, now);
        var reader = $"reader-{site.Id:n}";
        var scraper = $"scraper-{site.Id:n}";
        var listedAt = SitemapSeed.Now(stack);
        var newest = listedAt.AddDays(-2);
        var older = listedAt.AddDays(-40);
        var longAgo = listedAt.AddDays(-400);

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(site.Id, "/posts/newest/", listedAt, newest, title: "Newest post"),
            KeptPosts.Post(site.Id, "/posts/older/", listedAt, older),
            KeptPosts.Post(site.Id, "/posts/old-home/", listedAt, longAgo, landed: "/posts/new-home/", title: "Moved"),
            KeptPosts.Post(site.Id, "/posts/undated/", listedAt, title: "Undated"));
        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, lately, "/posts/newest/", reader),
                KeptRecord.Page(site.Id, lately.AddSeconds(1), "/posts/new-home/", reader),
                KeptRecord.Page(site.Id, lately.AddSeconds(2), "/posts/new-home/", reader),
                KeptRecord.Page(site.Id, lately.AddSeconds(3), "/posts/older/", reader),
                .. Enumerable.Range(0, 3).Select(page =>
                    KeptRecord.Page(site.Id, lately.AddSeconds(10 + page), "/posts/newest/", scraper)),
            ]);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, lately, lately.AddSeconds(3), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, site.Id, scraper, lately.AddSeconds(10), lately.AddSeconds(12), TrafficCategory.ContentScraper);

        return new PostsRead(site, calendar, now, newest, older, longAgo);
    }

    /// <summary>A moment this month, no more than forty minutes ago.</summary>
    private static DateTimeOffset Lately(ZoneCalendar calendar, DateOnly running, DateTimeOffset now) =>
        calendar.MidnightOn(running) > now.AddMinutes(-40) ? calendar.MidnightOn(running) : now.AddMinutes(-40);

    private static Task<InsightsPostsResponse> AskAsync(Browser browser, Guid siteId, string only = "") =>
        KeptRecord.GetAsync<InsightsPostsResponse>(browser, $"/api/sites/{siteId}/insights/posts{only}");

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Month(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string Domain() => $"insights-posts-{Guid.NewGuid():n}.example";

    /// <summary>
    /// A telemetry store that refuses every question, and counts how many it was asked.
    /// </summary>
    /// <remarks>Public and unsealed because a proxy is generated as a type derived from it.</remarks>
    public class RefusingTelemetry : DispatchProxy
    {
        private int _asked;

        /// <summary>How many questions it was asked.</summary>
        public int Asked => Volatile.Read(ref _asked);

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Interlocked.Increment(ref _asked);

            throw new InvalidOperationException($"The telemetry store was asked {targetMethod?.Name}.");
        }
    }

    /// <summary>What was written, and when.</summary>
    /// <param name="Site">The website.</param>
    /// <param name="Calendar">Its calendar.</param>
    /// <param name="Now">The moment the posts were written at.</param>
    /// <param name="Newest">When the newest post was published.</param>
    /// <param name="Older">When the post forty days old was published.</param>
    /// <param name="LongAgo">When the moved post was published.</param>
    private sealed record PostsRead(
        Site Site,
        ZoneCalendar Calendar,
        DateTimeOffset Now,
        DateTimeOffset Newest,
        DateTimeOffset Older,
        DateTimeOffset LongAgo);
}
