using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers asking a website for something: where the request may go, what it says about itself, and
/// everything a website can do with being asked.
/// </summary>
/// <remarks>
/// On the shared stack for the sake of the test that the client the engine itself registers refuses
/// this machine, which needs the engine. Everything else talks to a real server on a real socket,
/// through <see cref="LoopbackEndpoints"/>, the one way past that refusal, which is compiled into this
/// project and nothing else.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class WebsiteFetcherTests(AnalyticsStackFixture stack) : IAsyncLifetime
{
    private const string Domain = "blog.example.test";
    private const string Origin = "http://" + Domain;
    private const string SitemapXml = "<urlset><url><loc>http://blog.example.test/a</loc></url></urlset>";

    /// <summary>What these tests run under, with no gap to wait through and time for a slow machine.</summary>
    private static readonly SitemapOptions Quick = new()
    {
        RequestGap = TimeSpan.FromMilliseconds(1),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        SitemapTimeout = TimeSpan.FromSeconds(30),
        PageTimeout = TimeSpan.FromSeconds(30),
        LargestSitemapBytes = 1024 * 1024,
        PageBytesRead = 64 * 1024,
    };

    private readonly List<ServiceProvider> _providers = [];
    private readonly Site _site = new(Guid.NewGuid(), Guid.NewGuid(), Domain, "Etc/UTC", DateTimeOffset.MinValue);
    private LoopbackWebsite _website = null!;

    private WebsiteBounds Bounds => new(_site, new Uri(Origin + "/sitemap.xml"));

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _website = await LoopbackWebsite.StartAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _website.DisposeAsync();

        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(StatusCodes.Status301MovedPermanently)]
    [InlineData(StatusCodes.Status302Found)]
    [InlineData(StatusCodes.Status303SeeOther)]
    [InlineData(StatusCodes.Status307TemporaryRedirect)]
    [InlineData(StatusCodes.Status308PermanentRedirect)]
    public async Task A_Redirect_Within_The_Website_Is_Followed(int status)
    {
        _website.Redirect("/sitemap.xml", "/moved.xml", status);
        _website.Serve("/moved.xml", SitemapXml);

        using var fetched = await FetchAsync("/sitemap.xml", FetchKind.Sitemap);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);
        fetched.Address.Should().Be(new Uri(Origin + "/moved.xml"));
        (await ReadAllAsync(fetched)).Should().Be(SitemapXml);
    }

    /// <summary>
    /// The website at its other name is the same website, and a server sending people from one name to
    /// the other is about the most common redirect there is.
    /// </summary>
    /// <param name="location">Where it redirects to.</param>
    [Theory]
    [InlineData("http://www.blog.example.test/moved.xml")]
    [InlineData("HTTP://BLOG.EXAMPLE.TEST/moved.xml")]
    [InlineData("//www.blog.example.test/moved.xml")]
    [InlineData("moved.xml")]
    public async Task A_Redirect_To_The_Websites_Other_Name_Is_Followed(string location)
    {
        _website.Redirect("/sitemap.xml", location);
        _website.Serve("/moved.xml", SitemapXml);

        using var fetched = await FetchAsync("/sitemap.xml", FetchKind.Sitemap);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);
    }

    /// <summary>
    /// Anywhere at all but the website, and nothing is sent there. The last of these is below the
    /// website's domain, which is the location rule's business: a sitemap speaks for its own host.
    /// </summary>
    /// <param name="location">Where it redirects to.</param>
    [Theory]
    [InlineData("http://evil.test/moved.xml")]
    [InlineData("http://blog.example.test.evil.test/moved.xml")]
    [InlineData("http://notblog.example.test/moved.xml")]
    [InlineData("http://example.test/moved.xml")]
    [InlineData("http://shop.blog.example.test/moved.xml")]
    public async Task A_Redirect_Off_The_Website_Is_Refused(string location)
    {
        _website.Redirect("/sitemap.xml", location);
        _website.Serve("/moved.xml", SitemapXml);

        using var fetched = await FetchAsync("/sitemap.xml", FetchKind.Sitemap);

        fetched.Outcome.Should().Be(FetchOutcome.LeftTheWebsite);
        _website.Seen.Should().ContainSingle().Which.Target.Should().Be("/sitemap.xml");
    }

    [Fact]
    public async Task An_Address_Off_The_Website_Is_Not_Asked_For_At_All()
    {
        using var fetched = await Fetcher().FetchAsync(
            Bounds,
            new Uri("http://shop.blog.example.test/page"),
            FetchKind.Page,
            default,
            null,
            Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.LeftTheWebsite);
        _website.Seen.Should().BeEmpty();
    }

    [Fact]
    public async Task Five_Redirects_Are_Followed()
    {
        ChainOfRedirects(5);

        using var fetched = await FetchAsync("/hop0", FetchKind.Page);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);
        _website.Seen.Should().HaveCount(6);
    }

    [Fact]
    public async Task A_Sixth_Redirect_Is_Refused()
    {
        ChainOfRedirects(6);

        using var fetched = await FetchAsync("/hop0", FetchKind.Page);

        fetched.Outcome.Should().Be(FetchOutcome.TooManyRedirects);
        _website.Seen.Should().HaveCount(6);
    }

    [Theory]
    [InlineData("/a", "/a")]
    [InlineData("/a", "/b")]
    public async Task A_Redirect_Loop_Is_Refused(string first, string second)
    {
        _website.Redirect(first, second);

        if (first != second)
        {
            _website.Redirect(second, first);
        }

        using var fetched = await FetchAsync(first, FetchKind.Page);

        fetched.Outcome.Should().Be(FetchOutcome.TooManyRedirects);
        _website.Seen.Select(seen => seen.Target).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Each of these has the website's own name in it somewhere or points at something the engine can
    /// reach, and a redirect is the one place a website gets to choose freely what comes next.
    /// </summary>
    /// <param name="location">Where it redirects to.</param>
    [Theory]
    [InlineData("http://blog.example.test:8080/moved.xml")]
    [InlineData("http://blog.example.test:443/moved.xml")]
    [InlineData("https://blog.example.test:80/moved.xml")]
    [InlineData("http://127.0.0.1/moved.xml")]
    [InlineData("http://2130706433/moved.xml")]
    [InlineData("http://0x7f.1/moved.xml")]
    [InlineData("http://[::1]/moved.xml")]
    [InlineData("http://[::ffff:127.0.0.1]/moved.xml")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://168.63.129.16/")]
    [InlineData("http://user:secret@blog.example.test/moved.xml")]
    [InlineData("ftp://blog.example.test/moved.xml")]
    [InlineData("file:///etc/passwd")]
    public async Task A_Redirect_To_Another_Port_Or_An_Address_Literal_Is_Refused(string location)
    {
        _website.Redirect("/sitemap.xml", location);
        _website.Serve("/moved.xml", SitemapXml);

        using var fetched = await FetchAsync("/sitemap.xml", FetchKind.Sitemap);

        fetched.Outcome.Should().Be(FetchOutcome.LeftTheWebsite);
        _website.Seen.Should().ContainSingle();
    }

    [Fact]
    public async Task A_Redirect_That_Leads_Nowhere_Is_A_Fault_At_The_Website()
    {
        _website.Status("/sitemap.xml", StatusCodes.Status302Found);

        using var fetched = await FetchAsync("/sitemap.xml", FetchKind.Sitemap);

        fetched.Outcome.Should().Be(FetchOutcome.ServerError);
    }

    /// <summary>
    /// A redirect is a request as far as a website's server goes, so it waits its turn like the rest.
    /// </summary>
    [Fact]
    public async Task Every_Hop_Waits_Its_Turn()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var fetcher = Fetcher(new SitemapOptions { RequestGap = TimeSpan.FromSeconds(1) }, clock);

        _website.Redirect("/first", "/second");
        _website.Serve("/second", "second", "text/html");

        var fetching = fetcher.FetchAsync(Bounds, new Uri(Origin + "/first"), FetchKind.Page, default, null, Cancellation.Token);

        await EventuallyAsync(() => _website.Seen.Count == 1);

        await Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation.Token);
        _website.Seen.Should().ContainSingle();

        clock.Advance(TimeSpan.FromSeconds(1));

        await EventuallyAsync(() => _website.Seen.Count == 2);

        using var fetched = await fetching.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);
    }

    /// <summary>
    /// A website is told what is asking and nothing else: no cookie it set a moment ago, no credential,
    /// no address it came from, and nothing about what the engine was in the middle of.
    /// </summary>
    [Fact]
    public async Task Nothing_Is_Sent_But_The_Product_Name()
    {
        using var inTheMiddleOfSomething = new Activity("judging").Start();

        _website.Answer("/first", context =>
        {
            context.Response.Cookies.Append("visitor", "tracked");
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = "/second";

            return Task.CompletedTask;
        });
        _website.Serve("/second", "second", "text/html");

        using var fetched = await FetchAsync("/first", FetchKind.Page);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);

        _website.Seen.Should().HaveCount(2).And.AllSatisfy(seen =>
        {
            seen.Headers.Keys.Should().BeEquivalentTo("Host", "User-Agent", "Accept", "Accept-Encoding");
            seen.Headers["User-Agent"].Should().Be(WebsiteHttp.UserAgent);
            seen.Headers["Accept-Encoding"].Should().Be("gzip, br");
            seen.Protocol.Should().Be("HTTP/1.1");
        });
    }

    /// <summary>
    /// Whoever sees this in their logs can tell what it is and where to read about it, and the commit
    /// it was built from is nobody's business.
    /// </summary>
    [Fact]
    public void The_Reader_Says_What_It_Is_And_Where_To_Read_About_It()
    {
        WebsiteHttp.UserAgent.Should().StartWith("DewirideAnalytics/");
        WebsiteHttp.UserAgent.Should().EndWith(
            "(sitemap fetcher; +https://github.com/Dewiride-Open-Source/Dewiride-Analytics#reading-a-websites-sitemap)");
        WebsiteHttp.UserAgent.Split(' ')[0].Should().NotContain("+");
    }

    /// <summary>
    /// What is said to a website is asked for by name, so a host resolving to a machine with several
    /// websites on it answers with the right one.
    /// </summary>
    [Fact]
    public async Task A_Website_Is_Asked_For_By_Its_Name()
    {
        _website.Serve("/page", "page");

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        _website.Seen.Should().ContainSingle().Which.Host.Should().Be(Domain);
    }

    [Theory]
    [InlineData(FetchKind.Sitemap, "application/xml, text/xml;q=0.9, */*;q=0.1")]
    [InlineData(FetchKind.Page, "text/html, application/xhtml+xml;q=0.9")]
    [InlineData(FetchKind.Robots, "text/plain, */*;q=0.1")]
    public async Task A_Website_Is_Told_What_Is_Wanted(FetchKind kind, string accept)
    {
        _website.Serve("/thing", "thing");

        using var fetched = await FetchAsync("/thing", kind);

        _website.Seen.Should().ContainSingle().Which.Headers["Accept"].Should().Be(accept);
    }

    [Fact]
    public async Task An_Answer_Says_What_To_Ask_With_Next_Time()
    {
        var modified = new DateTimeOffset(2024, 5, 6, 10, 30, 0, TimeSpan.Zero);

        _website.Answer("/page", async context =>
        {
            context.Response.Headers.ETag = "\"version-1\"";
            context.Response.Headers.LastModified = "Mon, 06 May 2024 10:30:00 GMT";
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("page", context.RequestAborted);
        });

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        fetched.Validators.Should().Be(new WebsiteValidators("\"version-1\"", modified));
        fetched.ContentType.Should().Be("text/html; charset=utf-8");
    }

    [Fact]
    public async Task A_Website_Is_Asked_Whether_Anything_Has_Changed()
    {
        var modified = new DateTimeOffset(2024, 5, 6, 10, 30, 0, TimeSpan.Zero);

        _website.Status("/page", StatusCodes.Status304NotModified);

        using var fetched = await Fetcher().FetchAsync(
            Bounds,
            new Uri(Origin + "/page"),
            FetchKind.Page,
            new WebsiteValidators("\"version-1\"", modified),
            null,
            Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.NotModified);

        var seen = _website.Seen.Should().ContainSingle().Which;

        seen.Headers["If-None-Match"].Should().Be("\"version-1\"");
        seen.Headers["If-Modified-Since"].Should().Be("Mon, 06 May 2024 10:30:00 GMT");
    }

    /// <summary>
    /// A tag is whatever a server sent, and sending it back has to do no harm. One that is not a tag
    /// is not sent, and one that is longer than is kept is not kept.
    /// </summary>
    [Fact]
    public async Task A_Tag_That_Can_Not_Be_Sent_Back_Is_Left_Out()
    {
        _website.Serve("/page", "page", "text/html");

        using var fetched = await Fetcher().FetchAsync(
            Bounds,
            new Uri(Origin + "/page"),
            FetchKind.Page,
            new WebsiteValidators("not a tag\r\nX-Injected: yes", null),
            null,
            Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);

        var seen = _website.Seen.Should().ContainSingle().Which;

        seen.Headers.Should().NotContainKey("If-None-Match");
        seen.Headers.Should().NotContainKey("X-Injected");
    }

    [Fact]
    public async Task A_Tag_Longer_Than_Is_Kept_Is_Not_Kept()
    {
        _website.Answer("/page", async context =>
        {
            context.Response.Headers.ETag = $"\"{new string('a', SitemapRead.MaxEtagLength)}\"";
            await context.Response.WriteAsync("page", context.RequestAborted);
        });

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);
        fetched.Validators.ETag.Should().BeNull();
    }

    /// <summary>
    /// A tag may hold bytes past ASCII and a request may not, so one of those could never be sent back.
    /// </summary>
    [Fact]
    public async Task A_Tag_Holding_More_Than_Ascii_Is_Not_Kept()
    {
        _website.Answer("/page", async context =>
        {
            context.Response.Headers.ETag = "\"caf" + (char)0xE9 + "\"";
            await context.Response.WriteAsync("page", context.RequestAborted);
        });

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);
        fetched.Validators.ETag.Should().BeNull();
    }

    [Theory]
    [InlineData(StatusCodes.Status404NotFound, FetchOutcome.NotFound)]
    [InlineData(StatusCodes.Status410Gone, FetchOutcome.NotFound)]
    [InlineData(StatusCodes.Status400BadRequest, FetchOutcome.TurnedAway)]
    [InlineData(StatusCodes.Status401Unauthorized, FetchOutcome.TurnedAway)]
    [InlineData(StatusCodes.Status403Forbidden, FetchOutcome.TurnedAway)]
    [InlineData(StatusCodes.Status429TooManyRequests, FetchOutcome.AskedToSlowDown)]
    [InlineData(StatusCodes.Status451UnavailableForLegalReasons, FetchOutcome.TurnedAway)]
    [InlineData(StatusCodes.Status500InternalServerError, FetchOutcome.ServerError)]
    [InlineData(StatusCodes.Status502BadGateway, FetchOutcome.ServerError)]
    [InlineData(StatusCodes.Status503ServiceUnavailable, FetchOutcome.ServerError)]
    [InlineData(StatusCodes.Status300MultipleChoices, FetchOutcome.ServerError)]
    [InlineData(StatusCodes.Status304NotModified, FetchOutcome.ServerError)]
    [InlineData(StatusCodes.Status200OK, FetchOutcome.Succeeded)]
    [InlineData(StatusCodes.Status203NonAuthoritative, FetchOutcome.Succeeded)]
    [InlineData(StatusCodes.Status201Created, FetchOutcome.TurnedAway)]
    [InlineData(StatusCodes.Status202Accepted, FetchOutcome.TurnedAway)]
    [InlineData(StatusCodes.Status204NoContent, FetchOutcome.TurnedAway)]
    [InlineData(StatusCodes.Status205ResetContent, FetchOutcome.TurnedAway)]
    [InlineData(StatusCodes.Status206PartialContent, FetchOutcome.TurnedAway)]
    public async Task What_A_Website_Answered_Is_What_Came_Of_Asking(int status, FetchOutcome outcome)
    {
        _website.Status("/page", status);

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        fetched.Outcome.Should().Be(outcome);
    }

    /// <summary>
    /// A sitemap asked for by a firewall's challenge has been asked for all the same, and what comes back
    /// is not the sitemap. A robots.txt is the exception, because the protocol has any success followed.
    /// </summary>
    /// <param name="kind">What was asked for.</param>
    /// <param name="outcome">What comes of it.</param>
    [Theory]
    [InlineData(FetchKind.Sitemap, FetchOutcome.TurnedAway)]
    [InlineData(FetchKind.Robots, FetchOutcome.Succeeded)]
    public async Task A_Success_That_Hands_Over_Nothing_Asked_For_Is_Only_Read_As_A_Robots_Txt(FetchKind kind, FetchOutcome outcome)
    {
        _website.Status("/thing", StatusCodes.Status202Accepted);

        using var fetched = await FetchAsync("/thing", kind);

        fetched.Outcome.Should().Be(outcome);
    }

    /// <summary>A wait is heard from an answer. A redirect is on its way to one, and its turn comes when it comes.</summary>
    [Fact]
    public async Task A_Wait_A_Redirect_Asks_For_Is_Not_Heard()
    {
        _website.Answer("/page", context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = "/moved";
            context.Response.Headers.RetryAfter = "3600";

            return Task.CompletedTask;
        });
        _website.Serve("/moved", "page", "text/html");

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);
        fetched.RetryAfter.Should().BeNull();
        _website.Seen.Select(seen => seen.Target).Should().Equal("/page", "/moved");
    }

    /// <summary>
    /// A hop is an address to read like any other, so robots.txt has its say about it before it is asked for.
    /// </summary>
    [Fact]
    public async Task A_Redirect_Robots_Txt_Refuses_Is_Not_Followed()
    {
        _website.Redirect("/sitemap.xml", "/moved.xml");
        _website.Serve("/moved.xml", SitemapXml);

        using var fetched = await FetchAsking("/sitemap.xml", FetchKind.Sitemap, (_, _) => Says(new RobotsVerdict(false, null)));

        fetched.Outcome.Should().Be(FetchOutcome.RobotsDisallowed);
        fetched.StoppedByRobots.Should().BeTrue();
        fetched.RetryAfter.Should().BeNull();
        _website.Seen.Should().ContainSingle().Which.Target.Should().Be("/sitemap.xml");
    }

    /// <summary>
    /// Every hop is asked about, and what was asked for in the first place is not: whoever began this has
    /// already asked about that, and has done what it was told.
    /// </summary>
    [Fact]
    public async Task Robots_Txt_Is_Asked_About_Where_Each_Redirect_Leads()
    {
        var asked = new List<string>();

        ChainOfRedirects(3);

        using var fetched = await FetchAsking("/hop0", FetchKind.Page, (address, _) =>
        {
            asked.Add(address.PathAndQuery);

            return Says(new RobotsVerdict(true, null));
        });

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);
        fetched.StoppedByRobots.Should().BeFalse();
        asked.Should().Equal("/hop1", "/hop2", "/hop3");
        _website.Seen.Should().HaveCount(4);
    }

    /// <summary>
    /// Trouble reading the file comes back as that trouble, along with any wait it was asked to hear, and it
    /// is kept apart from the same answer coming from what was asked for.
    /// </summary>
    [Fact]
    public async Task What_Stopped_Robots_Txt_Being_Read_Is_What_A_Redirect_Comes_To()
    {
        var verdict = new RobotsVerdict(false, FetchOutcome.ServerError, TimeSpan.FromHours(2));

        _website.Redirect("/page", "/moved");
        _website.Serve("/moved", "page", "text/html");

        using var fetched = await FetchAsking("/page", FetchKind.Page, (_, _) => Says(verdict));

        fetched.Outcome.Should().Be(FetchOutcome.ServerError);
        fetched.RetryAfter.Should().Be(TimeSpan.FromHours(2));
        fetched.StoppedByRobots.Should().BeTrue();
        _website.Seen.Should().ContainSingle();
    }

    /// <summary>An address may not be asked for at all, and nobody's robots.txt is asked about it.</summary>
    [Fact]
    public async Task A_Redirect_Off_The_Website_Is_Refused_Before_Robots_Txt_Is_Asked()
    {
        var asked = 0;

        _website.Redirect("/page", "http://evil.test/moved");

        using var fetched = await FetchAsking("/page", FetchKind.Page, (_, _) =>
        {
            asked++;

            return Says(new RobotsVerdict(true, null));
        });

        fetched.Outcome.Should().Be(FetchOutcome.LeftTheWebsite);
        fetched.StoppedByRobots.Should().BeFalse();
        asked.Should().Be(0);
    }

    /// <summary>
    /// How long a claim has to last is worked out from how long one asking can take, so a robots.txt read
    /// on the way has to fit inside that and not start a clock of its own.
    /// </summary>
    [Fact]
    public async Task Asking_Robots_Txt_About_A_Redirect_Goes_Under_The_Time_The_Asking_Has()
    {
        var fetcher = Fetcher(new SitemapOptions { RequestGap = Quick.RequestGap, PageTimeout = TimeSpan.FromMilliseconds(300) });

        _website.Redirect("/page", "/moved");

        using var fetched = await fetcher
            .FetchAsync(
                Bounds,
                new Uri(Origin + "/page"),
                FetchKind.Page,
                default,
                async (_, token) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);

                    return new RobotsVerdict(true, null);
                },
                Cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.Unreachable);
        fetched.StoppedByRobots.Should().BeFalse();
    }

    [Fact]
    public async Task Whoever_Stops_Waiting_While_Robots_Txt_Is_Asked_Gets_A_Cancellation_And_No_Outcome()
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Cancellation.Token);
        var asking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _website.Redirect("/page", "/moved");

        var fetching = Fetcher().FetchAsync(
            Bounds,
            new Uri(Origin + "/page"),
            FetchKind.Page,
            default,
            async (_, token) =>
            {
                asking.TrySetResult();

                await Task.Delay(Timeout.InfiniteTimeSpan, token);

                return new RobotsVerdict(true, null);
            },
            stopping.Token);

        await asking.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);
        await stopping.CancelAsync();

        await fetching.Invoking(task => task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// A server reading a request and hanging up is what one in trouble does, and what a filter in front of one
    /// does. The handler left to itself sends the request again and again, a millisecond apart, below anything
    /// that could pace it, and the server being asked would see four where it was promised one.
    /// </summary>
    [Fact]
    public async Task A_Website_That_Hangs_Up_Is_Asked_Once()
    {
        await using var website = new HangingUpWebsite();

        var fetcher = Fetcher(endpoints: new LoopbackEndpoints(website.Port));

        using var fetched = await fetcher.FetchAsync(Bounds, new Uri(Origin + "/page"), FetchKind.Page, default, null, Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.Unreachable);
        website.Asked.Should().Equal("/page");
    }

    /// <summary>
    /// A connection kept open since the last answer can be closed by the server just as a request goes out on
    /// it, and nothing has gone wrong with the website. That request goes once more, on a connection of its own,
    /// and where that one is hung up on too the website has not answered.
    /// </summary>
    [Fact]
    public async Task A_Request_Hung_Up_On_Over_A_Connection_Already_Open_Is_Sent_Once_More_And_No_More()
    {
        await using var website = new HangingUpWebsite("/first");

        var fetcher = Fetcher(endpoints: new LoopbackEndpoints(website.Port));

        using (await fetcher.FetchAsync(Bounds, new Uri(Origin + "/first"), FetchKind.Page, default, null, Cancellation.Token))
        {
            // Its answer has no body, so its connection is free again once it has been let go of.
        }

        using var fetched = await fetcher.FetchAsync(Bounds, new Uri(Origin + "/page"), FetchKind.Page, default, null, Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.Unreachable);
        website.Asked.Should().Equal("/first", "/page", "/page");
    }

    /// <summary>What is refused is a second connection for one request, and the next request is another request.</summary>
    [Fact]
    public async Task A_Website_That_Hung_Up_Is_Asked_For_The_Next_Thing_As_Usual()
    {
        await using var website = new HangingUpWebsite("/next");

        var fetcher = Fetcher(endpoints: new LoopbackEndpoints(website.Port));

        using (var hungUpOn = await fetcher.FetchAsync(Bounds, new Uri(Origin + "/page"), FetchKind.Page, default, null, Cancellation.Token))
        {
            hungUpOn.Outcome.Should().Be(FetchOutcome.Unreachable);
        }

        using var fetched = await fetcher.FetchAsync(Bounds, new Uri(Origin + "/next"), FetchKind.Page, default, null, Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.NotFound);
        website.Asked.Should().Equal("/page", "/next");
    }

    /// <summary>A server resetting the connection is not sent anything again by the handler itself, and it has to stay that way.</summary>
    [Fact]
    public async Task A_Website_That_Resets_The_Connection_Is_Asked_Once()
    {
        _website.Answer("/page", context =>
        {
            context.Abort();

            return Task.CompletedTask;
        });

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        fetched.Outcome.Should().Be(FetchOutcome.Unreachable);
        _website.Seen.Should().ContainSingle();
    }

    [Fact]
    public async Task A_Website_Asking_To_Be_Left_Alone_For_A_While_Is_Heard()
    {
        _website.Answer("/page", context =>
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "120";

            return Task.CompletedTask;
        });

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        fetched.Outcome.Should().Be(FetchOutcome.AskedToSlowDown);
        fetched.RetryAfter.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData("Wed, 17 Jun 2026 12:00:00 GMT", 2.0)]
    [InlineData("Mon, 15 Jun 2026 11:00:00 GMT", null)]
    [InlineData("not a time", null)]
    [InlineData("-1", null)]
    public async Task A_Website_Asking_To_Be_Left_Alone_Until_A_Time_Is_Heard(string header, double? days)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));

        _website.Answer("/page", context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = header;

            return Task.CompletedTask;
        });

        using var fetched = await Fetcher(Quick, clock).FetchAsync(
            Bounds,
            new Uri(Origin + "/page"),
            FetchKind.Page,
            default,
            null,
            Cancellation.Token);

        fetched.RetryAfter.Should().Be(days is null ? null : TimeSpan.FromDays(days.Value));
    }

    [Theory]
    [InlineData("text/plain", FetchOutcome.NotAPage)]
    [InlineData("application/json", FetchOutcome.NotAPage)]
    [InlineData("application/pdf", FetchOutcome.NotAPage)]
    [InlineData("image/png", FetchOutcome.NotAPage)]
    [InlineData("text/html", FetchOutcome.Succeeded)]
    [InlineData("TEXT/HTML; charset=utf-8", FetchOutcome.Succeeded)]
    [InlineData("application/xhtml+xml", FetchOutcome.Succeeded)]
    public async Task A_Page_Has_To_Say_It_Is_One(string contentType, FetchOutcome outcome)
    {
        _website.Serve("/page", "page", contentType);

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        fetched.Outcome.Should().Be(outcome);
    }

    [Fact]
    public async Task A_Page_That_Says_Nothing_About_What_It_Is_Is_Given_The_Benefit_Of_The_Doubt()
    {
        _website.Answer("/page", context => context.Response.WriteAsync("page", context.RequestAborted));

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);
    }

    /// <summary>
    /// Sitemap files are served as all sorts of things, a compressed one least of all as anything
    /// helpful, so what one is depends on what is in it.
    /// </summary>
    /// <param name="contentType">What it is said to be.</param>
    [Theory]
    [InlineData("application/xml")]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData("text/html")]
    public async Task A_Sitemap_Can_Be_Said_To_Be_Anything(string contentType)
    {
        _website.Serve("/sitemap.xml", SitemapXml, contentType);

        using var fetched = await FetchAsync("/sitemap.xml", FetchKind.Sitemap);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);
    }

    [Fact]
    public async Task A_Compressed_Sitemap_Arrives_Unpacked_However_It_Was_Sent()
    {
        var compressed = Gzipped.Bytes(Encoding.UTF8.GetBytes(SitemapXml));

        _website.Serve("/in-transit.xml", compressed, "application/xml", "gzip");
        _website.Serve("/as-a-file.xml.gz", compressed, "application/gzip");
        _website.Serve("/both.xml.gz", Gzipped.Bytes(compressed), "application/gzip", "gzip");

        foreach (var target in new[] { "/in-transit.xml", "/as-a-file.xml.gz", "/both.xml.gz" })
        {
            using var fetched = await FetchAsync(target, FetchKind.Sitemap);

            (await ReadAllAsync(fetched)).Should().Be(SitemapXml);
        }
    }

    /// <summary>
    /// A corrupt compressed body is the one failure the framework does not report as an input or output
    /// failure, and it has to come to an answer, or the same part would be fetched again for good.
    /// </summary>
    /// <param name="encoding">What it says it was compressed with.</param>
    /// <param name="kind">What was asked for.</param>
    /// <param name="outcome">What comes of it.</param>
    [Theory]
    [InlineData("gzip", FetchKind.Sitemap, FetchOutcome.NotASitemap)]
    [InlineData("br", FetchKind.Sitemap, FetchOutcome.NotASitemap)]
    [InlineData("deflate", FetchKind.Sitemap, FetchOutcome.NotASitemap)]
    [InlineData("zstd", FetchKind.Sitemap, FetchOutcome.NotASitemap)]
    [InlineData("gzip", FetchKind.Page, FetchOutcome.NotAPage)]
    [InlineData("br", FetchKind.Page, FetchOutcome.NotAPage)]
    [InlineData("deflate", FetchKind.Page, FetchOutcome.NotAPage)]
    [InlineData("zstd", FetchKind.Page, FetchOutcome.NotAPage)]
    public async Task A_Corrupt_Body_Is_Not_What_Was_Asked_For(string encoding, FetchKind kind, FetchOutcome outcome)
    {
        var corrupt = new byte[4096];

        Array.Fill(corrupt, (byte)0xFF);

        _website.Serve("/thing", corrupt, "text/html", encoding);

        using var fetched = await FetchAsync("/thing", kind);

        (await OutcomeOfReadingAsync(fetched, kind)).Should().Be(outcome);
    }

    [Fact]
    public async Task A_File_That_Is_Not_A_Sitemap_Fails_As_Not_A_Sitemap()
    {
        _website.Serve("/sitemap.xml", "<urlset><url><loc>a</wrong></url></urlset>");

        using var fetched = await FetchAsync("/sitemap.xml", FetchKind.Sitemap);

        (await OutcomeOfReadingAsync(fetched, FetchKind.Sitemap)).Should().Be(FetchOutcome.NotASitemap);
    }

    [Fact]
    public async Task A_Sitemap_Past_The_Limit_Is_Too_Large()
    {
        _website.Serve("/sitemap.xml", SitemapOfLength((int)Quick.LargestSitemapBytes + 1), "application/xml");

        using var fetched = await FetchAsync("/sitemap.xml", FetchKind.Sitemap);

        (await OutcomeOfReadingAsync(fetched, FetchKind.Sitemap)).Should().Be(FetchOutcome.TooLarge);
    }

    [Fact]
    public async Task A_Compressed_Sitemap_That_Unpacks_Past_The_Limit_Is_Too_Large()
    {
        var bomb = Gzipped.Bytes(SitemapOfLength((int)Quick.LargestSitemapBytes * 8));

        bomb.Length.Should().BeLessThan((int)Quick.LargestSitemapBytes);

        _website.Serve("/sitemap.xml", bomb, "application/xml", "gzip");

        using var fetched = await FetchAsync("/sitemap.xml", FetchKind.Sitemap);

        (await OutcomeOfReadingAsync(fetched, FetchKind.Sitemap)).Should().Be(FetchOutcome.TooLarge);
    }

    /// <summary>
    /// What a page says about itself is at the top of it. Running on past what is read is not a fault,
    /// and the rest is not asked for.
    /// </summary>
    [Fact]
    public async Task A_Page_Longer_Than_Is_Read_Is_Read_As_Far_As_That()
    {
        _website.Serve("/page", new byte[Quick.PageBytesRead * 10], "text/html");

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        var prefix = await fetched.ReadPrefixAsync();

        prefix.Bytes.Count.Should().Be(Quick.PageBytesRead);
        prefix.Whole.Should().BeFalse();
    }

    [Fact]
    public async Task A_Page_Shorter_Than_Is_Read_Is_Read_Whole()
    {
        _website.Serve("/page", "<html></html>", "text/html");

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        var prefix = await fetched.ReadPrefixAsync();

        Encoding.UTF8.GetString(prefix.Bytes).Should().Be("<html></html>");
        prefix.Whole.Should().BeTrue();
    }

    [Fact]
    public async Task A_Compressed_Page_Is_Read_As_Far_As_Is_Read_However_Far_It_Unpacks()
    {
        _website.Serve("/page", Gzipped.Bytes(new byte[Quick.PageBytesRead * 100]), "text/html", "gzip");

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        var prefix = await fetched.ReadPrefixAsync();

        prefix.Bytes.Count.Should().Be(Quick.PageBytesRead);
    }

    /// <summary>
    /// Sections of a compressed page with nothing in them use up the connection while unpacking to
    /// nothing, and the limit on what arrives is what ends it.
    /// </summary>
    [Fact]
    public async Task A_Page_That_Arrives_Past_The_Limit_While_Unpacking_To_Nothing_Is_Too_Large()
    {
        _website.Serve("/page", Gzipped.EmptySections(2 * Quick.PageBytesRead + 1024), "text/html", "gzip");

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        (await OutcomeOfReadingAsync(fetched, FetchKind.Page)).Should().Be(FetchOutcome.TooLarge);
    }

    /// <summary>
    /// Headers are not counted against anything a body is, and a server can send as many as it likes.
    /// </summary>
    [Fact]
    public async Task Headers_Past_The_Limit_Are_Too_Large()
    {
        _website.Answer("/page", context =>
        {
            context.Response.Headers["X-Long"] = new string('a', 70 * 1024);

            return Task.CompletedTask;
        });

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        fetched.Outcome.Should().Be(FetchOutcome.TooLarge);
    }

    /// <summary>
    /// No website here has a certificate, so asking for it securely fails in the handshake, which is
    /// what a certificate nobody can trust does too.
    /// </summary>
    [Fact]
    public async Task A_Website_Whose_Encryption_Can_Not_Be_Trusted_Is_Insecure()
    {
        var bounds = new WebsiteBounds(_site, new Uri("https://blog.example.test/sitemap.xml"));

        using var fetched = await Fetcher().FetchAsync(
            bounds,
            new Uri("https://blog.example.test/page"),
            FetchKind.Page,
            default,
            null,
            Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.Insecure);
    }

    /// <summary>
    /// A certificate can say where its issuer's certificate and its revocation list are kept, and checking
    /// it would fetch them, with a client of the framework's own that the check of where a connection may go
    /// knows nothing about. Here both are on this machine, which a website's certificate has no business
    /// sending the reader to.
    /// </summary>
    [Fact]
    public async Task A_Certificate_Sends_The_Reader_Nowhere()
    {
        var elsewhere = $"http://127.0.0.1:{_website.Port}";

        using var certificate = CertificateFromAnIssuerNobodyTrusts($"{elsewhere}/issuer.cer", $"{elsewhere}/revoked.crl");

        await using var secure = await LoopbackWebsite.StartAsync(certificate);

        secure.Serve("/page", "a page", "text/html");

        var bounds = new WebsiteBounds(_site, new Uri("https://blog.example.test/sitemap.xml"));

        using var fetched = await Fetcher(endpoints: new LoopbackEndpoints(secure.Port)).FetchAsync(
            bounds,
            new Uri("https://blog.example.test/page"),
            FetchKind.Page,
            default,
            null,
            Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.Insecure);
        secure.Seen.Should().BeEmpty();

        // The engine runs on Linux, where nothing but the reader could ask. A website served from Windows goes through
        // the operating system's own TLS layer, which goes after an issuer for itself, so this could hear the website.
        if (OperatingSystem.IsLinux())
        {
            _website.Seen.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task A_Website_Nobody_Is_Listening_At_Is_Unreachable()
    {
        await _website.DisposeAsync();

        using var fetched = await FetchAsync("/page", FetchKind.Page);

        fetched.Outcome.Should().Be(FetchOutcome.Unreachable);

        _website = await LoopbackWebsite.StartAsync();
    }

    /// <summary>
    /// The real answer to where a connection may go, with a name server saying whatever a test tells
    /// it to, so that this is the check itself coming to the answer and not something standing in.
    /// </summary>
    /// <param name="answer">What the name resolves to.</param>
    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("168.63.129.16")]
    [InlineData("::1")]
    [InlineData("fd00:ec2::254")]
    [InlineData("64:ff9b::a00:5")]
    public async Task A_Name_That_Leads_Into_A_Private_Network_Is_Refused_And_Nothing_Is_Sent(string answer)
    {
        var fetcher = Fetcher(endpoints: new PublicInternetEndpoints((_, _) =>
            Task.FromResult<IPAddress[]>([IPAddress.Parse(answer)])));

        using var fetched = await fetcher.FetchAsync(
            Bounds,
            new Uri(Origin + "/page"),
            FetchKind.Page,
            default,
            null,
            Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.PrivateAddress);
        _website.Seen.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Name_That_Resolves_To_Nothing_Is_Unreachable()
    {
        var fetcher = Fetcher(endpoints: new PublicInternetEndpoints((_, _) => Task.FromResult<IPAddress[]>([])));

        using var fetched = await fetcher.FetchAsync(
            Bounds,
            new Uri(Origin + "/page"),
            FetchKind.Page,
            default,
            null,
            Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.Unreachable);
    }

    /// <summary>
    /// A redirect is a new connection like any other, so the name it leads to is judged like any other.
    /// That this one is on the website's own name is the point.
    /// </summary>
    [Fact]
    public async Task The_Website_Redirecting_To_Its_Twin_Does_Not_Get_Its_Twin_Past_The_Check()
    {
        var endpoints = new OneNameLoopbackOneNamePrivate(_website.Port, safe: Domain);
        var fetcher = Fetcher(endpoints: endpoints);

        _website.Redirect("/page", "http://www.blog.example.test/page");

        using var fetched = await fetcher.FetchAsync(
            Bounds,
            new Uri(Origin + "/page"),
            FetchKind.Page,
            default,
            null,
            Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.PrivateAddress);
        _website.Seen.Should().ContainSingle();
    }

    [Fact]
    public async Task A_Slow_Website_Is_Abandoned_At_The_Timeout()
    {
        var abandoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetcher = Fetcher(new SitemapOptions { RequestGap = Quick.RequestGap, PageTimeout = TimeSpan.FromMilliseconds(300) });

        _website.Answer("/page", async context =>
        {
            await WaitUntilAbandonedAsync(context);
            abandoned.TrySetResult();
        });

        using var fetched = await fetcher
            .FetchAsync(Bounds, new Uri(Origin + "/page"), FetchKind.Page, default, null, Cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.Unreachable);
        await abandoned.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);
    }

    /// <summary>
    /// The client's own timeout stops at the headers, and the XML reader has no way to pass a cancellation
    /// down. A website that starts its answer at once and then gives out a byte at a time would keep a
    /// reader waiting past any lease, for as long as it cared to go on, if nothing else stopped it.
    /// </summary>
    [Fact]
    public async Task A_Website_That_Drips_Its_Sitemap_Is_Abandoned_At_The_Timeout()
    {
        var abandoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetcher = Fetcher(new SitemapOptions
        {
            RequestGap = Quick.RequestGap,
            SitemapTimeout = TimeSpan.FromMilliseconds(500),
        });

        _website.Answer("/sitemap.xml", context => DripAsync(context, abandoned));

        using var fetched = await fetcher
            .FetchAsync(Bounds, new Uri(Origin + "/sitemap.xml"), FetchKind.Sitemap, default, null, Cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);

        var reading = OutcomeOfReadingAsync(fetched, FetchKind.Sitemap);

        (await reading.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token)).Should().Be(FetchOutcome.Unreachable);
        await abandoned.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);
    }

    [Fact]
    public async Task A_Website_That_Drips_A_Page_Is_Abandoned_At_The_Timeout()
    {
        var abandoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetcher = Fetcher(new SitemapOptions
        {
            RequestGap = Quick.RequestGap,
            PageTimeout = TimeSpan.FromMilliseconds(500),
        });

        _website.Answer("/page", context => DripAsync(context, abandoned));

        using var fetched = await fetcher
            .FetchAsync(Bounds, new Uri(Origin + "/page"), FetchKind.Page, default, null, Cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        var reading = OutcomeOfReadingAsync(fetched, FetchKind.Page);

        (await reading.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token)).Should().Be(FetchOutcome.Unreachable);
        await abandoned.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);
    }

    /// <summary>
    /// Compressed, what is waited for is the decompressor waiting for what is under it, and that has to
    /// be held to the deadline as well. It gets the start of a real file and then nothing more.
    /// </summary>
    [Fact]
    public async Task A_Website_That_Stalls_Partway_Through_A_Compressed_Sitemap_Is_Abandoned_At_The_Timeout()
    {
        var abandoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetcher = Fetcher(new SitemapOptions
        {
            RequestGap = Quick.RequestGap,
            SitemapTimeout = TimeSpan.FromMilliseconds(500),
        });
        var compressed = Gzipped.Bytes(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(SitemapXml, 100))));

        _website.Answer("/sitemap.xml", async context =>
        {
            context.Response.Headers.ContentEncoding = "gzip";
            await context.Response.Body.WriteAsync(compressed.AsMemory(0, compressed.Length / 2), context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await WaitUntilAbandonedAsync(context);
            abandoned.TrySetResult();
        });

        using var fetched = await fetcher
            .FetchAsync(Bounds, new Uri(Origin + "/sitemap.xml"), FetchKind.Sitemap, default, null, Cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        var reading = OutcomeOfReadingAsync(fetched, FetchKind.Sitemap);

        (await reading.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token)).Should().Be(FetchOutcome.Unreachable);
        await abandoned.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);
    }

    /// <summary>
    /// Every read of a body goes under the deadline, but a blocking read can not be cancelled by anybody.
    /// That is what the deadline disposing the response is for: the connection goes away from under it.
    /// </summary>
    [Fact]
    public async Task A_Blocking_Read_Nobody_Can_Cancel_Is_Ended_By_The_Deadline()
    {
        var abandoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetcher = Fetcher(new SitemapOptions
        {
            RequestGap = Quick.RequestGap,
            PageTimeout = TimeSpan.FromMilliseconds(500),
        });

        _website.Answer("/page", async context =>
        {
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await WaitUntilAbandonedAsync(context);
            abandoned.TrySetResult();
        });

        using var fetched = await fetcher
            .FetchAsync(Bounds, new Uri(Origin + "/page"), FetchKind.Page, default, null, Cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

#pragma warning disable CA1849, S6966 // The blocking read is what this is about.
        var blocked = Task.Run(() => fetched.Body.Read(new byte[1024]), Cancellation.Token);
#pragma warning restore CA1849, S6966

        var failure = await Record.ExceptionAsync(() => blocked.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token));

        failure.Should().NotBeNull();
        failure.Should().NotBeOfType<TimeoutException>();
        fetched.OutcomeOf(failure).Should().Be(FetchOutcome.Unreachable);
    }

    /// <summary>
    /// Whoever asked no longer wanting the answer is not a failure of the website's. It is the reading
    /// stopping, and it goes on up as what it is.
    /// </summary>
    [Fact]
    public async Task Whoever_Stops_Waiting_Gets_A_Cancellation_And_No_Outcome()
    {
        using var stopping = new CancellationTokenSource();

        _website.Answer("/page", WaitUntilAbandonedAsync);

        var fetching = Fetcher().FetchAsync(Bounds, new Uri(Origin + "/page"), FetchKind.Page, default, null, stopping.Token);

        await EventuallyAsync(() => _website.Seen.Count == 1);
        await stopping.CancelAsync();

        await fetching.Invoking(asking => asking.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Whoever_Stops_Waiting_While_The_Body_Is_Being_Read_Gets_No_Outcome_Either()
    {
        using var stopping = new CancellationTokenSource();
        var abandoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _website.Answer("/page", context => DripAsync(context, abandoned));

        using var fetched = await Fetcher()
            .FetchAsync(Bounds, new Uri(Origin + "/page"), FetchKind.Page, default, null, stopping.Token)
            .WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);

        var reading = fetched.ReadPrefixAsync();

        await stopping.CancelAsync();

        var failure = await Record.ExceptionAsync(() => reading.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token));

        failure.Should().BeAssignableTo<OperationCanceledException>();
        fetched.OutcomeOf(failure).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, FetchOutcome.Unreachable)]
    [InlineData(HttpRequestError.ConnectionError, FetchOutcome.Unreachable)]
    [InlineData(HttpRequestError.ResponseEnded, FetchOutcome.Unreachable)]
    [InlineData(HttpRequestError.InvalidResponse, FetchOutcome.Unreachable)]
    [InlineData(HttpRequestError.SecureConnectionError, FetchOutcome.Insecure)]
    [InlineData(HttpRequestError.ConfigurationLimitExceeded, FetchOutcome.TooLarge)]
    public void What_The_Client_Reports_Is_What_Came_Of_Asking(HttpRequestError error, FetchOutcome outcome)
    {
        WebsiteFailure.Classify(new HttpRequestException(error), FetchKind.Page).Should().Be(outcome);
    }

    /// <summary>
    /// These all arrive while a body is being read, and all come to something that can be written down.
    /// Anything not on the list goes on up as the fault here that it is.
    /// </summary>
    [Fact]
    public void A_Failure_Reading_A_Body_Is_What_Came_Of_Reading_It()
    {
        WebsiteFailure.Classify(new XmlException(), FetchKind.Sitemap).Should().Be(FetchOutcome.NotASitemap);
        WebsiteFailure.Classify(new InvalidDataException(), FetchKind.Sitemap).Should().Be(FetchOutcome.NotASitemap);
        WebsiteFailure.Classify(new InvalidDataException(), FetchKind.Page).Should().Be(FetchOutcome.NotAPage);
        WebsiteFailure.Classify(new InvalidDataException(), FetchKind.Robots).Should().Be(FetchOutcome.Unreachable);
        WebsiteFailure.Classify(new ResponseTooLargeException(), FetchKind.Page).Should().Be(FetchOutcome.TooLarge);
        WebsiteFailure.Classify(new AddressRefusedException("no"), FetchKind.Page).Should().Be(FetchOutcome.PrivateAddress);
        WebsiteFailure.Classify(new IOException(), FetchKind.Page).Should().Be(FetchOutcome.Unreachable);
        WebsiteFailure.Classify(new OperationCanceledException(), FetchKind.Page).Should().Be(FetchOutcome.Unreachable);
        WebsiteFailure.Classify(new InvalidOperationException(), FetchKind.Page).Should().BeNull();
    }

    [Fact]
    public void An_Address_Refused_Inside_What_The_Client_Throws_Is_Still_Refused()
    {
        var wrapped = new HttpRequestException("(blog.example.test:80)", new AddressRefusedException("no"));

        WebsiteFailure.Classify(wrapped, FetchKind.Page).Should().Be(FetchOutcome.PrivateAddress);
    }

    /// <summary>
    /// What these are about is not the check, which is proved above, but that the check is what the
    /// engine actually runs: this is the client it registers, from the host it starts, with nothing
    /// standing in for anything.
    /// </summary>
    /// <param name="address">Somewhere on this machine or next to it.</param>
    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://168.63.129.16/")]
    [InlineData("https://localhost/")]
    public async Task The_Client_The_Engine_Registers_Refuses_This_Machine(string address)
    {
        var client = stack.Services.GetRequiredService<IHttpClientFactory>().CreateClient(WebsiteHttp.ClientName);

        var refusal = await Record.ExceptionAsync(() => client.GetAsync(new Uri(address), Cancellation.Token));

        refusal.Should().BeOfType<HttpRequestException>()
            .Which.InnerException.Should().BeOfType<AddressRefusedException>();
    }

    [Fact]
    public async Task The_Client_The_Engine_Registers_Does_Not_Reach_A_Website_On_This_Machine()
    {
        var client = stack.Services.GetRequiredService<IHttpClientFactory>().CreateClient(WebsiteHttp.ClientName);

        _website.Serve("/secret", "secret");

        var refusal = await Record.ExceptionAsync(() =>
            client.GetAsync(new Uri($"http://127.0.0.1:{_website.Port}/secret"), Cancellation.Token));

        refusal.Should().BeOfType<HttpRequestException>()
            .Which.InnerException.Should().BeOfType<AddressRefusedException>();
        _website.Seen.Should().BeEmpty();
    }

    /// <summary>
    /// A redirect it followed for itself would go straight past the check every redirect has to pass.
    /// </summary>
    [Fact]
    public async Task The_Client_Follows_Nothing_By_Itself()
    {
        _website.Redirect("/first", "/second");
        _website.Serve("/second", "second");

        var client = FetcherServices(Quick).GetRequiredService<IHttpClientFactory>().CreateClient(WebsiteHttp.ClientName);

        using var answer = await client.GetAsync(new Uri(Origin + "/first"), Cancellation.Token);

        answer.StatusCode.Should().Be(HttpStatusCode.Found);
        _website.Seen.Should().ContainSingle();
    }

    /// <summary>
    /// The handler the engine itself ends up with. A proxy resolves names for itself, which would put the
    /// check of where a connection may go out of reach, and the rest of these are ways of telling a
    /// website something, or of doing something on its say-so, that nobody here asked for.
    /// </summary>
    [Fact]
    public void The_Handler_The_Engine_Registers_Does_Nothing_On_Its_Own()
    {
        var handler = stack.Services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(WebsiteHttp.ClientName);

        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler;
        }

        var sockets = handler.Should().BeOfType<SocketsHttpHandler>().Subject;

        sockets.UseProxy.Should().BeFalse();
        sockets.AllowAutoRedirect.Should().BeFalse();
        sockets.UseCookies.Should().BeFalse();
        sockets.Credentials.Should().BeNull();
        sockets.PreAuthenticate.Should().BeFalse();
        sockets.AutomaticDecompression.Should().Be(DecompressionMethods.None);
        sockets.ActivityHeadersPropagator.Should().BeNull();
        sockets.ConnectCallback.Should().NotBeNull();
        sockets.SslOptions.RemoteCertificateValidationCallback.Should().BeNull();
        sockets.SslOptions.CertificateChainPolicy.Should().NotBeNull();
        sockets.SslOptions.CertificateChainPolicy.DisableCertificateDownloads.Should().BeTrue();
        sockets.SslOptions.CertificateChainPolicy.RevocationMode.Should().Be(X509RevocationMode.NoCheck);
    }

    /// <summary>
    /// Every address here was written by somebody else, and the factory's own lines would put one in the
    /// engine's log for each request, at the level the engine writes at.
    /// </summary>
    [Fact]
    public async Task A_Request_Leaves_Nothing_In_The_Log()
    {
        var log = new FakeLogCollector();

        _website.Serve("/page", "page", "text/html");

        var services = FetcherServices(Quick, logs: log);
        var fetcher = new WebsiteFetcher(
            services.GetRequiredService<IHttpClientFactory>(),
            new Pacer(TimeProvider.System, Quick.RequestGap),
            Options.Create(Quick),
            TimeProvider.System);

        using var fetched = await fetcher.FetchAsync(Bounds, new Uri(Origin + "/page"), FetchKind.Page, default, null, Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);
        log.GetSnapshot().Should().BeEmpty();
    }

    /// <summary>
    /// How often a website is asked and how long it has are things to get wrong in an environment file.
    /// The first of each pair is too small, and the second more than the thing it is for can hold.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="value">A value it can not have.</param>
    [Theory]
    [InlineData(nameof(SitemapOptions.RequestGap), "00:00:00.999")]
    [InlineData(nameof(SitemapOptions.RequestGap), "60.00:00:00")]
    [InlineData(nameof(SitemapOptions.ConnectTimeout), "00:00:00.500")]
    [InlineData(nameof(SitemapOptions.ConnectTimeout), "00:01:01")]
    [InlineData(nameof(SitemapOptions.SitemapTimeout), "00:00:04")]
    [InlineData(nameof(SitemapOptions.SitemapTimeout), "60.00:00:00")]
    [InlineData(nameof(SitemapOptions.PageTimeout), "00:00:04")]
    [InlineData(nameof(SitemapOptions.PageTimeout), "60.00:00:00")]
    [InlineData(nameof(SitemapOptions.LargestSitemapBytes), "1048575")]
    [InlineData(nameof(SitemapOptions.LargestSitemapBytes), "52428801")]
    [InlineData(nameof(SitemapOptions.PageBytesRead), "65535")]
    [InlineData(nameof(SitemapOptions.PageBytesRead), "4194305")]
    public void An_Installation_Does_Not_Start_On_A_Reading_Setting_It_Could_Not_Keep(string setting, string value)
    {
        var key = $"{SitemapOptions.SectionName}:{setting}";

        HostRefusal.Of(() => stack.WithWebHostBuilder(builder => builder.UseSetting(key, value)))
            .Should().BeOfType<OptionsValidationException>()
            .Which.Message.Should().Contain(key);
    }

    /// <summary>
    /// A redirect waits its turn inside the time an asking has. A gap that leaves no room for that makes every
    /// website that redirects unreadable and reads as those websites not answering, so it is refused, and
    /// by name, because the fault is in how the three go together and not in any one of them.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="value">A value that leaves no room.</param>
    [Theory]
    [InlineData(nameof(SitemapOptions.RequestGap), "00:00:25")]
    [InlineData(nameof(SitemapOptions.RequestGap), "00:00:10")]
    [InlineData(nameof(SitemapOptions.PageTimeout), "00:00:06")]
    [InlineData(nameof(SitemapOptions.SitemapTimeout), "00:00:06")]
    public void An_Installation_Does_Not_Start_On_A_Gap_That_Leaves_No_Time_To_Follow_A_Redirect(string setting, string value)
    {
        var key = $"{SitemapOptions.SectionName}:{setting}";

        var refusal = HostRefusal.Of(() => stack.WithWebHostBuilder(builder => builder.UseSetting(key, value)))
            .Should().BeOfType<OptionsValidationException>().Subject;

        refusal.Message.Should().Contain($"{SitemapOptions.SectionName}:{nameof(SitemapOptions.RequestGap)}");
        refusal.Message.Should().Contain($"{SitemapOptions.SectionName}:{nameof(SitemapOptions.PageTimeout)}");
        refusal.Message.Should().Contain($"{SitemapOptions.SectionName}:{nameof(SitemapOptions.SitemapTimeout)}");
    }

    /// <summary>A gap well above the least, and redirects and a robots.txt all fit inside the time an asking has.</summary>
    [Fact]
    public void An_Installation_Starts_On_A_Gap_That_Leaves_Time_To_Follow_A_Redirect()
    {
        var key = $"{SitemapOptions.SectionName}:{nameof(SitemapOptions.RequestGap)}";

        HostRefusal.Of(() => stack.WithWebHostBuilder(builder => builder.UseSetting(key, "00:00:03"))).Should().BeNull();
    }

    /// <summary>
    /// The framework parses a name it can then not put in the form a name server needs, and throws when
    /// it is asked for that form. Coming from a sitemap, that has to be an answer and not an exception.
    /// </summary>
    [Fact]
    public void A_Name_With_No_Form_A_Name_Server_Could_Be_Asked_For_Is_Not_On_The_Website()
    {
        var address = new Uri("http://a" + (char)0x200D + "b.blog.example.test/page");

        Bounds.Admits(address).Should().BeFalse();
    }

    /// <summary>
    /// The address that a website's sitemap is read from, and what it leads to, have to pass both of the
    /// rules, and a sitemap on a website registered as a whole platform is the case where they differ.
    /// </summary>
    [Fact]
    public void A_Platform_Registered_As_A_Website_Speaks_Only_For_The_Host_Its_Sitemap_Is_On()
    {
        var platform = new Site(Guid.NewGuid(), Guid.NewGuid(), "github.io", "Etc/UTC", DateTimeOffset.MinValue);
        var bounds = new WebsiteBounds(platform, new Uri("https://alice.github.io/sitemap.xml"));

        bounds.Admits(new Uri("https://alice.github.io/post")).Should().BeTrue();
        bounds.Admits(new Uri("https://www.alice.github.io/post")).Should().BeTrue();
        bounds.Admits(new Uri("https://bob.github.io/post")).Should().BeFalse();
        bounds.Admits(new Uri("https://github.io/post")).Should().BeFalse();
    }


    /// <summary>
    /// The beginning of a sitemap followed by nothing but space, so that there is nothing wrong with it
    /// but its length.
    /// </summary>
    private static byte[] SitemapOfLength(int length)
    {
        var body = new byte[length];

        Array.Fill(body, (byte)' ');
        "<urlset>"u8.CopyTo(body);

        return body;
    }

    /// <summary>
    /// Begins an answer that could be a sitemap, and goes on with a byte at a time, until whoever is
    /// asking goes away.
    /// </summary>
    private static async Task DripAsync(HttpContext context, TaskCompletionSource abandoned)
    {
        try
        {
            await context.Response.Body.WriteAsync("<urlset>"u8.ToArray(), context.RequestAborted);

            while (true)
            {
                await context.Response.Body.WriteAsync(" "u8.ToArray(), context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await Task.Delay(TimeSpan.FromMilliseconds(20), context.RequestAborted);
            }
        }
        catch (Exception failure) when (failure is OperationCanceledException or IOException)
        {
            abandoned.TrySetResult();
        }
    }

    private static ValueTask<RobotsVerdict> Says(RobotsVerdict verdict) => ValueTask.FromResult(verdict);

    private static async Task WaitUntilAbandonedAsync(HttpContext context)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // Whoever asked has gone, which is what was being waited for.
        }
    }

    private static async Task EventuallyAsync(Func<bool> happened)
    {
        var began = TimeProvider.System.GetTimestamp();

        while (!happened())
        {
            TimeProvider.System.GetElapsedTime(began).Should().BeLessThan(TimeSpan.FromSeconds(30));
            await Task.Delay(TimeSpan.FromMilliseconds(10), Cancellation.Token);
        }
    }

    private static async Task<string> ReadAllAsync(Fetched fetched)
    {
        using var reader = new StreamReader(fetched.Body, leaveOpen: true);

        return await reader.ReadToEndAsync(Cancellation.Token);
    }

    /// <summary>
    /// Reads a body the way a reading will, and says what came of it.
    /// </summary>
    private static async Task<FetchOutcome> OutcomeOfReadingAsync(Fetched fetched, FetchKind kind)
    {
        if (fetched.Outcome != FetchOutcome.Succeeded)
        {
            return fetched.Outcome;
        }

        try
        {
            if (kind == FetchKind.Sitemap)
            {
                using var document = await SitemapDocument.OpenAsync(fetched.Body);

                if (document.Kind is null)
                {
                    return FetchOutcome.NotASitemap;
                }

                await foreach (var _ in document.EntriesAsync(Cancellation.Token))
                {
                    // Reading on to the end of the file, whatever it lists, is what this is for.
                }
            }
            else
            {
                await fetched.ReadPrefixAsync();
            }

            return FetchOutcome.Succeeded;
        }
        catch (Exception failure) when (fetched.OutcomeOf(failure) is { } outcome)
        {
            return outcome;
        }
    }

    private void ChainOfRedirects(int count)
    {
        for (var hop = 0; hop < count; hop++)
        {
            _website.Redirect($"/hop{hop}", $"/hop{hop + 1}");
        }

        _website.Serve($"/hop{count}", "the end", "text/html");
    }

    private Task<Fetched> FetchAsync(string target, FetchKind kind) =>
        Fetcher().FetchAsync(Bounds, new Uri(Origin + target), kind, default, null, Cancellation.Token);

    private Task<Fetched> FetchAsking(string target, FetchKind kind, RobotsQuestion robots) =>
        Fetcher().FetchAsync(Bounds, new Uri(Origin + target), kind, default, robots, Cancellation.Token);

    private WebsiteFetcher Fetcher(
        SitemapOptions? options = null,
        TimeProvider? clock = null,
        IPublicEndpoints? endpoints = null)
    {
        var settings = options ?? Quick;
        var time = clock ?? TimeProvider.System;
        var services = FetcherServices(settings, endpoints);

        return new WebsiteFetcher(
            services.GetRequiredService<IHttpClientFactory>(),
            new Pacer(time, settings.RequestGap),
            Options.Create(settings),
            time);
    }

    /// <summary>
    /// A certificate for the website, from an issuer it does not send and nobody has heard of, naming where to go
    /// to find out more about it.
    /// </summary>
    private static X509Certificate2 CertificateFromAnIssuerNobodyTrusts(string issuerKeptAt, string revocationListKeptAt)
    {
        var now = TimeProvider.System.GetUtcNow();

        using var issuerKey = RSA.Create(2048);

        var issuerRequest = new CertificateRequest("CN=Nobody", issuerKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        issuerRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

        using var issuer = issuerRequest.CreateSelfSigned(now.AddDays(-2), now.AddDays(2));
        using var key = RSA.Create(2048);

        var request = new CertificateRequest($"CN={Domain}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();

        names.AddDnsName(Domain);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension(null, [issuerKeptAt]));
        request.CertificateExtensions.Add(
            CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([revocationListKeptAt]));

        using var issued = request.Create(issuer, now.AddDays(-1), now.AddDays(1), RandomNumberGenerator.GetBytes(16));
        using var withKey = issued.CopyWithPrivateKey(key);

        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null);
    }

    /// <summary>
    /// Registers the client the way the engine does, and with the one thing it lets be replaced.
    /// </summary>
    private ServiceProvider FetcherServices(
        SitemapOptions settings,
        IPublicEndpoints? endpoints = null,
        FakeLogCollector? logs = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton(endpoints ?? new LoopbackEndpoints(_website.Port));
        services.AddSingleton(Options.Create(settings));
        services.AddLogging(logging => logging.AddProvider(new FakeLoggerProvider(logs ?? new FakeLogCollector())));
        WebsiteHttp.AddClient(services);

        var provider = services.BuildServiceProvider();

        _providers.Add(provider);

        return provider;
    }

    /// <summary>
    /// Puts one name on this machine's website and treats every other as a name leading into a
    /// private network.
    /// </summary>
    /// <param name="port">The website's port.</param>
    /// <param name="safe">The one name it answers to.</param>
    private sealed class OneNameLoopbackOneNamePrivate(int port, string safe) : IPublicEndpoints
    {
        public ValueTask<IReadOnlyList<IPEndPoint>> ResolveAsync(DnsEndPoint requested, CancellationToken cancellationToken) =>
            requested.Host == safe
                ? new LoopbackEndpoints(port).ResolveAsync(requested, cancellationToken)
                : throw new AddressRefusedException("That name leads somewhere other than the public internet.");
    }
}
