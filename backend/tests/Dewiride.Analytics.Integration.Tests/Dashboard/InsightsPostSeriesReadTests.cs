using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves what a reader is shown on one post's chart: its views bucket by bucket, counted as the website's
/// own series counts the website's and asked for, refused and answered on the same terms — and that a path
/// naming no post of the website, or a post of a website the reader has no role on, is answered exactly as a
/// website that does not exist is.
/// </summary>
/// <remarks>
/// The posts are written as a reading of the sitemap leaves them, and the views as reports placed relative to
/// the running clock, so nothing written here is older than a report is kept.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class InsightsPostSeriesReadTests(AnalyticsStackFixture stack)
{
    private const string Kolkata = "Asia/Kolkata";

    /// <summary>The post asked about.</summary>
    private const string Hello = "/posts/hello/";

    /// <summary>The path the post's page landed at.</summary>
    private const string HelloAgain = "/posts/hello-again/";

    /// <summary>
    /// Paths that only look like the post's or where it landed: without the slash, in another case, and
    /// beneath it.
    /// </summary>
    private static readonly string[] LookAlikes = ["/posts/hello", "/POSTS/HELLO/", "/posts/hello/comments/", "/posts/hello-again"];

    /// <summary>
    /// A post's series is cut into the buckets the website's own series is cut into, and counts in each the
    /// pages delivered at the post's own path and where its page landed. Pages at paths that only look like
    /// the post's are counted for the website, and for none of the post's buckets.
    /// </summary>
    /// <param name="granularity">How wide each bucket is.</param>
    [Theory]
    [InlineData("hour")]
    [InlineData("day")]
    [InlineData("week")]
    [InlineData("month")]
    public async Task A_Posts_Series_Counts_Its_Views_Bucket_By_Bucket(string granularity)
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var lately = Lately(calendar, ZoneCalendar.FirstOf(calendar.DateOf(now)), now);
        var before = InTheBucketBefore(calendar, lately, granularity);
        var reader = $"reader-{site.Id:n}";

        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(site.Id, Hello, SitemapSeed.Now(stack), landed: HelloAgain));
        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, before, Hello, reader),
                KeptRecord.Page(site.Id, lately, Hello, reader),
                KeptRecord.Page(site.Id, lately, HelloAgain, $"another-{site.Id:n}"),
                .. LookAlikes.Select(path => KeptRecord.Page(site.Id, lately, path, $"look-alike-{site.Id:n}")),
            ]);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var window = Window(granularity, calendar.MidnightOn(calendar.DateOf(before)), KeptRecord.Now(stack));
        var post = await KeptRecord.GetAsync<SeriesResponse>(browser, PostSeries(site.Id, Hello, window));
        var website = await KeptRecord.GetAsync<SeriesResponse>(browser, $"/api/sites/{site.Id}/series?metric=pageviews&{window}");

        post.Granularity.Should().Be(granularity);
        post.Points.Select(point => point.BucketStart).Should().Equal(website.Points.Select(point => point.BucketStart));
        Figure(post, before).Should().Be(1);
        Figure(post, lately).Should().Be(2);
        post.Points.Sum(point => point.Value).Should().Be(3);
        Figure(website, lately).Should().Be(2 + LookAlikes.Length);
    }

    /// <summary>
    /// Kept to people, a post's series counts the reader and leaves the scraper out; and it says it is
    /// complete only as far as judging has reached, where counted for everybody it is complete to its end.
    /// </summary>
    [Fact]
    public async Task A_Posts_Series_Of_People_Counts_The_Reader_Alone_And_Says_How_Far_Judging_Has_Reached()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var lately = Lately(calendar, ZoneCalendar.FirstOf(calendar.DateOf(now)), now);
        var judgedTo = calendar.MidnightOn(calendar.DateOf(now).AddDays(-1));
        var reader = $"reader-{site.Id:n}";
        var scraper = $"scraper-{site.Id:n}";

        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(site.Id, Hello, SitemapSeed.Now(stack)));
        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, lately, Hello, reader),
                .. Enumerable.Range(0, 3).Select(page =>
                    KeptRecord.Page(site.Id, lately.AddSeconds(10 + page), Hello, scraper)),
            ]);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, lately, lately, TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, site.Id, scraper, lately.AddSeconds(10), lately.AddSeconds(12), TrafficCategory.ContentScraper);
        await JudgingReachedAsync(site.Id, judgedTo);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var everybody = await KeptRecord.GetAsync<SeriesResponse>(browser, PostSeries(site.Id, Hello, "granularity=day"));
        var people = await KeptRecord.GetAsync<SeriesResponse>(browser, PostSeries(site.Id, Hello, "granularity=day&only=people"));

        everybody.Points.Sum(point => point.Value).Should().Be(4);
        everybody.CompleteTo.Should().Be(everybody.To);
        people.Points.Sum(point => point.Value).Should().Be(1);
        people.CompleteTo.Should().Be(judgedTo);
    }

    /// <summary>
    /// A post's series refuses what the website's own series refuses — buckets it does not cut, a window
    /// longer than it covers in the buckets asked for, one that ends before it starts, and anybody but
    /// everybody or people — with the same problem, word for word, and before the website is looked for:
    /// asked about a website that does not exist, the refusal is the same.
    /// </summary>
    /// <param name="question">What follows the post's path, and the measure on the website's series.</param>
    [Theory]
    [InlineData("granularity=year")]
    [InlineData("granularity=hour&from=2026-01-01T00:00:00Z&to=2026-02-02T00:00:00Z")]
    [InlineData("granularity=day&from=2025-01-01T00:00:00Z&to=2026-02-06T00:00:00Z")]
    [InlineData("granularity=day&from=2026-01-02T00:00:00Z&to=2026-01-01T00:00:00Z")]
    [InlineData("granularity=day&only=robots")]
    public async Task A_Posts_Series_Refuses_What_The_Websites_Series_Refuses_Before_The_Website_Is_Looked_For(string question)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(site.Id, Hello, SitemapSeed.Now(stack)));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var post = await Refusal.WholeAsync(await browser.GetAsync(PostSeries(site.Id, Hello, question)), Hello);
        var nowhere = await Refusal.WholeAsync(await browser.GetAsync(PostSeries(Guid.NewGuid(), Hello, question)), Hello);
        var website = await Refusal.WholeAsync(
            await browser.GetAsync($"/api/sites/{site.Id}/series?metric=pageviews&{question}"),
            Hello);

        post.Status.Should().Be(HttpStatusCode.BadRequest);
        post.Should().Be(website);
        nowhere.Should().Be(website);
    }

    /// <summary>
    /// A question naming no post is refused before anything else it asks is looked at, and before the
    /// website is looked for: the website here does not exist, and the refusal comes back regardless.
    /// </summary>
    /// <param name="question">What follows the address.</param>
    [Theory]
    [InlineData("")]
    [InlineData("?granularity=day")]
    [InlineData("?path=&granularity=day")]
    [InlineData("?granularity=year&only=robots")]
    public async Task A_Posts_Series_Refuses_A_Missing_Path_Before_The_Website_Is_Looked_For(string question)
    {
        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer);
        var response = await browser.GetAsync($"/api/sites/{Guid.NewGuid()}/insights/post/series{question}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var refusal = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation.Token));

        refusal.RootElement.GetProperty("detail").GetString().Should().Be("Ask about a post by the path Insights lists it at.");
    }

    /// <summary>
    /// A path that names no post of the website — never listed, a listed page that is not a post, a post no
    /// longer listed, a listed post's path in another case, and one longer than any a post may be listed at —
    /// and a listed post of a website the caller has no role on are answered exactly as a website that does
    /// not exist is, repeating nothing that was asked; and they are answered from the control plane alone:
    /// here the telemetry store refuses every question, and nothing asks it one.
    /// </summary>
    [Fact]
    public async Task A_Posts_Series_Of_A_Path_That_Is_No_Listed_Post_Or_Of_A_Website_Somebody_Cannot_See_Is_Not_Found()
    {
        var listedAt = SitemapSeed.Now(stack);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var refusing = DispatchProxy.Create<ITelemetryQueries, RefusingTelemetry>();
        string[] unlisted =
        [
            "/posts/never-listed/",
            "/about/",
            "/posts/gone/",
            "/Posts/Hello/",
            $"/{new string('a', Site.MaxSitemapAddressLength)}",
        ];

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(site.Id, Hello, listedAt),
            KeptPosts.NotAPost(site.Id, "/about/", listedAt),
            KeptPosts.Gone(site.Id, "/posts/gone/", listedAt),
            KeptPosts.Post(theirs.Id, Hello, listedAt));

        using var host = stack.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(refusing)));
        using var browser = await SignedIn.AsAsync(stack, host, SiteRole.Viewer, site.Id);
        var invented = await RefusalAsync(browser, Guid.NewGuid(), Hello);

        foreach (var path in unlisted)
        {
            (await RefusalAsync(browser, site.Id, path)).Should().Be(invented);
        }

        (await RefusalAsync(browser, theirs.Id, Hello)).Should().Be(invented);
        invented.Status.Should().Be(HttpStatusCode.NotFound);
        ((RefusingTelemetry)refusing).Asked.Should().Be(0);
    }

    /// <summary>
    /// A post's series by the month, over the month running now, is the figure its page gives that month, for
    /// everybody and for people alike — the one counted by the statement Insights counts every post by, the
    /// other by the series.
    /// </summary>
    /// <param name="only">Who the answer is about: nothing for everybody, or <c>people</c>.</param>
    [Theory]
    [InlineData("")]
    [InlineData("people")]
    public async Task A_Posts_Series_Of_This_Month_Is_The_Month_Its_Page_Shows(string only)
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var running = ZoneCalendar.FirstOf(calendar.DateOf(now));
        var lately = Lately(calendar, running, now);
        var reader = $"reader-{site.Id:n}";
        var scraper = $"scraper-{site.Id:n}";
        var population = only.Length == 0 ? string.Empty : $"&only={only}";

        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(site.Id, Hello, SitemapSeed.Now(stack), landed: HelloAgain));
        await KeptRecord.WriteAsync(
            stack,
            [
                KeptRecord.Page(site.Id, lately, Hello, reader),
                KeptRecord.Page(site.Id, lately.AddSeconds(1), HelloAgain, reader),
                .. Enumerable.Range(0, 3).Select(page =>
                    KeptRecord.Page(site.Id, lately.AddSeconds(10 + page), Hello, scraper)),
            ]);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, lately, lately.AddSeconds(1), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, site.Id, scraper, lately.AddSeconds(10), lately.AddSeconds(12), TrafficCategory.ContentScraper);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var page = await KeptRecord.GetAsync<InsightsPostResponse>(
            browser,
            $"/api/sites/{site.Id}/insights/post?path={Uri.EscapeDataString(Hello)}{population}");
        var series = await KeptRecord.GetAsync<SeriesResponse>(
            browser,
            PostSeries(site.Id, Hello, $"{Window("month", calendar.FirstInstantOn(running), page.To)}{population}"));

        page.Months[^1].Month.Should().Be(running.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        series.Points.Should().ContainSingle().Which.Value.Should().Be(page.Months[^1].PageViews);
        series.Points[0].Value.Should().Be(only.Length == 0 ? 5 : 2);
    }

    /// <summary>
    /// The names on the wire are those the website's own series is written under, at the top and in every
    /// bucket, so the dashboard reads a post's chart with the check it reads the website's with.
    /// </summary>
    [Fact]
    public async Task A_Posts_Series_Is_Written_Under_The_Names_The_Websites_Series_Is()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(site.Id, Hello, SitemapSeed.Now(stack)));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        using var post = await DocumentAsync(browser, PostSeries(site.Id, Hello, "granularity=day"));
        using var website = await DocumentAsync(browser, $"/api/sites/{site.Id}/series?metric=pageviews&granularity=day");

        Names(post.RootElement).Should().Equal(Names(website.RootElement));
        Names(post.RootElement.GetProperty("points")[0]).Should().Equal(Names(website.RootElement.GetProperty("points")[0]));
        post.RootElement.GetProperty("metric").GetString().Should().Be("pageviews");
        post.RootElement.GetProperty("granularity").GetString().Should().Be("day");
    }

    /// <summary>Starts the website's bookmark where judging under the rules running now has reached.</summary>
    private async Task JudgingReachedAsync(Guid siteId, DateTimeOffset reached)
    {
        await using var scope = stack.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IClassificationProgressStore>()
            .ResumeFromAsync(siteId, RulesetVersion.Current, reached, Cancellation.Token);
    }

    /// <summary>Everything the refusal of a post's series says, and whether it repeats the path asked about.</summary>
    private static async Task<Refusal.Said> RefusalAsync(Browser browser, Guid siteId, string path) =>
        await Refusal.WholeAsync(await browser.GetAsync(PostSeries(siteId, path, "granularity=day")), path);

    /// <summary>Reads an answer the dashboard asks for whole, and insists it was answered.</summary>
    private static async Task<JsonDocument> DocumentAsync(Browser browser, string address)
    {
        var response = await browser.GetAsync(address);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation.Token));
    }

    /// <summary>The address a post's series is asked for at, with its path escaped as the dashboard escapes it.</summary>
    private static string PostSeries(Guid siteId, string path, string question) =>
        $"/api/sites/{siteId}/insights/post/series?path={Uri.EscapeDataString(path)}&{question}";

    /// <summary>A series' buckets and the window it covers, as an address asks for them.</summary>
    private static string Window(string granularity, DateTimeOffset from, DateTimeOffset to) =>
        $"granularity={granularity}&from={KeptRecord.Moment(from)}&to={KeptRecord.Moment(to)}";

    /// <summary>A moment in the bucket before the one an instant falls in, where the website is.</summary>
    private static DateTimeOffset InTheBucketBefore(ZoneCalendar calendar, DateTimeOffset at, string granularity) =>
        granularity switch
        {
            "hour" => at.AddHours(-1),
            "day" => at.AddDays(-1),
            "week" => at.AddDays(-7),
            "month" => calendar.MidnightOn(ZoneCalendar.FirstOf(calendar.DateOf(at)).AddMonths(-1)).AddHours(12),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Not a bucket a series is cut into."),
        };

    /// <summary>What a series holds in the bucket an instant falls in.</summary>
    private static long Figure(SeriesResponse series, DateTimeOffset at) =>
        series.Points.Last(point => point.BucketStart <= at).Value;

    /// <summary>A moment this month, no more than forty minutes ago.</summary>
    private static DateTimeOffset Lately(ZoneCalendar calendar, DateOnly running, DateTimeOffset now) =>
        calendar.MidnightOn(running) > now.AddMinutes(-40) ? calendar.MidnightOn(running) : now.AddMinutes(-40);

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private static string Domain() => $"insights-post-series-{Guid.NewGuid():n}.example";
}
