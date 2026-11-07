using System.Net;
using System.Net.Sockets;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers how a connection to a website is opened, where a name has several addresses and they do not all answer.
/// </summary>
public sealed class WebsiteHttpTests
{
    private static readonly IPEndPoint First = new(IPAddress.Parse("192.0.2.1"), 80);
    private static readonly IPEndPoint Second = new(IPAddress.Parse("192.0.2.2"), 80);
    private static readonly TimeSpan Share = TimeSpan.FromMilliseconds(100);

    /// <summary>Far longer than any of this takes, so that what would wait for ever fails instead.</summary>
    private static readonly TimeSpan LongEnough = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A dead server left in round-robin, or an address of the newer family behind a firewall, drops what is sent
    /// to it. It has its share of the time, and then the next address is tried, as a browser would.
    /// </summary>
    [Fact]
    public async Task An_Address_That_Stays_Silent_Has_Its_Share_Of_The_Time_And_The_Next_Is_Tried()
    {
        var tried = new List<IPEndPoint>();
        using var answer = new MemoryStream();

        var stream = await WebsiteHttp.FirstToAnswerAsync(
            [First, Second],
            Share,
            async (endpoint, token) =>
            {
                tried.Add(endpoint);

                if (endpoint.Equals(First))
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }

                return answer;
            },
            Cancellation.Token).AsTask().WaitAsync(LongEnough, Cancellation.Token);

        stream.Should().BeSameAs(answer);
        tried.Should().Equal(First, Second);
    }

    [Fact]
    public async Task An_Address_That_Refuses_Is_Passed_Over_At_Once()
    {
        var tried = new List<IPEndPoint>();
        using var answer = new MemoryStream();

        var stream = await WebsiteHttp.FirstToAnswerAsync(
            [First, Second],
            Timeout.InfiniteTimeSpan,
            (endpoint, _) =>
            {
                tried.Add(endpoint);

                return endpoint.Equals(First)
                    ? throw new SocketException((int)SocketError.ConnectionRefused)
                    : Task.FromResult<Stream>(answer);
            },
            Cancellation.Token);

        stream.Should().BeSameAs(answer);
        tried.Should().Equal(First, Second);
    }

    /// <summary>The first that answers is the one, and the rest are not disturbed.</summary>
    [Fact]
    public async Task The_First_To_Answer_Is_The_Last_Asked()
    {
        var tried = new List<IPEndPoint>();
        using var answer = new MemoryStream();

        await WebsiteHttp.FirstToAnswerAsync(
            [First, Second],
            Share,
            (endpoint, _) =>
            {
                tried.Add(endpoint);

                return Task.FromResult<Stream>(answer);
            },
            Cancellation.Token);

        tried.Should().Equal(First);
    }

    /// <summary>
    /// Nobody answering is a website not answering, which comes to what any failure to connect comes to, and
    /// not to somebody having stopped waiting.
    /// </summary>
    [Fact]
    public async Task Every_Address_Staying_Silent_Is_A_Failure_To_Connect()
    {
        var connecting = async () => await WebsiteHttp.FirstToAnswerAsync(
            [First, Second],
            Share,
            async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);

                return Stream.Null;
            },
            Cancellation.Token).AsTask().WaitAsync(LongEnough, Cancellation.Token);

        (await connecting.Should().ThrowAsync<SocketException>()).Which.SocketErrorCode.Should().Be(SocketError.TimedOut);
    }

    /// <summary>The time for all of it runs out, or the engine stops, and no other address gets tried.</summary>
    [Fact]
    public async Task Whoever_Stops_Waiting_Stops_All_Of_It()
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Cancellation.Token);
        var tried = new List<IPEndPoint>();

        var connecting = async () => await WebsiteHttp.FirstToAnswerAsync(
            [First, Second],
            Timeout.InfiniteTimeSpan,
            async (endpoint, token) =>
            {
                tried.Add(endpoint);

                await stopping.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);

                return Stream.Null;
            },
            stopping.Token).AsTask().WaitAsync(LongEnough, Cancellation.Token);

        await connecting.Should().ThrowAsync<OperationCanceledException>();
        tried.Should().Equal(First);
    }

    [Fact]
    public async Task No_Address_At_All_Is_A_Name_That_Leads_Nowhere()
    {
        var connecting = async () => await WebsiteHttp.FirstToAnswerAsync(
            [],
            Share,
            (_, _) => Task.FromResult(Stream.Null),
            Cancellation.Token);

        (await connecting.Should().ThrowAsync<SocketException>()).Which.SocketErrorCode.Should().Be(SocketError.HostNotFound);
    }

    /// <summary>
    /// The same through the client the engine registers. An address set aside for documentation goes nowhere,
    /// which on most networks means that nothing ever answers, and on the rest that it is refused at once.
    /// Either way the website at the next address is read, inside the time for connecting that the two share.
    /// </summary>
    [Fact]
    public async Task A_Website_Is_Reached_At_Its_Next_Address_When_The_First_Goes_Nowhere()
    {
        await using var website = await LoopbackWebsite.StartAsync();

        website.Serve("/page", "page", "text/html");

        var settings = new SitemapOptions
        {
            RequestGap = TimeSpan.FromMilliseconds(1),
            ConnectTimeout = TimeSpan.FromSeconds(2),
            PageTimeout = TimeSpan.FromSeconds(30),
        };

        var services = new ServiceCollection();

        services.AddSingleton<IPublicEndpoints>(new FirstGoesNowhere(website.Port));
        services.AddSingleton(Options.Create(settings));
        WebsiteHttp.AddClient(services);

        await using var provider = services.BuildServiceProvider();

        var fetcher = new WebsiteFetcher(
            provider.GetRequiredService<IHttpClientFactory>(),
            new Pacer(TimeProvider.System, settings.RequestGap),
            Options.Create(settings),
            TimeProvider.System);

        var address = new Uri("http://blog.example.test/page");
        var site = new Site(Guid.NewGuid(), Guid.NewGuid(), "blog.example.test", "Etc/UTC", DateTimeOffset.MinValue);

        using var fetched = await fetcher.FetchAsync(
            new WebsiteBounds(site, address),
            address,
            FetchKind.Page,
            default,
            null,
            Cancellation.Token);

        fetched.Outcome.Should().Be(FetchOutcome.Succeeded);
        website.Seen.Should().ContainSingle();
    }

    /// <summary>
    /// Answers a name with an address nothing is at, and then with this machine's website.
    /// </summary>
    /// <param name="port">The port the website on this machine listens on.</param>
    private sealed class FirstGoesNowhere(int port) : IPublicEndpoints
    {
        /// <inheritdoc />
        public ValueTask<IReadOnlyList<IPEndPoint>> ResolveAsync(DnsEndPoint requested, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IPEndPoint>>(
                [new IPEndPoint(First.Address, port), new IPEndPoint(IPAddress.Loopback, port)]);
    }
}
