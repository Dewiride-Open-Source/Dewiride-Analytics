using System.Net;
using System.Net.Http.Json;
using ClickHouse.Driver;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Dewiride.Analytics.Integration.Tests.Collector;

/// <summary>
/// Proves that a site's own server can report the traffic no browser script will ever see, and
/// that nobody without its key can.
/// </summary>
/// <remarks>
/// This is the surface the product's whole proposition rests on. A crawler asks for the page,
/// reads the markup and stops; it never runs the tracker, so it is invisible to every other
/// surface. Here it arrives with its declared identity, the address it came from, and the status
/// the site gave it.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class ServerCollectTests(AnalyticsStackFixture stack)
{
    private const string Endpoint = "/collect/server";

    /// <summary>A crawler that fetches HTML and runs nothing, which no browser surface can observe.</summary>
    private const string Crawler = "Mozilla/5.0 (compatible; ExampleBot/1.0; +https://example.test/bot)";

    [Fact]
    public async Task A_Crawler_No_Script_Would_Ever_See_Is_Recorded_With_Its_Own_Address()
    {
        var (site, secret) = await MeasuredSiteAsync();
        using var client = stack.CreateClient();

        var answer = await PostAsync(
            client,
            secret,
            Batch("cloudflare-worker", Observation(site.Domain, "/posts/hello", address: "203.0.113.7")));

        answer.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountedAsync(answer)).Accepted.Should().Be(1);

        var stored = await RowsAsync(site.Id);

        stored.Should().ContainSingle();
        stored[0]["path"].Should().Be("/posts/hello");
        stored[0]["kind"].Should().Be("PageView");
        stored[0]["surface"].Should().Be("CloudflareWorker");
        stored[0]["ip_address"].Should().Be("203.0.113.7");
        stored[0]["user_agent"].Should().Be(Crawler);
    }

    /// <summary>
    /// The one thing only this kind of surface can see. Security scanners are recognised almost
    /// entirely by streams of requests to paths that were never there, and a browser tracker on a
    /// page that does not exist is a tracker that was never loaded.
    /// </summary>
    [Fact]
    public async Task What_The_Site_Answered_Is_Recorded_Because_Only_A_Server_Can_See_It()
    {
        var (site, secret) = await MeasuredSiteAsync();
        using var client = stack.CreateClient();

        await PostAsync(
            client,
            secret,
            Batch(
                "cloudflare-worker",
                Observation(site.Domain, "/.env", status: 404, contentType: "text/html", bytes: 512)));

        var stored = await RowsAsync(site.Id);

        stored[0]["path"].Should().Be("/.env");
        stored[0]["status_code"].Should().Be((short)404);
        stored[0]["content_type"].Should().Be("text/html");
        stored[0]["response_bytes"].Should().Be(512L);
    }

    /// <summary>
    /// A server sees the request and nothing about the reader. Recording nought engaged
    /// milliseconds would assert that a page nobody was watching held nobody's attention.
    /// </summary>
    [Fact]
    public async Task What_A_Server_Cannot_Observe_Is_Recorded_As_Unobserved()
    {
        var (site, secret) = await MeasuredSiteAsync();
        using var client = stack.CreateClient();

        await PostAsync(client, secret, Batch("cloudflare-worker", Observation(site.Domain, "/posts/hello")));

        var stored = await RowsAsync(site.Id);

        stored[0]["had_pointer_interaction"].Should().Be("Unobserved");
        stored[0]["had_keyboard_interaction"].Should().Be("Unobserved");
        stored[0]["declared_web_driver"].Should().Be("Unobserved");
        stored[0]["engaged_ms"].Should().BeNull();
        stored[0]["scroll_depth_percent"].Should().BeNull();
    }

    [Fact]
    public async Task A_Batch_Is_Stored_Whole_And_Counted_Back()
    {
        var (site, secret) = await MeasuredSiteAsync();
        using var client = stack.CreateClient();

        var answer = await PostAsync(
            client,
            secret,
            Batch(
                "wordpress-plugin",
                Observation(site.Domain, "/a"),
                Observation(site.Domain, "/b"),
                Observation(site.Domain, "/c")));

        var counted = await CountedAsync(answer);

        counted.Accepted.Should().Be(3);
        counted.Rejected.Should().Be(0);
        (await RowsAsync(site.Id)).Should().HaveCount(3);
    }

    /// <summary>
    /// A reporter written against a later release must keep being able to report to an earlier
    /// engine. It is recorded as a server-side reporter of unstated identity, which is a
    /// different claim from no provenance at all.
    /// </summary>
    [Fact]
    public async Task A_Reporter_This_Release_Does_Not_Know_Is_Still_Recorded_As_A_Server()
    {
        var (site, secret) = await MeasuredSiteAsync();
        using var client = stack.CreateClient();

        await PostAsync(client, secret, Batch("something-invented-later", Observation(site.Domain, "/posts/hello")));

        (await RowsAsync(site.Id))[0]["surface"].Should().Be("ServerSide");
    }

    [Fact]
    public async Task An_Observation_That_Cannot_Be_Used_Is_Counted_Without_Costing_The_Others()
    {
        var (site, secret) = await MeasuredSiteAsync();
        using var client = stack.CreateClient();

        var answer = await PostAsync(
            client,
            secret,
            Batch(
                "cloudflare-worker",
                Observation(site.Domain, "/kept"),
                Observation(site.Domain, "/bad-address", address: "not-an-address"),
                Observation(site.Domain, "/also-kept")));

        var counted = await CountedAsync(answer);

        counted.Accepted.Should().Be(2);
        counted.Rejected.Should().Be(1);
    }

    /// <summary>
    /// The key says which site is being reported for. It must not also become a way of writing
    /// into a hostname that site does not cover.
    /// </summary>
    [Fact]
    public async Task A_Page_On_A_Hostname_The_Site_Does_Not_Cover_Is_Refused()
    {
        var (site, secret) = await MeasuredSiteAsync();
        using var client = stack.CreateClient();

        var answer = await PostAsync(
            client,
            secret,
            Batch("cloudflare-worker", Observation("somewhere-else.test", "/posts/hello")));

        (await CountedAsync(answer)).Rejected.Should().Be(1);
        (await RowsAsync(site.Id)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dwk_this-is-not-a-key-that-was-ever-issued-xx")]
    [InlineData("nonsense")]
    public async Task Nothing_Is_Recorded_Without_A_Key_That_Was_Issued(string? secret)
    {
        var (site, _) = await MeasuredSiteAsync();
        using var client = stack.CreateClient();

        var answer = await PostAsync(client, secret, Batch("cloudflare-worker", Observation(site.Domain, "/posts/hello")));

        answer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        answer.Headers.WwwAuthenticate.Should().ContainSingle(header => header.Scheme == "Bearer");
        (await RowsAsync(site.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Withdrawn_Key_Stops_Working()
    {
        var (site, secret) = await MeasuredSiteAsync();
        await ControlPlaneSeed.RevokeServerKeysAsync(stack, site.Id);

        using var client = stack.CreateClient();

        var answer = await PostAsync(client, secret, Batch("cloudflare-worker", Observation(site.Domain, "/posts/hello")));

        answer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RowsAsync(site.Id)).Should().BeEmpty();
    }

    /// <summary>
    /// A key for one site must never be able to write into another, whatever the body says. The
    /// body cannot say anything about it: the site is not in there at all.
    /// </summary>
    [Fact]
    public async Task A_Key_For_One_Site_Writes_Nothing_Into_Another()
    {
        var (mine, secret) = await MeasuredSiteAsync();
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        using var client = stack.CreateClient();

        await PostAsync(client, secret, Batch("cloudflare-worker", Observation(theirs.Domain, "/posts/hello")));

        (await RowsAsync(theirs.Id)).Should().BeEmpty();
        (await RowsAsync(mine.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Batch_Beyond_What_Is_Accepted_Is_Refused_Outright()
    {
        var (site, secret) = await MeasuredSiteAsync();
        using var client = stack.CreateClient();

        var tooMany = Enumerable.Range(0, 101).Select(index => Observation(site.Domain, $"/page-{index}")).ToArray();

        var answer = await PostAsync(client, secret, Batch("cloudflare-worker", tooMany));

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RowsAsync(site.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Body_That_Is_Not_A_Batch_Is_Said_To_Be_Unreadable()
    {
        var (_, secret) = await MeasuredSiteAsync();
        using var client = stack.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"),
        };

        request.Headers.Add(HeaderNames.Authorization, $"Bearer {secret}");

        var answer = await client.SendAsync(request, Cancellation.Token);

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_Answer_Is_Never_Held_By_Anything_In_Between()
    {
        var (site, secret) = await MeasuredSiteAsync();
        using var client = stack.CreateClient();

        var answer = await PostAsync(client, secret, Batch("cloudflare-worker", Observation(site.Domain, "/posts/hello")));

        answer.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    private IClickHouseClient Client => stack.Services.GetRequiredService<IClickHouseClient>();

    private static string Domain() => $"server-{Guid.NewGuid():n}.example";

    private async Task<(Domain.Sites.Site Site, string Secret)> MeasuredSiteAsync()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var secret = await ControlPlaneSeed.AddServerKeyAsync(stack, site.Id);

        return (site, secret);
    }

    private static ServerCollectRequest Batch(string surface, params ServerObservation[] events) =>
        new() { Surface = surface, Events = events };

    private static ServerObservation Observation(
        string host,
        string path,
        string? address = "198.51.100.42",
        short? status = 200,
        string? contentType = null,
        long? bytes = null) =>
        new()
        {
            Kind = "pageview",
            Url = $"https://{host}{path}",
            IpAddress = address,
            UserAgent = Crawler,
            StatusCode = status,
            ContentType = contentType,
            ResponseBytes = bytes,
        };

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string? secret,
        ServerCollectRequest batch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(batch),
        };

        if (secret is not null)
        {
            request.Headers.Add(HeaderNames.Authorization, $"Bearer {secret}");
        }

        return await client.SendAsync(request, Cancellation.Token);
    }

    private static async Task<ServerCollectResponse> CountedAsync(HttpResponseMessage answer)
    {
        var counted = await answer.Content.ReadFromJsonAsync<ServerCollectResponse>(Cancellation.Token);

        counted.Should().NotBeNull();

        return counted;
    }

    private async Task<List<Dictionary<string, object?>>> RowsAsync(Guid siteId) =>
        await TelemetryStore.RowsAsync(
            Client,
            "SELECT path, kind, surface, ip_address, user_agent, status_code, content_type, "
            + "response_bytes, had_pointer_interaction, had_keyboard_interaction, "
            + "declared_web_driver, engaged_ms, scroll_depth_percent "
            + "FROM events WHERE site_id = {site_id:UUID} ORDER BY path",
            TelemetryStore.Bind("site_id", siteId));
}
