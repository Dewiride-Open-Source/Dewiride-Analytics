using System.Collections.Immutable;
using System.Globalization;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that a month of a website's busiest pages is kept as the dashboard's list of pages for that
/// month, and that what it leaves out is what the bound leaves out and nothing else.
/// </summary>
/// <remarks>
/// A month of pages outlives its reports as a month's totals do, so it is checked here while they are
/// still held: each kept path's views against its row on the list of pages for the same month, read
/// through the dashboard's own address, and what was left out against everything the list holds besides.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class PageMonthConsolidationTests(AnalyticsStackFixture stack)
{
    private const string Kolkata = "Asia/Kolkata";

    /// <summary>
    /// Each kept path's views are its row on the list of pages for the month, for everybody and for the
    /// people alone, and the pages the month left out are every other page the list holds: a page only one
    /// visitor opened among them.
    /// </summary>
    /// <param name="peopleOnly">Whether the people's figures are the ones compared.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Pages_Month_Is_The_Pages_List_Of_That_Month(bool peopleOnly)
    {
        var (site, calendar, month) = await LastMonthReadAndScrapedAsync();

        await KeptRecord.CountAsync(stack, site);

        var kept = (await KeptPageMonths.RowsAsync(stack, site.Id)).Should().ContainSingle().Subject;
        var views = KeptPosts.ViewsIn(kept, peopleOnly ? "people_page_views" : "page_views");
        var leftOut = KeptPageMonths.LeftOutIn(kept);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var pages = await KeptRecord.GetAsync<PagesResponse>(
            browser,
            $"/api/sites/{site.Id}/pages?{Span(calendar, month)}&limit=100{(peopleOnly ? "&only=people" : string.Empty)}");

        ((string[])kept["paths"]!).Should().Equal("/about", "/posts/first/");
        Convert.ToInt32(kept["counting_rules"], CultureInfo.InvariantCulture).Should().Be(CountingRules.Pages);
        views.Should().Equal(peopleOnly
            ? new Dictionary<string, long>(StringComparer.Ordinal) { ["/about"] = 1, ["/posts/first/"] = 2 }
            : new Dictionary<string, long>(StringComparer.Ordinal) { ["/about"] = 2, ["/posts/first/"] = 5 });
        leftOut.Should().Be(new PagesLeftOut(1, 1, 0));

        foreach (var (path, counted) in views)
        {
            pages.Pages.Should().ContainSingle(page => page.Path == path).Which.PageViews.Should().Be(counted);
        }

        (peopleOnly ? leftOut.PeoplePageViews : leftOut.PageViews).Should().Be(pages.PageViews - views.Values.Sum());
    }

    /// <summary>
    /// Automation that asks for more addresses than a month keeps fills the everybody list, ties broken by
    /// the path's bytes, and the pages people read are kept regardless from the list of what people read;
    /// the two busiest-for-nobody paths that fall off the end are counted only as what they add up to.
    /// </summary>
    [Fact]
    public async Task The_Busiest_Pages_For_Everybody_And_For_People_Are_Kept_And_The_Rest_Counted_As_Left_Out()
    {
        var (site, month, window) = await ThisMonthAsync();
        var flooded = KeptPages.MostPerPopulation + 2;
        string[] bots = [.. Enumerable.Range(0, flooded).Select(index => $"/bot/{index:0000}/")];
        string[] read = ["/read/1/", "/read/2/", "/read/3/"];
        var reading = window.From.AddDays(1);

        await KeptRecord.WriteAsync(
            stack,
            [
                .. bots.SelectMany((path, index) => new[]
                {
                    KeptRecord.Page(site.Id, window.From.AddSeconds(index), path, "bot-a"),
                    KeptRecord.Page(site.Id, window.From.AddSeconds(index).AddMilliseconds(1), path, "bot-a"),
                    KeptRecord.Page(site.Id, window.From.AddSeconds(index).AddMilliseconds(2), path, "bot-b"),
                }),
                .. read.SelectMany((path, index) => new[]
                {
                    KeptRecord.Page(site.Id, reading.AddMinutes(index), path, "person-a"),
                    KeptRecord.Page(site.Id, reading.AddMinutes(index).AddSeconds(1), path, "person-b"),
                }),
            ]);
        await KeptRecord.JudgedAsync(stack, site.Id, "person-a", reading, reading.AddMinutes(2), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(
            stack,
            site.Id,
            "person-b",
            reading.AddSeconds(1),
            reading.AddMinutes(2).AddSeconds(1),
            TrafficCategory.LikelyHuman);

        var counted = await CountAsync(site, month, window);

        counted.Kept.Should().HaveCount(KeptPages.MostPerPopulation + read.Length);
        counted.Kept.Select(view => view.Path).Should().NotContain(["/bot/5000/", "/bot/5001/"]);
        counted.Kept.Should().Contain(read.Select(path => new PathMonthViews(path, 2, 2)));
        counted.Kept.Should().Contain(new PathMonthViews("/bot/4999/", 3, 0));
        counted.Kept.Select(view => view.Path).Should().BeInAscendingOrder(StringComparer.Ordinal);
        counted.LeftOut.Should().Be(new PagesLeftOut(2, 6, 0));
    }

    /// <summary>
    /// A path longer than any a month keeps — one written in four hundred and ten letters the collector stores
    /// escaped — is counted only among what was left out however many visitors read it, while a path exactly
    /// as long as the bound is kept.
    /// </summary>
    [Fact]
    public async Task A_Path_Longer_Than_Any_Kept_Is_Counted_As_Left_Out()
    {
        var (site, month, window) = await ThisMonthAsync();
        var longest = $"/{new string('a', KeptPages.LongestPath - 1)}";
        var tooLong = PagePath.Of(new Uri($"https://example.com/{new string('é', 410)}/"));

        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, window.From.AddHours(1), longest, "reader-a"),
                KeptRecord.Page(site.Id, window.From.AddHours(2), longest, "reader-b"),
                KeptRecord.Page(site.Id, window.From.AddHours(3), tooLong, "reader-a"),
                KeptRecord.Page(site.Id, window.From.AddHours(4), tooLong, "reader-b"),
            ]);

        var counted = await CountAsync(site, month, window);

        longest.Should().HaveLength(KeptPages.LongestPath);
        tooLong.Length.Should().BeGreaterThan(KeptPages.LongestPath);
        counted.Kept.Should().Equal(new PathMonthViews(longest, 2, 0));
        counted.LeftOut.Should().Be(new PagesLeftOut(1, 2, 0));
    }

    /// <summary>
    /// A path only one visitor was delivered a page at — however often, and whatever activity without a key
    /// added to it — is counted only among what was left out, and a path two visitors were delivered a page
    /// at is kept.
    /// </summary>
    [Fact]
    public async Task A_Path_One_Visitor_Opened_Is_Left_Out_And_One_Two_Visitors_Opened_Is_Kept()
    {
        var (site, month, window) = await ThisMonthAsync();
        var unkeyed = KeptRecord.Page(site.Id, window.From.AddHours(5), "/feed/", string.Empty) with
        {
            Surface = IngestSurface.CloudflareWorker,
        };

        await KeptRecord.WriteAsync(
            stack,
            [
                .. Enumerable.Range(0, 3).Select(view =>
                    KeptRecord.Page(site.Id, window.From.AddHours(1).AddMinutes(view), "/search/my-own-words/", "searcher")),
                KeptRecord.Page(site.Id, window.From.AddHours(2), "/pricing/", "reader-a"),
                KeptRecord.Page(site.Id, window.From.AddHours(3), "/pricing/", "reader-b"),
                KeptRecord.Page(site.Id, window.From.AddHours(4), "/feed/", "reader-a"),
                unkeyed,
                unkeyed with { EventId = Guid.CreateVersion7(window.From.AddHours(6)), ServerTimestamp = window.From.AddHours(6) },
            ]);

        var counted = await CountAsync(site, month, window);

        counted.Kept.Should().Equal(new PathMonthViews("/pricing/", 2, 0));
        counted.LeftOut.Should().Be(new PagesLeftOut(2, 6, 0));
    }

    /// <summary>
    /// Every page is counted at its path exactly as the collector stored it, however it is spelled — a tilde,
    /// quotes, a placeholder's own spelling, text written to end a statement, escapes that are not text, and
    /// as many characters as a month keeps — for keeping and for the dashboard alike.
    /// </summary>
    [Fact]
    public async Task Every_Page_Is_Counted_However_Awkward_Its_Path()
    {
        var (site, month, window) = await ThisMonthAsync();
        string[] paths =
        [
            "/~editor/notes/",
            "/posts/it's-here/",
            "/posts/\"quoted\"/",
            "/caf%C3%A9/",
            "/100%25-of-it's-%22x%22/",
            "/bad%FF/",
            "/odd%C3(/",
            "/{site_id:UUID}/",
            "/'); DROP TABLE events; --/",
            $"/{new string('x', KeptPages.LongestPath - 1)}",
        ];
        ImmutableArray<string> ordered = [.. paths.Order(StringComparer.Ordinal)];

        await KeptRecord.WriteAsync(
            stack,
            [
                .. paths.SelectMany((path, index) => new[]
                {
                    KeptRecord.Page(site.Id, window.From.AddMinutes(index), path, "reader-a"),
                    KeptRecord.Page(site.Id, window.From.AddMinutes(index).AddSeconds(1), path, "reader-b"),
                }),
            ]);

        var counted = await CountAsync(site, month, window);
        var read = await stack.Services.GetRequiredService<ITelemetryQueries>().GetSitePagesAsync(
            new TenantScope(site.Id, site.OrganizationId, SiteRole.Viewer, site.TimeZoneId, site.CreatedAt),
            new SitePagesQuery(window, SitePagesQuery.MostPages),
            Cancellation.Token);

        counted.Kept.Should().Equal(ordered.Select(path => new PathMonthViews(path, 2, 0)));
        read.Pages.Select(page => (page.Path, page.PageViews)).Should()
            .BeEquivalentTo(counted.Kept.Select(view => (view.Path, view.PageViews)));
        counted.LeftOut.Should().Be(new PagesLeftOut(0, 0, 0));
    }

    /// <summary>
    /// Last month on a website in Kolkata: a reader judged a person read the first post twice and the about
    /// page once; a scraper took the first post three times and an archive page nobody else opened; and a
    /// passer-by nothing has judged opened the about page.
    /// </summary>
    private async Task<(Site Site, ZoneCalendar Calendar, DateOnly Month)> LastMonthReadAndScrapedAsync()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var month = ZoneCalendar.FirstOf(calendar.DateOf(now)).AddMonths(-1);
        var reader = $"reader-{site.Id:n}";
        var scraper = $"scraper-{site.Id:n}";
        var passer = $"passer-{site.Id:n}";
        var morning = calendar.MidnightOn(month.AddDays(2)).AddHours(10);
        var afternoon = calendar.MidnightOn(month.AddDays(2)).AddHours(15);
        var evening = calendar.MidnightOn(month.AddDays(2)).AddHours(20);

        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, morning, "/posts/first/", reader),
                KeptRecord.Page(site.Id, morning.AddMinutes(1), "/posts/first/", reader),
                KeptRecord.Page(site.Id, morning.AddMinutes(2), "/about", reader),
                .. Enumerable.Range(0, 3).Select(page =>
                    KeptRecord.Page(site.Id, afternoon.AddSeconds(page), "/posts/first/", scraper)),
                KeptRecord.Page(site.Id, afternoon.AddSeconds(3), "/archive/1", scraper),
                KeptRecord.Page(site.Id, evening, "/about", passer),
            ]);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, morning, morning.AddMinutes(2), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, site.Id, scraper, afternoon, afternoon.AddSeconds(3), TrafficCategory.ContentScraper);

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

    /// <summary>A month's pages as they are counted for keeping.</summary>
    private Task<CountedPages> CountAsync(Site site, DateOnly month, TimeRange window) =>
        stack.Services.GetRequiredService<IMonthConsolidation>().CountPagesAsync(
            new MonthToCount(site.Id, site.TimeZoneId, month, window),
            Cancellation.Token);

    private static string Span(ZoneCalendar calendar, DateOnly month) =>
        $"from={KeptRecord.Moment(calendar.MidnightOn(month))}&to={KeptRecord.Moment(calendar.MidnightOn(month.AddMonths(1)))}";

    private static string Domain() => $"pages-{Guid.NewGuid():n}.example";
}
