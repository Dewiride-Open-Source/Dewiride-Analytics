using System.Collections.Immutable;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that a month of a website's posts is kept as the dashboard's list of pages for that month,
/// however many posts there are and whenever they were found.
/// </summary>
/// <remarks>
/// A month of posts outlives its reports as a month's totals do, so it is checked here while they are
/// still held: each post's kept views against its row on the list of pages for the same month, read
/// through the dashboard's own address.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class PostMonthConsolidationTests(AnalyticsStackFixture stack)
{
    private const string Kolkata = "Asia/Kolkata";

    /// <summary>
    /// Each post's kept views are its row on the list of pages for the month, for everybody and for the
    /// people alone. A post nobody read that month, and a page that is not a post, are not kept.
    /// </summary>
    /// <param name="peopleOnly">Whether the people's figures are the ones compared.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Posts_Month_Is_The_Pages_List_Of_That_Month_For_Its_Paths(bool peopleOnly)
    {
        var (site, calendar, month) = await LastMonthsPostsReadAndScrapedAsync();

        await KeptRecord.CountAsync(stack, site);

        var kept = (await KeptPosts.RowsAsync(stack, site.Id)).Should().ContainSingle().Subject;
        var views = KeptPosts.ViewsIn(kept, peopleOnly ? "people_page_views" : "page_views");

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var pages = await KeptRecord.GetAsync<PagesResponse>(
            browser,
            $"/api/sites/{site.Id}/pages?{Span(calendar, month)}&limit=100{(peopleOnly ? "&only=people" : string.Empty)}");

        ((string[])kept["paths"]!).Should().Equal("/posts/first/", "/posts/second/");
        views.Should().Equal(peopleOnly
            ? new Dictionary<string, long>(StringComparer.Ordinal) { ["/posts/first/"] = 2, ["/posts/second/"] = 1 }
            : new Dictionary<string, long>(StringComparer.Ordinal) { ["/posts/first/"] = 5, ["/posts/second/"] = 1 });

        foreach (var (path, counted) in views)
        {
            pages.Pages.Should().ContainSingle(page => page.Path == path).Which.PageViews.Should().Be(counted);
        }
    }

    /// <summary>
    /// Every post is counted however many a website lists and however their paths are spelled: three
    /// thousand here, fifty of them near the longest path a sitemap may give, and others with a tilde,
    /// quotes or escapes in them. Each is counted at its path exactly as the collector stored it, for
    /// keeping and for the dashboard alike.
    /// </summary>
    [Fact]
    public async Task Every_Post_Is_Counted_However_Many_There_Are_And_However_Long_Or_Awkward_Their_Paths()
    {
        var (site, month, window) = await ThisMonthAsync();
        string[] paths =
        [
            .. Enumerable.Range(0, 2_900).Select(index => $"/posts/{index:0000}/"),
            .. Enumerable.Range(0, 50).Select(index => $"/long/{index:00}/{new string('x', 2_030)}"),
            .. Enumerable.Range(0, 10).Select(index => $"/~editor/{index}/"),
            .. Enumerable.Range(0, 10).Select(index => $"/posts/it's-number-{index}/"),
            .. Enumerable.Range(0, 10).Select(index => $"/posts/\"quoted\"-{index}/"),
            .. Enumerable.Range(0, 10).Select(index => $"/posts/caf%C3%A9-{index}/"),
            .. Enumerable.Range(0, 10).Select(index => $"/posts/100%25-of-it's-%22{index}%22/"),
        ];
        ImmutableArray<string> ordered = [.. paths.Order(StringComparer.Ordinal)];

        await KeptRecord.WriteAsync(
            stack,
            [.. paths.Select((path, index) => KeptRecord.Page(site.Id, window.From.AddSeconds(index), path, $"reader-{index}"))]);

        var (counted, read) = await CountedAndReadAsync(site, month, window, ordered);

        counted.Should().Equal(ordered.Select(path => new PathViews(path, 1)));
        read.Should().Equal(counted);
    }

    /// <summary>
    /// A page that is not one of the website's posts is neither kept with them nor added to one, however
    /// closely its path resembles a post's — escaped another way, in other letters, without its last slash
    /// or beneath it. A post is counted at its own path and at no other.
    /// </summary>
    [Fact]
    public async Task A_Page_That_Is_No_Listed_Post_Is_Neither_Kept_Nor_Credited_To_A_Post()
    {
        var (site, month, window) = await ThisMonthAsync();
        ImmutableArray<string> posts = ["/posts/caf%C3%A9/", "/posts/first/", "/~editor/notes/"];
        string[] others =
        [
            "/posts/caf%c3%a9/",
            "/posts/caf\u00E9/",
            "/Posts/first/",
            "/posts/first",
            "/posts/first/comments/",
            "/~editor/notes",
            "/about",
        ];

        await KeptRecord.WriteAsync(
            stack,
            [
                .. posts.Select((path, index) => KeptRecord.Page(site.Id, window.From.AddSeconds(index), path, $"reader-{index}")),
                .. others.SelectMany((path, index) => Enumerable.Range(0, 3).Select(view =>
                    KeptRecord.Page(site.Id, window.From.AddMinutes(1 + index).AddSeconds(view), path, $"other-{index}"))),
            ]);

        var (counted, read) = await CountedAndReadAsync(site, month, window, posts);

        counted.Should().Equal(posts.Select(path => new PathViews(path, 1)));
        read.Should().Equal(counted);
    }

    /// <summary>
    /// A post a sitemap lists only after a month was counted is counted into that month once what was
    /// kept is an hour old, while the month's reports are still held — so a post found late is not left
    /// out of the months it was read in.
    /// </summary>
    [Fact]
    public async Task An_Older_Post_Found_Later_Is_Counted_For_The_Months_Still_Held()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var month = ZoneCalendar.FirstOf(calendar.DateOf(now)).AddMonths(-1);
        var then = calendar.MidnightOn(month.AddDays(3)).AddHours(10);
        var reader = $"reader-{site.Id:n}";
        var listedAt = SitemapSeed.Now(stack);

        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, then, "/posts/new/", reader),
                KeptRecord.Page(site.Id, then.AddMinutes(1), "/posts/older/", reader),
                KeptRecord.Page(site.Id, then.AddMinutes(2), "/posts/older/", reader),
            ]);
        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(site.Id, "/posts/new/", listedAt));

        // What an engine kept of the month two hours ago, when the sitemap listed one post.
        await KeptPosts.KeepAsync(
            stack,
            KeptPosts.Counted(
                site,
                calendar,
                month,
                PostFingerprint.Of(["/posts/new/"]),
                now.AddHours(-2),
                new PostMonthViews("/posts/new/", 1, 0)));

        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(site.Id, "/posts/older/", listedAt));
        await KeptRecord.CountAsync(stack, site);

        var kept = (await KeptPosts.RowsAsync(stack, site.Id)).Should().ContainSingle().Subject;

        KeptPosts.ViewsIn(kept, "page_views").Should().Equal(
            new Dictionary<string, long>(StringComparer.Ordinal) { ["/posts/new/"] = 1, ["/posts/older/"] = 2 });
        kept["posts_fingerprint"].Should().Be(PostFingerprint.Of(["/posts/new/", "/posts/older/"]));
    }

    /// <summary>
    /// Last month on a website in Kolkata, listing three posts: a reader judged a person read the first
    /// twice, the second once and a page that is not a post; a scraper took the first three times and an
    /// archive page; nobody read the third.
    /// </summary>
    private async Task<(Site Site, ZoneCalendar Calendar, DateOnly Month)> LastMonthsPostsReadAndScrapedAsync()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var month = ZoneCalendar.FirstOf(calendar.DateOf(now)).AddMonths(-1);
        var reader = $"reader-{site.Id:n}";
        var scraper = $"scraper-{site.Id:n}";
        var morning = calendar.MidnightOn(month.AddDays(2)).AddHours(10);
        var afternoon = calendar.MidnightOn(month.AddDays(2)).AddHours(15);
        var listedAt = SitemapSeed.Now(stack);

        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, morning, "/posts/first/", reader),
                KeptRecord.Page(site.Id, morning.AddMinutes(1), "/posts/second/", reader),
                KeptRecord.Page(site.Id, morning.AddMinutes(2), "/posts/first/", reader),
                KeptRecord.Page(site.Id, morning.AddMinutes(3), "/about", reader),
                .. Enumerable.Range(0, 3).Select(page =>
                    KeptRecord.Page(site.Id, afternoon.AddSeconds(page), "/posts/first/", scraper)),
                KeptRecord.Page(site.Id, afternoon.AddSeconds(3), "/archive/1", scraper),
            ]);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, morning, morning.AddMinutes(3), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, site.Id, scraper, afternoon, afternoon.AddSeconds(3), TrafficCategory.ContentScraper);
        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(site.Id, "/posts/first/", listedAt),
            KeptPosts.Post(site.Id, "/posts/second/", listedAt),
            KeptPosts.Post(site.Id, "/posts/unread/", listedAt));

        return (site, calendar, month);
    }

    /// <summary>A website, the month a report written now is still held in on its calendar, and that month's window.</summary>
    private async Task<(Site Site, DateOnly Month, TimeRange Window)> ThisMonthAsync()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var calendar = new ZoneCalendar(site.TimeZoneId);
        var month = ZoneCalendar.FirstOf(calendar.DateOf(StillHeld.Midnight));

        return (site, month, new TimeRange(calendar.FirstInstantOn(month), calendar.FirstInstantOn(month.AddMonths(1))));
    }

    /// <summary>
    /// A month's posts as they are counted for keeping, and as the dashboard reads them from the reports.
    /// </summary>
    private async Task<(ImmutableArray<PathViews> Counted, ImmutableArray<PathViews> Read)> CountedAndReadAsync(
        Site site,
        DateOnly month,
        TimeRange window,
        ImmutableArray<string> posts)
    {
        var counted = await stack.Services.GetRequiredService<IMonthConsolidation>().CountPostsAsync(
            new MonthToCount(site.Id, site.TimeZoneId, month, window),
            Population.Everybody,
            posts,
            Cancellation.Token);
        var read = await stack.Services.GetRequiredService<ITelemetryQueries>().GetPostViewsAsync(
            new TenantScope(site.Id, site.OrganizationId, SiteRole.Viewer, site.TimeZoneId, site.CreatedAt),
            new PostViewsQuery(window, posts),
            Cancellation.Token);

        return (counted, read);
    }

    private static string Span(ZoneCalendar calendar, DateOnly month) =>
        $"from={KeptRecord.Moment(calendar.MidnightOn(month))}&to={KeptRecord.Moment(calendar.MidnightOn(month.AddMonths(1)))}";

    private static string Domain() => $"posts-{Guid.NewGuid():n}.example";
}
