using System.Text;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers asking a website's robots.txt about addresses in the course of one pass: what comes of
/// asking for the file, and how often it is asked for.
/// </summary>
public sealed class RobotsPassTests : IAsyncLifetime
{
    private const string Domain = "blog.example.test";
    private const string Origin = "http://" + Domain;

    private static readonly SitemapOptions Quick = new()
    {
        RequestGap = TimeSpan.FromMilliseconds(1),
        PageTimeout = TimeSpan.FromSeconds(30),
    };

    private readonly Site _site = new(Guid.NewGuid(), Guid.NewGuid(), Domain, "Etc/UTC", DateTimeOffset.MinValue);
    private LoopbackWebsite _website = null!;
    private ServiceProvider _services = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _website = await LoopbackWebsite.StartAsync();

        var services = new ServiceCollection();

        services.AddSingleton<IPublicEndpoints>(new LoopbackEndpoints(_website.Port));
        services.AddSingleton(Options.Create(Quick));
        WebsiteHttp.AddClient(services);

        _services = services.BuildServiceProvider();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _website.DisposeAsync();
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task What_The_File_Says_Is_Followed()
    {
        _website.Serve("/robots.txt", "User-Agent: DewirideAnalytics\nDisallow: /private\n", "text/plain");

        var pass = Pass();

        (await AskAsync(pass, "/public")).Should().Be(new RobotsVerdict(true, null));
        (await AskAsync(pass, "/private")).Should().Be(new RobotsVerdict(false, null));
    }

    /// <summary>
    /// A whole pass over a website is one question about the file, and asking per page would double what
    /// the website is put through.
    /// </summary>
    [Fact]
    public async Task The_File_Is_Asked_For_Once_However_Many_Addresses_Are_Asked_About()
    {
        _website.Serve("/robots.txt", "User-Agent: *\nDisallow: /private\n", "text/plain");

        var pass = Pass();

        await AskAsync(pass, "/one");
        await AskAsync(pass, "/two");
        await AskAsync(pass, "/private");

        _website.Seen.Should().ContainSingle().Which.Target.Should().Be("/robots.txt");
    }

    /// <summary>
    /// The website's two names can answer differently, so each has its own file. The rules are the
    /// rules of the name they came from.
    /// </summary>
    [Fact]
    public async Task Each_Host_Has_A_File_Of_Its_Own()
    {
        _website.Answer("/robots.txt", context => context.Response.WriteAsync(
            context.Request.Host.Host.StartsWith("www.", StringComparison.Ordinal)
                ? "User-Agent: *\nDisallow: /\n"
                : "User-Agent: *\nDisallow:\n",
            context.RequestAborted));

        var pass = Pass();

        (await pass.AskAsync(new Uri(Origin + "/page"), Cancellation.Token)).Permitted.Should().BeTrue();
        (await pass.AskAsync(new Uri("http://www.blog.example.test/page"), Cancellation.Token)).Permitted.Should().BeFalse();

        _website.Seen.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_New_Pass_Reads_The_File_Again()
    {
        _website.Serve("/robots.txt", "User-Agent: *\nDisallow:\n", "text/plain");

        await AskAsync(Pass(), "/one");
        await AskAsync(Pass(), "/one");

        _website.Seen.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_File_Waits_Its_Turn_With_Everything_Else()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var pacer = new Pacer(clock, TimeSpan.FromSeconds(1));

        await pacer.WaitTurnAsync(Domain, Cancellation.Token);

        _website.Serve("/robots.txt", "User-Agent: *\nDisallow:\n", "text/plain");

        var asking = Pass(pacer, clock).AskAsync(new Uri(Origin + "/page"), Cancellation.Token).AsTask();

        await Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation.Token);
        _website.Seen.Should().BeEmpty();

        clock.Advance(TimeSpan.FromSeconds(1));

        (await asking.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token)).Permitted.Should().BeTrue();
        _website.Seen.Should().ContainSingle();
    }

    /// <summary>
    /// Section 2.3.1.3 of the RFC: a file the server says is unavailable leaves everything open, 403 no
    /// less than 404.
    /// </summary>
    /// <param name="status">What the server answers.</param>
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status410Gone)]
    public async Task A_File_The_Server_Says_Is_Unavailable_Permits_Everything(int status)
    {
        _website.Status("/robots.txt", status);

        (await AskAsync(Pass(), "/page")).Should().Be(new RobotsVerdict(true, null));
    }

    /// <summary>
    /// A 429 says nothing about whether there is a file, and the real one can say anything. The RFC lets it
    /// pass as unavailable and says only that it may, and Google's reading of it leaves 429 out of the 4xx
    /// that mean no file, so nothing is taken as permitted until the file can be read.
    /// </summary>
    [Fact]
    public async Task A_Server_Saying_It_Is_Being_Asked_Too_Often_Permits_Nothing_This_Pass()
    {
        _website.Status("/robots.txt", StatusCodes.Status429TooManyRequests);

        (await AskAsync(Pass(), "/page")).Should().Be(new RobotsVerdict(false, FetchOutcome.AskedToSlowDown));
    }

    /// <summary>
    /// An answer asking for a wait is asking for one, whatever else it is, and whoever is asking gets to hear
    /// it. So it permits nothing, whichever status it came with.
    /// </summary>
    /// <param name="status">What the server answers.</param>
    /// <param name="trouble">What stopped the file being read.</param>
    [Theory]
    [InlineData(StatusCodes.Status429TooManyRequests, FetchOutcome.AskedToSlowDown)]
    [InlineData(StatusCodes.Status503ServiceUnavailable, FetchOutcome.ServerError)]
    [InlineData(StatusCodes.Status403Forbidden, FetchOutcome.TurnedAway)]
    [InlineData(StatusCodes.Status404NotFound, FetchOutcome.TurnedAway)]
    public async Task A_Wait_Asked_For_With_The_File_Comes_With_The_Verdict(int status, FetchOutcome trouble)
    {
        _website.Answer("/robots.txt", context =>
        {
            context.Response.StatusCode = status;
            context.Response.Headers.RetryAfter = "7200";

            return Task.CompletedTask;
        });

        var pass = Pass();

        (await AskAsync(pass, "/page")).Should().Be(new RobotsVerdict(false, trouble, TimeSpan.FromHours(2)));
        (await AskAsync(pass, "/another")).Should().Be(new RobotsVerdict(false, trouble, TimeSpan.FromHours(2)));
        _website.Seen.Should().ContainSingle();
    }

    /// <summary>
    /// Section 2.3.1.1 of the RFC: a file that was downloaded is followed. What a website says about its
    /// robots.txt is obeyed however the answer was labelled.
    /// </summary>
    /// <param name="status">What the server answers.</param>
    [Theory]
    [InlineData(StatusCodes.Status200OK)]
    [InlineData(StatusCodes.Status202Accepted)]
    [InlineData(StatusCodes.Status203NonAuthoritative)]
    [InlineData(StatusCodes.Status206PartialContent)]
    public async Task A_File_Is_Followed_Whichever_Success_It_Arrives_As(int status)
    {
        _website.Answer("/robots.txt", context =>
        {
            context.Response.StatusCode = status;

            return context.Response.WriteAsync("User-Agent: *\nDisallow: /private\n", context.RequestAborted);
        });

        (await AskAsync(Pass(), "/private")).Should().Be(new RobotsVerdict(false, null));
    }

    /// <summary>A success with nothing in it is a file with nothing to say.</summary>
    [Fact]
    public async Task A_Success_With_Nothing_In_It_Permits_Everything()
    {
        _website.Status("/robots.txt", StatusCodes.Status204NoContent);

        (await AskAsync(Pass(), "/page")).Should().Be(new RobotsVerdict(true, null));
    }

    /// <summary>
    /// Section 2.3.1.4 of the RFC: a file that can not be told apart from one saying "go away" has to
    /// be treated as saying it. The website has not actually asked for anything, so what got in the way is
    /// what is reported.
    /// </summary>
    /// <param name="status">What the server answers.</param>
    [Theory]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status502BadGateway)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    public async Task A_Server_In_Trouble_Permits_Nothing_This_Pass(int status)
    {
        _website.Status("/robots.txt", status);

        (await AskAsync(Pass(), "/page")).Should().Be(new RobotsVerdict(false, FetchOutcome.ServerError));
    }

    [Fact]
    public async Task A_Server_That_Can_Not_Be_Reached_Permits_Nothing_This_Pass()
    {
        var pass = Pass();

        await _website.DisposeAsync();

        (await AskAsync(pass, "/page")).Should().Be(new RobotsVerdict(false, FetchOutcome.Unreachable));

        _website = await LoopbackWebsite.StartAsync();
    }

    [Fact]
    public async Task Once_It_Could_Not_Be_Read_It_Is_Not_Asked_For_Again_This_Pass()
    {
        _website.Status("/robots.txt", StatusCodes.Status503ServiceUnavailable);

        var pass = Pass();

        await AskAsync(pass, "/one");
        await AskAsync(pass, "/two");

        _website.Seen.Should().ContainSingle();
    }

    /// <summary>
    /// A redirect stays on the website like every other request, and where it leaves, the file is one
    /// that was asked for and could not be read. That has its own word, because what is at fault is the file
    /// and not whatever was being asked about.
    /// </summary>
    [Fact]
    public async Task A_Redirect_Off_The_Website_Permits_Nothing_This_Pass()
    {
        _website.Redirect("/robots.txt", "http://evil.test/robots.txt");

        (await AskAsync(Pass(), "/page")).Should().Be(new RobotsVerdict(false, FetchOutcome.RobotsUnreadable));
        _website.Seen.Should().ContainSingle();
    }

    [Fact]
    public async Task A_Redirect_That_Never_Lands_Permits_Nothing_This_Pass()
    {
        _website.Redirect("/robots.txt", "/robots.txt");

        (await AskAsync(Pass(), "/page")).Should().Be(new RobotsVerdict(false, FetchOutcome.RobotsUnreadable));
    }

    /// <summary>
    /// Section 2.3.1.2 of the RFC: a file's own redirects are followed. Nothing asks the file about itself, or
    /// a website saying it may not be read would not be able to say so from where it moved the file to.
    /// </summary>
    [Fact]
    public async Task A_Redirect_Of_The_File_Is_Not_Itself_Asked_About()
    {
        _website.Redirect("/robots.txt", "/rules/robots.txt");
        _website.Serve("/rules/robots.txt", "User-Agent: *\nDisallow: /rules/\nDisallow: /private\n", "text/plain");

        (await AskAsync(Pass(), "/private")).Should().Be(new RobotsVerdict(false, null));
        _website.Seen.Select(seen => seen.Target).Should().Equal("/robots.txt", "/rules/robots.txt");
    }

    /// <summary>
    /// Section 2.3.1.2 of the RFC: what is found at the end of a redirect is the file of the host that
    /// was asked. A website moving everything to its other name moves this too.
    /// </summary>
    [Fact]
    public async Task A_Redirect_To_The_Websites_Other_Name_Reaches_Its_File()
    {
        _website.Answer("/robots.txt", context =>
        {
            if (!context.Request.Host.Host.StartsWith("www.", StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                context.Response.Headers.Location = "http://www.blog.example.test/robots.txt";

                return Task.CompletedTask;
            }

            return context.Response.WriteAsync("User-Agent: *\nDisallow: /private\n", context.RequestAborted);
        });

        var pass = Pass();

        (await AskAsync(pass, "/private")).Permitted.Should().BeFalse();
        (await AskAsync(pass, "/public")).Permitted.Should().BeTrue();
        _website.Seen.Should().HaveCount(2);
    }

    /// <summary>
    /// Nobody can tell what a file that can not be unpacked said, which makes it undefined, and that is
    /// a matter of nothing being read rather than everything.
    /// </summary>
    [Fact]
    public async Task A_File_That_Can_Not_Be_Unpacked_Permits_Nothing_This_Pass()
    {
        var corrupt = new byte[1024];

        Array.Fill(corrupt, (byte)0xFF);

        _website.Serve("/robots.txt", corrupt, "text/plain", "gzip");

        (await AskAsync(Pass(), "/page")).Should().Be(new RobotsVerdict(false, FetchOutcome.Unreachable));
    }

    /// <summary>
    /// The address the reader wants is off the website, so it is not read and there is nothing to ask
    /// anybody about it.
    /// </summary>
    [Fact]
    public async Task An_Address_Off_The_Website_Is_Not_Asked_About()
    {
        var verdict = await Pass().AskAsync(new Uri("http://evil.test/page"), Cancellation.Token);

        verdict.Should().Be(new RobotsVerdict(false, FetchOutcome.LeftTheWebsite));
        _website.Seen.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Compressed_File_Is_Unpacked()
    {
        _website.Serve(
            "/robots.txt",
            Gzipped.Bytes(Encoding.UTF8.GetBytes("User-Agent: *\nDisallow: /private\n")),
            "text/plain",
            "gzip");

        (await AskAsync(Pass(), "/private")).Permitted.Should().BeFalse();
    }

    /// <summary>
    /// Section 2.5 of the RFC says what must at least be read, and this reads a little more. A rule
    /// past that is not read, and a rule cut off by it is not read either, because the start of an allow
    /// permits more than the whole of it would.
    /// </summary>
    [Fact]
    public async Task The_File_Is_Read_As_Far_As_The_Protocol_Requires_And_No_Further()
    {
        const int limit = 512 * 1024;
        var file = new StringBuilder("User-Agent: *\nDisallow: /early\n");

        while (file.Length < 500 * 1024)
        {
            file.Append("# nothing to see here\n");
        }

        file.Append("Disallow: /within-what-must-be-read\n");

        while (file.Length < limit - 16)
        {
            file.Append('\n');
        }

        // Ends past the limit, partway through the rule.
        file.Append("Disallow: /cut-off-by-the-limit-so-it-says-nothing\nDisallow: /past-it\n");

        _website.Serve("/robots.txt", file.ToString(), "text/plain");

        var pass = Pass();

        (await AskAsync(pass, "/early")).Permitted.Should().BeFalse();
        (await AskAsync(pass, "/within-what-must-be-read")).Permitted.Should().BeFalse();
        (await AskAsync(pass, "/cut-off-by-the-limit-so-it-says-nothing")).Permitted.Should().BeTrue();
        (await AskAsync(pass, "/past-it")).Permitted.Should().BeTrue();
    }

    /// <summary>
    /// The same limit on what comes of it, for a small file that unpacks to a great many bytes.
    /// </summary>
    [Fact]
    public async Task A_File_That_Unpacks_Past_The_Limit_Is_Read_As_Far_As_The_Limit()
    {
        var file = "User-Agent: *\nDisallow: /early\n" + new string('\n', 20 * 1024 * 1024) + "Disallow: /late\n";

        _website.Serve("/robots.txt", Gzipped.Bytes(Encoding.UTF8.GetBytes(file)), "text/plain", "gzip");

        var pass = Pass();

        (await AskAsync(pass, "/early")).Permitted.Should().BeFalse();
        (await AskAsync(pass, "/late")).Permitted.Should().BeTrue();
    }

    /// <summary>
    /// A compressed file running past the limit on what arrives has been read as far as it is going
    /// to be, and what came of it by then still stands.
    /// </summary>
    [Fact]
    public async Task A_File_That_Arrives_Past_The_Limit_Is_Read_As_Far_As_It_Got()
    {
        byte[] body =
        [
            .. Gzipped.Bytes(Encoding.UTF8.GetBytes("User-Agent: *\nDisallow: /early\n")),
            .. Gzipped.EmptySections(1024 * 1024),
        ];

        _website.Serve("/robots.txt", body, "text/plain", "gzip");

        var pass = Pass();

        (await AskAsync(pass, "/early")).Permitted.Should().BeFalse();
        (await AskAsync(pass, "/anything-else")).Permitted.Should().BeTrue();
    }


    private static async Task<RobotsVerdict> AskAsync(RobotsPass pass, string path) =>
        await pass.AskAsync(new Uri(Origin + path), Cancellation.Token);

    private RobotsPass Pass() => Pass(new Pacer(TimeProvider.System, Quick.RequestGap), TimeProvider.System);

    private RobotsPass Pass(Pacer pacer, TimeProvider clock)
    {
        var fetcher = new WebsiteFetcher(
            _services.GetRequiredService<IHttpClientFactory>(),
            pacer,
            Options.Create(Quick),
            clock);

        return new RobotsPass(fetcher, new WebsiteBounds(_site, new Uri(Origin + "/sitemap.xml")));
    }
}
