using System.Globalization;
using System.Text;
using System.Text.Json;
using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers reading a website's sitemap from end to end: what gets stored, how often a website is asked,
/// and what becomes of a reading when the website, the engine or somebody's settings get in the way.
/// </summary>
/// <remarks>
/// Each test reads from a real server on a real socket, through <see cref="LoopbackEndpoints"/>, and writes
/// to a real database. No clock is faked for what is real here: the gap before a request and the time a
/// website has to answer run in real time, and <see cref="MovableClock"/> only moves on the time a reading
/// writes down, which is how an hour from now comes round without taking an hour.
/// </remarks>
/// <param name="stack">The running stack, whose database server is used.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SitemapReaderTests(AnalyticsStackFixture stack) : IAsyncLifetime
{
    private const string Domain = "blog.example.test";
    private const string OtherName = "www." + Domain;
    private const string Origin = "http://" + Domain;
    private const string Html = "text/html";

    private SitemapRig _rig = null!;

    private DateTimeOffset Now => _rig.Clock.GetUtcNow();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _rig = await SitemapRig.StartAsync(stack);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _rig.DisposeAsync();

    [Fact]
    public async Task Reading_A_Sitemap_Stores_Its_Posts()
    {
        var published = Now.AddDays(-10);

        _rig.Website.Serve("/sitemap.xml", UrlSet("/first", "/second"));
        _rig.Website.Serve("/first", Article("The first post", published), Html);
        _rig.Website.Serve("/second", Article("The second post", published.AddDays(1)), Html);

        var site = await WebsiteAsync();

        (await _rig.Engine().RunAsync()).Should().BeTrue();

        var pages = await _rig.PagesAsync(site.Id);

        pages.Select(page => page.Path).Should().Equal("/first", "/second");

        pages[0].Address.Should().Be(Origin + "/first");
        pages[0].Title.Should().Be("The first post");
        pages[0].TitleSource.Should().Be(PostTitleSource.OpenGraph);
        pages[0].PublishedAt.Should().BeCloseTo(published, TimeSpan.FromSeconds(1));
        pages[0].PublishedSource.Should().Be(PostDateSource.OpenGraph);
        pages[0].PageType.Should().Be(PostPageType.Article);
        pages[0].IsPost.Should().BeTrue();
        pages[0].PageOutcome.Should().Be(PostPageOutcome.Read);
        pages[0].PageDueAt.Should().BeNull();
        pages[0].PageAttempts.Should().Be(0);
        pages[0].UnlistedAt.Should().BeNull();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Read);
        row.Problem.Should().BeNull();
        row.RootKind.Should().Be(SitemapRootKind.UrlSet);
        row.ReadAt.Should().NotBeNull();
        row.SucceededAt.Should().NotBeNull();
        row.CycleStartedAt.Should().BeNull();
    }

    /// <summary>
    /// What a reading leaves behind is what the settings panel is told: the posts it found, how many of
    /// them say when they were published, and how many pages it has yet to get to.
    /// </summary>
    [Fact]
    public async Task What_A_Reading_Found_Is_What_The_Status_Reports()
    {
        _rig.Website.Serve(
            "/post-sitemap.xml",
            UrlSetOf(("/dated", Now.AddDays(-1)), ("/undated", Now.AddDays(-2)), ("/not-yet", Now.AddDays(-3))));

        _rig.Website.Serve("/dated", Article("Dated", Now.AddDays(-4)), Html);
        _rig.Website.Serve("/undated", "<title>Undated</title>", Html);

        var site = await WebsiteAsync("/post-sitemap.xml");
        var engine = _rig.Engine(Settings(pagesPerPass: 2));

        await engine.RunAsync();

        SitemapReport? report = null;

        await engine.InScopeAsync(async services =>
        {
            var posts = new SitePosts(
                services.GetRequiredService<ControlPlaneDbContext>(),
                services.GetRequiredService<IOptions<SitemapOptions>>());

            report = await posts.DescribeSitemapAsync(
                new TenantScope(site.Id, site.OrganizationId, SiteRole.Viewer, site.TimeZoneId, site.CreatedAt),
                Cancellation.Token);
        });

        report.Should().NotBeNull();
        report.Address.Should().Be(Origin + "/post-sitemap.xml");
        report.State.Should().Be(SitemapState.Read);
        report.Problem.Should().BeNull();
        report.ReadAt.Should().NotBeNull();
        report.SucceededAt.Should().NotBeNull();
        report.Posts.Should().Be(3);
        report.Dated.Should().Be(1);
        report.Reading.Should().Be(1);
    }

    /// <summary>
    /// A pass lets go of a website when it is done, and says the next time it has anything to do, which
    /// with every page read is the next look at the sitemap.
    /// </summary>
    [Fact]
    public async Task A_Pass_Lets_Go_Of_The_Website_And_Says_When_It_Next_Has_Anything_To_Do()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/first"));
        _rig.Website.Serve("/first", Article("The first post", Now.AddDays(-1)), Html);

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        var row = await RowAsync(site.Id);

        row.ClaimToken.Should().BeNull();
        row.NextCycleAt.Should().BeCloseTo(Now + SitemapRig.Quick.Cadence, TimeSpan.FromMinutes(1));
        row.NextPassAt.Should().Be(row.NextCycleAt);

        (await engine.RunAsync()).Should().BeFalse("nothing is due for another hour");
    }

    [Fact]
    public async Task Only_The_Posts_Part_Of_An_Index_Is_Read()
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap.xml", "/page-sitemap.xml", "/category-sitemap.xml"));
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/hello"));
        _rig.Website.Serve("/page-sitemap.xml", UrlSet("/about"));
        _rig.Website.Serve("/category-sitemap.xml", UrlSet("/category/news"));
        _rig.Website.Serve("/hello", Article("Hello", Now.AddDays(-1)), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        _rig.Asked("/page-sitemap.xml").Should().Be(0);
        _rig.Asked("/category-sitemap.xml").Should().Be(0);
        _rig.Asked("/about").Should().Be(0);

        var row = await RowAsync(site.Id);

        row.RootKind.Should().Be(SitemapRootKind.Index);

        (await _rig.PartsAsync(site.Id)).Should().ContainSingle()
            .Which.Address.Should().Be(Origin + "/post-sitemap.xml");

        var page = await _rig.PageAsync(site.Id, "/hello");

        page.ListedInPostsPart.Should().BeTrue();
        page.IsPost.Should().BeTrue();
    }

    /// <summary>
    /// An index with nothing recognised as holding posts is read whole, and what each page says about
    /// itself decides. Otherwise a sitemap from a publisher nobody has heard of would come to nothing.
    /// </summary>
    [Fact]
    public async Task An_Index_With_No_Posts_Part_Is_Read_Whole()
    {
        _rig.Website.Serve("/sitemap.xml", Index("/one.xml", "/two.xml"));
        _rig.Website.Serve("/one.xml", UrlSet("/article"));
        _rig.Website.Serve("/two.xml", UrlSet("/landing"));
        _rig.Website.Serve("/article", Article("An article", Now.AddDays(-1)), Html);
        _rig.Website.Serve("/landing", "<title>A landing page</title>", Html);

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        (await _rig.PartsAsync(site.Id)).Should().HaveCount(2).And.OnlyContain(part => !part.IsPosts);
        (await _rig.PageAsync(site.Id, "/article")).IsPost.Should().BeTrue();
        (await _rig.PageAsync(site.Id, "/landing")).IsPost.Should().BeFalse();
    }

    /// <summary>
    /// What WordPress writes with nothing added to it: its own sitemap, split by kind, and a block theme
    /// whose only date is the one its post date block prints.
    /// </summary>
    [Fact]
    public async Task What_WordPress_Itself_Writes_Is_Read()
    {
        var published = Now.AddDays(-30);

        _rig.Website.Serve(
            "/wp-sitemap.xml",
            Index(
                "/wp-sitemap-posts-post-1.xml",
                "/wp-sitemap-posts-page-1.xml",
                "/wp-sitemap-taxonomies-category-1.xml",
                "/wp-sitemap-users-1.xml"));
        _rig.Website.Serve("/wp-sitemap-posts-post-1.xml", UrlSetOf(("/2026/hello-world/", Now.AddDays(-2))));
        _rig.Website.Serve(
            "/2026/hello-world/",
            $"""
            <!doctype html><html><head><title>Hello world! &#8211; My blog</title></head><body>
            <div class="wp-block-post-date"><time datetime="{Iso(published)}">A while ago</time></div>
            <div class="wp-block-post-date wp-block-post-date__modified-date"><time datetime="{Iso(Now)}">Today</time></div>
            </body></html>
            """,
            Html);

        var site = await WebsiteAsync("/wp-sitemap.xml");

        await _rig.Engine().RunAsync();

        _rig.Asked("/wp-sitemap-posts-page-1.xml").Should().Be(0);

        var page = await _rig.PageAsync(site.Id, "/2026/hello-world/");

        page.Title.Should().StartWith("Hello world!");
        page.TitleSource.Should().Be(PostTitleSource.Document);
        page.PublishedAt.Should().BeCloseTo(published, TimeSpan.FromSeconds(1));
        page.PublishedSource.Should().Be(PostDateSource.WordPressBlock);
        page.IsPost.Should().BeTrue();
    }

    /// <summary>
    /// What Yoast writes: an index at its own address, a numbered file of posts, and a graph of structured
    /// data, with Open Graph tags in front of it.
    /// </summary>
    [Fact]
    public async Task What_Yoast_Writes_Is_Read()
    {
        var published = Now.AddDays(-30);

        _rig.Website.Serve(
            "/sitemap_index.xml",
            Index("/post-sitemap.xml", "/post-sitemap2.xml", "/page-sitemap.xml", "/author-sitemap.xml"));
        _rig.Website.Serve("/post-sitemap.xml", UrlSetOf(("/older-post/", Now.AddDays(-20))));
        _rig.Website.Serve("/post-sitemap2.xml", UrlSetOf(("/newer-post/", Now.AddDays(-2))));

        _rig.Website.Serve(
            "/older-post/",
            $$"""
            <!doctype html><html><head><title>An older post - My blog</title>
            <meta property="og:type" content="article">
            <meta property="og:title" content="An older post">
            <meta property="article:published_time" content="{{Iso(published)}}">
            <script type="application/ld+json">
            {"@context":"https://schema.org","@graph":[{"@type":"WebPage","name":"Not this"},
            {"@type":"Article","headline":"An older post, headline","datePublished":"{{Iso(published)}}"}]}
            </script></head><body></body></html>
            """,
            Html);

        _rig.Website.Serve(
            "/newer-post/",
            $$"""
            <!doctype html><html><head><title>A newer post - My blog</title>
            <script type="application/ld+json">
            {"@context":"https://schema.org","@graph":[
            {"@type":"BlogPosting","headline":"A newer post","datePublished":"{{Iso(published.AddDays(1))}}"}]}
            </script></head><body></body></html>
            """,
            Html);

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        _rig.Asked("/page-sitemap.xml").Should().Be(0);
        _rig.Asked("/author-sitemap.xml").Should().Be(0);

        var older = await _rig.PageAsync(site.Id, "/older-post/");

        older.Title.Should().Be("An older post");
        older.PublishedSource.Should().Be(PostDateSource.OpenGraph);

        var newer = await _rig.PageAsync(site.Id, "/newer-post/");

        newer.Title.Should().Be("A newer post");
        newer.TitleSource.Should().Be(PostTitleSource.StructuredData);
        newer.PublishedSource.Should().Be(PostDateSource.StructuredData);
        newer.IsPost.Should().BeTrue();
    }

    /// <summary>
    /// What Hugo writes: one list of everything it built, the front page and the tag pages among it, and
    /// nothing to say which are posts but what each page says about itself.
    /// </summary>
    [Fact]
    public async Task What_Hugo_Writes_Is_Read()
    {
        _rig.Website.Serve(
            "/sitemap.xml",
            UrlSetOf(
                ("/", Now.AddDays(-1)),
                ("/posts/my-first-post/", Now.AddDays(-5)),
                ("/tags/news/", Now.AddDays(-5))));

        _rig.Website.Serve("/posts/my-first-post/", Article("My first post", Now.AddDays(-5)), Html);
        _rig.Website.Serve(
            "/tags/news/",
            """<html><head><title>News</title><meta property="og:type" content="website"></head></html>""",
            Html);

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        _rig.Asked("/").Should().Be(0, "a website's front page is never a post");

        var pages = await _rig.PagesAsync(site.Id);

        pages.Select(page => page.Path).Should().Equal("/posts/my-first-post/", "/tags/news/");
        pages[0].IsPost.Should().BeTrue();
        pages[1].PageType.Should().Be(PostPageType.Other);
        pages[1].IsPost.Should().BeFalse();
    }

    /// <summary>
    /// What Hugo's own template makes of a page in no section of posts, in a sitemap read whole. It says article
    /// of an About page and of a post alike, and only the post says when it was published.
    /// </summary>
    [Fact]
    public async Task What_Hugo_Says_Of_An_About_Page_Does_Not_Make_It_A_Post()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/about/", "/posts/hello/"));
        _rig.Website.Serve("/about/", ThemePages.HugoPage("About"), Html);
        _rig.Website.Serve("/posts/hello/", Article("Hello", Now.AddDays(-5)), Html);

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        var about = await _rig.PageAsync(site.Id, "/about/");

        about.PageType.Should().BeNull();
        about.IsPost.Should().BeFalse();
        about.Title.Should().Be("About");
        (await _rig.PageAsync(site.Id, "/posts/hello/")).IsPost.Should().BeTrue();
    }

    /// <summary>
    /// A file of posts is where a website says which pages are posts, so one saying only that it is an article
    /// counts there, with no date to its name.
    /// </summary>
    [Fact]
    public async Task A_Page_Saying_Only_That_It_Is_An_Article_Counts_In_A_File_Of_Posts()
    {
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/hello/"));
        _rig.Website.Serve("/hello/", ThemePages.HugoPage("Hello"), Html);

        var site = await WebsiteAsync("/post-sitemap.xml");

        await _rig.Engine().RunAsync();

        var page = await _rig.PageAsync(site.Id, "/hello/");

        page.PageType.Should().BeNull();
        page.PublishedAt.Should().BeNull();
        page.IsPost.Should().BeTrue();
    }

    /// <summary>
    /// Yoast lists WordPress's page of posts first in its file of posts, and calls it an article. Under a block
    /// theme or a classic one it comes to the same: not a post, and with no date from whichever post it shows
    /// first. So the count is of the website's posts, and every one of them has its date.
    /// </summary>
    /// <param name="classic">Whether the theme is a classic one.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Page_Yoast_Lists_First_Is_Not_A_Post(bool classic)
    {
        var first = Iso(Now.AddDays(-9));
        var second = Iso(Now.AddDays(-19));

        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap.xml", "/page-sitemap.xml", "/category-sitemap.xml"));
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/blog/", "/hello/", "/second/"));
        _rig.Website.Serve(
            "/blog/",
            ThemePages.YoastPostsPage(classic ? ThemePages.ClassicEntries(first, second) : ThemePages.ListOfPosts(2)),
            Html);
        _rig.Website.Serve("/hello/", ThemePages.YoastPost("Hello", first), Html);
        _rig.Website.Serve("/second/", ThemePages.YoastPost("Second", second), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        var page = await _rig.PageAsync(site.Id, "/blog/");

        page.PageType.Should().Be(PostPageType.Other);
        page.PublishedAt.Should().BeNull();
        page.IsPost.Should().BeFalse();

        var posts = (await _rig.PagesAsync(site.Id)).Where(listed => listed.IsPost).ToList();

        posts.Select(post => post.Path).Should().BeEquivalentTo("/hello/", "/second/");
        posts.Should().OnlyContain(post => post.PublishedAt != null);
    }

    /// <summary>
    /// A website with no posts has Yoast leave its file of posts out, so the sitemap is read whole, and
    /// Yoast says article of every page in it. None of them says when it was published, so none is a post.
    /// </summary>
    [Fact]
    public async Task A_Website_Under_Yoast_With_No_Posts_Has_None()
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/page-sitemap.xml"));
        _rig.Website.Serve("/page-sitemap.xml", UrlSet("/about/", "/contact/", "/services/"));
        _rig.Website.Serve("/about/", ThemePages.YoastPage("About"), Html);
        _rig.Website.Serve("/contact/", ThemePages.YoastPage("Contact"), Html);
        _rig.Website.Serve("/services/", ThemePages.YoastPage("Services"), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        (await _rig.PagesAsync(site.Id)).Should().HaveCount(3).And.OnlyContain(page => !page.IsPost);
    }

    /// <summary>
    /// A shop under Yoast is the same, with a description of each product added by whatever runs the shop.
    /// </summary>
    [Fact]
    public async Task A_Shops_Products_Are_Not_Posts()
    {
        const string Product = """<script type="application/ld+json">{"@type":"Product","name":"A shoe"}</script>""";

        _rig.Website.Serve("/sitemap_index.xml", Index("/product-sitemap.xml", "/page-sitemap.xml"));
        _rig.Website.Serve("/product-sitemap.xml", UrlSet("/product/shoe/", "/product/hat/"));
        _rig.Website.Serve("/page-sitemap.xml", UrlSet("/about/"));
        _rig.Website.Serve("/product/shoe/", ThemePages.YoastPage("Shoe", Product), Html);
        _rig.Website.Serve("/product/hat/", ThemePages.YoastPage("Hat", Product), Html);
        _rig.Website.Serve("/about/", ThemePages.YoastPage("About"), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        (await _rig.PageAsync(site.Id, "/product/shoe/")).PageType.Should().Be(PostPageType.Other);
        (await _rig.PagesAsync(site.Id)).Should().HaveCount(3).And.OnlyContain(page => !page.IsPost);
    }

    /// <summary>
    /// A sitemap read whole can list a category's page and a tag's beside the posts in them. Each is a list
    /// of the same posts, and dated by none of them, so there is one post on this website and not three.
    /// </summary>
    [Fact]
    public async Task A_List_Of_Posts_Read_Beside_Them_Adds_Nothing_To_Them()
    {
        var published = Iso(Now.AddDays(-9));

        _rig.Website.Serve("/sitemap.xml", UrlSet("/category/news/", "/tag/rare/", "/2026/first/"));
        _rig.Website.Serve(
            "/category/news/",
            $"""
            <!doctype html><html><head><title>News - My site</title></head>
            <body class="archive category">{ThemePages.ClassicEntries(published)}</body></html>
            """,
            Html);
        _rig.Website.Serve(
            "/tag/rare/",
            $"""
            <!doctype html><html><head><title>Rare - My site</title></head>
            <body class="archive tag">{ThemePages.ListOfPosts(1)}</body></html>
            """,
            Html);
        _rig.Website.Serve(
            "/2026/first/",
            $"""
            <!doctype html><html><head><title>First - My site</title></head>
            <body class="single single-post">{ThemePages.ClassicEntries(published)}</body></html>
            """,
            Html);

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        (await _rig.PagesAsync(site.Id)).Where(page => page.IsPost).Select(page => page.Path)
            .Should().Equal("/2026/first/");
    }

    /// <summary>
    /// What Ipsum prints, the default theme of the WordPress release after Twenty Twenty-Five: the post's own
    /// date under its title, and a list of others under the comments that says nothing about it.
    /// </summary>
    [Fact]
    public async Task A_Posts_Own_Date_Is_Read_Above_A_List_Of_Others()
    {
        var first = Now.AddDays(-9);
        var second = Now.AddDays(-19);

        _rig.Website.Serve("/wp-sitemap.xml", Index("/wp-sitemap-posts-post-1.xml", "/wp-sitemap-posts-page-1.xml"));
        _rig.Website.Serve("/wp-sitemap-posts-post-1.xml", UrlSet("/one/", "/two/"));
        _rig.Website.Serve("/one/", ThemePages.Ipsum(Iso(first), 3), Html);
        _rig.Website.Serve("/two/", ThemePages.Ipsum(Iso(second), 3), Html);

        var site = await WebsiteAsync("/wp-sitemap.xml");

        await _rig.Engine().RunAsync();

        var one = await _rig.PageAsync(site.Id, "/one/");

        one.PublishedAt.Should().BeCloseTo(first, TimeSpan.FromSeconds(1));
        one.PublishedSource.Should().Be(PostDateSource.WordPressBlock);
        one.IsPost.Should().BeTrue();
        (await _rig.PageAsync(site.Id, "/two/")).PublishedAt.Should().BeCloseTo(second, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Twenty Twenty-Five prints no date for a post, so its posts are counted and not dated, however many other
    /// posts' dates are printed under them. Nothing is there to read, and that is what is said of them.
    /// </summary>
    /// <param name="listed">How many posts its list holds.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task The_Posts_Of_A_Theme_Printing_No_Date_Are_Counted_And_Not_Dated(int listed)
    {
        _rig.Website.Serve("/wp-sitemap.xml", Index("/wp-sitemap-posts-post-1.xml", "/wp-sitemap-posts-page-1.xml"));
        _rig.Website.Serve("/wp-sitemap-posts-post-1.xml", UrlSet("/one/"));
        _rig.Website.Serve("/one/", ThemePages.TwentyTwentyFive(listed), Html);

        var site = await WebsiteAsync("/wp-sitemap.xml");

        await _rig.Engine().RunAsync();

        var page = await _rig.PageAsync(site.Id, "/one/");

        page.IsPost.Should().BeTrue();
        page.PublishedAt.Should().BeNull();
        page.Title.Should().StartWith("Mine");
    }

    /// <summary>
    /// A website can not take the whole of the engine down, or hold on to a reader, with what its pages hold.
    /// These count as pages saying nothing, and the next is read straight after.
    /// </summary>
    [Fact]
    public async Task A_Page_Made_To_Bring_Down_The_Reader_Is_A_Page_Saying_Nothing()
    {
        var frames = "x";

        for (var level = 0; level < 40; level++)
        {
            var escaped = frames.Replace("&", "&amp;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

            frames = $"<iframe/srcdoc={escaped}>";
        }

        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/fine/", "/empty-tags/", "/frames/", "/pre/"));
        _rig.Website.Serve("/fine/", Article("Fine", Now.AddDays(-1)), Html);
        _rig.Website.Serve("/empty-tags/", string.Concat(Enumerable.Repeat("</>", 20_000)), Html);
        _rig.Website.Serve("/frames/", frames, Html);
        _rig.Website.Serve("/pre/", string.Concat(Enumerable.Repeat("<pre>", 10_000)), Html);

        var site = await WebsiteAsync("/post-sitemap.xml");

        await _rig.Engine().RunAsync();

        var pages = await _rig.PagesAsync(site.Id);

        pages.Should().HaveCount(4).And.OnlyContain(page => page.PageOutcome == PostPageOutcome.Read);
        pages.Should().OnlyContain(page => page.IsPost);
        pages.Where(page => page.PublishedAt != null).Select(page => page.Path).Should().Equal("/fine/");
    }

    [Fact]
    public async Task A_Page_Is_Read_Once_And_Again_Only_When_Its_Lastmod_Changes()
    {
        var modified = Now.AddDays(-2);

        _rig.Website.Serve("/sitemap.xml", UrlSetOf(("/post", modified)));
        _rig.Website.Serve("/post", Article("Before", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        _rig.Asked("/post").Should().Be(1);

        // Read again, listed again, and nothing to say it has changed.
        await NextReadingAsync(engine);

        _rig.Asked("/post").Should().Be(1);

        // The sitemap has changed, but not what it says about this page.
        _rig.Website.Serve("/sitemap.xml", UrlSetOf(("/post", modified), ("/other", modified)));
        _rig.Website.Serve("/other", Article("Other", Now.AddDays(-3)), Html);

        await NextReadingAsync(engine);

        _rig.Asked("/post").Should().Be(1);
        _rig.Asked("/other").Should().Be(1);

        // A date going backwards says nothing happened to the page.
        _rig.Website.Serve("/sitemap.xml", UrlSetOf(("/post", modified.AddDays(-1)), ("/other", modified)));

        await NextReadingAsync(engine);

        _rig.Asked("/post").Should().Be(1);

        // A date later than the one it was read under does.
        _rig.Website.Serve("/post", Article("After", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/sitemap.xml", UrlSetOf(("/post", modified.AddDays(1)), ("/other", modified)));

        await NextReadingAsync(engine);

        _rig.Asked("/post").Should().Be(2);
        (await _rig.PageAsync(site.Id, "/post")).Title.Should().Be("After");
    }

    /// <summary>
    /// Plenty of sitemaps give no dates, and then there is nothing to say a page has changed, so it is not
    /// read again however many times the sitemap is.
    /// </summary>
    [Fact]
    public async Task A_Page_A_Sitemap_Gives_No_Date_For_Is_Read_Once()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Once", Now.AddDays(-3)), Html);

        await WebsiteAsync();

        var engine = _rig.Engine();

        await engine.RunAsync();
        await NextReadingAsync(engine);
        await NextReadingAsync(engine);

        _rig.Asked("/post").Should().Be(1);
        _rig.Asked("/sitemap.xml").Should().Be(3);
    }

    [Fact]
    public async Task An_Unchanged_Page_Is_Answered_From_Its_Validators()
    {
        const string Tag = "\"version-1\"";

        var modified = Now.AddDays(-2);

        _rig.Website.Serve("/sitemap.xml", UrlSetOf(("/post", modified)));
        _rig.Website.Answer("/post", async context =>
        {
            if (context.Request.Headers.IfNoneMatch == Tag)
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;

                return;
            }

            context.Response.ContentType = Html;
            context.Response.Headers.ETag = Tag;

            await context.Response.WriteAsync(Article("Kept", Now.AddDays(-3)), context.RequestAborted);
        });

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        _rig.Website.Serve("/sitemap.xml", UrlSetOf(("/post", modified.AddDays(1))));

        await NextReadingAsync(engine);

        _rig.Asked("/post").Should().Be(2);

        var page = await _rig.PageAsync(site.Id, "/post");

        page.PageOutcome.Should().Be(PostPageOutcome.Unchanged);
        page.Title.Should().Be("Kept");
        page.PageDueAt.Should().BeNull();
        page.PageReadLastmod.Should().BeCloseTo(modified.AddDays(1), TimeSpan.FromSeconds(1));

        await NextReadingAsync(engine);

        _rig.Asked("/post").Should().Be(2);
    }

    /// <summary>
    /// A sitemap that has not changed costs one question and nothing is written about what it lists, and
    /// what it listed goes on being listed.
    /// </summary>
    [Fact]
    public async Task A_Sitemap_That_Has_Not_Changed_Vouches_For_What_It_Listed()
    {
        const string Tag = "\"sitemap-1\"";

        _rig.Website.Answer("/sitemap.xml", async context =>
        {
            if (context.Request.Headers.IfNoneMatch == Tag)
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;

                return;
            }

            context.Response.ContentType = "application/xml";
            context.Response.Headers.ETag = Tag;

            await context.Response.WriteAsync(UrlSet("/post"), context.RequestAborted);
        });

        _rig.Website.Serve("/post", Article("Kept", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        (await RowAsync(site.Id)).RootEtag.Should().Be(Tag);

        await NextReadingAsync(engine);

        _rig.Asked("/sitemap.xml").Should().Be(2);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Read);
        (await _rig.PartsAsync(site.Id)).Should().ContainSingle()
            .Which.Outcome.Should().Be(SitemapPartOutcome.Unchanged);
        (await _rig.PageAsync(site.Id, "/post")).UnlistedAt.Should().BeNull();
    }

    /// <summary>
    /// An index that has not changed says nothing about the files it names, and those are where a new post
    /// turns up. Each is still asked, and what was wrong with the list itself is still wrong.
    /// </summary>
    [Fact]
    public async Task An_Index_That_Has_Not_Changed_Still_Has_Its_Parts_Asked_For()
    {
        ServeWithATag("/sitemap_index.xml", Index("/post-sitemap.xml", "http://shop.example.test/post-sitemap2.xml"));
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/first"));
        _rig.Website.Serve("/first", Article("First", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/second", Article("Second", Now.AddDays(-3)), Html);

        var site = await _rig.AddWebsiteAsync("example.test", Origin + "/sitemap_index.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/first", "/second"));

        await NextReadingAsync(engine);

        _rig.Asked("/sitemap_index.xml").Should().Be(2);
        _rig.Asked("/post-sitemap.xml").Should().Be(2);
        (await _rig.PageAsync(site.Id, "/second")).Title.Should().Be("Second");

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.PartlyRead);
        row.RootProblem.Should().Be(SitemapProblem.LeftTheWebsite);
        row.Problem.Should().Be(SitemapProblem.LeftTheWebsite);
    }

    /// <summary>
    /// What a one-list sitemap said about itself is kept only once all of it has been read. Kept any
    /// sooner, the next reading would be told nothing had changed about a list it never got to the end of.
    /// </summary>
    [Fact]
    public async Task A_List_Not_Read_To_Its_End_Is_Asked_For_In_Full_Next_Time()
    {
        const string Tag = "\"sitemap-1\"";

        _rig.Website.Answer("/sitemap.xml", async context =>
        {
            context.Response.ContentType = "application/xml";
            context.Response.Headers.ETag = Tag;

            await context.Response.WriteAsync(
                "<urlset><url><loc>http://blog.example.test/post</loc></url><url><loc>broken",
                context.RequestAborted);
        });

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        (await RowAsync(site.Id)).RootEtag.Should().BeNull();

        await NextReadingAsync(engine);

        _rig.Website.Seen.Where(request => request.Target == "/sitemap.xml")
            .Should().AllSatisfy(request => request.Headers.Should().NotContainKey("If-None-Match"));
    }

    [Fact]
    public async Task A_Post_No_Longer_Listed_Stops_Counting()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/staying", "/going"));
        _rig.Website.Serve("/staying", Article("Staying", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/going", Article("Going", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        _rig.Website.Serve("/sitemap.xml", UrlSet("/staying"));

        await NextReadingAsync(engine);

        (await _rig.PageAsync(site.Id, "/staying")).UnlistedAt.Should().BeNull();
        (await _rig.PageAsync(site.Id, "/going")).UnlistedAt.Should().NotBeNull();

        // Coming back is the website's to decide, and it was read once already.
        _rig.Website.Serve("/sitemap.xml", UrlSet("/staying", "/going"));

        await NextReadingAsync(engine);

        (await _rig.PageAsync(site.Id, "/going")).UnlistedAt.Should().BeNull();
        _rig.Asked("/going").Should().Be(1);
    }

    /// <summary>The ways a part can fail to be read to its end.</summary>
    public enum HowAPartFails
    {
        /// <summary>The website has trouble answering.</summary>
        ServerError,

        /// <summary>It stops being there.</summary>
        NotFound,

        /// <summary>It arrives broken partway through.</summary>
        Broken,
    }

    /// <summary>
    /// Only a reading that got through all of a sitemap may say a page has stopped being listed. A part
    /// that failed says nothing about what it would have listed.
    /// </summary>
    /// <param name="how">How the part fails.</param>
    [Theory]
    [InlineData(HowAPartFails.ServerError)]
    [InlineData(HowAPartFails.NotFound)]
    [InlineData(HowAPartFails.Broken)]
    public async Task A_Read_That_Did_Not_Finish_Unlists_Nothing(HowAPartFails how)
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml"));
        _rig.Website.Serve("/post-sitemap1.xml", UrlSet("/one"));
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet("/two"));
        _rig.Website.Serve("/one", Article("One", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/two", Article("Two", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        _rig.Website.Serve("/post-sitemap1.xml", UrlSet());
        Fail("/post-sitemap2.xml", how);

        await NextReadingAsync(engine);

        (await RowAsync(site.Id)).State.Should().Be(SitemapState.PartlyRead);
        (await _rig.PagesAsync(site.Id)).Should().AllSatisfy(page => page.UnlistedAt.Should().BeNull());

        // Once it has been read to its end, what it did not list has stopped being listed.
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet("/two"));

        await NextReadingAsync(engine);

        (await RowAsync(site.Id)).State.Should().Be(SitemapState.Read);
        (await _rig.PageAsync(site.Id, "/one")).UnlistedAt.Should().NotBeNull();
        (await _rig.PageAsync(site.Id, "/two")).UnlistedAt.Should().BeNull();
    }

    /// <summary>
    /// A post moved from one part to another is found again in the second, whichever is read first, and
    /// a part that has not changed goes on vouching for the pages it listed.
    /// </summary>
    [Fact]
    public async Task A_Post_Moved_Between_Parts_Stays_Listed()
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml", "/post-sitemap3.xml"));
        _rig.Website.Serve("/post-sitemap1.xml", UrlSet("/moving"));
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet("/waiting"));
        ServeWithATag("/post-sitemap3.xml", UrlSet("/vouched-for"));
        _rig.Website.Serve("/moving", Article("Moving", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/waiting", Article("Waiting", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/vouched-for", Article("Vouched for", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        _rig.Website.Serve("/post-sitemap1.xml", UrlSet());
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet("/waiting", "/moving"));

        await NextReadingAsync(engine);

        (await RowAsync(site.Id)).State.Should().Be(SitemapState.Read);
        (await _rig.PartsAsync(site.Id)).Single(part => part.Address.EndsWith("3.xml", StringComparison.Ordinal))
            .Outcome.Should().Be(SitemapPartOutcome.Unchanged);
        (await _rig.PagesAsync(site.Id)).Should().AllSatisfy(page => page.UnlistedAt.Should().BeNull());
    }

    /// <summary>
    /// A part the sitemap no longer names goes, and whatever only it listed has stopped being listed.
    /// </summary>
    [Fact]
    public async Task A_Post_In_A_Part_No_Longer_Named_Stops_Counting()
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml"));
        _rig.Website.Serve("/post-sitemap1.xml", UrlSet("/one"));
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet("/two"));
        _rig.Website.Serve("/one", Article("One", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/two", Article("Two", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml"));

        await NextReadingAsync(engine);

        (await _rig.PartsAsync(site.Id)).Should().ContainSingle();
        (await _rig.PageAsync(site.Id, "/one")).UnlistedAt.Should().BeNull();
        (await _rig.PageAsync(site.Id, "/two")).UnlistedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_Page_Off_The_List_For_A_Year_Is_Forgotten()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/staying"));
        _rig.Website.Serve("/staying", Article("Staying", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        var at = Now;

        await _rig.SqlAsync(
            $"""
            INSERT INTO site_posts (site_id, path, address, listed_in_posts_part, first_listed_at, unlisted_at)
            VALUES ({site.Id}, '/ancient', 'http://blog.example.test/ancient', false, {at}, {at.AddDays(-366)}),
                   ({site.Id}, '/recent', 'http://blog.example.test/recent', false, {at}, {at.AddDays(-364)})
            """);

        await NextReadingAsync(engine);

        (await _rig.PagesAsync(site.Id)).Select(page => page.Path).Should().Equal("/recent", "/staying");
    }

    /// <summary>
    /// One request a second to one website, whatever each is for, on the real gap the product runs with.
    /// Measured where it counts: at the website.
    /// </summary>
    [Fact]
    public async Task Pages_Are_Read_At_Most_One_A_Second()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/one", "/two", "/three"));

        foreach (var path in new[] { "/one", "/two", "/three" })
        {
            _rig.Website.Serve(path, Article(path, Now.AddDays(-3)), Html);
        }

        await WebsiteAsync();

        var settings = new SitemapOptions();

        settings.RequestGap.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));

        await _rig.Engine(settings).RunAsync();

        var seen = _rig.Website.Seen;

        seen.Select(request => request.Target).Take(2).Should().Equal("/robots.txt", "/sitemap.xml");
        seen.Select(request => request.Target).Skip(2).Should().BeEquivalentTo("/one", "/two", "/three");

        // Somewhat under a second between two arrivals is the first held up on its way, on a busy machine,
        // and not the second sent early. A gap that was not kept comes to a few milliseconds.
        for (var request = 1; request < seen.Count; request++)
        {
            TimeProvider.System.GetElapsedTime(seen[request - 1].At, seen[request].At)
                .Should().BeGreaterThan(TimeSpan.FromMilliseconds(500));
        }

        TimeProvider.System.GetElapsedTime(seen[0].At, seen[^1].At)
            .Should().BeGreaterThan(TimeSpan.FromSeconds(3.5));
    }

    [Fact]
    public async Task A_Page_Never_Fetches_Anything_It_Links_To()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve(
            "/post",
            $"""
            <!doctype html><html><head><title>Links</title>
            <link rel="stylesheet" href="/style.css"><link rel="canonical" href="/elsewhere">
            <script src="/app.js"></script><meta http-equiv="refresh" content="0; url=/refreshed">
            </head><body><img src="/picture.png"><iframe src="/frame.html"></iframe>
            <a href="/linked">A link</a><object data="/object.bin"></object></body></html>
            """,
            Html);

        await WebsiteAsync();
        await _rig.Engine().RunAsync();

        _rig.Website.Seen.Select(request => request.Target).Should().Equal("/robots.txt", "/sitemap.xml", "/post");
    }

    /// <summary>
    /// A pass started while another holds the claim finds nothing to do, and there is no second request.
    /// </summary>
    [Fact]
    public async Task Two_Engines_Never_Read_One_Website_At_Once()
    {
        var held = HoldAnswer("/sitemap.xml", UrlSet());

        await WebsiteAsync();

        var first = _rig.Engine().RunAsync();

        await held.Reached.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        (await _rig.Engine().RunAsync()).Should().BeFalse();

        held.Release();

        (await first).Should().BeTrue();
        _rig.Asked("/sitemap.xml").Should().Be(1);
    }

    /// <summary>
    /// However many engines look at the same moment, and however many times, one gets the website.
    /// </summary>
    [Fact]
    public async Task Engines_Looking_At_The_Same_Moment_Do_Not_Both_Get_A_Website()
    {
        var site = await WebsiteAsync();
        var engines = Enumerable.Range(0, 6).Select(_ => _rig.Engine()).ToList();

        for (var round = 0; round < 10; round++)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var claims = engines.Select(engine => Task.Run(
                async () =>
                {
                    await start.Task;

                    return await engine.ClaimAsync();
                },
                Cancellation.Token)).ToList();

            start.SetResult();

            (await Task.WhenAll(claims)).Count(lease => lease is not null).Should().Be(1);

            await ReleaseEverythingAsync(site.Id);
        }
    }

    [Fact]
    public async Task A_Lease_Left_By_A_Stopped_Engine_Expires()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet());

        var site = await WebsiteAsync();

        // An engine claims the website and is never heard from again.
        (await _rig.Engine().ClaimAsync()).Should().NotBeNull();

        var engine = _rig.Engine();

        (await engine.RunAsync()).Should().BeFalse();
        _rig.Website.Seen.Should().BeEmpty();

        await _rig.ExpireClaimAsync(site.Id);

        (await engine.RunAsync()).Should().BeTrue();
        (await RowAsync(site.Id)).State.Should().Be(SitemapState.Read);
    }

    /// <summary>
    /// How long a claim lasts is measured by the database's clock, so an engine whose own clock is days
    /// ahead cannot decide that somebody else's claim has run out.
    /// </summary>
    [Fact]
    public async Task A_Claim_Runs_Out_By_The_Databases_Clock_And_Not_An_Engines()
    {
        var site = await WebsiteAsync();

        (await _rig.Engine().ClaimAsync()).Should().NotBeNull();

        _rig.Clock.MoveOn(TimeSpan.FromDays(3));

        (await _rig.Engine().ClaimAsync()).Should().BeNull();

        var row = await RowAsync(site.Id);

        row.ClaimedUntil.Should().BeBefore(Now, "by the clock of the engine that was looking, it has already run out");
    }

    /// <summary>The ways a pass can find it no longer holds its claim.</summary>
    public enum HowAClaimIsLost
    {
        /// <summary>It ran out and somebody else took it.</summary>
        Taken,

        /// <summary>It ran out, and nobody has taken it yet.</summary>
        RanOut,

        /// <summary>The address was changed or the website removed, which deletes the row.</summary>
        RowGone,
    }

    /// <summary>
    /// A pass that has lost its claim changes nothing at all, by any of the writes it can make. Those that
    /// have to change something say the claim is gone, and those that may properly change nothing do so.
    /// </summary>
    /// <param name="how">How the claim is lost.</param>
    [Theory]
    [InlineData(HowAClaimIsLost.Taken)]
    [InlineData(HowAClaimIsLost.RanOut)]
    [InlineData(HowAClaimIsLost.RowGone)]
    public async Task Writes_From_A_Pass_Whose_Claim_Was_Lost_Change_Nothing(HowAClaimIsLost how)
    {
        var (site, engine, lease, part) = await AWebsiteClaimedPartWayThroughAsync();

        await LoseClaimAsync(site.Id, how);

        var before = await EverythingAsync(site.Id);

        await engine.InScopeAsync(async services =>
        {
            var writer = services.GetRequiredService<PostWriter>();

            foreach (var (name, write) in WritesThatHaveToChangeSomething(writer, lease, part))
            {
                var act = () => write();

                await act.Should().ThrowAsync<SitemapClaimLostException>(name);
            }

            foreach (var (_, write) in WritesThatMayChangeNothing(writer, lease))
            {
                await write();
            }
        });

        (await EverythingAsync(site.Id)).Should().Be(before);
    }

    /// <summary>
    /// The same writes under a claim still held do change things, which is what makes the test above say
    /// anything: a write that could never find its row would change nothing there either.
    /// </summary>
    [Fact]
    public async Task Writes_From_A_Pass_That_Holds_Its_Claim_Are_Written()
    {
        var (site, engine, lease, part) = await AWebsiteClaimedPartWayThroughAsync();

        await engine.InScopeAsync(async services =>
        {
            var writer = services.GetRequiredService<PostWriter>();

            foreach (var (name, write) in WritesThatHaveToChangeSomething(writer, lease, part))
            {
                var before = await EverythingAsync(site.Id);

                await write();

                (await EverythingAsync(site.Id)).Should().NotBe(before, name);
            }

            foreach (var (name, write) in WritesThatMayChangeNothing(writer, lease))
            {
                var before = await EverythingAsync(site.Id);

                await write();

                (await EverythingAsync(site.Id)).Should().NotBe(before, name);
            }
        });
    }

    /// <summary>
    /// The write that had already looked and found the claim held is what a plain check would let through.
    /// One arriving while an address is being changed waits for the change, and then finds the claim gone
    /// and changes nothing, rather than landing just after the change as if it had never happened.
    /// </summary>
    [Fact]
    public async Task A_Write_Arriving_During_A_Change_Of_Address_Waits_And_Changes_Nothing()
    {
        var (site, engine, lease, _) = await AWebsiteClaimedPartWayThroughAsync();

        await using var change = _rig.OwnContext();
        await using var transaction = await change.Database.BeginTransactionAsync(Cancellation.Token);

        await change.SitemapReads
            .Where(read => read.SiteId == site.Id)
            .ExecuteDeleteAsync(Cancellation.Token);

        var writing = engine.InScopeAsync(services => services.GetRequiredService<PostWriter>()
            .SettlePageAsync(lease, "/hello", PostPageOutcome.Gone, null, Now, Cancellation.Token));

        await Task.Delay(TimeSpan.FromMilliseconds(500), Cancellation.Token);

        writing.IsCompleted.Should().BeFalse("it has to wait to learn how the change turns out");

        await transaction.CommitAsync(Cancellation.Token);

        var act = () => writing;

        await act.Should().ThrowAsync<SitemapClaimLostException>();

        (await _rig.PageAsync(site.Id, "/hello")).PageOutcome.Should().NotBe(PostPageOutcome.Gone);
    }

    /// <summary>
    /// A change of address deletes the row, which voids whatever pass is under way, on any engine. It
    /// stops at its next write and leaves nothing behind about the address nobody wants read any more.
    /// </summary>
    [Fact]
    public async Task Changing_The_Address_During_A_Pass_Voids_It()
    {
        var held = HoldAnswer("/post", Article("Late", Now.AddDays(-1)), Html);

        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));

        var site = await WebsiteAsync();
        var engine = _rig.Engine();
        var pass = engine.RunAsync();

        await held.Reached.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        await _rig.ChangeSitemapAsync(site.Id, Origin + "/elsewhere.xml");

        held.Release();

        (await pass).Should().BeTrue();

        (await _rig.RowAsync(site.Id)).Should().BeNull();
        (await _rig.PageAsync(site.Id, "/post")).Title.Should().BeNull();
        engine.Log.GetSnapshot().Should().Contain(record => record.Id.Id == 1303);
        engine.Log.GetSnapshot().Should().NotContain(record => record.Level >= LogLevel.Warning);
    }

    /// <summary>
    /// Removing a website takes its rows from under a pass, and the pass stops without a word of complaint.
    /// </summary>
    [Fact]
    public async Task Removing_A_Website_During_A_Pass_Voids_It()
    {
        var held = HoldAnswer("/post", Article("Late", Now.AddDays(-1)), Html);

        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));

        var site = await WebsiteAsync();
        var engine = _rig.Engine();
        var pass = engine.RunAsync();

        await held.Reached.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        await _rig.RemoveWebsiteAsync(site.Id);

        held.Release();

        (await pass).Should().BeTrue();

        (await _rig.PagesAsync(site.Id)).Should().BeEmpty();
        engine.Log.GetSnapshot().Should().NotContain(record => record.Level >= LogLevel.Warning);
    }

    /// <summary>
    /// A changed address is a new sitemap, and it takes its place in the queue as of the change: a website
    /// that has waited longer is read first. Saving an address costs nothing, so a new one going to the
    /// front would let one account keep every reader for itself.
    /// </summary>
    [Fact]
    public async Task Changing_The_Address_Takes_Its_Turn()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/kept", "/dropped"));
        _rig.Website.Serve("/kept", Article("Kept", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/dropped", Article("Dropped", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/neighbour.xml", UrlSet());
        _rig.Website.Serve("/new.xml", UrlSet("/kept"));

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        // A neighbour has been waiting an hour, and goes first.
        var neighbour = await _rig.AddWebsiteAsync("neighbour.example.test", "http://neighbour.example.test/neighbour.xml");

        await engine.ClaimAsync();
        await ReleaseEverythingAsync(neighbour.Id, Now.AddHours(-1));

        await _rig.ChangeSitemapAsync(site.Id, Origin + "/new.xml");

        var targetsBefore = _rig.Website.Seen.Count;

        await engine.RunAsync();

        _rig.Website.Seen.Skip(targetsBefore).Select(request => request.Target)
            .Should().Contain("/neighbour.xml").And.NotContain("/new.xml");

        await engine.RunAsync();

        _rig.Asked("/new.xml").Should().Be(1);

        // Posts are kept across the change, and only a complete reading of the new one unlists any.
        (await RowAsync(site.Id)).Address.Should().Be(Origin + "/new.xml");
        (await _rig.PageAsync(site.Id, "/kept")).UnlistedAt.Should().BeNull();
        (await _rig.PageAsync(site.Id, "/kept")).Title.Should().Be("Kept");
        (await _rig.PageAsync(site.Id, "/dropped")).UnlistedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Clearing_The_Address_Forgets_The_Posts()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        var asked = _rig.Website.Seen.Count;

        await _rig.ChangeSitemapAsync(site.Id, string.Empty);

        (await engine.RunAsync()).Should().BeFalse();

        _rig.Website.Seen.Should().HaveCount(asked);
        (await _rig.RowAsync(site.Id)).Should().BeNull();
        (await _rig.PartsAsync(site.Id)).Should().BeEmpty();
        (await _rig.PagesAsync(site.Id)).Should().BeEmpty();
    }

    /// <summary>
    /// A website is read only while its account is being measured. When measurement stops, nothing is
    /// asked of the website on the account's behalf, and reading carries on by itself once measurement
    /// does.
    /// </summary>
    [Fact]
    public async Task A_Website_Whose_Account_Is_Not_Being_Measured_Is_Not_Read()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet());

        var site = await WebsiteAsync();
        var allowance = new SwitchedAllowance();
        var engine = _rig.Engine(configure: services => services.AddSingleton<IMeasurementAllowance>(allowance));

        (await engine.RunAsync()).Should().BeTrue();

        _rig.Website.Seen.Should().BeEmpty();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Waiting);
        row.NextPassAt.Should().BeCloseTo(Now + new SitemapOptions().RetryAfterFailure, TimeSpan.FromMinutes(1));

        allowance.Measuring = true;

        await LaterAsync(engine, new SitemapOptions().RetryAfterFailure);

        (await RowAsync(site.Id)).State.Should().Be(SitemapState.Read);
    }

    /// <summary>
    /// A closed account is kept whole so it can be brought back, and in that time nothing is fetched on
    /// its behalf, whether its website was being read already or was only named.
    /// </summary>
    [Fact]
    public async Task A_Closed_Accounts_Websites_Are_Not_Read()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet());

        var named = await WebsiteAsync();
        var read = await _rig.AddWebsiteAsync("other.example.test", "http://other.example.test/sitemap.xml");
        var engine = _rig.Engine();

        await _rig.CloseAccountAsync(named);

        (await engine.RunAsync()).Should().BeTrue();

        var asked = _rig.Website.Seen.Count;

        _rig.Website.Seen.Should().OnlyContain(request => request.Host == "other.example.test");
        (await _rig.RowAsync(named.Id)).Should().BeNull();

        await _rig.CloseAccountAsync(read);
        _rig.Clock.MoveOn(TimeSpan.FromHours(2));

        (await engine.RunAsync()).Should().BeFalse();

        _rig.Website.Seen.Should().HaveCount(asked);
    }

    /// <summary>
    /// Two accounts can name one website, and one website answers at both of its names. A claim is refused
    /// while another row for the same website holds one, so the website is asked one thing at a time
    /// however many rows name it. A row for another website is no hindrance.
    /// </summary>
    /// <param name="firstDomain">The first website's domain.</param>
    /// <param name="secondDomain">The second's.</param>
    [Theory]
    [InlineData("blog.example.test", "blog.example.test")]
    [InlineData("blog.example.test", "www.blog.example.test")]
    [InlineData("www.blog.example.test", "blog.example.test")]
    public async Task Two_Websites_On_One_Host_Are_Never_Read_At_Once(string firstDomain, string secondDomain)
    {
        var held = HoldAnswer("/first.xml", UrlSet());

        _rig.Website.Serve("/second.xml", UrlSet());

        var first = await _rig.AddWebsiteAsync(firstDomain, $"http://{firstDomain}/first.xml");

        var firstEngine = _rig.Engine();
        var pass = firstEngine.RunAsync();

        await held.Reached.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        var second = await _rig.AddWebsiteAsync(secondDomain, $"http://{secondDomain}/second.xml");
        var secondEngine = _rig.Engine();

        (await secondEngine.RunAsync()).Should().BeFalse();
        _rig.Asked("/second.xml").Should().Be(0);

        // Another website altogether goes ahead.
        _rig.Website.Serve("/third.xml", UrlSet());

        var third = await _rig.AddWebsiteAsync("third.example.test", "http://third.example.test/third.xml");

        (await secondEngine.RunAsync()).Should().BeTrue();
        (await RowAsync(third.Id)).State.Should().Be(SitemapState.Read);

        held.Release();
        await pass;

        (await RowAsync(first.Id)).State.Should().Be(SitemapState.Read);

        (await secondEngine.RunAsync()).Should().BeTrue();
        (await RowAsync(second.Id)).State.Should().Be(SitemapState.Read);
    }

    /// <summary>
    /// One engine's requests are kept apart by its pacer, and another engine knows nothing of that. It
    /// could otherwise pick up another row for the website the moment a pass lets go, and have its first
    /// request arrive within a moment of the last one. So the website stays taken for one more gap.
    /// </summary>
    [Fact]
    public async Task Another_Engine_Waits_Out_The_Gap_After_A_Pass_Lets_Go()
    {
        _rig.Website.Serve("/first.xml", UrlSet());
        _rig.Website.Serve("/second.xml", UrlSet());

        var gap = TimeSpan.FromSeconds(5);
        var settings = new SitemapOptions { RequestGap = gap };

        // Each engine's own requests are kept apart as closely as they can be, which leaves only the wait
        // being tested here between this test and its running time.
        static void QuicklyPaced(IServiceCollection services) =>
            services.AddSingleton(new Pacer(TimeProvider.System, TimeSpan.FromMilliseconds(1)));

        var first = await _rig.AddWebsiteAsync(Domain, Origin + "/first.xml");

        await _rig.Engine(settings, QuicklyPaced).RunAsync();

        (await RowAsync(first.Id)).ClaimToken.Should().BeNull();

        var second = await _rig.AddWebsiteAsync(Domain, Origin + "/second.xml");
        var other = _rig.Engine(settings, QuicklyPaced);

        (await other.RunAsync()).Should().BeFalse();
        _rig.Asked("/second.xml").Should().Be(0);

        var began = TimeProvider.System.GetTimestamp();

        while (!await other.RunAsync())
        {
            TimeProvider.System.GetElapsedTime(began).Should().BeLessThan(gap * 2);

            await Task.Delay(TimeSpan.FromMilliseconds(100), Cancellation.Token);
        }

        (await RowAsync(second.Id)).State.Should().Be(SitemapState.Read);

        var seen = _rig.Website.Seen;

        TimeProvider.System.GetElapsedTime(seen[1].At, seen[2].At)
            .Should().BeGreaterThan(gap * 0.8, "that is the first request to reach the website after the last one");
    }

    /// <summary>
    /// A claim let go of cannot be written under in the moment the website stays taken. All that is left
    /// of it is the wait.
    /// </summary>
    [Fact]
    public async Task A_Claim_Let_Go_Of_Cannot_Be_Written_Under()
    {
        var (site, engine, lease, _) = await AWebsiteClaimedPartWayThroughAsync();

        await engine.InScopeAsync(services => services.GetRequiredService<SitemapClaims>()
            .ReleaseAsync(lease, Now, Cancellation.Token));

        var row = await RowAsync(site.Id);

        row.ClaimToken.Should().BeNull();

        var act = () => engine.InScopeAsync(services => services.GetRequiredService<PostWriter>()
            .SettlePageAsync(lease, "/hello", PostPageOutcome.Gone, null, Now, Cancellation.Token));

        await act.Should().ThrowAsync<SitemapClaimLostException>();
    }

    /// <summary>
    /// Whether anybody holds a claim on a website, and writing a claim, are two steps on different rows.
    /// Claims side by side on different rows of one website are where each would find the way clear, so
    /// this goes after exactly that, again and again, at the same moment.
    /// </summary>
    /// <param name="twinned">Whether half the rows name the website's other name.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Claims_On_Different_Rows_Of_One_Website_At_The_Same_Moment_Go_To_One_Reader(bool twinned)
    {
        const int Rows = 6;

        var sites = new List<Site>();

        for (var row = 0; row < Rows; row++)
        {
            var domain = twinned && row % 2 == 1 ? "www." + Domain : Domain;

            sites.Add(await _rig.AddWebsiteAsync(domain, $"http://{domain}/sitemap.xml"));
        }

        var engine = _rig.Engine();

        await engine.ClaimAsync();
        await ReleaseEverythingAsync(sites[0].Id);

        var rows = new List<SitemapRead>();

        foreach (var site in sites)
        {
            rows.Add(await RowAsync(site.Id));
        }

        for (var round = 0; round < 25; round++)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var claims = rows.Select(row => Task.Run(
                async () =>
                {
                    await start.Task;

                    await using var scope = engine.Services.CreateAsyncScope();

                    return await scope.ServiceProvider.GetRequiredService<SitemapClaims>()
                        .TryClaimAsync(row, Cancellation.Token);
                },
                Cancellation.Token)).ToList();

            start.SetResult();

            (await Task.WhenAll(claims)).Count(lease => lease is not null).Should().Be(1);

            await _rig.SqlAsync($"UPDATE sitemap_reads SET claim_token = NULL, claimed_until = NULL");
        }
    }

    /// <summary>
    /// A website named by more rows than are tried at one look, at either of its names, does not hold up
    /// every other website while it is being read, however long those rows have waited.
    /// </summary>
    /// <param name="name">The name the other rows give it.</param>
    [Theory]
    [InlineData(Domain)]
    [InlineData("www." + Domain)]
    public async Task A_Website_Named_Many_Times_Does_Not_Hold_Up_The_Next_Website(string name)
    {
        var held = HoldAnswer("/busy.xml", UrlSet());

        var busy = await _rig.AddWebsiteAsync(Domain, $"{Origin}/busy.xml");
        var pass = _rig.Engine().RunAsync();

        await held.Reached.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        for (var row = 0; row < 12; row++)
        {
            await _rig.AddWebsiteAsync(Domain, $"http://{name}/busy.xml");
        }

        _rig.Website.Serve("/other.xml", UrlSet());

        var other = await _rig.AddWebsiteAsync("other.example.test", "http://other.example.test/other.xml");
        var engine = _rig.Engine();

        await engine.InScopeAsync(services =>
            services.GetRequiredService<SitemapClaims>().PrepareAsync(Cancellation.Token));

        await _rig.SqlAsync(
            $"UPDATE sitemap_reads SET next_pass_at = {Now.AddMinutes(-2)} WHERE site_id <> {other.Id} AND claim_token IS NULL");

        await _rig.SqlAsync($"UPDATE sitemap_reads SET next_pass_at = {Now.AddMinutes(-1)} WHERE site_id = {other.Id}");

        (await engine.RunAsync()).Should().BeTrue();
        (await RowAsync(other.Id)).State.Should().Be(SitemapState.Read);

        held.Release();
        await pass;

        (await RowAsync(busy.Id)).State.Should().Be(SitemapState.Read);
    }

    /// <summary>
    /// A file that cannot be unpacked is what it is, is written down as such, and costs that one request
    /// until the next reading. The rest of the sitemap is read all the same.
    /// </summary>
    [Fact]
    public async Task A_Corrupt_Part_Is_Written_Down_And_The_Rest_Is_Read()
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml"));
        _rig.Website.Serve("/post-sitemap1.xml", Corrupt(), "application/xml", "gzip");
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.PartlyRead);
        row.Problem.Should().Be(SitemapProblem.NotASitemap);
        (await _rig.PageAsync(site.Id, "/post")).Title.Should().Be("Post");

        await _rig.MakeDueAsync(site.Id);
        await engine.RunAsync();

        _rig.Asked("/post-sitemap1.xml").Should().Be(1, "it was tried in this reading already");
        engine.Log.GetSnapshot().Should().NotContain(record => record.Level >= LogLevel.Warning);
    }

    /// <summary>
    /// A sitemap's address answering with something that cannot be unpacked is the whole reading failing,
    /// and it is the next reading that asks again.
    /// </summary>
    [Fact]
    public async Task A_Corrupt_Address_Fails_The_Reading_And_Is_Not_Asked_For_Again_Until_The_Next()
    {
        _rig.Website.Serve("/sitemap.xml", Corrupt(), "application/xml", "gzip");

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.NotASitemap);
        row.CycleStartedAt.Should().BeNull();
        row.NextCycleAt.Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, TimeSpan.FromMinutes(1));

        await _rig.MakeDueAsync(site.Id);
        await engine.RunAsync();

        _rig.Asked("/sitemap.xml").Should().Be(1);
    }

    /// <summary>
    /// A body spoiled on its way says nothing about what the page is, so it does not stop it being a post,
    /// and it uses up an attempt, each waiting longer than the one before. When they have run out the page
    /// waits for the sitemap to say it has changed.
    /// </summary>
    [Fact]
    public async Task A_Corrupt_Page_Uses_Up_An_Attempt_And_Comes_Round_Again_Later()
    {
        var modified = Now.AddDays(-2);

        _rig.Website.Serve("/post-sitemap.xml", UrlSetOf(("/post", modified)));
        _rig.Website.Serve("/post", Corrupt(), Html, "gzip");

        var site = await WebsiteAsync("/post-sitemap.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        var first = await _rig.PageAsync(site.Id, "/post");

        first.PageOutcome.Should().Be(PostPageOutcome.NotAPage);
        first.PageType.Should().BeNull();
        first.IsPost.Should().BeTrue();
        first.PageAttempts.Should().Be(1);
        first.PageDueAt.Should().BeCloseTo(Now + SitemapRig.Quick.Cadence, TimeSpan.FromMinutes(1));

        // Looking again straight away finds nothing due.
        await _rig.MakeDueAsync(site.Id);
        await engine.RunAsync();

        _rig.Asked("/post").Should().Be(1);

        await LaterAsync(engine, SitemapRig.Quick.Cadence);

        _rig.Asked("/post").Should().Be(2);

        var second = await _rig.PageAsync(site.Id, "/post");

        second.PageAttempts.Should().Be(2);
        second.PageDueAt.Should().BeCloseTo(Now + (2 * SitemapRig.Quick.Cadence), TimeSpan.FromMinutes(1));

        await LaterAsync(engine, 2 * SitemapRig.Quick.Cadence);

        _rig.Asked("/post").Should().Be(3);

        var last = await _rig.PageAsync(site.Id, "/post");

        last.PageAttempts.Should().Be(3);
        last.PageDueAt.Should().BeNull();

        // However often the sitemap is read after that.
        await LaterAsync(engine, TimeSpan.FromHours(10));
        await LaterAsync(engine, TimeSpan.FromHours(10));

        _rig.Asked("/post").Should().Be(3);

        // Until the sitemap says the page has changed, which starts it over.
        _rig.Website.Serve("/post-sitemap.xml", UrlSetOf(("/post", modified.AddDays(1))));
        _rig.Website.Serve("/post", Article("Back", Now.AddDays(-3)), Html);

        await LaterAsync(engine, TimeSpan.FromHours(1));

        _rig.Asked("/post").Should().Be(4);

        var mended = await _rig.PageAsync(site.Id, "/post");

        mended.Title.Should().Be("Back");
        mended.PageAttempts.Should().Be(0);
    }

    /// <summary>Where an engine can die with a request in hand.</summary>
    public enum WhereAnEngineDies
    {
        /// <summary>Asking for the sitemap's address.</summary>
        AtTheAddress,

        /// <summary>Asking for a part of the sitemap.</summary>
        AtAPart,

        /// <summary>Asking for a page.</summary>
        AtAPage,
    }

    /// <summary>
    /// An engine that dies the moment its request reaches the website leaves the attempt written down, so
    /// the next engine does not ask again as soon as the claim has run out. An engine brought down by what
    /// a website sends is the one thing that could otherwise have this repeat for ever.
    /// </summary>
    /// <param name="where">Where it dies.</param>
    [Theory]
    [InlineData(WhereAnEngineDies.AtTheAddress)]
    [InlineData(WhereAnEngineDies.AtAPart)]
    [InlineData(WhereAnEngineDies.AtAPage)]
    public async Task An_Engine_Dying_With_A_Request_In_Hand_Has_Used_Up_The_Attempt(WhereAnEngineDies where)
    {
        var death = new EngineDeath();

        var target = where switch
        {
            WhereAnEngineDies.AtTheAddress => "/sitemap_index.xml",
            WhereAnEngineDies.AtAPart => "/post-sitemap.xml",
            _ => "/post",
        };

        var bodies = new Dictionary<string, (string Body, string Type)>
        {
            ["/sitemap_index.xml"] = (Index("/post-sitemap.xml"), "application/xml"),
            ["/post-sitemap.xml"] = (UrlSet("/post"), "application/xml"),
            ["/post"] = (Article("Post", Now.AddDays(-3)), Html),
        };

        foreach (var (address, (body, type)) in bodies)
        {
            _rig.Website.Answer(address, async context =>
            {
                if (address == target)
                {
                    death.Happen();
                }

                context.Response.ContentType = type;

                await context.Response.WriteAsync(body, context.RequestAborted);
            });
        }

        var site = await WebsiteAsync("/sitemap_index.xml");
        var dying = _rig.Engine(configure: services =>
            services.ConfigureDbContext<ControlPlaneDbContext>(options => options.AddInterceptors(death)));

        await Record.ExceptionAsync(dying.RunAsync);

        _rig.Asked(target).Should().Be(1);

        // Whatever it had said by then is what is there, and what it held runs out in its own time.
        (await RowAsync(site.Id)).ClaimToken.Should().NotBeNull();

        await _rig.ExpireClaimAsync(site.Id);
        await _rig.MakeDueAsync(site.Id);

        var nextEngine = _rig.Engine();

        await nextEngine.RunAsync();

        _rig.Asked(target).Should().Be(1, "the attempt was written down before the website was asked");
    }

    /// <summary>
    /// A fault here, and not at the website, costs what was in hand and nothing else: it is written in the
    /// log with no more than whose it was, what comes next goes ahead, and nothing asks again any sooner.
    /// </summary>
    [Fact]
    public async Task A_Fault_On_A_Part_Is_Written_Down_And_The_Next_Part_Is_Read()
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml"));
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");
        var engine = _rig.Engine(configure: ThrowingFor("/post-sitemap1.xml"));

        await engine.RunAsync();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.PartlyRead);
        row.Problem.Should().Be(SitemapProblem.TooSlow);
        (await _rig.PageAsync(site.Id, "/post")).Title.Should().Be("Post");

        var failed = engine.Log.GetSnapshot().Should().ContainSingle(record => record.Id.Id == 1306).Subject;

        failed.Level.Should().Be(LogLevel.Error);
        failed.Message.Should().Contain(site.Id.ToString());
    }

    /// <summary>
    /// The same for a page. Its attempt was written down before it was asked for, so it comes round when
    /// that said it would, and the pages after it go ahead.
    /// </summary>
    [Fact]
    public async Task A_Fault_On_A_Page_Is_Written_Down_And_The_Next_Page_Is_Read()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSetOf(("/first", Now.AddDays(-1)), ("/second", Now.AddDays(-2))));
        _rig.Website.Serve("/second", Article("Second", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();
        var engine = _rig.Engine(configure: ThrowingFor("/first"));

        await engine.RunAsync();

        var first = await _rig.PageAsync(site.Id, "/first");

        first.PageAttempts.Should().Be(1);
        first.PageDueAt.Should().BeCloseTo(Now + SitemapRig.Quick.Cadence, TimeSpan.FromMinutes(1));
        (await _rig.PageAsync(site.Id, "/second")).Title.Should().Be("Second");

        engine.Log.GetSnapshot().Should().ContainSingle(record => record.Id.Id == 1306);
    }

    /// <summary>The same for the address: the reading fails, says so, and comes round when it said it would.</summary>
    [Fact]
    public async Task A_Fault_On_The_Address_Fails_The_Reading_And_Comes_Round_Later()
    {
        var site = await WebsiteAsync();
        var engine = _rig.Engine(configure: ThrowingFor("/sitemap.xml"));

        (await engine.RunAsync()).Should().BeTrue();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.TooSlow);
        row.ClaimToken.Should().BeNull();
        row.NextCycleAt.Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, TimeSpan.FromMinutes(1));

        engine.Log.GetSnapshot().Should().ContainSingle(record => record.Id.Id == 1306);
    }

    /// <summary>
    /// A title and an address are written by whoever runs the website. Neither they nor anything quoting
    /// them reach the log, whatever happened, including an exception carrying them in its message.
    /// </summary>
    [Fact]
    public async Task Nothing_A_Website_Wrote_Reaches_The_Log()
    {
        const string Marker = "written-by-a-stranger";

        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml", "/post-sitemap3.xml"));
        _rig.Website.Serve("/post-sitemap1.xml", $"<urlset><url><loc>{Origin}/{Marker}</loc></url><broken {Marker}");
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet($"/{Marker}-2", "/faulty"));
        _rig.Website.Serve($"/{Marker}", Article(Marker, Now.AddDays(-3)), Html);
        _rig.Website.Serve($"/{Marker}-2", Corrupt(), Html, "gzip");

        await WebsiteAsync("/sitemap_index.xml");

        var engine = _rig.Engine(configure: ThrowingFor("/post-sitemap3.xml", Marker));

        await engine.RunAsync();

        var records = engine.Log.GetSnapshot();

        records.Should().Contain(record => record.Id.Id == 1306, "or this would prove nothing about a fault");
        records.Should().AllSatisfy(record =>
        {
            record.Message.Should().NotContain(Marker);
            record.Exception?.ToString().Should().NotContain(Marker);
        });
    }

    /// <summary>
    /// The website's answer starts at once and then never finishes. It is abandoned when its time is up,
    /// what had arrived is kept, the claim is let go of, and the next website gets its turn.
    /// </summary>
    [Fact]
    public async Task A_Website_That_Drips_Its_Sitemap_Is_Abandoned_At_The_Timeout()
    {
        var abandoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _rig.Website.Answer("/sitemap.xml", async context =>
        {
            context.RequestAborted.Register(() => abandoned.TrySetResult());
            context.Response.ContentType = "application/xml";

            await context.Response.WriteAsync($"<urlset><url><loc>{Origin}/arrived</loc></url><url>", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            while (!context.RequestAborted.IsCancellationRequested)
            {
                await context.Response.WriteAsync(" ", CancellationToken.None);
                await context.Response.Body.FlushAsync(CancellationToken.None);
                await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None);
            }
        });

        var site = await WebsiteAsync();
        var engine = _rig.Engine(Settings(sitemapTimeout: TimeSpan.FromSeconds(2)));

        (await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token)).Should().BeTrue();

        await abandoned.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.TooSlow);
        row.SucceededAt.Should().BeNull("it never got through the sitemap");
        row.ClaimToken.Should().BeNull("the lease has been let go of");
        row.NextCycleAt.Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, TimeSpan.FromMinutes(1));

        (await _rig.PageAsync(site.Id, "/arrived")).UnlistedAt.Should().BeNull();
        _rig.Asked("/arrived").Should().Be(0, "a website this slow is not asked for anything more just now");
    }

    /// <summary>The same for a page: the page comes round again, and the pass ends.</summary>
    [Fact]
    public async Task A_Website_That_Drips_A_Page_Is_Abandoned_At_The_Timeout()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));
        _rig.Website.Answer("/post", async context =>
        {
            context.Response.ContentType = Html;

            while (!context.RequestAborted.IsCancellationRequested)
            {
                await context.Response.WriteAsync(" ", CancellationToken.None);
                await context.Response.Body.FlushAsync(CancellationToken.None);
                await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None);
            }
        });

        var site = await WebsiteAsync();
        var engine = _rig.Engine(Settings(pageTimeout: TimeSpan.FromSeconds(2)));

        (await engine.RunAsync().WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token)).Should().BeTrue();

        var page = await _rig.PageAsync(site.Id, "/post");

        page.PageOutcome.Should().Be(PostPageOutcome.Unreachable);
        page.PageAttempts.Should().Be(1);

        (await RowAsync(site.Id)).ClaimToken.Should().BeNull();
    }

    [Fact]
    public async Task A_Sitemap_Robots_Txt_Disallows_Is_Not_Asked_For()
    {
        _rig.Website.Serve("/robots.txt", "User-agent: DewirideAnalytics\nDisallow: /\n", "text/plain");
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        _rig.Asked("/sitemap.xml").Should().Be(0);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.RobotsDisallowed);
        (await _rig.PagesAsync(site.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Part_Robots_Txt_Disallows_Is_Not_Asked_For_And_Says_So()
    {
        _rig.Website.Serve("/robots.txt", "User-agent: *\nDisallow: /post-sitemap2.xml\n", "text/plain");
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml"));
        _rig.Website.Serve("/post-sitemap1.xml", UrlSet());
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet());

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        _rig.Asked("/post-sitemap2.xml").Should().Be(0);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.PartlyRead);
        row.Problem.Should().Be(SitemapProblem.RobotsDisallowed);
    }

    /// <summary>
    /// A page robots.txt asks to be left alone is left alone: it is turned away without a request, there is
    /// no second try, and the pages it does not mention go ahead.
    /// </summary>
    [Fact]
    public async Task A_Page_Robots_Txt_Disallows_Is_Turned_Away_And_Not_Tried_Again()
    {
        _rig.Website.Serve("/robots.txt", "User-agent: DewirideAnalytics\nDisallow: /private/\n", "text/plain");
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/private/post", "/public/post"));
        _rig.Website.Serve("/private/post", Article("Private", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/public/post", Article("Public", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/post-sitemap.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();
        await LaterAsync(engine, TimeSpan.FromHours(5));

        _rig.Asked("/private/post").Should().Be(0);
        _rig.Asked("/public/post").Should().Be(1);

        var refused = await _rig.PageAsync(site.Id, "/private/post");

        refused.PageOutcome.Should().Be(PostPageOutcome.TurnedAway);
        refused.PageDueAt.Should().BeNull();
        refused.PageAttempts.Should().Be(0);
    }

    /// <summary>
    /// A server in trouble, one that cannot be reached, or a file that cannot be read says nothing about
    /// what may be read, so nothing is. It costs no attempts, and it is not asked again for an hour.
    /// </summary>
    [Fact]
    public async Task Nothing_Is_Asked_For_While_Robots_Txt_Is_In_Trouble()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        // A page falls due, and the file can no longer be read.
        await _rig.SqlAsync($"UPDATE site_posts SET page_due_at = {Now} WHERE site_id = {site.Id}");
        _rig.Website.Status("/robots.txt", StatusCodes.Status503ServiceUnavailable);

        var askedOfPost = _rig.Asked("/post");
        var askedOfSitemap = _rig.Asked("/sitemap.xml");
        var askedOfRobots = _rig.Asked("/robots.txt");

        // First with only the page due: nothing is known about it, and nothing is used up.
        await _rig.MakeDueAsync(site.Id);
        await engine.RunAsync();

        _rig.Asked("/post").Should().Be(askedOfPost);
        _rig.Asked("/robots.txt").Should().Be(askedOfRobots + 1);
        (await _rig.PageAsync(site.Id, "/post")).PageAttempts.Should().Be(0);
        (await RowAsync(site.Id)).NextPassAt
            .Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, TimeSpan.FromMinutes(1), "it is left alone for a while");

        // Then with the reading due too: the sitemap is not asked for either, and the reading says why.
        await LaterAsync(engine, SitemapRig.Quick.RetryAfterFailure);

        _rig.Asked("/sitemap.xml").Should().Be(askedOfSitemap);
        _rig.Asked("/post").Should().Be(askedOfPost);
        _rig.Asked("/robots.txt").Should().Be(askedOfRobots + 2);
        (await _rig.PageAsync(site.Id, "/post")).PageAttempts.Should().Be(0);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.ServerError);
        row.NextPassAt.Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Robots_Txt_Is_Read_Once_In_A_Pass()
    {
        _rig.Website.Serve("/robots.txt", "User-agent: *\nAllow: /\n", "text/plain");
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml"));
        _rig.Website.Serve("/post-sitemap1.xml", UrlSet("/one"));
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet("/two"));
        _rig.Website.Serve("/one", Article("One", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/two", Article("Two", Now.AddDays(-3)), Html);

        await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        _rig.Asked("/robots.txt").Should().Be(1);
    }

    /// <summary>
    /// Where a page redirects is an address like any other as far as robots.txt goes, and a sign-in page is what
    /// it most often comes to. It comes to what the page would have come to listed there itself: turned away,
    /// no title taken from something the website asked nobody to read, and no attempt spent.
    /// </summary>
    /// <param name="listed">What the sitemap lists.</param>
    /// <param name="leadsTo">Where that redirects to.</param>
    /// <param name="rule">What robots.txt says.</param>
    [Theory]
    [InlineData("/old", "/private/new", "Disallow: /private/")]
    [InlineData("/members/guide", "/wp-login.php?redirect_to=members", "Disallow: /wp-login.php")]
    public async Task A_Page_Redirecting_To_Where_Robots_Txt_Disallows_Is_Turned_Away(string listed, string leadsTo, string rule)
    {
        _rig.Website.Serve("/robots.txt", $"User-agent: *\n{rule}\n", "text/plain");
        _rig.Website.Serve("/post-sitemap.xml", UrlSet(listed, "/public"));
        _rig.Website.Redirect(listed, leadsTo);
        _rig.Website.Serve(leadsTo, Article("Log In", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/public", Article("Public", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/post-sitemap.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();
        await LaterAsync(engine, TimeSpan.FromHours(5));

        _rig.Asked(listed).Should().Be(1);
        _rig.Asked(leadsTo).Should().Be(0);

        var refused = await _rig.PageAsync(site.Id, listed);

        refused.Title.Should().BeNull();
        refused.PageOutcome.Should().Be(PostPageOutcome.TurnedAway);
        refused.PageDueAt.Should().BeNull();
        refused.PageAttempts.Should().Be(0);

        (await _rig.PageAsync(site.Id, "/public")).Title.Should().Be("Public");
        (await RowAsync(site.Id)).State.Should().Be(SitemapState.Read);
    }

    /// <summary>
    /// A page the website redirects to another of its own paths answers at the second, which is where a
    /// browser following the link ends and what a visit is recorded under. The page is read once, so where
    /// it landed is kept as it is read, beside the path the sitemap lists.
    /// </summary>
    [Fact]
    public async Task Where_A_Redirected_Page_Landed_Is_Kept_Beside_The_Path_Listed()
    {
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/moved", "/straight"));
        _rig.Website.Redirect("/moved", "/moved/", StatusCodes.Status301MovedPermanently);
        _rig.Website.Serve("/moved/", Article("Moved", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/straight", Article("Straight", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/post-sitemap.xml");

        await _rig.Engine().RunAsync();

        var moved = await _rig.PageAsync(site.Id, "/moved");

        moved.Title.Should().Be("Moved");
        moved.LandedPath.Should().Be("/moved/");
        (await _rig.PageAsync(site.Id, "/straight")).LandedPath.Should().BeNull();
    }

    /// <summary>The same rule with nothing against where it leads, and the page is read from there.</summary>
    [Fact]
    public async Task A_Page_Redirecting_To_Where_Robots_Txt_Permits_Is_Read()
    {
        _rig.Website.Serve("/robots.txt", "User-agent: *\nDisallow: /private/\n", "text/plain");
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/old"));
        _rig.Website.Redirect("/old", "/new");
        _rig.Website.Serve("/new", Article("Moved", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/post-sitemap.xml");

        await _rig.Engine().RunAsync();

        (await _rig.PageAsync(site.Id, "/old")).Title.Should().Be("Moved");
    }

    /// <summary>
    /// A website's other name has a robots.txt of its own, which is read once it comes to that, and a pass
    /// asks it once however many pages lead there.
    /// </summary>
    [Fact]
    public async Task A_Page_Redirecting_To_The_Websites_Other_Name_Is_Held_To_What_That_Name_Says()
    {
        AnswerByName("/robots.txt", Text("User-agent: *\nDisallow:\n"), Text("User-agent: *\nDisallow: /\n"));
        AnswerByName("/one", RedirectToTheOtherName("/one"), Text(Article("One", Now.AddDays(-3)), Html));
        AnswerByName("/two", RedirectToTheOtherName("/two"), Text(Article("Two", Now.AddDays(-3)), Html));
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/one", "/two"));

        var site = await WebsiteAsync("/post-sitemap.xml");

        await _rig.Engine().RunAsync();

        AskedOfTheOtherName("/robots.txt").Should().Be(1);
        AskedOfTheOtherName("/one").Should().Be(0);
        AskedOfTheOtherName("/two").Should().Be(0);

        var refused = await _rig.PageAsync(site.Id, "/one");

        refused.Title.Should().BeNull();
        refused.PageOutcome.Should().Be(PostPageOutcome.TurnedAway);
        refused.PageAttempts.Should().Be(0);
    }

    /// <summary>
    /// A file of an index that redirects is held to the same, and says what it would have said had it been
    /// named there in the first place. The rest of the index is read.
    /// </summary>
    [Fact]
    public async Task A_File_Redirecting_To_Where_Robots_Txt_Disallows_Is_Not_Asked_For_And_Says_So()
    {
        _rig.Website.Serve("/robots.txt", "User-agent: *\nDisallow: /private/\n", "text/plain");
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml"));
        _rig.Website.Serve("/post-sitemap1.xml", UrlSet("/post"));
        _rig.Website.Redirect("/post-sitemap2.xml", "/private/post-sitemap2.xml");
        _rig.Website.Serve("/private/post-sitemap2.xml", UrlSet("/private/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        _rig.Asked("/private/post-sitemap2.xml").Should().Be(0);
        (await _rig.PartsAsync(site.Id)).Single(part => part.Address.EndsWith("2.xml", StringComparison.Ordinal))
            .Outcome.Should().Be(SitemapPartOutcome.RobotsDisallowed);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.PartlyRead);
        row.Problem.Should().Be(SitemapProblem.RobotsDisallowed);
    }

    [Fact]
    public async Task An_Address_Redirecting_To_Where_Robots_Txt_Disallows_Is_Not_Asked_For_And_Says_So()
    {
        _rig.Website.Serve("/robots.txt", "User-agent: *\nDisallow: /internal/\n", "text/plain");
        _rig.Website.Redirect("/sitemap.xml", "/internal/sitemap.xml");
        _rig.Website.Serve("/internal/sitemap.xml", UrlSet("/post"));

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        _rig.Asked("/internal/sitemap.xml").Should().Be(0);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.RobotsDisallowed);
        (await _rig.PagesAsync(site.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_Address_Redirecting_To_The_Websites_Other_Name_Is_Held_To_What_That_Name_Says()
    {
        AnswerByName("/robots.txt", Text("User-agent: *\nDisallow:\n"), Text("User-agent: *\nDisallow: /\n"));
        AnswerByName("/sitemap.xml", RedirectToTheOtherName("/sitemap.xml"), Text(UrlSet("/post"), "application/xml"));

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        AskedOfTheOtherName("/robots.txt").Should().Be(1);
        AskedOfTheOtherName("/sitemap.xml").Should().Be(0);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.RobotsDisallowed);
    }

    /// <summary>
    /// Trouble reading the other name's file on the way to a page is trouble with the file, and comes to what it
    /// comes to when it is the first thing asked: nothing is known, so nothing is read and the website is left
    /// alone, and however few attempts the page had left, it still has them. An attempt had been written
    /// for it by then, because nobody knew it would redirect.
    /// </summary>
    /// <param name="attemptsBefore">How many attempts the page had used before this.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task A_Page_Redirecting_To_A_Name_Whose_Robots_Txt_Is_In_Trouble_Costs_No_Attempt(int attemptsBefore)
    {
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/one", "/two"));
        _rig.Website.Serve("/one", Article("One", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/two", Article("Two", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/post-sitemap.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        // Both have just come due again, and now they lead to a name whose file can not be read.
        await _rig.SqlAsync(
            $"UPDATE site_posts SET page_attempts = {(short)attemptsBefore}, page_due_at = {Now} WHERE site_id = {site.Id}");
        AnswerByName("/robots.txt", Text("User-agent: *\nDisallow:\n"), Status(StatusCodes.Status503ServiceUnavailable));
        AnswerByName("/one", RedirectToTheOtherName("/one"), Text(Article("One again", Now.AddDays(-3)), Html));
        AnswerByName("/two", RedirectToTheOtherName("/two"), Text(Article("Two again", Now.AddDays(-3)), Html));

        var askedBefore = _rig.Asked("/one") + _rig.Asked("/two");

        await _rig.MakeDueAsync(site.Id);
        await engine.RunAsync();

        (_rig.Asked("/one") + _rig.Asked("/two")).Should().Be(askedBefore + 1, "the website is left alone after the first");
        AskedOfTheOtherName("/one").Should().Be(0);
        AskedOfTheOtherName("/two").Should().Be(0);

        var inTrouble = TimeSpan.FromMinutes(1);

        (await RowAsync(site.Id)).NextPassAt.Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, inTrouble);

        var pages = await _rig.PagesAsync(site.Id);

        pages.Should().AllSatisfy(page => page.PageAttempts.Should().Be((short)attemptsBefore));
        pages.Should().AllSatisfy(page => page.PageDueAt.Should().NotBeNull());

        // The trouble passes, and each is read as if none of it had happened.
        _rig.Website.Answer("/robots.txt", Text("User-agent: *\nDisallow:\n"));

        await LaterAsync(engine, SitemapRig.Quick.RetryAfterFailure);

        (await _rig.PageAsync(site.Id, "/one")).Title.Should().Be("One again");
        (await _rig.PageAsync(site.Id, "/two")).Title.Should().Be("Two again");
    }

    /// <summary>
    /// The same for a sitemap's address and for a file of an index, where what the trouble was is what the
    /// reading has to say, and a wait the file asked for is heard from there as well.
    /// </summary>
    /// <param name="status">What the other name's robots.txt answers.</param>
    /// <param name="problem">What the reading says.</param>
    [Theory]
    [InlineData(StatusCodes.Status503ServiceUnavailable, SitemapProblem.ServerError)]
    [InlineData(StatusCodes.Status429TooManyRequests, SitemapProblem.TurnedAway)]
    public async Task An_Address_Redirecting_To_A_Name_Whose_Robots_Txt_Is_In_Trouble_Says_What_The_Trouble_Is(int status, SitemapProblem problem)
    {
        AnswerByName("/robots.txt", Text("User-agent: *\nDisallow:\n"), AskingForAWait(status, 3));
        AnswerByName("/sitemap.xml", RedirectToTheOtherName("/sitemap.xml"), Text(UrlSet("/post"), "application/xml"));

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        AskedOfTheOtherName("/robots.txt").Should().Be(1);
        AskedOfTheOtherName("/sitemap.xml").Should().Be(0);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(problem);
        row.NextCycleAt.Should().BeCloseTo(Now + TimeSpan.FromHours(3), TimeSpan.FromMinutes(1));
        row.NextPassAt.Should().BeCloseTo(Now + TimeSpan.FromHours(3), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task A_File_Redirecting_To_A_Name_Whose_Robots_Txt_Is_In_Trouble_Says_What_The_Trouble_Is()
    {
        AnswerByName("/robots.txt", Text("User-agent: *\nDisallow:\n"), Status(StatusCodes.Status503ServiceUnavailable));
        AnswerByName("/post-sitemap.xml", RedirectToTheOtherName("/post-sitemap.xml"), Text(UrlSet(), "application/xml"));
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap.xml"));

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        AskedOfTheOtherName("/post-sitemap.xml").Should().Be(0);
        (await _rig.PartsAsync(site.Id)).Should().ContainSingle().Which.Outcome.Should().Be(SitemapPartOutcome.ServerError);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.ServerError);
    }

    /// <summary>
    /// A robots.txt that leads off the website or never lands is about that file, and nothing about the sitemap
    /// would help. Nothing is read, as with any other file that cannot be, and the website is left alone.
    /// </summary>
    /// <param name="location">Where the file redirects to.</param>
    [Theory]
    [InlineData("http://other.test/robots.txt")]
    [InlineData("/robots.txt")]
    public async Task A_Robots_Txt_That_Can_Not_Be_Followed_Is_What_The_Reading_Says_Stopped_It(string location)
    {
        _rig.Website.Redirect("/robots.txt", location);
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        _rig.Asked("/sitemap.xml").Should().Be(0);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.RobotsUnreadable);
        row.NextPassAt.Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// The same met on the way to a file named on the website's other name, and on the way to the file through
    /// a redirect. The word has to survive being written down as a part's outcome and read back as the reading's.
    /// </summary>
    /// <param name="redirects">Whether the file is only reached by a redirect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_File_On_A_Name_Whose_Robots_Txt_Can_Not_Be_Followed_Says_That_Stopped_It(bool redirects)
    {
        AnswerByName("/robots.txt", Text("User-agent: *\nDisallow:\n"), RedirectTo("http://other.test/robots.txt"));
        AnswerByName("/post-sitemap.xml", RedirectToTheOtherName("/post-sitemap.xml"), Text(UrlSet(), "application/xml"));
        _rig.Website.Serve(
            "/sitemap_index.xml",
            Index(redirects ? "/post-sitemap.xml" : $"http://{OtherName}/post-sitemap.xml"));

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        AskedOfTheOtherName("/post-sitemap.xml").Should().Be(0);
        (await _rig.PartsAsync(site.Id)).Should().ContainSingle().Which.Outcome.Should().Be(SitemapPartOutcome.RobotsUnreadable);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.RobotsUnreadable);
    }

    /// <summary>A website that is down has its own word, which is not this one, as does a file not being let out.</summary>
    /// <param name="status">What robots.txt answers.</param>
    /// <param name="problem">What the reading says.</param>
    [Theory]
    [InlineData(StatusCodes.Status500InternalServerError, SitemapProblem.ServerError)]
    [InlineData(StatusCodes.Status503ServiceUnavailable, SitemapProblem.ServerError)]
    public async Task A_Robots_Txt_In_Any_Other_Trouble_Says_What_The_Trouble_Is(int status, SitemapProblem problem)
    {
        _rig.Website.Status("/robots.txt", status);

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        (await RowAsync(site.Id)).Problem.Should().Be(problem);
    }

    /// <summary>
    /// A 429 says nothing about whether there is a file, and the file it stands in front of can say anything at
    /// all. So nothing is taken as permitted: a website whose file does say to stay out is not read for
    /// having been busy the one time it was asked.
    /// </summary>
    [Fact]
    public async Task Nothing_Is_Read_While_Robots_Txt_Says_It_Is_Being_Asked_Too_Often()
    {
        _rig.Website.Answer("/robots.txt", AskingForAWait(StatusCodes.Status429TooManyRequests, 2));
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        _rig.Asked("/sitemap.xml").Should().Be(0);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.TurnedAway);
        row.NextPassAt.Should().BeCloseTo(Now + TimeSpan.FromHours(2), TimeSpan.FromMinutes(1));

        _rig.Website.Serve("/robots.txt", "User-agent: *\nDisallow: /\n", "text/plain");

        await LaterAsync(engine, TimeSpan.FromHours(2));

        _rig.Asked("/sitemap.xml").Should().Be(0);
        _rig.Asked("/post").Should().Be(0);
        (await RowAsync(site.Id)).Problem.Should().Be(SitemapProblem.RobotsDisallowed);
    }

    /// <summary>The same with no word on how long, which is still saying it.</summary>
    [Fact]
    public async Task Robots_Txt_Saying_It_Is_Being_Asked_Too_Often_Leaves_The_Website_Alone_For_The_Usual_Time()
    {
        _rig.Website.Status("/robots.txt", StatusCodes.Status429TooManyRequests);

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        var row = await RowAsync(site.Id);

        row.Problem.Should().Be(SitemapProblem.TurnedAway);
        row.NextPassAt.Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// A website under maintenance saying to come back tomorrow is taken at its word, and robots.txt is the first
    /// thing every pass asks. It is asked for nothing else in that time, and that one file is not asked again
    /// each hour until then either. What 4xx says about the file is that there is none, and that does not
    /// count for much once it has said to go away for a while.
    /// </summary>
    /// <param name="status">What it answers.</param>
    /// <param name="problem">What the reading says.</param>
    [Theory]
    [InlineData(StatusCodes.Status503ServiceUnavailable, SitemapProblem.ServerError)]
    [InlineData(StatusCodes.Status429TooManyRequests, SitemapProblem.TurnedAway)]
    [InlineData(StatusCodes.Status403Forbidden, SitemapProblem.TurnedAway)]
    [InlineData(StatusCodes.Status404NotFound, SitemapProblem.TurnedAway)]
    public async Task A_Wait_Asked_For_On_Robots_Txt_Is_Heard_Before_The_Address_Is_Asked_For(int status, SitemapProblem problem)
    {
        _rig.Website.Answer("/robots.txt", AskingForAWait(status, 24));
        _rig.Website.Serve("/sitemap.xml", UrlSet());

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        _rig.Website.Seen.Should().ContainSingle().Which.Target.Should().Be("/robots.txt");

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(problem);
        row.NextCycleAt.Should().BeCloseTo(Now + TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
        row.NextPassAt.Should().BeCloseTo(Now + TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));

        await LaterAsync(engine, TimeSpan.FromHours(23));

        _rig.Website.Seen.Should().ContainSingle("it asked for a day");
    }

    /// <summary>A page's attempts are its own, and a file that said to wait has had nothing to say about the page.</summary>
    [Fact]
    public async Task A_Wait_Asked_For_On_Robots_Txt_Is_Heard_Before_A_Page_Is_Asked_For()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        await _rig.SqlAsync($"UPDATE site_posts SET page_due_at = {Now} WHERE site_id = {site.Id}");
        _rig.Website.Answer("/robots.txt", AskingForAWait(StatusCodes.Status503ServiceUnavailable, 3));

        var asked = _rig.Asked("/post");

        await _rig.MakeDueAsync(site.Id);
        await engine.RunAsync();

        _rig.Asked("/post").Should().Be(asked);
        (await _rig.PageAsync(site.Id, "/post")).PageAttempts.Should().Be(0);
        (await RowAsync(site.Id)).NextPassAt.Should().BeCloseTo(Now + TimeSpan.FromHours(3), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// The reading stays open, with what has not been asked for still waiting, and takes up where it stopped
    /// once the wait is over.
    /// </summary>
    [Fact]
    public async Task A_Wait_Asked_For_On_Robots_Txt_Is_Heard_Before_A_File_Is_Asked_For()
    {
        AnswerByName("/robots.txt", Text("User-agent: *\nDisallow:\n"), AskingForAWait(StatusCodes.Status503ServiceUnavailable, 3));
        _rig.Website.Serve(
            "/sitemap_index.xml",
            Index($"http://{OtherName}/post-sitemap1.xml", $"http://{OtherName}/post-sitemap2.xml"));

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        _rig.Asked("/post-sitemap1.xml").Should().Be(0);
        _rig.Asked("/post-sitemap2.xml").Should().Be(0);

        var row = await RowAsync(site.Id);

        row.CycleStartedAt.Should().NotBeNull();
        row.NextPassAt.Should().BeCloseTo(Now + TimeSpan.FromHours(3), TimeSpan.FromMinutes(1));

        var parts = await _rig.PartsAsync(site.Id);

        parts.Count(part => part.Outcome == SitemapPartOutcome.ServerError).Should().Be(1);
        parts.Count(part => part.ReadAt is null).Should().Be(1);
    }

    /// <summary>
    /// A firewall's challenge comes back as a 202, with a page of its own that is nothing the website published.
    /// It is the website not letting the reader in, which comes round again like any other turning away, so a
    /// page that was never read is read once the owner has let the reader through.
    /// </summary>
    [Fact]
    public async Task A_Challenge_Answered_As_A_Success_Is_Not_The_Page_And_The_Page_Is_Read_Once_It_Stops()
    {
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/post"));
        _rig.Website.Answer("/post", context =>
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            context.Response.ContentType = Html;
            context.Response.Headers["x-amzn-waf-action"] = "challenge";

            return context.Response.WriteAsync("<html><head><title>Human Verification</title></head></html>", context.RequestAborted);
        });

        var site = await WebsiteAsync("/post-sitemap.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        var challenged = await _rig.PageAsync(site.Id, "/post");

        challenged.Title.Should().BeNull();
        challenged.PageOutcome.Should().Be(PostPageOutcome.TurnedAway);
        challenged.PageDueAt.Should().NotBeNull();
        challenged.PageAttempts.Should().Be(1);

        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        await LaterAsync(engine, SitemapRig.Quick.RetryAfterFailure);

        (await _rig.PageAsync(site.Id, "/post")).Title.Should().Be("Post");
    }

    /// <summary>Several of them in a row is a website in trouble like any other.</summary>
    [Fact]
    public async Task A_Website_Answering_Every_Page_With_A_Challenge_Costs_A_Few_Attempts_And_No_More()
    {
        var paths = Enumerable.Range(1, 8).Select(number => $"/page-{number}").ToArray();

        _rig.Website.Serve("/post-sitemap.xml", UrlSet(paths));

        foreach (var path in paths)
        {
            _rig.Website.Status(path, StatusCodes.Status202Accepted);
        }

        var site = await WebsiteAsync("/post-sitemap.xml");

        await _rig.Engine().RunAsync();

        paths.Sum(_rig.Asked).Should().Be(3);

        (await RowAsync(site.Id)).NextPassAt
            .Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, TimeSpan.FromMinutes(1));
    }

    /// <summary>A file of an index comes to the same, where it would otherwise read as not being a sitemap.</summary>
    [Fact]
    public async Task A_File_Answering_With_A_Challenge_Is_Turned_Away_And_Not_Taken_For_Something_Else()
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap.xml"));
        _rig.Website.Answer("/post-sitemap.xml", context =>
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;

            return context.Response.WriteAsync("<html></html>", context.RequestAborted);
        });

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        (await _rig.PartsAsync(site.Id)).Should().ContainSingle().Which.Outcome.Should().Be(SitemapPartOutcome.TurnedAway);
        (await RowAsync(site.Id)).Problem.Should().Be(SitemapProblem.TurnedAway);
    }

    /// <summary>
    /// A tag may hold bytes past ASCII and a request may not. One of those kept would be refused at this end every
    /// time it was asked with, reading as the website not answering, with nothing able to replace it because
    /// nothing is ever asked. So none is kept, and what that costs is reading the answer in full each time.
    /// </summary>
    /// <param name="sitemap">The address the sitemap is at.</param>
    [Theory]
    [InlineData("/sitemap.xml")]
    [InlineData("/sitemap_index.xml")]
    public async Task A_Sitemap_Whose_Tags_Hold_More_Than_Ascii_Goes_On_Being_Read(string sitemap)
    {
        ServeWithATagPastAscii("/sitemap.xml", UrlSet("/post"), "application/xml");
        ServeWithATagPastAscii("/sitemap_index.xml", Index("/post-sitemap.xml"), "application/xml");
        ServeWithATagPastAscii("/post-sitemap.xml", UrlSet("/post"), "application/xml");
        ServeWithATagPastAscii("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync(sitemap);
        var engine = _rig.Engine();

        await engine.RunAsync();

        (await RowAsync(site.Id)).RootEtag.Should().BeNull();
        (await _rig.PartsAsync(site.Id)).Should().AllSatisfy(part => part.Etag.Should().BeNull());
        (await _rig.PageAsync(site.Id, "/post")).PageEtag.Should().BeNull();

        var asked = _rig.Asked(sitemap);

        await NextReadingAsync(engine);

        _rig.Asked(sitemap).Should().Be(asked + 1);
        _rig.Website.Seen.Should().AllSatisfy(request => request.Headers.Should().NotContainKey("If-None-Match"));

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Read);
        row.Problem.Should().BeNull();
    }

    /// <summary>
    /// A file that goes on longer than anything in a sitemap does is not a sitemap, which the whole way through
    /// the reader comes to what it comes to for any other. A file that does so partway through has the pages it
    /// listed before that kept, and has not been read to its end.
    /// </summary>
    [Fact]
    public async Task A_File_With_A_Tag_Longer_Than_Anything_In_A_Sitemap_Is_Not_A_Sitemap()
    {
        var space = new string(' ', MarkupGuardStream.LongestStretch + 1);

        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml"));
        _rig.Website.Serve("/post-sitemap1.xml", "<urlset" + space + "></urlset>");
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet("/post").Replace("</urlset>", "<url" + space + "/></urlset>", StringComparison.Ordinal));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine(new SitemapOptions { RequestGap = SitemapRig.Quick.RequestGap }).RunAsync();

        (await _rig.PartsAsync(site.Id)).Should().AllSatisfy(part => part.Outcome.Should().Be(SitemapPartOutcome.NotASitemap));

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.NotASitemap);

        (await _rig.PageAsync(site.Id, "/post")).Title.Should().Be("Post");
    }

    /// <summary>
    /// A website saying what it sent is not a web page has said what it is. Saying it compressed it with
    /// something that is not read has said no such thing, any more than a compressed body that turns out to be
    /// corrupt has, so it comes round again and goes on counting.
    /// </summary>
    /// <param name="encoding">What the website says it compressed it with.</param>
    [Theory]
    [InlineData("zstd")]
    [InlineData("gzip, gzip")]
    [InlineData("none")]
    [InlineData("utf-8")]
    public async Task A_Page_Compressed_With_Something_That_Is_Not_Read_Has_Not_Said_It_Is_Not_A_Post(string encoding)
    {
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Encoding.UTF8.GetBytes(Article("Post", Now.AddDays(-3))), Html, encoding);

        var site = await WebsiteAsync("/post-sitemap.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        var unread = await _rig.PageAsync(site.Id, "/post");

        unread.PageOutcome.Should().Be(PostPageOutcome.NotAPage);
        unread.PageType.Should().BeNull();
        unread.IsPost.Should().BeTrue();
        unread.PageDueAt.Should().NotBeNull();
        unread.PageAttempts.Should().Be(1);

        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        await LaterAsync(engine, SitemapRig.Quick.Cadence);

        (await _rig.PageAsync(site.Id, "/post")).Title.Should().Be("Post");
    }

    /// <summary>
    /// A sitemap speaks for its own host, and what is registered as the website can be far wider. Whatever
    /// a sitemap lists off its own host, its other name aside, is skipped and counted, nothing is sent
    /// there, and a reading is still complete for having skipped it.
    /// </summary>
    [Fact]
    public async Task What_A_Sitemap_Lists_Off_Its_Own_Host_Is_Skipped_And_Counted()
    {
        _rig.Website.Serve(
            "/sitemap.xml",
            UrlSet(
                "/kept",
                "http://www.blog.example.test/twin",
                "http://shop.example.test/sibling",
                "http://example.test/parent",
                "http://shop.blog.example.test/child",
                "http://other.test/elsewhere",
                "ftp://blog.example.test/not-the-web",
                "not an address at all"));

        _rig.Website.Serve("/kept", Article("Kept", Now.AddDays(-3)), Html);
        _rig.Website.Serve("/twin", Article("Twin", Now.AddDays(-3)), Html);

        var site = await _rig.AddWebsiteAsync("example.test", Origin + "/sitemap.xml");

        await _rig.Engine().RunAsync();

        (await _rig.PagesAsync(site.Id)).Select(page => page.Path).Should().Equal("/kept", "/twin");
        (await _rig.PartsAsync(site.Id)).Should().ContainSingle().Which.OffSiteEntries.Should().Be(6);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.PartlyRead);
        row.Problem.Should().Be(SitemapProblem.LeftTheWebsite);

        _rig.Website.Seen.Select(request => request.Host).Should().OnlyContain(host => host.EndsWith(Domain, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_Part_An_Index_Names_Off_Its_Own_Host_Is_Skipped_And_Counted()
    {
        _rig.Website.Serve(
            "/sitemap_index.xml",
            Index(
                "/post-sitemap.xml",
                "http://shop.example.test/post-sitemap2.xml",
                "http://www.blog.example.test/post-sitemap3.xml"));

        _rig.Website.Serve("/post-sitemap.xml", UrlSet());
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet());
        _rig.Website.Serve("/post-sitemap3.xml", UrlSet());

        var site = await _rig.AddWebsiteAsync("example.test", Origin + "/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        _rig.Asked("/post-sitemap2.xml").Should().Be(0);
        _rig.Asked("/post-sitemap3.xml").Should().Be(1);

        (await _rig.PartsAsync(site.Id)).Should().HaveCount(2);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.PartlyRead);
        row.Problem.Should().Be(SitemapProblem.LeftTheWebsite);
        row.RootProblem.Should().Be(SitemapProblem.LeftTheWebsite);
    }

    /// <summary>
    /// A list that leads nowhere but off the website has said nothing about the website's posts, so it
    /// fails, and whatever an earlier reading found stays where it is.
    /// </summary>
    [Fact]
    public async Task An_Index_Leading_Only_Off_The_Website_Fails_And_Unlists_Nothing()
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap.xml"));
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await _rig.AddWebsiteAsync("example.test", Origin + "/sitemap_index.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        _rig.Website.Serve("/sitemap_index.xml", Index("http://shop.example.test/post-sitemap.xml"));

        await NextReadingAsync(engine);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.LeftTheWebsite);
        (await _rig.PageAsync(site.Id, "/post")).UnlistedAt.Should().BeNull();
        (await _rig.PartsAsync(site.Id)).Should().ContainSingle();
    }

    /// <summary>
    /// Permitted hosts replace a website's domain. One that named a sitemap and has since stopped
    /// permitting its host is a website the address is no longer on, so nothing is asked of it.
    /// </summary>
    [Fact]
    public async Task A_Sitemap_On_A_Host_The_Website_No_Longer_Permits_Is_Not_Asked_For()
    {
        var site = await _rig.AddWebsiteAsync(
            "example.test",
            Origin + "/sitemap.xml",
            configure: website => website.ReplaceAllowedOrigins(["shop.example.test"]));

        await _rig.Engine().RunAsync();

        _rig.Website.Seen.Should().BeEmpty();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.LeftTheWebsite);
    }

    /// <summary>A website permitting only some hosts is read on those, whatever else it is registered as.</summary>
    [Fact]
    public async Task A_Sitemap_On_A_Host_The_Website_Permits_Is_Read()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await _rig.AddWebsiteAsync(
            "example.test",
            Origin + "/sitemap.xml",
            configure: website => website.ReplaceAllowedOrigins([Domain]));

        await _rig.Engine().RunAsync();

        (await RowAsync(site.Id)).State.Should().Be(SitemapState.Read);
        (await _rig.PageAsync(site.Id, "/post")).Title.Should().Be("Post");
    }

    /// <summary>
    /// The same from one list of pages: every entry on another host says nothing about this website, so
    /// it fails, and what an earlier reading found stays where it is.
    /// </summary>
    [Fact]
    public async Task A_Sitemap_Listing_Only_Other_Hosts_Fails_And_Unlists_Nothing()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await _rig.AddWebsiteAsync("example.test", Origin + "/sitemap.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        _rig.Website.Serve("/sitemap.xml", UrlSet("http://shop.example.test/post"));

        await NextReadingAsync(engine);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.LeftTheWebsite);
        (await _rig.PageAsync(site.Id, "/post")).UnlistedAt.Should().BeNull();
    }

    /// <summary>
    /// An index naming another index is not followed. That could go on for as long as somebody cared to
    /// write, and no publisher's sitemap needs it.
    /// </summary>
    [Fact]
    public async Task An_Index_Inside_An_Index_Is_Not_Followed()
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/inner_index.xml"));
        _rig.Website.Serve("/inner_index.xml", Index("/post-sitemap.xml"));
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/post"));

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        _rig.Asked("/post-sitemap.xml").Should().Be(0);

        var row = await RowAsync(site.Id);

        row.Problem.Should().Be(SitemapProblem.NotASitemap);
        (await _rig.PartsAsync(site.Id)).Should().ContainSingle()
            .Which.Outcome.Should().Be(SitemapPartOutcome.NotASitemap);
    }

    /// <summary>
    /// The collector knows a page by its path alone, so posts told apart only by a query string would all
    /// be one page to it. A website with nothing but those gets told why it found no posts.
    /// </summary>
    [Fact]
    public async Task Addresses_Told_Apart_Only_By_A_Query_Are_Skipped_And_Say_Why()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/?p=1", "/?p=2"));

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        (await _rig.PagesAsync(site.Id)).Should().BeEmpty();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.PartlyRead);
        row.Problem.Should().Be(SitemapProblem.AddressesWithQueries);
    }

    /// <summary>A website with some readable addresses has those counted, and nothing to fix for them.</summary>
    [Fact]
    public async Task Addresses_With_A_Query_Beside_Readable_Ones_Are_Skipped_In_Silence()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/?p=1", "/readable"));
        _rig.Website.Serve("/readable", Article("Readable", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        (await _rig.PagesAsync(site.Id)).Should().ContainSingle();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Read);
        row.Problem.Should().BeNull();
    }

    /// <summary>The same address twice in one file would otherwise be one statement changing one row twice.</summary>
    [Fact]
    public async Task An_Address_Listed_Twice_Is_One_Page()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post", "/post", "/post#fragment"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        (await RowAsync(site.Id)).State.Should().Be(SitemapState.Read);
        (await _rig.PagesAsync(site.Id)).Should().ContainSingle();
        _rig.Asked("/post").Should().Be(1);
    }

    /// <summary>A file naming more parts than are read has the rest left out, and nothing can be unlisted.</summary>
    [Fact]
    public async Task More_Parts_Than_Are_Read_Say_So_And_Unlist_Nothing()
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml"));
        _rig.Website.Serve("/post-sitemap1.xml", UrlSet("/one"));
        _rig.Website.Serve("/one", Article("One", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");
        var engine = _rig.Engine(Settings(mostParts: 2));

        await engine.RunAsync();

        _rig.Website.Serve("/post-sitemap2.xml", UrlSet());
        _rig.Website.Serve("/post-sitemap3.xml", UrlSet());
        _rig.Website.Serve("/post-sitemap1.xml", UrlSet());
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml", "/post-sitemap3.xml"));

        await NextReadingAsync(engine);

        _rig.Asked("/post-sitemap3.xml").Should().Be(0);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.PartlyRead);
        row.Problem.Should().Be(SitemapProblem.TooMany);
        (await _rig.PageAsync(site.Id, "/one")).UnlistedAt.Should().BeNull();
    }

    /// <summary>
    /// The limit on how many pages one website keeps is across every part of its sitemap, so splitting a
    /// list into files does not get past it.
    /// </summary>
    [Fact]
    public async Task More_Pages_Than_Are_Kept_Say_So()
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml"));
        _rig.Website.Serve("/post-sitemap1.xml", UrlSet("/1", "/2"));
        _rig.Website.Serve("/post-sitemap2.xml", UrlSet("/3", "/4"));

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine(Settings(mostPosts: 3)).RunAsync();

        (await _rig.PagesAsync(site.Id)).Should().HaveCount(3);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.PartlyRead);
        row.Problem.Should().Be(SitemapProblem.TooMany);
    }

    /// <summary>
    /// A sitemap listing different pages every time has each lot taken off the list, and they would pile up
    /// for a year. No more are known than the limit on what a website lists, the oldest going first.
    /// </summary>
    [Fact]
    public async Task A_Sitemap_Listing_Different_Pages_Every_Time_Cannot_Fill_The_Database()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/first-1", "/first-2"));

        var site = await WebsiteAsync();
        var engine = _rig.Engine(Settings(mostPosts: 2, pagesPerPass: 1));

        await engine.RunAsync();

        foreach (var round in Enumerable.Range(1, 5))
        {
            _rig.Website.Serve("/sitemap.xml", UrlSet($"/round-{round}-1", $"/round-{round}-2"));

            await NextReadingAsync(engine);
        }

        var pages = await _rig.PagesAsync(site.Id);

        pages.Where(page => page.UnlistedAt is null).Select(page => page.Path).Should().BeEquivalentTo("/round-5-1", "/round-5-2");
        pages.Where(page => page.UnlistedAt is not null).Select(page => page.Path).Should().BeEquivalentTo("/round-4-1", "/round-4-2");
    }

    /// <summary>
    /// A sitemap that breaks at the end of the file every time is never read to its end, so nothing ever
    /// comes off the list, and it could list different pages every time. What is held has a ceiling, and
    /// every reading that closes forgets the oldest of what no file vouches for, so the ceiling is never a
    /// trap: once the sitemap is put right it is read in full.
    /// </summary>
    [Fact]
    public async Task A_Sitemap_That_Never_Finishes_Cannot_Fill_The_Database()
    {
        _rig.Website.Serve("/sitemap.xml", Broken("/first-1", "/first-2"));

        var site = await WebsiteAsync();
        var engine = _rig.Engine(Settings(mostPosts: 2, pagesPerPass: 1));

        await engine.RunAsync();

        foreach (var round in Enumerable.Range(1, 8))
        {
            _rig.Website.Serve("/sitemap.xml", Broken($"/round-{round}-1", $"/round-{round}-2"));

            await NextReadingAsync(engine);

            (await _rig.PagesAsync(site.Id)).Should().HaveCountLessThanOrEqualTo(6);
        }

        _rig.Website.Serve("/sitemap.xml", UrlSet("/fixed-1", "/fixed-2"));

        await NextReadingAsync(engine);

        (await RowAsync(site.Id)).State.Should().Be(SitemapState.Read);

        (await _rig.PagesAsync(site.Id)).Where(page => page.UnlistedAt is null).Select(page => page.Path)
            .Should().BeEquivalentTo("/fixed-1", "/fixed-2");
    }

    /// <summary>
    /// What a website's pages take up is limited as well as how many there are, so a sitemap listing
    /// made-up addresses as long as the protocol allows cannot fill the database with fewer of them.
    /// </summary>
    [Fact]
    public async Task Pages_With_The_Longest_Addresses_Are_Held_To_What_They_Take_Up()
    {
        var paths = Enumerable.Range(1, 2).Select(page => $"/{page}-{new string('a', 1990)}").ToArray();

        _rig.Website.Serve("/sitemap.xml", UrlSet(paths));

        var site = await WebsiteAsync();

        await _rig.Engine(Settings(mostPosts: 2, pagesPerPass: 1)).RunAsync();

        (await _rig.PagesAsync(site.Id)).Should().ContainSingle().Which.Path.Should().Be(paths[0]);
        (await RowAsync(site.Id)).Problem.Should().Be(SitemapProblem.TooMany);
    }

    /// <summary>The newest pages are what a website's latest posts are read from, and a large website takes hours.</summary>
    [Fact]
    public async Task The_Newest_Pages_Are_Read_First_And_No_More_Than_A_Pass_Allows()
    {
        _rig.Website.Serve(
            "/post-sitemap.xml",
            UrlSetOf(
                ("/no-date", null),
                ("/oldest", Now.AddDays(-30)),
                ("/newest", Now.AddDays(-1)),
                ("/middle", Now.AddDays(-10))));

        foreach (var path in new[] { "/no-date", "/oldest", "/newest", "/middle" })
        {
            _rig.Website.Serve(path, Article(path, Now.AddDays(-30)), Html);
        }

        var site = await WebsiteAsync("/post-sitemap.xml");
        var engine = _rig.Engine(Settings(pagesPerPass: 2));

        await engine.RunAsync();

        _rig.Asked("/newest").Should().Be(1);
        _rig.Asked("/middle").Should().Be(1);
        _rig.Asked("/oldest").Should().Be(0);
        _rig.Asked("/no-date").Should().Be(0);

        // More is due, and the next pass comes along shortly rather than in an hour.
        var row = await RowAsync(site.Id);

        row.NextPassAt.Should().BeCloseTo(Now + SitemapRig.Quick.Interval, TimeSpan.FromSeconds(30));

        (await engine.RunAsync()).Should().BeFalse();

        await LaterAsync(engine, SitemapRig.Quick.Interval);

        _rig.Asked("/oldest").Should().Be(1);
        _rig.Asked("/no-date").Should().Be(1);
    }

    /// <summary>
    /// A pass out of budget stops starting new work, and a reading left open picks up where it stopped.
    /// Nothing says how it went until there is nothing left to ask for.
    /// </summary>
    [Fact]
    public async Task A_Reading_That_Runs_Out_Of_Budget_Carries_On_In_The_Next_Pass()
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap.xml"));
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");
        var spent = _rig.Engine(Settings(passBudget: TimeSpan.Zero));

        await spent.RunAsync();

        _rig.Asked("/sitemap_index.xml").Should().Be(1);
        _rig.Asked("/post-sitemap.xml").Should().Be(0);

        var open = await RowAsync(site.Id);

        open.State.Should().Be(SitemapState.Waiting);
        open.CycleStartedAt.Should().NotBeNull();
        open.ClaimToken.Should().BeNull();
        open.NextPassAt.Should().BeCloseTo(Now + SitemapRig.Quick.Interval, TimeSpan.FromSeconds(30));

        await LaterAsync(_rig.Engine(), SitemapRig.Quick.Interval);

        _rig.Asked("/sitemap_index.xml").Should().Be(1, "what the address held is still known");
        _rig.Asked("/post").Should().Be(1);
        (await RowAsync(site.Id)).State.Should().Be(SitemapState.Read);
    }

    /// <summary>
    /// A website saying it is being asked too often is taken at its word for everything else there was to
    /// ask, not only the page it said it about, and the reader comes back no sooner than it asked.
    /// </summary>
    [Fact]
    public async Task A_Website_Asking_To_Be_Left_Alone_Is()
    {
        _rig.Website.Serve("/post-sitemap.xml", UrlSetOf(("/first", Now.AddDays(-1)), ("/second", Now.AddDays(-2))));
        _rig.Website.Answer("/first", context =>
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "7200";

            return Task.CompletedTask;
        });

        var site = await WebsiteAsync("/post-sitemap.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        _rig.Asked("/first").Should().Be(1);
        _rig.Asked("/second").Should().Be(0);

        var first = await _rig.PageAsync(site.Id, "/first");

        first.PageOutcome.Should().Be(PostPageOutcome.TurnedAway);
        first.PageDueAt.Should().BeCloseTo(Now + TimeSpan.FromHours(2), TimeSpan.FromMinutes(1));

        (await RowAsync(site.Id)).NextPassAt.Should().BeCloseTo(Now + TimeSpan.FromHours(2), TimeSpan.FromMinutes(1));
        (await engine.RunAsync()).Should().BeFalse();
    }

    /// <summary>
    /// Saying it without saying how long is still saying it. And a website cannot put a page off for
    /// decades by asking to be left alone until then.
    /// </summary>
    /// <param name="header">How long it asks for.</param>
    /// <param name="hours">How long it is left alone for.</param>
    [Theory]
    [InlineData(null, 1)]
    [InlineData("2147483647", 7 * 24)]
    public async Task A_Website_Asking_To_Be_Left_Alone_Is_Heard_Within_Reason(string? header, int hours)
    {
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/first"));
        _rig.Website.Answer("/first", context =>
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            if (header is not null)
            {
                context.Response.Headers.RetryAfter = header;
            }

            return Task.CompletedTask;
        });

        var site = await WebsiteAsync("/post-sitemap.xml");

        await _rig.Engine().RunAsync();

        (await RowAsync(site.Id)).NextPassAt
            .Should().BeCloseTo(Now + TimeSpan.FromHours(hours), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// One page that fails is that page's trouble. Several in a row is the website's, and every page it
    /// lists would use up one of its few attempts on the same answer, so the reader goes away and comes
    /// back later. An outage costs the attempts of a few pages rather than of all of them.
    /// </summary>
    [Fact]
    public async Task A_Website_In_Trouble_Costs_A_Few_Attempts_And_No_More()
    {
        var paths = Enumerable.Range(1, 8).Select(number => $"/page-{number}").ToArray();

        _rig.Website.Serve("/post-sitemap.xml", UrlSet(paths));

        foreach (var path in paths)
        {
            _rig.Website.Status(path, StatusCodes.Status500InternalServerError);
        }

        var site = await WebsiteAsync("/post-sitemap.xml");

        await _rig.Engine().RunAsync();

        paths.Sum(_rig.Asked).Should().Be(3);

        (await _rig.PagesAsync(site.Id)).Count(page => page.PageAttempts == 0).Should().Be(5);
        (await RowAsync(site.Id)).NextPassAt
            .Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, TimeSpan.FromMinutes(1));
    }

    /// <summary>How a website can be in trouble with its parts.</summary>
    public enum TroubleWithParts
    {
        /// <summary>Its server is having trouble.</summary>
        ServerError,

        /// <summary>It sends more than can be read.</summary>
        TooLarge,
    }

    /// <summary>
    /// A few parts in a row in trouble is the website's trouble, and the rest would go the same way, so
    /// they are not asked for. Nothing can be said to have gone, the reading ends, and one whose every
    /// file costs the most it can does not get to keep a reader for itself.
    /// </summary>
    /// <param name="trouble">What each part does.</param>
    [Theory]
    [InlineData(TroubleWithParts.ServerError)]
    [InlineData(TroubleWithParts.TooLarge)]
    public async Task A_Website_In_Trouble_Is_Given_Up_On_For_This_Reading(TroubleWithParts trouble)
    {
        var parts = Enumerable.Range(1, 8).Select(number => $"/post-sitemap{number}.xml").ToArray();

        _rig.Website.Serve("/sitemap_index.xml", Index(parts));

        foreach (var part in parts)
        {
            if (trouble == TroubleWithParts.ServerError)
            {
                _rig.Website.Status(part, StatusCodes.Status500InternalServerError);
            }
            else
            {
                _rig.Website.Serve(part, UrlSet([.. Enumerable.Range(1, 2000).Select(number => $"/page-{number}")]));
            }
        }

        var site = await WebsiteAsync("/sitemap_index.xml");
        var engine = _rig.Engine(new SitemapOptions
        {
            RequestGap = SitemapRig.Quick.RequestGap,
            LargestSitemapBytes = 8 * 1024,
            PagesPerPass = 1,
        });

        await engine.RunAsync();

        parts.Sum(_rig.Asked).Should().Be(3);

        var expected = trouble == TroubleWithParts.ServerError ? SitemapPartOutcome.ServerError : SitemapPartOutcome.TooLarge;

        (await _rig.PartsAsync(site.Id)).Should().AllSatisfy(part => part.Outcome.Should().Be(expected));

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.CycleStartedAt.Should().BeNull();
        row.NextPassAt.Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, TimeSpan.FromMinutes(1));
        _rig.Asked("/page-1").Should().Be(0);
    }

    /// <summary>
    /// A file that is not there says nothing about the next one, and neither does a part that had nothing
    /// wrong with it between two that did. It takes several in a row.
    /// </summary>
    [Fact]
    public async Task Parts_That_Are_Not_In_Trouble_Are_All_Asked_For()
    {
        var parts = Enumerable.Range(1, 6).Select(number => $"/post-sitemap{number}.xml").ToArray();

        _rig.Website.Serve("/sitemap_index.xml", Index(parts));

        foreach (var part in parts)
        {
            _rig.Website.Status(part, StatusCodes.Status404NotFound);
        }

        var site = await WebsiteAsync("/sitemap_index.xml");

        await _rig.Engine().RunAsync();

        parts.Sum(_rig.Asked).Should().Be(6);
        (await RowAsync(site.Id)).Problem.Should().Be(SitemapProblem.NotFound);
    }

    /// <summary>One bad page among good ones does not stop the rest.</summary>
    [Fact]
    public async Task One_Page_In_Trouble_Does_Not_Hold_Up_The_Rest()
    {
        var paths = Enumerable.Range(1, 5).Select(number => $"/page-{number}").ToArray();

        _rig.Website.Serve("/post-sitemap.xml", UrlSet(paths));

        foreach (var path in paths)
        {
            _rig.Website.Serve(path, Article(path, Now.AddDays(-3)), Html);
        }

        _rig.Website.Status("/page-3", StatusCodes.Status500InternalServerError);

        var site = await WebsiteAsync("/post-sitemap.xml");

        await _rig.Engine().RunAsync();

        paths.Sum(_rig.Asked).Should().Be(5);

        var failing = await _rig.PageAsync(site.Id, "/page-3");

        failing.PageOutcome.Should().Be(PostPageOutcome.ServerError);
        failing.PageAttempts.Should().Be(1);
    }

    /// <summary>What a page answers with, and what that makes it, in the cases nothing else has covered.</summary>
    /// <param name="status">What it answers.</param>
    /// <param name="outcome">What is recorded.</param>
    /// <param name="comesRoundAgain">Whether it is tried again.</param>
    /// <param name="isPost">Whether it still counts as a post, in a part that holds posts.</param>
    [Theory]
    [InlineData(StatusCodes.Status404NotFound, PostPageOutcome.Gone, false, false)]
    [InlineData(StatusCodes.Status410Gone, PostPageOutcome.Gone, false, false)]
    [InlineData(StatusCodes.Status403Forbidden, PostPageOutcome.TurnedAway, true, true)]
    [InlineData(StatusCodes.Status500InternalServerError, PostPageOutcome.ServerError, true, true)]
    [InlineData(StatusCodes.Status202Accepted, PostPageOutcome.TurnedAway, true, true)]
    [InlineData(StatusCodes.Status204NoContent, PostPageOutcome.TurnedAway, true, true)]
    [InlineData(StatusCodes.Status205ResetContent, PostPageOutcome.TurnedAway, true, true)]
    [InlineData(StatusCodes.Status206PartialContent, PostPageOutcome.TurnedAway, true, true)]
    public async Task What_A_Page_Answers_Is_What_It_Comes_To(
        int status,
        PostPageOutcome outcome,
        bool comesRoundAgain,
        bool isPost)
    {
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/post"));
        _rig.Website.Status("/post", status);

        var site = await WebsiteAsync("/post-sitemap.xml");

        await _rig.Engine().RunAsync();

        var page = await _rig.PageAsync(site.Id, "/post");

        page.PageOutcome.Should().Be(outcome);
        page.IsPost.Should().Be(isPost);
        (page.PageDueAt is not null).Should().Be(comesRoundAgain);
    }

    /// <summary>
    /// A website saying what it sent is not a web page has said what it is, whichever part lists it. That
    /// is not a matter of asking again.
    /// </summary>
    [Fact]
    public async Task What_Says_It_Is_Not_A_Page_Is_Not_A_Post()
    {
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/download.pdf"));
        _rig.Website.Serve("/download.pdf", "%PDF-1.7", "application/pdf");

        var site = await WebsiteAsync("/post-sitemap.xml");

        await _rig.Engine().RunAsync();

        var page = await _rig.PageAsync(site.Id, "/download.pdf");

        page.PageOutcome.Should().Be(PostPageOutcome.NotAPage);
        page.PageType.Should().Be(PostPageType.Other);
        page.IsPost.Should().BeFalse();
        page.PageDueAt.Should().BeNull();
    }

    /// <summary>What a sitemap's address answers with, and what the reading comes to.</summary>
    /// <param name="status">What it answers.</param>
    /// <param name="problem">What the status says.</param>
    [Theory]
    [InlineData(StatusCodes.Status404NotFound, SitemapProblem.NotFound)]
    [InlineData(StatusCodes.Status403Forbidden, SitemapProblem.TurnedAway)]
    [InlineData(StatusCodes.Status429TooManyRequests, SitemapProblem.TurnedAway)]
    [InlineData(StatusCodes.Status500InternalServerError, SitemapProblem.ServerError)]
    [InlineData(StatusCodes.Status202Accepted, SitemapProblem.TurnedAway)]
    [InlineData(StatusCodes.Status204NoContent, SitemapProblem.TurnedAway)]
    [InlineData(StatusCodes.Status205ResetContent, SitemapProblem.TurnedAway)]
    [InlineData(StatusCodes.Status206PartialContent, SitemapProblem.TurnedAway)]
    public async Task What_The_Address_Answers_Is_What_The_Reading_Comes_To(int status, SitemapProblem problem)
    {
        _rig.Website.Status("/sitemap.xml", status);

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(problem);
        row.SucceededAt.Should().BeNull();
        row.NextCycleAt.Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, TimeSpan.FromMinutes(1));
        (await engine.RunAsync()).Should().BeFalse("it is an hour before the next attempt");
    }

    /// <summary>
    /// A website that used to be read and now cannot be keeps what was found, and goes on saying when it
    /// was last read in full.
    /// </summary>
    [Fact]
    public async Task A_Reading_That_Fails_Leaves_Everything_Already_Known()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();
        var engine = _rig.Engine();

        await engine.RunAsync();

        var succeededAt = (await RowAsync(site.Id)).SucceededAt;

        _rig.Website.Status("/sitemap.xml", StatusCodes.Status500InternalServerError);

        await NextReadingAsync(engine);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.SucceededAt.Should().Be(succeededAt);
        (await _rig.PageAsync(site.Id, "/post")).Title.Should().Be("Post");
    }

    /// <summary>Where a reading can be when the engine is told to stop.</summary>
    public enum WhereAnEngineStops
    {
        /// <summary>Asking for the sitemap's address.</summary>
        AtTheAddress,

        /// <summary>Asking for a part of the sitemap.</summary>
        AtAPart,
    }

    /// <summary>
    /// An engine stopping in good order is not the website's doing. What it had in hand is given back, the
    /// claim is let go of, and the next start carries on at once: a sitemap does not read as failed for the
    /// next hour because somebody deployed.
    /// </summary>
    /// <param name="where">Where it stops.</param>
    [Theory]
    [InlineData(WhereAnEngineStops.AtTheAddress)]
    [InlineData(WhereAnEngineStops.AtAPart)]
    public async Task An_Engine_Stopping_Gives_Back_What_It_Had_In_Hand(WhereAnEngineStops where)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Cancellation.Token);

        var target = where == WhereAnEngineStops.AtTheAddress ? "/sitemap_index.xml" : "/post-sitemap.xml";
        var stopped = false;

        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap.xml"));
        _rig.Website.Serve("/post-sitemap.xml", UrlSet());

        var serving = target == "/sitemap_index.xml" ? Index("/post-sitemap.xml") : UrlSet();

        _rig.Website.Answer(target, async context =>
        {
            if (!stopped)
            {
                stopped = true;

                await stopping.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            }

            context.Response.ContentType = "application/xml";

            await context.Response.WriteAsync(serving, context.RequestAborted);
        });

        var site = await WebsiteAsync("/sitemap_index.xml");
        var engine = _rig.Engine();

        var ending = await Record.ExceptionAsync(() => engine.Pass.RunNextAsync(stopping.Token));

        ending.Should().BeAssignableTo<OperationCanceledException>();

        var row = await RowAsync(site.Id);

        row.ClaimToken.Should().BeNull();
        row.State.Should().Be(SitemapState.Waiting);

        if (where == WhereAnEngineStops.AtTheAddress)
        {
            row.NextCycleAt.Should().BeOnOrBefore(Now);
        }
        else
        {
            var part = (await _rig.PartsAsync(site.Id)).Single();

            part.ReadAt.Should().BeNull();
            part.Outcome.Should().BeNull();
        }

        engine.Log.GetSnapshot().Should().NotContain(record => record.Level >= LogLevel.Warning);

        (await _rig.Engine().RunAsync()).Should().BeTrue();

        (await RowAsync(site.Id)).State.Should().Be(SitemapState.Read);
    }

    /// <summary>
    /// The engine itself has the reader registered and its parts resolvable, whichever way the host goes
    /// on to use them, and it reports a sitemap as waiting with no reader running.
    /// </summary>
    [Fact]
    public async Task The_Engine_Has_A_Reader()
    {
        stack.Services.GetServices<IHostedService>().OfType<SitemapReadingService>().Should().ContainSingle();
        stack.Services.GetRequiredService<SitemapPass>().Should().NotBeNull();

        await using var scope = stack.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<SitemapCycle>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<PageReader>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<SitemapClaims>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<PostWriter>().Should().NotBeNull();
    }

    /// <summary>
    /// A setting that could not work is refused as the installation starts, with a sentence naming it, and
    /// not found out partway through reading somebody's website. The least of these keep what is promised
    /// to a website; the most of them are what no timer can keep, and a claim that could run out while the
    /// pass holding it is still running is no claim at all.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="value">What it is set to.</param>
    [Theory]
    [InlineData("SitesAtOnce", "-1")]
    [InlineData("SitesAtOnce", "17")]
    [InlineData("Interval", "00:00:04")]
    [InlineData("Interval", "60.00:00:00")]
    [InlineData("Cadence", "00:59:00")]
    [InlineData("Cadence", "60.00:00:00")]
    [InlineData("PagesPerPass", "0")]
    [InlineData("PagesPerPass", "601")]
    [InlineData("PassBudget", "00:00:59")]
    [InlineData("PassBudget", "60.00:00:00")]
    [InlineData("Lease", "00:10:00")]
    [InlineData("Lease", "60.00:00:00")]
    [InlineData("MostSitemapParts", "0")]
    [InlineData("MostSitemapParts", "50001")]
    [InlineData("MostPostsPerSite", "999")]
    [InlineData("MostPostsPerSite", "1000001")]
    [InlineData("RetryAfterFailure", "00:04:59")]
    [InlineData("RetryAfterFailure", "60.00:00:00")]
    public void An_Installation_Does_Not_Start_On_A_Setting_That_Could_Not_Work(string setting, string value)
    {
        var key = $"{SitemapOptions.SectionName}:{setting}";

        HostRefusal.Of(() => stack.WithWebHostBuilder(builder => builder.UseSetting(key, value)))
            .Should().BeOfType<OptionsValidationException>()
            .Which.Message.Should().Contain(key);
    }

    /// <summary>
    /// What an installation reads under as it ships. These are what is promised about reading somebody's
    /// website, so changing one is a decision and not a tidy-up.
    /// </summary>
    [Fact]
    public void An_Installation_Reads_Under_These_Limits_As_It_Ships()
    {
        var settings = new SitemapOptions();

        settings.Enabled.Should().BeTrue();
        settings.RequestGap.Should().Be(TimeSpan.FromSeconds(1));
        settings.Cadence.Should().Be(TimeSpan.FromHours(1));
        settings.PagesPerPass.Should().Be(120);
        settings.LargestSitemapBytes.Should().Be(50 * 1024 * 1024);
        settings.MostPostsPerSite.Should().Be(50_000);
        settings.MostSitemapParts.Should().Be(500);
        settings.PageBytesRead.Should().Be(512 * 1024);
        settings.Lease.Should().BeGreaterThan(settings.PassBudget);
    }

    /// <summary>
    /// A claim has to outlast the pass holding it by the most that pass can run over by, whichever way its
    /// settings are moved, and not only as they ship.
    /// </summary>
    [Fact]
    public void An_Installation_Does_Not_Start_On_A_Budget_Its_Claim_Would_Not_Outlast()
    {
        var key = $"{SitemapOptions.SectionName}:{nameof(SitemapOptions.PassBudget)}";

        HostRefusal.Of(() => stack.WithWebHostBuilder(builder => builder.UseSetting(key, "00:14:00")))
            .Should().BeOfType<OptionsValidationException>()
            .Which.Message.Should().Contain($"{SitemapOptions.SectionName}:{nameof(SitemapOptions.Lease)}");
    }

    /// <summary>
    /// A look that read a website's old address a moment before it was changed can leave a row describing
    /// that address. Nothing could claim such a row or replace it, so it is cleared, and the address that
    /// is actually named gets read.
    /// </summary>
    [Fact]
    public async Task A_Row_Describing_An_Address_Nobody_Names_Any_More_Is_Cleared()
    {
        _rig.Website.Serve("/sitemap.xml", UrlSet());

        var site = await WebsiteAsync();
        var at = Now;

        await _rig.SqlAsync(
            $"""
            INSERT INTO sitemap_reads (site_id, address, host, state, next_pass_at, next_cycle_at, updated_at)
            VALUES ({site.Id}, 'http://blog.example.test/old.xml', 'blog.example.test', 'Read', {at}, {at}, {at})
            """);

        await _rig.SqlAsync(
            $"""
            INSERT INTO sitemap_parts (id, site_id, address, is_posts, entries, off_site_entries, query_entries)
            VALUES ({Guid.NewGuid()}, {site.Id}, 'http://blog.example.test/old.xml', false, 0, 0, 0)
            """);

        await _rig.Engine().RunAsync();

        _rig.Asked("/sitemap.xml").Should().Be(1);
        _rig.Asked("/old.xml").Should().Be(0);

        var row = await RowAsync(site.Id);

        row.Address.Should().Be(Origin + "/sitemap.xml");
        row.State.Should().Be(SitemapState.Read);
        (await _rig.PartsAsync(site.Id)).Should().ContainSingle().Which.Address.Should().Be(Origin + "/sitemap.xml");
    }

    /// <summary>A file goes to the database a batch at a time, and everything in it arrives, first and last.</summary>
    [Fact]
    public async Task A_Part_Larger_Than_One_Write_Is_Stored_In_Full()
    {
        const int Pages = 2500;

        _rig.Website.Serve("/sitemap.xml", UrlSet([.. Enumerable.Range(1, Pages).Select(number => $"/page-{number}")]));

        var site = await WebsiteAsync();

        await _rig.Engine(Settings(pagesPerPass: 1)).RunAsync();

        (await _rig.PagesAsync(site.Id)).Should().HaveCount(Pages);

        var part = (await _rig.PartsAsync(site.Id)).Single();

        part.Entries.Should().Be(Pages);
        part.Outcome.Should().Be(SitemapPartOutcome.Read);
    }

    /// <summary>
    /// An address and a title are whatever somebody wrote, and go in as data. A title of markup stays that
    /// title, and an address with SQL in it stays that address, with the table still there afterwards.
    /// </summary>
    [Fact]
    public async Task What_A_Website_Writes_Goes_In_As_Data()
    {
        const string Path = "/it's';--";

        _rig.Website.Serve("/sitemap.xml", UrlSet(Path));
        _rig.Website.Serve(
            Path,
            """<html><head><meta property="og:title" content="&lt;script&gt;alert('x')&lt;/script&gt;"></head></html>""",
            Html);

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        var page = await _rig.PageAsync(site.Id, Path);

        page.Address.Should().Be(Origin + Path);
        page.Title.Should().Be("<script>alert('x')</script>");
    }

    /// <summary>
    /// A fault reading a website's robots.txt is not a reason for the pass to fail, and it costs a page no
    /// attempt, since nothing was asked of the page. It goes on happening, so before long the website is
    /// left alone rather than asked again for every page it has.
    /// </summary>
    [Fact]
    public async Task A_Fault_Reading_Robots_Txt_Costs_No_Attempts_And_Soon_Stops()
    {
        var paths = Enumerable.Range(1, 6).Select(number => $"/page-{number}").ToArray();

        _rig.Website.Serve("/post-sitemap.xml", UrlSet(paths));

        foreach (var path in paths)
        {
            _rig.Website.Serve(path, Article(path, Now.AddDays(-3)), Html);
        }

        var site = await WebsiteAsync("/post-sitemap.xml");

        await _rig.Engine(Settings(pagesPerPass: 1)).RunAsync();

        await _rig.SqlAsync($"UPDATE site_posts SET page_due_at = {Now} WHERE site_id = {site.Id}");
        await _rig.MakeDueAsync(site.Id);

        var engine = _rig.Engine(configure: ThrowingFor("/robots.txt"));

        (await engine.RunAsync()).Should().BeTrue();

        engine.Log.GetSnapshot().Where(record => record.Id.Id == 1306).Should().HaveCount(3);
        engine.Log.GetSnapshot().Should().NotContain(record => record.Id.Id == 1304);
        (await _rig.PagesAsync(site.Id)).Where(page => page.Path != paths[0]).Should()
            .AllSatisfy(page => page.PageAttempts.Should().Be(0));
    }

    /// <summary>
    /// A website asking to be left alone while its parts are being read keeps the reading open: what it
    /// said it about is written down, and the rest are read when the time is up. The status goes on saying
    /// what it said until there is nothing left to ask for.
    /// </summary>
    [Fact]
    public async Task A_Website_Asking_To_Be_Left_Alone_Partway_Through_Its_Parts_Is()
    {
        var alreadyTurnedAway = 0;

        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap1.xml", "/post-sitemap2.xml"));

        foreach (var part in new[] { "/post-sitemap1.xml", "/post-sitemap2.xml" })
        {
            _rig.Website.Answer(part, async context =>
            {
                if (Interlocked.Increment(ref alreadyTurnedAway) == 1)
                {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers.RetryAfter = "7200";

                    return;
                }

                context.Response.ContentType = "application/xml";

                await context.Response.WriteAsync(UrlSet(), context.RequestAborted);
            });
        }

        var site = await WebsiteAsync("/sitemap_index.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        (_rig.Asked("/post-sitemap1.xml") + _rig.Asked("/post-sitemap2.xml")).Should().Be(1);

        var open = await RowAsync(site.Id);

        open.State.Should().Be(SitemapState.Waiting);
        open.CycleStartedAt.Should().NotBeNull();
        open.NextPassAt.Should().BeCloseTo(Now + TimeSpan.FromHours(2), TimeSpan.FromMinutes(1));

        await LaterAsync(engine, TimeSpan.FromHours(2));

        (_rig.Asked("/post-sitemap1.xml") + _rig.Asked("/post-sitemap2.xml")).Should().Be(2);

        var closed = await RowAsync(site.Id);

        closed.State.Should().Be(SitemapState.PartlyRead);
        closed.Problem.Should().Be(SitemapProblem.TurnedAway);
    }

    /// <summary>The reading is put off as long as the website asked, where that is longer than it would be anyway.</summary>
    [Fact]
    public async Task A_Sitemap_Asking_For_Longer_Than_Usual_Is_Given_It()
    {
        _rig.Website.Answer("/sitemap.xml", context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "10800";

            return Task.CompletedTask;
        });

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.NextCycleAt.Should().BeCloseTo(Now + TimeSpan.FromHours(3), TimeSpan.FromMinutes(1));
        row.NextPassAt.Should().BeCloseTo(Now + TimeSpan.FromHours(3), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Somebody names a sitemap on a name that leads into a private network. Nothing is asked of it, and
    /// the reading says why.
    /// </summary>
    [Fact]
    public async Task A_Sitemap_Whose_Name_Leads_Somewhere_Private_Says_So()
    {
        var site = await _rig.AddWebsiteAsync("intranet.example.com", "http://intranet.example.com/sitemap.xml");

        await _rig.Engine().RunAsync();

        _rig.Website.Seen.Should().BeEmpty();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.PrivateAddress);
    }

    [Fact]
    public async Task A_Website_That_Does_Not_Answer_Says_So()
    {
        var site = await WebsiteAsync();

        // Nothing on this machine listens on this port, and so nothing takes the connection.
        var engine = _rig.Engine(configure: services =>
            services.AddSingleton<IPublicEndpoints>(new LoopbackEndpoints(1)));

        await engine.RunAsync();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.Unreachable);
        row.NextCycleAt.Should().BeCloseTo(Now + SitemapRig.Quick.RetryAfterFailure, TimeSpan.FromMinutes(1));
    }

    /// <summary>What is at the address is not a sitemap, which is somebody having named the wrong thing.</summary>
    /// <param name="body">What is there.</param>
    [Theory]
    [InlineData("<!doctype html><html><head><title>A blog</title></head><body>Hello</body></html>")]
    [InlineData("<rss version=\"2.0\"><channel><title>A feed</title></channel></rss>")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE urlset [<!ENTITY a \"b\">]><urlset></urlset>")]
    [InlineData("{ \"not\": \"xml\" }")]
    public async Task What_Is_Not_A_Sitemap_Says_So(string body)
    {
        _rig.Website.Serve("/sitemap.xml", body);

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.NotASitemap);
        (await _rig.PartsAsync(site.Id)).Should().BeEmpty();
    }

    /// <summary>A file past the limit is cut off, what came before it stays, and the reading says it was too large.</summary>
    [Fact]
    public async Task A_Sitemap_Larger_Than_Is_Read_Says_So()
    {
        var paths = Enumerable.Range(1, 1000).Select(number => $"/page-{number}").ToArray();

        _rig.Website.Serve("/sitemap.xml", UrlSet(paths));

        var site = await WebsiteAsync();
        var engine = _rig.Engine(new SitemapOptions { RequestGap = SitemapRig.Quick.RequestGap, LargestSitemapBytes = 8 * 1024, PagesPerPass = 1 });

        await engine.RunAsync();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.TooLarge);

        (await _rig.PagesAsync(site.Id)).Should().NotBeEmpty();
    }

    /// <summary>The way off the website is closed at every step, and the reading says it was asked to take it.</summary>
    [Fact]
    public async Task A_Sitemap_That_Redirects_Off_The_Website_Says_So()
    {
        _rig.Website.Redirect("/sitemap.xml", "http://other.test/sitemap.xml");

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        _rig.Website.Seen.Should().OnlyContain(request => request.Host == Domain);

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.LeftTheWebsite);
    }

    /// <summary>
    /// A redirect from one of a website's names to the other is the most common there is, and what the
    /// sitemap then lists is on the website whichever name it is written with.
    /// </summary>
    [Fact]
    public async Task A_Sitemap_That_Redirects_To_The_Websites_Other_Name_Is_Read()
    {
        _rig.Website.Answer("/sitemap.xml", async context =>
        {
            if (context.Request.Host.Host == Domain)
            {
                context.Response.Redirect("http://www.blog.example.test/sitemap.xml");

                return;
            }

            context.Response.ContentType = "application/xml";

            await context.Response.WriteAsync(UrlSet("http://www.blog.example.test/post"), context.RequestAborted);
        });

        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync();

        await _rig.Engine().RunAsync();

        (await RowAsync(site.Id)).State.Should().Be(SitemapState.Read);
        (await _rig.PageAsync(site.Id, "/post")).Address.Should().Be("http://www.blog.example.test/post");
    }

    /// <summary>What a page's redirects come to is the end of it, until the sitemap says the page has changed.</summary>
    /// <param name="location">Where it redirects to.</param>
    /// <param name="outcome">What is recorded.</param>
    [Theory]
    [InlineData("http://other.test/post", PostPageOutcome.LeftTheWebsite)]
    [InlineData("/post", PostPageOutcome.TooManyRedirects)]
    public async Task A_Page_That_Redirects_Where_It_Should_Not_Is_Not_Asked_For_Again(string location, PostPageOutcome outcome)
    {
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/post"));
        _rig.Website.Redirect("/post", location);

        var site = await WebsiteAsync("/post-sitemap.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();
        await LaterAsync(engine, TimeSpan.FromHours(5));

        var page = await _rig.PageAsync(site.Id, "/post");

        page.PageOutcome.Should().Be(outcome);
        page.PageDueAt.Should().BeNull();
        _rig.Asked("/post").Should().Be(1);
    }

    /// <summary>
    /// A page keeps the address it was listed under, and the sitemap can move on from under it. One no
    /// longer on the website is not asked for, whatever it was before.
    /// </summary>
    [Fact]
    public async Task A_Page_Left_Behind_On_Another_Host_Is_Not_Asked_For()
    {
        _rig.Website.Serve("/post-sitemap.xml", UrlSet());

        var site = await WebsiteAsync("/post-sitemap.xml");
        var at = Now;

        await _rig.SqlAsync(
            $"""
            INSERT INTO site_posts (site_id, path, address, listed_in_posts_part, first_listed_at, page_due_at)
            VALUES ({site.Id}, '/moved', 'http://other.test/moved', true, {at}, {at})
            """);

        _rig.Website.Serve("/moved", Article("Moved", Now.AddDays(-3)), Html);

        var engine = _rig.Engine();

        (await engine.ClaimAsync()).Should().NotBeNull();

        await _rig.SqlAsync(
            $"""
            UPDATE sitemap_reads
            SET claim_token = NULL, claimed_until = NULL, next_cycle_at = {at.AddHours(1)}, next_pass_at = {at.AddHours(-1)}
            WHERE site_id = {site.Id}
            """);

        await engine.RunAsync();

        _rig.Asked("/moved").Should().Be(0);

        var page = await _rig.PageAsync(site.Id, "/moved");

        page.PageOutcome.Should().Be(PostPageOutcome.LeftTheWebsite);
        page.PageDueAt.Should().BeNull();
    }

    /// <summary>Where a website can say nothing has changed when it was not asked.</summary>
    public enum WhereNobodyAsked
    {
        /// <summary>The sitemap's address.</summary>
        AtTheAddress,

        /// <summary>A part of the sitemap.</summary>
        AtAPart,

        /// <summary>A page.</summary>
        AtAPage,
    }

    /// <summary>
    /// "Nothing has changed" is the answer to a question, and it was not asked, because nothing has been
    /// kept to ask it with. Believed, it would count as having read something nobody has seen, and a part
    /// that had supposedly been read could then have its pages taken off the list.
    /// </summary>
    /// <param name="where">Where it is said.</param>
    [Theory]
    [InlineData(WhereNobodyAsked.AtTheAddress)]
    [InlineData(WhereNobodyAsked.AtAPart)]
    [InlineData(WhereNobodyAsked.AtAPage)]
    public async Task A_Website_Saying_Nothing_Has_Changed_When_Nobody_Asked_Is_Not_Believed(WhereNobodyAsked where)
    {
        _rig.Website.Serve("/sitemap_index.xml", Index("/post-sitemap.xml"));
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/post"));
        _rig.Website.Serve("/post", Article("Post", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/sitemap_index.xml");
        var engine = _rig.Engine();

        var target = where switch
        {
            WhereNobodyAsked.AtTheAddress => "/sitemap_index.xml",
            WhereNobodyAsked.AtAPart => "/post-sitemap.xml",
            _ => "/post",
        };

        _rig.Website.Status(target, StatusCodes.Status304NotModified);

        await engine.RunAsync();

        if (where == WhereNobodyAsked.AtAPage)
        {
            var page = await _rig.PageAsync(site.Id, "/post");

            page.PageOutcome.Should().Be(PostPageOutcome.ServerError);
            page.PageDueAt.Should().NotBeNull();
        }
        else
        {
            var row = await RowAsync(site.Id);

            row.State.Should().Be(SitemapState.Failed);
            row.Problem.Should().Be(SitemapProblem.ServerError);
        }
    }

    /// <summary>
    /// The same from an address that was asked, but somehow has nothing to say what it held: there is
    /// nothing for its answer to go on from, and it fails rather than guessing.
    /// </summary>
    [Fact]
    public async Task An_Address_Unchanged_From_Something_Nobody_Has_A_Record_Of_Fails_The_Reading()
    {
        _rig.Website.Status("/sitemap.xml", StatusCodes.Status304NotModified);

        var site = await WebsiteAsync();

        (await _rig.Engine().ClaimAsync()).Should().NotBeNull();

        await _rig.SqlAsync(
            $"""
            UPDATE sitemap_reads
            SET claim_token = NULL, claimed_until = NULL, root_etag = '"x"', next_pass_at = {DateTimeOffset.UnixEpoch}
            WHERE site_id = {site.Id}
            """);

        await _rig.Engine().RunAsync();

        var row = await RowAsync(site.Id);

        row.State.Should().Be(SitemapState.Failed);
        row.Problem.Should().Be(SitemapProblem.ServerError);
    }

    /// <summary>What these tests read under, with one thing changed.</summary>
    private static SitemapOptions Settings(
        int mostParts = 500,
        int mostPosts = 50_000,
        int pagesPerPass = 120,
        TimeSpan? passBudget = null,
        TimeSpan? sitemapTimeout = null,
        TimeSpan? pageTimeout = null) =>
        new()
        {
            RequestGap = SitemapRig.Quick.RequestGap,
            LargestSitemapBytes = SitemapRig.Quick.LargestSitemapBytes,
            PageBytesRead = SitemapRig.Quick.PageBytesRead,
            SitemapTimeout = sitemapTimeout ?? SitemapRig.Quick.SitemapTimeout,
            PageTimeout = pageTimeout ?? SitemapRig.Quick.PageTimeout,
            MostSitemapParts = mostParts,
            MostPostsPerSite = mostPosts,
            PagesPerPass = pagesPerPass,
            PassBudget = passBudget ?? SitemapRig.Quick.PassBudget,
        };

    private static string Iso(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Absolute(string path) => path.StartsWith('/') ? Origin + path : path;

    private static string UrlSet(params string[] paths) =>
        UrlSetOf([.. paths.Select(path => (path, (DateTimeOffset?)null))]);

    private static string UrlSetOf(params (string Path, DateTimeOffset? Modified)[] pages)
    {
        var xml = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");

        foreach (var (path, modified) in pages)
        {
            xml.Append("<url><loc>").Append(Absolute(path)).Append("</loc>");

            if (modified is { } lastmod)
            {
                xml.Append("<lastmod>").Append(Iso(lastmod)).Append("</lastmod>");
            }

            xml.Append("</url>");
        }

        return xml.Append("</urlset>").ToString();
    }

    /// <summary>A list of pages that breaks after the last of them.</summary>
    private static string Broken(params string[] paths) =>
        UrlSet(paths).Replace("</urlset>", "<url><loc>broken", StringComparison.Ordinal);

    private static string Index(params string[] paths)
    {
        var xml = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");

        foreach (var path in paths)
        {
            xml.Append("<sitemap><loc>").Append(Absolute(path)).Append("</loc></sitemap>");
        }

        return xml.Append("</sitemapindex>").ToString();
    }

    private static string Article(string title, DateTimeOffset published) =>
        $"""
        <!doctype html><html><head><title>{title} | A blog</title>
        <meta property="og:type" content="article">
        <meta property="og:title" content="{title}">
        <meta property="article:published_time" content="{Iso(published)}">
        </head><body><h1>{title}</h1></body></html>
        """;

    private static RequestDelegate Text(string text, string contentType = "text/plain") => context =>
    {
        context.Response.ContentType = contentType;

        return context.Response.WriteAsync(text, context.RequestAborted);
    };

    private static RequestDelegate Status(int status) => context =>
    {
        context.Response.StatusCode = status;

        return Task.CompletedTask;
    };

    private static RequestDelegate RedirectTo(string location) => context =>
    {
        context.Response.StatusCode = StatusCodes.Status302Found;
        context.Response.Headers.Location = location;

        return Task.CompletedTask;
    };

    private static RequestDelegate RedirectToTheOtherName(string path) => RedirectTo($"http://{OtherName}{path}");

    private static RequestDelegate AskingForAWait(int status, int hours) => context =>
    {
        context.Response.StatusCode = status;
        context.Response.Headers.RetryAfter = TimeSpan.FromHours(hours).TotalSeconds.ToString(CultureInfo.InvariantCulture);

        return Task.CompletedTask;
    };

    /// <summary>A body that says it is compressed, and cannot be unpacked.</summary>
    private static byte[] Corrupt() =>
        [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    /// <summary>Makes a request for one address end in a fault of this product's own.</summary>
    private static Action<IServiceCollection> ThrowingFor(string target, string message = "A fault.") =>
        services => services
            .AddHttpClient(WebsiteHttp.ClientName)
            .AddHttpMessageHandler(() => new Fault(target, message));

    private Task<Site> WebsiteAsync(string sitemap = "/sitemap.xml") => _rig.AddWebsiteAsync(Domain, Origin + sitemap);

    private async Task<SitemapRead> RowAsync(Guid siteId)
    {
        var row = await _rig.RowAsync(siteId);

        row.Should().NotBeNull();

        return row;
    }

    /// <summary>Moves the clock on as far as the next reading, and runs a pass.</summary>
    private Task NextReadingAsync(SitemapEngine engine) => LaterAsync(engine, SitemapRig.Quick.Cadence);

    /// <summary>Moves the clock on, a little past where it is told, and runs a pass.</summary>
    private async Task LaterAsync(SitemapEngine engine, TimeSpan by)
    {
        _rig.Clock.MoveOn(by + TimeSpan.FromSeconds(1));

        await engine.RunAsync();
    }

    private void Fail(string target, HowAPartFails how)
    {
        switch (how)
        {
            case HowAPartFails.ServerError:
                _rig.Website.Status(target, StatusCodes.Status500InternalServerError);
                break;

            case HowAPartFails.NotFound:
                _rig.Website.Status(target, StatusCodes.Status404NotFound);
                break;

            default:
                _rig.Website.Serve(target, $"<urlset><url><loc>{Origin}/two</loc></url><url><loc>broken");
                break;
        }
    }

    /// <summary>Answers an address with a tag, and says nothing has changed when asked with it.</summary>
    private void ServeWithATag(string target, string body)
    {
        const string Tag = "\"tagged\"";

        _rig.Website.Answer(target, async context =>
        {
            if (context.Request.Headers.IfNoneMatch == Tag)
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;

                return;
            }

            context.Response.ContentType = "application/xml";
            context.Response.Headers.ETag = Tag;

            await context.Response.WriteAsync(body, context.RequestAborted);
        });
    }

    /// <summary>Answers an address with a tag no request could carry back.</summary>
    private void ServeWithATagPastAscii(string target, string body, string contentType) =>
        _rig.Website.Answer(target, async context =>
        {
            context.Response.ContentType = contentType;
            context.Response.Headers.ETag = "\"caf" + (char)0xE9 + "\"";

            await context.Response.WriteAsync(body, context.RequestAborted);
        });

    /// <summary>Answers an address one way at the website's own name and another at its other name.</summary>
    private void AnswerByName(string target, RequestDelegate atItsOwnName, RequestDelegate atItsOtherName) =>
        _rig.Website.Answer(target, context =>
            context.Request.Host.Host == OtherName ? atItsOtherName(context) : atItsOwnName(context));

    /// <summary>How many times the website's other name was asked for an address.</summary>
    private int AskedOfTheOtherName(string target) =>
        _rig.Website.Seen.Count(request => request.Host == OtherName && request.Target == target);

    /// <summary>Holds up the answer to an address until told to let it go.</summary>
    private HeldAnswer HoldAnswer(string target, string body, string contentType = "application/xml")
    {
        var held = new HeldAnswer();

        _rig.Website.Answer(target, async context =>
        {
            held.Arrive();

            await held.Released.WaitAsync(TimeSpan.FromSeconds(60), context.RequestAborted);

            context.Response.ContentType = contentType;

            await context.Response.WriteAsync(body, context.RequestAborted);
        });

        return held;
    }

    /// <summary>
    /// Sets everything a claim would leave as it was before, with the website's time come at a given moment.
    /// </summary>
    private Task ReleaseEverythingAsync(Guid siteId, DateTimeOffset? nextPassAt = null)
    {
        var at = nextPassAt ?? DateTimeOffset.UnixEpoch;

        return _rig.SqlAsync(
            $"""
            UPDATE sitemap_reads
            SET claim_token = NULL, claimed_until = NULL, next_pass_at = CASE WHEN site_id = {siteId} THEN {at} ELSE next_pass_at END
            """);
    }

    private Task LoseClaimAsync(Guid siteId, HowAClaimIsLost how) => how switch
    {
        HowAClaimIsLost.Taken => _rig.SqlAsync(
            $"UPDATE sitemap_reads SET claim_token = {Guid.NewGuid()} WHERE site_id = {siteId}"),
        HowAClaimIsLost.RanOut => _rig.ExpireClaimAsync(siteId),
        _ => _rig.SqlAsync($"DELETE FROM sitemap_reads WHERE site_id = {siteId}"),
    };

    /// <summary>
    /// A website with something of every kind for a write to change, claimed by an engine.
    /// </summary>
    private async Task<(Site Site, SitemapEngine Engine, SitemapLease Lease, Guid Part)> AWebsiteClaimedPartWayThroughAsync()
    {
        _rig.Website.Serve("/post-sitemap.xml", UrlSet("/hello"));
        _rig.Website.Serve("/hello", Article("Hello", Now.AddDays(-3)), Html);

        var site = await WebsiteAsync("/post-sitemap.xml");
        var engine = _rig.Engine();

        await engine.RunAsync();

        var at = Now;

        await _rig.SqlAsync(
            $"""
            INSERT INTO site_posts (site_id, path, address, listed_in_posts_part, first_listed_at, unlisted_at)
            VALUES ({site.Id}, '/nothing-vouches-for', 'http://blog.example.test/nothing-vouches-for', true, {at}, NULL),
                   ({site.Id}, '/long-gone', 'http://blog.example.test/long-gone', true, {at}, {at.AddYears(-2)})
            """);

        await _rig.SqlAsync(
            $"""
            INSERT INTO sitemap_parts (id, site_id, address, is_posts, entries, off_site_entries, query_entries)
            VALUES ({Guid.NewGuid()}, {site.Id}, 'http://blog.example.test/waiting.xml', true, 0, 0, 0)
            """);

        await _rig.MakeDueAsync(site.Id);

        var lease = await engine.ClaimAsync();

        lease.Should().NotBeNull();

        var part = (await _rig.PartsAsync(site.Id)).Single(candidate => candidate.ReadAt is not null);

        return (site, engine, lease, part.Id);
    }

    /// <summary>Everything a reading of one website has written, in a form that can be compared.</summary>
    private async Task<string> EverythingAsync(Guid siteId) =>
        JsonSerializer.Serialize(new
        {
            Row = await _rig.RowAsync(siteId),
            Parts = await _rig.PartsAsync(siteId),
            Pages = await _rig.PagesAsync(siteId),
        });

    private IEnumerable<(string Name, Func<Task> Write)> WritesThatHaveToChangeSomething(
        PostWriter writer,
        SitemapLease lease,
        Guid part)
    {
        var token = Cancellation.Token;
        var now = Now.AddMinutes(1);
        var other = new WebsiteValidators("\"other\"", now);

        yield return (nameof(writer.BeginAddressAsync), () => writer.BeginAddressAsync(lease, now.AddHours(1), now, token));
        yield return (nameof(writer.GiveBackAddressAsync), () => writer.GiveBackAddressAsync(lease, now, token));
        yield return (
            nameof(writer.OpenReadingAsync),
            () => writer.OpenReadingAsync(
                lease,
                new ReadingOpening(SitemapRootKind.Index, new WebsiteValidators("\"opened\"", now), null),
                now,
                token));
        yield return (nameof(writer.RememberAddressAsync), () => writer.RememberAddressAsync(lease, other, now, token));
        yield return (
            nameof(writer.CloseReadingAsync),
            () => writer.CloseReadingAsync(lease, new ReadingClosing(SitemapState.PartlyRead, SitemapProblem.TooMany, now, now), now, token));
        yield return (nameof(writer.FailReadingAsync), () => writer.FailReadingAsync(lease, SitemapProblem.NotFound, now, now, token));

        yield return (nameof(writer.BeginPartAsync), () => writer.BeginPartAsync(lease, part, now, token));
        yield return (nameof(writer.GiveBackPartAsync), () => writer.GiveBackPartAsync(lease, part, token));
        yield return (
            nameof(writer.FinishPartAsync),
            () => writer.FinishPartAsync(lease, part, new PartReading(Guid.NewGuid(), other, 9, 8, 7), now, token));
        yield return (nameof(writer.CutPartShortAsync), () => writer.CutPartShortAsync(lease, part, 3, now, token));
        yield return (nameof(writer.SettlePartAsync), () => writer.SettlePartAsync(lease, part, SitemapPartOutcome.NotFound, now, token));

        yield return (
            nameof(writer.ListPagesAsync),
            () => writer.ListPagesAsync(
                lease,
                new PartListing(part, Guid.NewGuid(), true),
                [new PostListing("/new-page", Origin + "/new-page", now)],
                now,
                token));

        yield return (nameof(writer.BeginPageAsync), () => writer.BeginPageAsync(lease, "/hello", 2, now, now, token));
        yield return (
            nameof(writer.SavePageAsync),
            () => writer.SavePageAsync(
                lease,
                "/hello",
                new PageSaying(new PageFacts("Changed", PostTitleSource.Twitter, now, PostDateSource.Microdata, PostPageType.Other), other, "/hello/"),
                now,
                token));
        yield return (nameof(writer.SettlePageAsync), () => writer.SettlePageAsync(lease, "/hello", PostPageOutcome.Gone, null, now, token));
        yield return (nameof(writer.PutPageOffAsync), () => writer.PutPageOffAsync(lease, "/hello", PostPageOutcome.Unreachable, now.AddYears(1), token));
        yield return (nameof(writer.GiveBackPageAsync), () => writer.GiveBackPageAsync(lease, "/hello", 1, now, token));
    }

    private IEnumerable<(string Name, Func<Task> Write)> WritesThatMayChangeNothing(PostWriter writer, SitemapLease lease)
    {
        var token = Cancellation.Token;
        var now = Now.AddMinutes(1);

        yield return (nameof(writer.AskPartsAgainAsync), () => writer.AskPartsAgainAsync(lease, token));
        yield return (nameof(writer.GiveUpOnTheRestAsync), () => writer.GiveUpOnTheRestAsync(lease, SitemapPartOutcome.ServerError, now, token));
        yield return (nameof(writer.KeepPartsAsTheyAreAsync), () => writer.KeepPartsAsTheyAreAsync(lease, now, token));
        yield return (nameof(writer.UnlistAsync), () => writer.UnlistAsync(lease, now, token));
        yield return (nameof(writer.ForgetUnvouchedAsync), () => writer.ForgetUnvouchedAsync(lease, 2, long.MaxValue, token));
        yield return (nameof(writer.ForgetUnlistedAsync), () => writer.ForgetUnlistedAsync(lease, now, 0, token));
        yield return (
            nameof(writer.NamePartsAsync),
            () => writer.NamePartsAsync(lease, [new NamedPart(Guid.NewGuid(), Origin + "/new-part.xml", true)], token));
    }

    /// <summary>An answer a test is holding up.</summary>
    private sealed class HeldAnswer
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes once the request has arrived.</summary>
        public Task Reached => _reached.Task;

        /// <summary>Completes once the answer may go.</summary>
        public Task Released => _released.Task;

        /// <summary>Says the request has arrived.</summary>
        public void Arrive() => _reached.TrySetResult();

        /// <summary>Lets the answer go.</summary>
        public void Release() => _released.TrySetResult();
    }

    /// <summary>Whether a website's account is being measured, as a test says.</summary>
    private sealed class SwitchedAllowance : IMeasurementAllowance
    {
        /// <summary>Whether it is.</summary>
        public bool Measuring { get; set; }

        /// <inheritdoc />
        public ValueTask<bool> AllowsAsync(SiteSnapshot site, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Measuring);
    }

    /// <summary>Ends a request for one address in an exception nothing knows what to do with.</summary>
    private sealed class Fault(string target, string message) : DelegatingHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri?.PathAndQuery == target
                ? throw new InvalidOperationException(message)
                : base.SendAsync(request, cancellationToken);
    }
}
