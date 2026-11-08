using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves what a reader is shown on the list of every post: each post a website's sitemap lists, with the
/// views Insights gives it, searched for by its title or its address, in the order asked for and a page at
/// a time — and that a question that cannot be answered as asked is refused before the website is looked
/// for.
/// </summary>
/// <remarks>
/// The posts are written as a reading of the sitemap leaves them; the views come from reports. A search is
/// written by whoever asks, so it is proved to stand only for itself, to change nothing, and never to be
/// repeated in a refusal.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SitePostsReadTests(AnalyticsStackFixture stack)
{
    private const string Kolkata = "Asia/Kolkata";

    private const string HelloWorld = "/posts/hello-world/";
    private const string OldHome = "/posts/old-home/";
    private const string Older = "/posts/older/";
    private const string Cafe = "/caf%C3%A9/";
    private const string Undated = "/posts/undated/";
    private const string Quiet = "/posts/quiet/";

    private const string UnknownOrder = "List the posts by the most read or by the newest.";
    private const string ImpossiblePage = "Ask for between 1 and 100 posts at a time.";
    private const string BeforeTheStart = "Start the list at the beginning or further along it, never before it.";
    private const string UnknownPopulation = "Ask about everybody, or only about people.";
    private const string SearchTooLong = "Search for at most 100 characters.";

    /// <summary>A search one character longer than a search may be, of text written nowhere else.</summary>
    private static readonly string TooLong = "refused-" + new string('x', 93);

    /// <summary>
    /// Searches holding what a pattern gives a meaning to, or what another database does, each with the one
    /// post it finds: wildcards, the escape character, quotes and brackets stand for themselves.
    /// </summary>
    public static TheoryData<string, string> Literal => new()
    {
        { "100%", "/posts/percent/" },
        { "%", "/posts/percent/" },
        { "snake_case", "/posts/underscore/" },
        { "_", "/posts/underscore/" },
        { @"C:\temp", "/posts/backslash/" },
        { @"\", "/posts/backslash/" },
        { "It's \"quoted\"", "/posts/quotes/" },
        { "'", "/posts/quotes/" },
        { "\"", "/posts/quotes/" },
        { "[draft]", "/posts/brackets/" },
    };

    /// <summary>
    /// Searches written as statements, as a wildcard meant to widen one, as placeholders, and as bytes that
    /// are not text or are a character no text value can hold — each as it travels in an address.
    /// </summary>
    public static TheoryData<string> ReadAsSql =>
    [
        Uri.EscapeDataString("'; DROP TABLE site_posts; --"),
        Uri.EscapeDataString("' OR '1'='1"),
        Uri.EscapeDataString("%' OR title ILIKE '%"),
        Uri.EscapeDataString(@"\' ESCAPE '' OR TRUE --"),
        Uri.EscapeDataString("') UNION SELECT path FROM site_posts --"),
        Uri.EscapeDataString("$1 @p0 {search:String}"),
        "%FF",
        "%ED%A0%80",
        "no%00such%00post",
    ];

    /// <summary>
    /// Questions that cannot be answered as asked, each with what its refusal says and something it asked
    /// that the refusal must not repeat: alone, and together in the order they are checked in.
    /// </summary>
    public static TheoryData<string, string, string> CannotBeAnsweredAsAsked => new()
    {
        { "order=oldest", UnknownOrder, "oldest" },
        { "limit=0", ImpossiblePage, "limit=0" },
        { "limit=101", ImpossiblePage, "limit=101" },
        { "limit=-1", ImpossiblePage, "limit=-1" },
        { "offset=-1", BeforeTheStart, "offset=-1" },
        { "only=robots", UnknownPopulation, "robots" },
        { $"search={TooLong}", SearchTooLong, TooLong },
        { $"order=oldest&limit=0&search={TooLong}&only=robots", UnknownOrder, TooLong },
        { $"limit=0&search={TooLong}&only=robots", ImpossiblePage, TooLong },
        { $"search={TooLong}&only=robots", SearchTooLong, TooLong },
    };

    /// <summary>
    /// Every post is listed, each with the very figure Insights gives it — its title, address, day and
    /// views, a post moved to a new path read there — for everybody and for people alike, the most read
    /// first as Insights picks it, and a post that does not say when it was published as well.
    /// </summary>
    /// <param name="only">Who the answer is about: nothing for everybody, or <c>people</c>.</param>
    [Theory]
    [InlineData("")]
    [InlineData("people")]
    public async Task Every_Post_Is_Listed_With_The_Views_Insights_Gives_It(string only)
    {
        var site = await ListedAsync();
        var question = only.Length == 0 ? string.Empty : $"?only={only}";

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var insights = await KeptRecord.GetAsync<InsightsPostsResponse>(
            browser,
            $"/api/sites/{site.Id}/insights/posts{question}");
        var page = await AskAsync(browser, site.Id, question);
        var listed = page.Shown.ToDictionary(post => post.Path, StringComparer.Ordinal);

        page.Posts.Should().Be(insights.Posts);
        page.Matching.Should().Be(6);
        page.Shown.Should().HaveCount(6);
        page.ViewsFrom.Should().NotBeNull();
        page.ViewsFrom.Should().Be(insights.ViewsFrom);
        page.Shown[0].Should().Be(insights.MostRead);
        insights.Latest.Should().HaveCount(5);
        insights.Latest.Should().AllSatisfy(post => listed[post.Path].Should().Be(post));
        listed[Undated].Should().Be(
            new InsightsPost(Undated, $"https://example.com{Undated}", null, null, only.Length == 0 ? 2 : 0));
    }

    /// <summary>
    /// The most read come first, two read as much the one published first and any with a date before any
    /// without, whatever case the order is asked in; the newest first where they are asked for, with the
    /// post that does not say when it was published last.
    /// </summary>
    [Fact]
    public async Task Posts_Are_Listed_Most_Read_First_Unless_The_Newest_Are_Asked_For()
    {
        var site = await ListedAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var mostRead = await AskAsync(browser, site.Id);
        var named = await AskAsync(browser, site.Id, "?order=Most-Read");
        var newest = await AskAsync(browser, site.Id, "?order=NEWEST");

        mostRead.Shown.Select(post => (post.Path, post.PageViews)).Should().Equal(
            (HelloWorld, 4L), (OldHome, 2L), (Undated, 2L), (Older, 1L), (Cafe, 1L), (Quiet, 0L));
        mostRead.MostPageViews.Should().Be(4);
        named.Shown.Should().Equal(mostRead.Shown);
        Paths(newest).Should().Equal(HelloWorld, Cafe, Quiet, Older, OldHome, Undated);
    }

    /// <summary>Kept to people, a post's views are the reader's alone, and the order follows them.</summary>
    [Fact]
    public async Task Posts_Of_People_Count_The_Reader_Alone()
    {
        var site = await ListedAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var people = await AskAsync(browser, site.Id, "?only=people");

        people.Shown.Select(post => (post.Path, post.PageViews)).Should().Equal(
            (OldHome, 2L), (Older, 1L), (Cafe, 1L), (HelloWorld, 1L), (Quiet, 0L), (Undated, 0L));
        people.MostPageViews.Should().Be(2);
    }

    /// <summary>
    /// A search finds a post whose title holds it, one whose path does — an untitled post among them — and
    /// several at once, without regard to the case of a letter of the English alphabet, read as words
    /// however it was spaced; what it finds is counted, every post the website lists still is, and a search
    /// of nothing but space is no search.
    /// </summary>
    [Fact]
    public async Task A_Search_Finds_Posts_By_Their_Title_Or_Their_Path_Whatever_The_Case()
    {
        var site = await ListedAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var title = await AskAsync(browser, site.Id, Search("mOVED hOUSE"));
        var spaced = await AskAsync(browser, site.Id, Search("  hello \t WORLD "));
        var path = await AskAsync(browser, site.Id, Search("POSTS/OLDER"));
        var several = await AskAsync(browser, site.Id, Search("/POSTS/"));
        var blank = await AskAsync(browser, site.Id, Search("   "));

        Paths(title).Should().Equal(OldHome);
        Paths(spaced).Should().Equal(HelloWorld);
        Paths(path).Should().Equal(Older);
        Paths(several).Should().Equal(HelloWorld, OldHome, Undated, Older, Quiet);
        several.Posts.Should().Be(6);
        several.Matching.Should().Be(5);
        several.MostPageViews.Should().Be(4);
        blank.Matching.Should().Be(6);
    }

    /// <summary>
    /// What every address the website lists holds — the website's name, its scheme — finds no post for
    /// holding it, so a search for the name of a website, or of the brand it is named after, finds only the
    /// posts that say it.
    /// </summary>
    [Fact]
    public async Task A_Search_For_What_Every_Address_Holds_Finds_Only_The_Posts_That_Say_It()
    {
        var site = await ListedAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var name = await AskAsync(browser, site.Id, Search("example.com"));
        var scheme = await AskAsync(browser, site.Id, Search("https"));
        var brand = await AskAsync(browser, site.Id, Search("example"));

        name.Matching.Should().Be(0);
        scheme.Matching.Should().Be(0);
        brand.Matching.Should().Be(0);
        brand.Posts.Should().Be(6);
    }

    /// <summary>
    /// An address pasted whole finds its post by the path it names, whatever followed the path and whatever
    /// case its scheme and name were written in — a post at a path an address escapes among them, found by
    /// the letters it is written in.
    /// </summary>
    [Fact]
    public async Task An_Address_Pasted_Whole_Finds_Its_Post()
    {
        var site = await ListedAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var older = await AskAsync(
            browser,
            site.Id,
            Search("https://example.com/posts/older/?utm_source=newsletter#top"));
        var cafe = await AskAsync(browser, site.Id, Search("HTTPS://EXAMPLE.COM/café/"));

        Paths(older).Should().Equal(Older);
        Paths(cafe).Should().Equal(Cafe);
    }

    /// <summary>
    /// A search holding a wildcard, the escape character, a quote or a bracket finds the post that holds
    /// exactly that, and none of the posts it would find if any of them meant anything but itself.
    /// </summary>
    /// <param name="search">What is searched for.</param>
    /// <param name="found">The one post that holds it.</param>
    [Theory]
    [MemberData(nameof(Literal))]
    public async Task A_Search_Holding_Wildcards_A_Backslash_Or_Quotes_Matches_Them_And_Nothing_Else(
        string search,
        string found)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var listedAt = SitemapSeed.Now(stack);

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(site.Id, "/posts/percent/", listedAt, title: "100% sure"),
            KeptPosts.Post(site.Id, "/posts/thousand/", listedAt, title: "1000 sure"),
            KeptPosts.Post(site.Id, "/posts/underscore/", listedAt, title: "snake_case"),
            KeptPosts.Post(site.Id, "/posts/any-letter/", listedAt, title: "snakeXcase"),
            KeptPosts.Post(site.Id, "/posts/backslash/", listedAt, title: @"C:\temp"),
            KeptPosts.Post(site.Id, "/posts/no-backslash/", listedAt, title: "C:temp"),
            KeptPosts.Post(site.Id, "/posts/quotes/", listedAt, title: "It's \"quoted\""),
            KeptPosts.Post(site.Id, "/posts/no-quotes/", listedAt, title: "Its quoted"),
            KeptPosts.Post(site.Id, "/posts/brackets/", listedAt, title: "Notes [draft]"),
            KeptPosts.Post(site.Id, "/posts/no-brackets/", listedAt, title: "Notes draft"));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var page = await AskAsync(browser, site.Id, Search(search));

        page.Posts.Should().Be(10);
        page.Matching.Should().Be(1);
        Paths(page).Should().Equal(found);
    }

    /// <summary>
    /// A search written as a statement is compared as the text it is, finds no post, and leaves every post
    /// where it was; bytes that are not text and a character no text value can hold are answered the same
    /// way rather than failing.
    /// </summary>
    /// <param name="search">The search, as it travels in an address.</param>
    [Theory]
    [MemberData(nameof(ReadAsSql))]
    public async Task A_Search_That_Reads_As_Sql_Finds_Nothing_And_Changes_Nothing(string search)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var listedAt = SitemapSeed.Now(stack);

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(site.Id, "/posts/a/", listedAt, listedAt.AddDays(-1), title: "A post"),
            KeptPosts.Post(site.Id, "/posts/b/", listedAt, title: "Another post"));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var searched = await AskAsync(browser, site.Id, $"?search={search}");
        var afterwards = await AskAsync(browser, site.Id);

        searched.Posts.Should().Be(2);
        searched.Matching.Should().Be(0);
        searched.ViewsFrom.Should().BeNull();
        searched.MostPageViews.Should().Be(0);
        searched.Shown.Should().BeEmpty();
        afterwards.Matching.Should().Be(2);
        Paths(afterwards).Should().BeEquivalentTo("/posts/a/", "/posts/b/");
    }

    /// <summary>
    /// A page holds as many posts as were asked for, from as far along the list as was asked, in either
    /// order; a page past the end is empty and still says how many posts there are and how much the most
    /// read was read, and a page as long as a page may be is answered.
    /// </summary>
    [Fact]
    public async Task Posts_Are_Read_A_Page_At_A_Time_And_A_Page_Past_The_End_Is_Empty()
    {
        var site = await ListedAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var first = await AskAsync(browser, site.Id, "?limit=4");
        var second = await AskAsync(browser, site.Id, "?limit=4&offset=4");
        var last = await AskAsync(browser, site.Id, "?order=newest&limit=2&offset=4");
        var beyond = await AskAsync(browser, site.Id, "?limit=4&offset=6");
        var longest = await AskAsync(browser, site.Id, "?limit=100");

        Paths(first).Should().Equal(HelloWorld, OldHome, Undated, Older);
        Paths(second).Should().Equal(Cafe, Quiet);
        second.MostPageViews.Should().Be(4);
        Paths(last).Should().Equal(OldHome, Undated);
        beyond.Shown.Should().BeEmpty();
        beyond.Posts.Should().Be(6);
        beyond.Matching.Should().Be(6);
        beyond.MostPageViews.Should().Be(4);
        longest.Shown.Should().HaveCount(6);
    }

    /// <summary>
    /// A website listing more posts than one question may name — none of them read or dated, so listed by
    /// path — is listed all the same, because only the posts on a page are named: twenty-five to a page,
    /// from the first, unless more are asked for, and the last post on its own on a page as long as a page
    /// may be, a hundred posts along.
    /// </summary>
    [Fact]
    public async Task However_Many_Posts_Are_Listed_A_Page_Holds_Twenty_Five_Unless_More_Are_Asked_For()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var listedAt = SitemapSeed.Now(stack);
        string[] paths = [.. Enumerable.Range(0, 101).Select(post => $"/posts/p{post:000}/")];

        await SitemapSeed.WriteAsync(stack, [.. paths.Select(path => KeptPosts.Post(site.Id, path, listedAt))]);

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var unasked = await AskAsync(browser, site.Id);
        var last = await AskAsync(browser, site.Id, "?limit=100&offset=100");

        unasked.Posts.Should().Be(101);
        unasked.Matching.Should().Be(101);
        Paths(unasked).Should().Equal(paths[..25]);
        Paths(last).Should().Equal(paths[^1]);
    }

    /// <summary>
    /// A search no listed post holds is answered from the control plane alone — here the telemetry store
    /// refuses every question, and nothing asks it one — and a page that is not a post, or a post no longer
    /// listed, is never found by one.
    /// </summary>
    [Fact]
    public async Task A_Search_That_Matches_Nothing_Is_Answered_Without_Asking_The_Telemetry_Store()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var listedAt = SitemapSeed.Now(stack);
        var refusing = DispatchProxy.Create<ITelemetryQueries, RefusingTelemetry>();

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(site.Id, "/posts/hello/", listedAt, title: "Hello"),
            KeptPosts.NotAPost(site.Id, "/about/", listedAt),
            KeptPosts.Gone(site.Id, "/posts/gone/", listedAt));

        using var host = stack.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(refusing)));
        using var browser = await SignedIn.AsAsync(stack, host, SiteRole.Viewer, site.Id);

        foreach (var search in new[] { "nothing like it", "about", "gone" })
        {
            var page = await AskAsync(browser, site.Id, Search(search));

            page.Posts.Should().Be(1);
            page.Matching.Should().Be(0);
            page.ViewsFrom.Should().BeNull();
            page.MostPageViews.Should().Be(0);
            page.Shown.Should().BeEmpty();
        }

        ((RefusingTelemetry)refusing).Asked.Should().Be(0);
    }

    /// <summary>
    /// A website that lists no posts has nothing to list, and is answered from the control plane alone, with
    /// a search or without one: here the telemetry store refuses every question, and nothing asks it one.
    /// </summary>
    [Fact]
    public async Task A_Website_That_Lists_No_Posts_Is_Answered_Without_Asking_The_Telemetry_Store()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var listedAt = SitemapSeed.Now(stack);
        var refusing = DispatchProxy.Create<ITelemetryQueries, RefusingTelemetry>();

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.NotAPost(site.Id, "/about/", listedAt),
            KeptPosts.Gone(site.Id, "/posts/gone/", listedAt));

        using var host = stack.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(refusing)));
        using var browser = await SignedIn.AsAsync(stack, host, SiteRole.Viewer, site.Id);

        foreach (var question in new[] { string.Empty, Search("about"), "?order=newest&only=people" })
        {
            var page = await AskAsync(browser, site.Id, question);

            page.Posts.Should().Be(0);
            page.Matching.Should().Be(0);
            page.ViewsFrom.Should().BeNull();
            page.MostPageViews.Should().Be(0);
            page.Shown.Should().BeEmpty();
        }

        ((RefusingTelemetry)refusing).Asked.Should().Be(0);
    }

    /// <summary>
    /// A website somebody has no role on answers exactly as one that does not exist — the same status and the
    /// same problem — so the posts of another account's website can neither be read nor found to be there,
    /// and the answer never repeats what was searched for.
    /// </summary>
    [Fact]
    public async Task Posts_Of_A_Website_Somebody_Cannot_See_Are_Not_Found()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var marker = $"marker-{Guid.NewGuid():n}";

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(theirs.Id, $"/posts/{marker}/", SitemapSeed.Now(stack), title: marker));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, mine.Id);
        var existing = await RefusalAsync(browser, theirs.Id, Search(marker), marker);
        var invented = await RefusalAsync(browser, Guid.NewGuid(), Search(marker), marker);
        var unsearched = await RefusalAsync(browser, theirs.Id, string.Empty, marker);

        existing.Status.Should().Be(HttpStatusCode.NotFound);
        existing.Should().Be(invented);
        existing.Should().Be(unsearched);
        existing.RepeatsWhatWasAsked.Should().BeFalse();
    }

    /// <summary>
    /// A question that cannot be answered as asked is refused before the website is looked for — the same
    /// status and the same problem for a website the caller can read and for one that does not exist — with
    /// the first thing wrong with it, in the order the checks are made, and without repeating what was asked.
    /// </summary>
    /// <param name="question">What follows the address.</param>
    /// <param name="detail">What the refusal says.</param>
    /// <param name="asked">Something the question asked that the refusal must not repeat.</param>
    [Theory]
    [MemberData(nameof(CannotBeAnsweredAsAsked))]
    public async Task A_List_That_Cannot_Be_Answered_As_Asked_Is_Refused_Whether_Or_Not_The_Website_Exists(
        string question,
        string detail,
        string asked)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(site.Id, "/posts/hello/", SitemapSeed.Now(stack)));

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var real = await RefusalAsync(browser, site.Id, $"?{question}", asked);
        var invented = await RefusalAsync(browser, Guid.NewGuid(), $"?{question}", asked);

        real.Status.Should().Be(HttpStatusCode.BadRequest);
        real.Should().Be(invented);
        real.RepeatsWhatWasAsked.Should().BeFalse();
        JsonNode.Parse(real.Problem)!["detail"]!.GetValue<string>().Should().Be(detail);
    }

    [Fact]
    public async Task The_List_Is_Not_Read_By_Anybody_Signed_Out()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);

        await SitemapSeed.WriteAsync(stack, KeptPosts.Post(site.Id, "/posts/hello/", SitemapSeed.Now(stack)));

        using var browser = await Browser.OpenAsync(stack);
        var response = await browser.GetAsync($"/api/sites/{site.Id}/posts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The names on the wire are what the dashboard's own check of this answer is written against, and this
    /// is the only place that sees them as they are sent: a post with neither a title nor a date among them.
    /// </summary>
    [Fact]
    public async Task The_Answer_Is_Written_Under_The_Names_The_Dashboard_Reads()
    {
        var site = await ListedAsync();

        using var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);
        var response = await browser.GetAsync($"/api/sites/{site.Id}/posts?order=newest");

        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation.Token));
        var root = answer.RootElement;
        var undated = root.GetProperty("shown")[5];

        Names(root).Should().Equal("posts", "matching", "viewsFrom", "mostPageViews", "shown");
        Names(root.GetProperty("shown")[0]).Should().Equal("path", "address", "title", "published", "pageViews");
        undated.GetProperty("path").GetString().Should().Be(Undated);
        undated.GetProperty("title").ValueKind.Should().Be(JsonValueKind.Null);
        undated.GetProperty("published").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// A website in Kolkata listing six posts. This month a reader judged a person read the newest — two days
    /// old — once, the one moved to a new path four hundred days ago twice at its new path, the untitled one
    /// forty days old once, and the one at an escaped path five days old once; a scraper read the newest
    /// three times and the one with neither a title nor a date twice; nobody read the one ten days old.
    /// </summary>
    private async Task<Site> ListedAsync()
    {
        var calendar = new ZoneCalendar(Kolkata);
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: Kolkata);
        var now = await KeptRecord.NowInsideTheMonthAsync(stack, calendar);
        var lately = Lately(calendar, ZoneCalendar.FirstOf(calendar.DateOf(now)), now);
        var reader = $"reader-{site.Id:n}";
        var scraper = $"scraper-{site.Id:n}";
        var listedAt = SitemapSeed.Now(stack);
        string[] read = [HelloWorld, "/posts/new-home/", "/posts/new-home/", Older, Cafe];
        string[] scraped = [HelloWorld, HelloWorld, HelloWorld, Undated, Undated];

        await SitemapSeed.WriteAsync(
            stack,
            KeptPosts.Post(site.Id, HelloWorld, listedAt, listedAt.AddDays(-2), title: "Hello World"),
            KeptPosts.Post(site.Id, OldHome, listedAt, listedAt.AddDays(-400), landed: "/posts/new-home/", title: "Moved House"),
            KeptPosts.Post(site.Id, Older, listedAt, listedAt.AddDays(-40)),
            KeptPosts.Post(site.Id, Cafe, listedAt, listedAt.AddDays(-5), title: "Café au lait"),
            KeptPosts.Post(site.Id, Undated, listedAt),
            KeptPosts.Post(site.Id, Quiet, listedAt, listedAt.AddDays(-10), title: "A Quiet Week"));
        await KeptRecord.WriteAsync(
            stack,
            [
                .. read.Select((path, second) => KeptRecord.Page(site.Id, lately.AddSeconds(second), path, reader)),
                .. scraped.Select((path, second) =>
                    KeptRecord.Page(site.Id, lately.AddSeconds(10 + second), path, scraper)),
            ]);
        await KeptRecord.JudgedAsync(stack, site.Id, reader, lately, lately.AddSeconds(4), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(
            stack,
            site.Id,
            scraper,
            lately.AddSeconds(10),
            lately.AddSeconds(14),
            TrafficCategory.ContentScraper);

        return site;
    }

    /// <summary>A moment this month, no more than forty minutes ago.</summary>
    private static DateTimeOffset Lately(ZoneCalendar calendar, DateOnly running, DateTimeOffset now) =>
        calendar.MidnightOn(running) > now.AddMinutes(-40) ? calendar.MidnightOn(running) : now.AddMinutes(-40);

    /// <summary>Asks for a page of a website's posts and insists it was answered.</summary>
    private static Task<PostsPageResponse> AskAsync(Browser browser, Guid siteId, string question = "") =>
        KeptRecord.GetAsync<PostsPageResponse>(browser, $"/api/sites/{siteId}/posts{question}");

    /// <summary>Everything the refusal of a page of posts says, and whether it repeats what was asked.</summary>
    private static async Task<Refusal.Said> RefusalAsync(Browser browser, Guid siteId, string question, string asked) =>
        await Refusal.WholeAsync(await browser.GetAsync($"/api/sites/{siteId}/posts{question}"), asked);

    /// <summary>The question searching for a text, escaped as the dashboard escapes it.</summary>
    private static string Search(string text) => $"?search={Uri.EscapeDataString(text)}";

    private static string[] Paths(PostsPageResponse page) => [.. page.Shown.Select(post => post.Path)];

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private static string Domain() => $"site-posts-{Guid.NewGuid():n}.example";
}
