using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that where a site's visitors came from is counted once per person, and that the site
/// itself never appears on its own list.
/// </summary>
/// <remarks>
/// Two properties here only a real store can settle. Every page after a visit's first was reached
/// from the site being measured, so without the exclusion the answer to "where is this traffic
/// from" is the customer's own address at the top with everything real beneath it. And a visitor
/// carries their source across every page they go on to read, so a busy reader from one link is
/// one arrival rather than twenty.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SiteSourcesTests(AnalyticsStackFixture stack)
{
    private static readonly DateTimeOffset Midnight = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Measured = "example.com";

    [Fact]
    public async Task Sending_Sites_Are_Listed_By_How_Many_People_They_Sent()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "visitor-a", "https://news.ycombinator.com/item?id=1"),
            From(siteId, Midnight.AddHours(1), "visitor-b", "https://news.ycombinator.com/item?id=1"),
            From(siteId, Midnight.AddHours(2), "visitor-c", "https://news.ycombinator.com/newest"),
            From(siteId, Midnight.AddHours(3), "visitor-d", "https://duckduckgo.com/"));

        var sources = await PageOfSources(siteId);

        sources.Sources.Select(source => source.Source)
            .Should().Equal("news.ycombinator.com", "DuckDuckGo");
        sources.Sources.Select(source => source.Visitors).Should().Equal(3, 1);
    }

    /// <summary>
    /// The whole reason the site's own address is excluded. Reading four pages produces three
    /// reports naming the site itself, and taken at face value the busiest source of a site's
    /// traffic is always that site.
    /// </summary>
    [Fact]
    public async Task A_Site_Is_Never_Its_Own_Source()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "reader", "https://duckduckgo.com/", path: "/a"),
            From(siteId, Midnight.AddHours(1), "reader", $"https://{Measured}/a", path: "/b"),
            From(siteId, Midnight.AddHours(2), "reader", $"https://{Measured}/b", path: "/c"),
            From(siteId, Midnight.AddHours(2), "reader", $"https://{Measured}/c", path: "/d"));

        var sources = await PageOfSources(siteId);

        sources.Sources.Select(source => source.Source).Should().Equal("DuckDuckGo");
        sources.Sources[0].Visitors.Should().Be(1);
        sources.Sources[0].PageViews.Should().Be(4);
    }

    /// <summary>
    /// Nor is anything below it. A site reachable at both its bare address and a documentation
    /// subdomain would otherwise list one half of itself as a source of the other.
    /// </summary>
    [Fact]
    public async Task A_Subdomain_Of_The_Site_Is_Not_A_Source_Either()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "reader", $"https://docs.{Measured}/guide"),
            From(siteId, Midnight.AddHours(2), "visitor-b", "https://duckduckgo.com/"));

        var sources = await PageOfSources(siteId);

        sources.Sources.Select(source => source.Source).Should().Equal("", "DuckDuckGo");
    }

    /// <summary>
    /// A near miss the exclusion must not make. A domain that merely ends with the site's is a
    /// different site, and dropping it would hide a real source.
    /// </summary>
    [Fact]
    public async Task A_Site_That_Merely_Resembles_The_Measured_One_Is_A_Source()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "visitor-a", $"https://not{Measured}/post"));

        var sources = await PageOfSources(siteId);

        sources.Sources.Select(source => source.Source).Should().Equal($"not{Measured}");
    }

    /// <summary>
    /// A visitor who arrived from somewhere keeps that source across everything they read, so one
    /// arrival is one arrival however far it went.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Is_Credited_To_One_Source_However_Many_Pages_They_Read()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "one-reader", "https://news.ycombinator.com/item", path: "/a"),
            From(siteId, Midnight.AddHours(1), "one-reader", $"https://{Measured}/a", path: "/b"),
            From(siteId, Midnight.AddHours(2), "one-reader", $"https://{Measured}/b", path: "/c"),
            From(siteId, Midnight.AddHours(3), "two-readers-1", "https://duckduckgo.com/"),
            From(siteId, Midnight.AddHours(3), "two-readers-2", "https://duckduckgo.com/"));

        var sources = await PageOfSources(siteId);

        sources.Sources.Select(source => source.Source)
            .Should().Equal("DuckDuckGo", "news.ycombinator.com");
        sources.Sources.Select(source => source.Visitors).Should().Equal(2, 1);
    }

    /// <summary>
    /// Arrivals naming nowhere are a row rather than an omission. On most sites they are the
    /// largest share of the audience, and a list that quietly dropped them would leave every share
    /// on the screen taken against a total that excluded most of the people.
    /// </summary>
    [Fact]
    public async Task Arrivals_Naming_Nowhere_Are_A_Row_Of_Their_Own()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "visitor-a", referrer: null),
            From(siteId, Midnight.AddHours(1), "visitor-b", referrer: null),
            From(siteId, Midnight.AddHours(2), "visitor-c", "https://duckduckgo.com/"));

        var sources = await PageOfSources(siteId);

        sources.Sources.Select(source => source.Source).Should().Equal("", "DuckDuckGo");
        sources.Sources.Select(source => source.Visitors).Should().Equal(2, 1);
        sources.TotalVisitors.Should().Be(3);
    }

    /// <summary>
    /// Grouped by page, the row is the address of the page the link was on — which article sent
    /// the readers, rather than merely which site.
    /// </summary>
    [Fact]
    public async Task Sending_Pages_Name_The_Page_The_Link_Was_On()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "visitor-a", "https://news.ycombinator.com/item?id=1"),
            From(siteId, Midnight.AddHours(1), "visitor-b", "https://news.ycombinator.com/item?id=1"),
            From(siteId, Midnight.AddHours(2), "visitor-c", "https://news.ycombinator.com/newest"));

        var sources = await PageOfSources(siteId, SourceGrouping.Page);

        sources.Sources.Select(source => source.Source)
            .Should().Equal("news.ycombinator.com/item", "news.ycombinator.com/newest");
        sources.Sources.Select(source => source.Site)
            .Should().Equal("news.ycombinator.com", "news.ycombinator.com");
    }

    /// <summary>
    /// And carries nothing after the question mark. That part is somebody else's site holding
    /// somebody else's state, and which article sent the readers is answered without it.
    /// </summary>
    [Fact]
    public async Task A_Sending_Page_Drops_Whatever_Followed_A_Question_Mark()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "visitor-a", "https://elsewhere.test/post?token=secret-value"));

        var sources = await PageOfSources(siteId, SourceGrouping.Page);

        sources.Sources.Select(source => source.Source).Should().Equal("elsewhere.test/post");
        sources.Sources[0].Source.Should().NotContain("secret-value");
    }

    /// <summary>
    /// The figures beside the rows describe the whole window rather than the slice, so a share
    /// stays true on the fourth screenful of a long list.
    /// </summary>
    [Fact]
    public async Task A_Slice_Describes_The_Whole_Window_It_Came_From()
    {
        var siteId = Guid.NewGuid();

        RawEvent[] traffic =
        [
            .. Enumerable.Range(0, 12).Select(rank =>
                From(siteId, Midnight.AddHours(1), $"visitor-{rank}", $"https://sender-{rank}.test/")),
        ];

        await WriteAsync(traffic);

        var sources = await PageOfSources(siteId, limit: 5);

        sources.Sources.Should().HaveCount(5);
        sources.TotalVisitors.Should().Be(12);
        sources.TotalSources.Should().Be(12);
        sources.MostVisitors.Should().Be(1);
    }

    /// <summary>
    /// Successive slices neither repeat a source nor skip one, which needs the total ordering the
    /// statement carries: twelve sources of equal size have nothing else to be told apart by.
    /// </summary>
    [Fact]
    public async Task Every_Source_A_Window_Holds_Can_Be_Reached_A_Slice_At_A_Time()
    {
        var siteId = Guid.NewGuid();

        RawEvent[] traffic =
        [
            .. Enumerable.Range(0, 12).Select(rank =>
                From(siteId, Midnight.AddHours(1), $"visitor-{rank}", $"https://sender-{rank}.test/")),
        ];

        await WriteAsync(traffic);

        var first = await PageOfSources(siteId, limit: 5);
        var second = await PageOfSources(siteId, limit: 5, offset: 5);
        var third = await PageOfSources(siteId, limit: 5, offset: 10);

        var walked = first.Sources
            .Concat(second.Sources)
            .Concat(third.Sources)
            .Select(source => source.Source)
            .ToArray();

        walked.Should().HaveCount(12);
        walked.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Another_Site_Traffic_Takes_No_Part()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "visitor-a", "https://duckduckgo.com/"),
            From(Guid.NewGuid(), Midnight.AddHours(1), "visitor-b", "https://elsewhere.test/"));

        var sources = await PageOfSources(siteId);

        sources.Sources.Select(source => source.Source).Should().Equal("DuckDuckGo");
    }

    [Fact]
    public async Task A_Window_With_No_Traffic_Answers_With_Nothing_And_A_Nought()
    {
        var sources = await PageOfSources(Guid.NewGuid());

        sources.Sources.Should().BeEmpty();
        sources.TotalVisitors.Should().Be(0);
        sources.TotalSources.Should().Be(0);
        sources.MostVisitors.Should().Be(0);
    }

    /// <summary>
    /// A referrer is written by whoever visited the site, so it is grouped on and never built into
    /// the statement — the same treatment a requested path gets, and for the same reason.
    /// </summary>
    [Fact]
    public async Task A_Referrer_Written_To_Break_The_Statement_Is_Counted_Like_Any_Other()
    {
        var siteId = Guid.NewGuid();
        const string hostile = "https://elsewhere.test/'); DROP TABLE events; --";

        await WriteAsync(From(siteId, Midnight.AddHours(1), "visitor-a", hostile));

        var sources = await PageOfSources(siteId);

        sources.Sources.Select(source => source.Source).Should().Equal("elsewhere.test");
    }

    /// <summary>
    /// The whole point of naming a site rather than listing its addresses. One search engine
    /// answers on hundreds of them, and without this its traffic is spread over a dozen rows and
    /// appears on none of them at its real size.
    /// </summary>
    [Fact]
    public async Task One_Search_Engine_Is_One_Row_However_Many_Addresses_It_Answers_On()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "visitor-a", "https://www.google.com/"),
            From(siteId, Midnight.AddHours(1), "visitor-b", "https://google.com/"),
            From(siteId, Midnight.AddHours(2), "visitor-c", "https://www.google.co.in/"),
            From(siteId, Midnight.AddHours(2), "visitor-d", "https://google.de/"),
            From(siteId, Midnight.AddHours(3), "visitor-e", "https://duckduckgo.com/"));

        var sources = await PageOfSources(siteId);

        sources.Sources.Select(source => source.Source).Should().Equal("Google", "DuckDuckGo");
        sources.Sources.Select(source => source.Visitors).Should().Equal(4, 1);
    }

    /// <summary>
    /// A referrer is written by whoever visited the site. Recognising a site by any label in its
    /// address would let somebody who registers <c>google.attacker.test</c> file their traffic
    /// under Google's name on a stranger's dashboard; the label in front of the public suffix is
    /// <c>attacker</c>, so they cannot.
    /// </summary>
    [Fact]
    public async Task A_Site_Cannot_Take_Another_Site_Name_By_Borrowing_Its_Word()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "visitor-a", "https://google.attacker.test/"),
            From(siteId, Midnight.AddHours(2), "visitor-b", "https://www.google.com/"));

        var sources = await PageOfSources(siteId);

        sources.Sources.Select(source => source.Source).Should().BeEquivalentTo("Google", "google.attacker.test");
        sources.Sources.Should().ContainSingle(source => source.Source == "Google")
            .Which.Visitors.Should().Be(1);
    }

    /// <summary>
    /// An address whose job differs from the rest of its site's is named separately. Somebody
    /// sending a link by mail is not a search, and counting it under search engines would overstate
    /// the one figure this card exists to give honestly.
    /// </summary>
    [Fact]
    public async Task An_Address_Doing_A_Different_Job_Is_Not_Its_Site()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "visitor-a", "https://mail.google.com/"),
            From(siteId, Midnight.AddHours(2), "visitor-b", "https://www.google.com/"));

        var sources = await PageOfSources(siteId);

        sources.Sources.Select(source => source.Source).Should().BeEquivalentTo("Gmail", "Google");
    }

    /// <summary>
    /// The question a list of hostnames cannot answer: how much of an audience search brings,
    /// without the reader having to know which of the names are search engines.
    /// </summary>
    [Fact]
    public async Task Sources_Can_Be_Counted_By_What_Kind_Of_Thing_They_Are()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "searcher-1", "https://www.google.com/"),
            From(siteId, Midnight.AddHours(1), "searcher-2", "https://duckduckgo.com/"),
            From(siteId, Midnight.AddHours(1), "searcher-3", "https://www.bing.com/search"),
            From(siteId, Midnight.AddHours(2), "friend-1", "https://bsky.app/profile/someone"),
            From(siteId, Midnight.AddHours(2), "friend-2", "https://www.reddit.com/r/selfhosted"),
            From(siteId, Midnight.AddHours(3), "asked", "https://chatgpt.com/"),
            From(siteId, Midnight.AddHours(3), "linked", "https://lobste.rs/s/abc"),
            From(siteId, Midnight.AddHours(4), "nobody", referrer: null));

        var sources = await PageOfSources(siteId, SourceGrouping.Kind);

        sources.Sources.Select(source => source.Source)
            .Should().BeEquivalentTo("search", "social", "assistant", "link", "");

        sources.Sources.Should().ContainSingle(source => source.Source == "search")
            .Which.Visitors.Should().Be(3);
        sources.Sources.Should().ContainSingle(source => source.Source == "social")
            .Which.Visitors.Should().Be(2);
        sources.Sources.Should().ContainSingle(source => source.Source == "assistant")
            .Which.Visitors.Should().Be(1);
    }

    /// <summary>
    /// A site nobody has catalogued is a link from another website, which is what it is. There is
    /// no list of every website, and a product that pretended otherwise would be inventing
    /// precision.
    /// </summary>
    [Fact]
    public async Task An_Uncatalogued_Site_Is_A_Link_And_Keeps_Its_Own_Address()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            From(siteId, Midnight.AddHours(1), "visitor-a", "https://www.example-blog.test/post"));

        var byKind = await PageOfSources(siteId, SourceGrouping.Kind);
        var bySite = await PageOfSources(siteId);

        byKind.Sources.Select(source => source.Source).Should().Equal("link");
        bySite.Sources.Select(source => source.Source).Should().Equal("example-blog.test");
    }

    private Task<SiteSources> PageOfSources(
        Guid siteId,
        SourceGrouping grouping = SourceGrouping.Site,
        int limit = 10,
        int offset = 0) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteSourcesAsync(
            Scope(siteId),
            new SiteSourcesQuery(
                new TimeRange(Midnight, Midnight.AddDays(1)),
                grouping,
                Measured,
                limit,
                offset),
            Cancellation.Token);

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    private static TenantScope Scope(Guid siteId) =>
        new(siteId, Guid.NewGuid(), SiteRole.Viewer, "Etc/UTC");

    private static RawEvent From(
        Guid siteId,
        DateTimeOffset at,
        string? visitorKey,
        string? referrer,
        string path = "/",
        IngestSurface surface = IngestSurface.BrowserTracker) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = EventKind.PageView,
            Surface = surface,
            ServerTimestamp = at,
            VisitorKey = visitorKey,
            Host = Measured,
            Path = path,
            Referrer = referrer,
            ReferrerDomain = HostOf(referrer),
        };

    /// <summary>
    /// The host of a referrer, derived here as the collector derives it on the way in.
    /// </summary>
    /// <param name="referrer">The address the browser named, or nothing.</param>
    /// <returns>The host, or <see langword="null"/> when there was no usable address.</returns>
    private static string? HostOf(string? referrer) =>
        Uri.TryCreate(referrer, UriKind.Absolute, out var address) ? address.Host : null;
}
