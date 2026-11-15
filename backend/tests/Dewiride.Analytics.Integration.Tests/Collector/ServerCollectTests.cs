using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClickHouse.Driver;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
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

    /// <summary>
    /// A batch with nothing in it stores nothing and is still answered in full, which is how a
    /// reporter checks its connection before it has anything to send: whose key it holds, which
    /// hostnames its pages have to be on, and whether anything sent is being kept.
    /// </summary>
    [Fact]
    public async Task An_Empty_Batch_Is_Answered_With_What_The_Key_Is_For()
    {
        var (site, secret) = await MeasuredSiteAsync();
        using var client = stack.CreateClient();

        var answer = await PostAsync(client, secret, Batch("wordpress-plugin"));

        answer.StatusCode.Should().Be(HttpStatusCode.OK);

        var counted = await CountedAsync(answer);

        counted.Accepted.Should().Be(0);
        counted.Rejected.Should().Be(0);
        counted.Site.Should().Be(site.Id);
        counted.Hosts.Should().Equal(site.Domain);
        counted.Measuring.Should().BeTrue();
        (await RowsAsync(site.Id)).Should().BeEmpty();
    }

    /// <summary>
    /// A website that names the hostnames it is published at is answered with those, because they
    /// are what its pages are checked against.
    /// </summary>
    [Fact]
    public async Task The_Hostnames_A_Website_Names_Are_The_Ones_It_Is_Answered_With()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: Domain(),
            configure: created => created.ReplaceAllowedOrigins(["docs.example.org", "blog.example.org"]));
        var secret = await ControlPlaneSeed.AddServerKeyAsync(stack, site.Id);
        using var client = stack.CreateClient();

        var counted = await CountedAsync(await PostAsync(client, secret, Batch("wordpress-plugin")));

        counted.Hosts.Should().BeEquivalentTo("docs.example.org", "blog.example.org");
    }

    /// <summary>
    /// A reporter is told the address its batch arrived from, which is the address its own server's
    /// requests to its own website arrive from too, so it can leave them out.
    /// </summary>
    [Fact]
    public async Task A_Reporter_Is_Told_The_Address_Its_Batch_Arrived_From()
    {
        var (_, secret) = await MeasuredSiteAsync();

        var answered = await stack.Server.SendAsync(
            context =>
            {
                context.Request.Method = HttpMethods.Post;
                context.Request.Path = Endpoint;
                context.Request.ContentType = "application/json";
                context.Request.Headers.Authorization = $"Bearer {secret}";
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"events":[]}"""));
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
            },
            Cancellation.Token);

        var counted = await JsonSerializer.DeserializeAsync<ServerCollectResponse>(
            answered.Response.Body,
            JsonSerializerOptions.Web,
            Cancellation.Token);

        counted!.From.Should().Be("203.0.113.9");
    }

    /// <summary>
    /// Batches with a live key are allowed per key, so two websites reporting from one machine — the
    /// ordinary arrangement on shared hosting — each have the whole allowance from their first batch,
    /// and the one that uses its own up is told how long to wait.
    /// </summary>
    [Fact]
    public async Task Websites_Reporting_From_One_Address_Each_Have_An_Allowance_Of_Their_Own()
    {
        var (first, firstSecret) = await MeasuredSiteAsync();
        var (second, secondSecret) = await MeasuredSiteAsync();

        using var throttled = stack.WithWebHostBuilder(builder =>
            builder.UseSetting("Dewiride:Collector:ServerBatchesPerMinutePerKey", "2"));

        using var client = throttled.CreateClient();
        var answers = new List<HttpStatusCode>();

        foreach (var (site, secret) in new[] { (first, firstSecret), (second, secondSecret) })
        {
            for (var batch = 0; batch < 2; batch++)
            {
                using var answer = await PostAsync(client, secret, Batch("wordpress-plugin", Observation(site.Domain, "/")));
                answers.Add(answer.StatusCode);
            }
        }

        using var refused = await PostAsync(client, firstSecret, Batch("wordpress-plugin", Observation(first.Domain, "/")));

        answers.Should().AllBeEquivalentTo(HttpStatusCode.OK);
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter!.Delta.Should().BePositive();
    }

    /// <summary>
    /// A key costs nothing to invent, so a batch whose key is not live is counted against the address
    /// it came from before anything is looked up, and an address past its allowance is turned away for
    /// the rest of the minute. A reporter at the same address whose key is live is counted on its own
    /// and carries on.
    /// </summary>
    [Fact]
    public async Task Keys_Nobody_Issued_Are_Counted_Against_The_Address_They_Came_From()
    {
        var (site, secret) = await MeasuredSiteAsync();

        using var throttled = stack.WithWebHostBuilder(builder =>
            builder.UseSetting("Dewiride:Collector:ServerBatchesPerMinutePerAddress", "2"));

        using var client = throttled.CreateClient();
        using var known = await PostAsync(client, secret, Batch("wordpress-plugin", Observation(site.Domain, "/")));
        var guesses = new List<HttpStatusCode>();

        for (var guess = 0; guess < 3; guess++)
        {
            using var answer = await PostAsync(client, Invented(), Batch("wordpress-plugin"));
            guesses.Add(answer.StatusCode);
        }

        using var stillKnown = await PostAsync(client, secret, Batch("wordpress-plugin", Observation(site.Domain, "/pricing")));

        known.StatusCode.Should().Be(HttpStatusCode.OK);
        guesses.Should().Equal(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests);
        stillKnown.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private IClickHouseClient Client => stack.Services.GetRequiredService<IClickHouseClient>();

    /// <summary>A value shaped exactly like a server key, which nobody issued.</summary>
    private static string Invented() =>
        $"dwk_{Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32))}";

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
