using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClickHouse.Driver;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Dewiride.Analytics.Integration.Tests.Collector;

/// <summary>
/// Proves what the public collection endpoint accepts, refuses and reveals.
/// </summary>
/// <remarks>
/// Almost everything here is about what the answer does <em>not</em> say. A site identifier is
/// printed in the source of every page it measures, so anyone can send reports; the endpoint must
/// therefore answer a report for a site that does not exist exactly as it answers a stored one,
/// or it becomes a way to find out which identifiers are real.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class CollectEndpointTests(AnalyticsStackFixture stack)
{
    private const string Collect = "/collect";

    /// <summary>Address the connection itself came from.</summary>
    private const string ConnectionAddress = "203.0.113.7";

    /// <summary>Address a forwarded header claims the visitor is at.</summary>
    private const string ClaimedAddress = "198.51.100.99";

    [Fact]
    public async Task A_Report_From_The_Measured_Site_Is_Accepted_And_Stored()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        var response = await PostAsync(client, Report(site.Id, $"https://{site.Domain}/posts/hello"), site.Domain);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredCountAsync(site.Id)).Should().Be(1);
    }

    /// <summary>
    /// The same empty answer as a stored report, and nothing written.
    /// </summary>
    [Fact]
    public async Task A_Report_For_A_Site_That_Does_Not_Exist_Is_Answered_The_Same_Way()
    {
        var unknown = Guid.NewGuid();
        using var client = stack.CreateClient();

        var response = await PostAsync(client, Report(unknown, "https://example.com/posts/hello"), "example.com");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredCountAsync(unknown)).Should().Be(0);
    }

    [Fact]
    public async Task A_Report_Sent_From_Another_Website_Is_Answered_The_Same_Way()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        var response = await PostAsync(
            client,
            Report(site.Id, $"https://{site.Domain}/posts/hello"),
            origin: "attacker.test");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredCountAsync(site.Id)).Should().Be(0);
    }

    [Fact]
    public async Task A_Report_From_A_Subdomain_Is_Accepted()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        var response = await PostAsync(
            client,
            Report(site.Id, $"https://docs.{site.Domain}/guide"),
            origin: $"docs.{site.Domain}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredCountAsync(site.Id)).Should().Be(1);
    }

    /// <summary>
    /// The settings a site was saved with have to reach the collector. This is the whole path:
    /// the database, the cache in front of it, and the decision the collector makes from it.
    /// </summary>
    [Fact]
    public async Task A_Site_That_Keeps_Query_Strings_Has_Them_Stored()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: Domain(),
            configure: created => created.SetQueryStringRetention(true));

        using var client = stack.CreateClient();

        await PostAsync(
            client,
            Report(site.Id, $"https://{site.Domain}/posts/hello?utm_source=news"),
            site.Domain);

        var stored = await TelemetryStore.RowsAsync(
            Client,
            "SELECT query_string FROM events WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", site.Id));

        stored.Should().ContainSingle().Which["query_string"].Should().Be("?utm_source=news");
    }

    [Fact]
    public async Task A_Site_That_Does_Not_Keep_Query_Strings_Stores_None()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        await PostAsync(
            client,
            Report(site.Id, $"https://{site.Domain}/posts/hello?utm_source=news"),
            site.Domain);

        var stored = await TelemetryStore.RowsAsync(
            Client,
            "SELECT query_string FROM events WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", site.Id));

        stored.Should().ContainSingle().Which["query_string"].Should().Be(string.Empty);
    }

    /// <summary>
    /// An explicit list replaces the default rather than adding to it, so the site's own domain is
    /// admitted only if it appears on the list.
    /// </summary>
    [Fact]
    public async Task A_Site_With_A_Declared_Origin_List_Admits_Only_What_It_Named()
    {
        var domain = Domain();
        string[] origins = [$"docs.{domain}"];
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: domain,
            configure: created => created.ReplaceAllowedOrigins(origins));

        using var client = stack.CreateClient();

        var admitted = await PostAsync(client, Report(site.Id, $"https://docs.{domain}/guide"), $"docs.{domain}");
        var refused = await PostAsync(client, Report(site.Id, $"https://{domain}/posts/hello"), domain);

        admitted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        refused.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredCountAsync(site.Id)).Should().Be(1);
    }

    /// <summary>
    /// The page describes its own controls in an open vocabulary, partly of its own invention. The
    /// words it uses are resolved into the closed set the store holds, and anything unrecognised
    /// becomes a press on something the product cannot name rather than a refusal.
    /// </summary>
    [Theory]
    [InlineData("button", "Button")]
    [InlineData("a", "Link")]
    [InlineData("input", "Field")]
    [InlineData("menuitem", "Button")]
    [InlineData("blancmange", "Unknown")]
    [InlineData(null, "Unknown")]
    public async Task What_A_Page_Calls_Its_Control_Is_Resolved_Into_A_Closed_Set(
        string? declared,
        string expected)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        var report = Report(site.Id, $"https://{site.Domain}/posts/hello");
        report["kind"] = "action";
        report["element"] = declared;
        report["label"] = "Subscribe";

        var response = await PostAsync(client, report, site.Domain);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredControlAsync(site.Id)).Should().Be(expected);
    }

    /// <summary>
    /// A site that has turned recording off has its presses dropped where they arrive. Storing
    /// them and leaving them out of every later question would collect exactly what it asked not
    /// to have collected.
    /// </summary>
    [Fact]
    public async Task A_Press_Reported_For_A_Site_That_Records_None_Is_Never_Stored()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: Domain(),
            configure: created => created.SetClickCapture(false));

        using var client = stack.CreateClient();

        var report = Report(site.Id, $"https://{site.Domain}/posts/hello");
        report["kind"] = "action";
        report["element"] = "button";
        report["label"] = "Subscribe";

        var response = await PostAsync(client, report, site.Domain);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredCountAsync(site.Id)).Should().Be(0);
    }

    /// <summary>
    /// A malformed body is the one thing answered plainly, because it is a mistake by whoever is
    /// writing an integration and telling them nothing helps nobody.
    /// </summary>
    [Fact]
    public async Task A_Body_That_Is_Not_A_Report_Is_Answered_Plainly()
    {
        using var client = stack.CreateClient();
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(Collect, content, Cancellation.Token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("""{"siteId":"0197c0de-0000-7000-8000-000000000001","kind":"teleport","url":"https://example.com/"}""")]
    [InlineData("""{"siteId":"0197c0de-0000-7000-8000-000000000001","kind":"pageview","url":"/relative"}""")]
    [InlineData("""{"siteId":"0197c0de-0000-7000-8000-000000000001","kind":"pageview","url":"javascript:alert(1)"}""")]
    [InlineData("""{"siteId":"00000000-0000-0000-0000-000000000000","kind":"pageview","url":"https://example.com/"}""")]
    [InlineData("""{"kind":"pageview","url":"https://example.com/"}""")]
    public async Task A_Report_That_Cannot_Be_Read_Is_Answered_Plainly(string body)
    {
        using var client = stack.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(Collect, content, Cancellation.Token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_Reading_That_Could_Not_Have_Happened_Is_Answered_Plainly()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        var report = Report(site.Id, $"https://{site.Domain}/posts/hello");
        report["scrollDepthPercent"] = 150;

        var response = await PostAsync(client, report, site.Domain);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// An oversized body answers plainly rather than as a fault of ours. Reported as a server
    /// error it would write a stack trace for every attempt, which is a log flood anybody could
    /// set off from a shell.
    /// </summary>
    /// <remarks>
    /// This covers a body whose size is declared up front, which is what every real client sends.
    /// A body that arrives in chunks with no declared length is stopped mid-read by the web server
    /// itself; the in-memory host these tests run against has no such limit to enforce, so that
    /// path is proven against the real server by the end-to-end suite rather than here.
    /// </remarks>
    [Fact]
    public async Task A_Report_Far_Larger_Than_A_Report_Is_Refused_As_Too_Large()
    {
        using var client = stack.CreateClient();
        using var content = new StringContent(new string('a', 64 * 1024), Encoding.UTF8, "application/json");

        var response = await client.PostAsync(Collect, content, Cancellation.Token);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await response.Content.ReadAsStringAsync(Cancellation.Token)).Should().NotContain("Exception");
    }

    /// <summary>
    /// The tracker runs on the customer's own domain, so the browser has to be told any origin may
    /// post. Which site a report may be filed under is decided from the site's own declared list
    /// after the body is read, not from the browser's willingness to enforce anything.
    /// </summary>
    [Fact]
    public async Task The_Browser_Is_Told_Any_Page_May_Send_A_Report()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        var response = await PostAsync(client, Report(site.Id, $"https://{site.Domain}/x"), site.Domain);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("*");
    }

    /// <summary>
    /// One address can only send so many reports a minute. The limit is counted per address
    /// because the address is the only thing known before the body is read, and it is configurable
    /// because a busy publisher and a personal blog need very different numbers.
    /// </summary>
    [Fact]
    public async Task An_Address_Sending_Past_Its_Allowance_Is_Turned_Away()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        using var throttled = stack.WithWebHostBuilder(builder =>
            builder.UseSetting("Dewiride:Collector:RequestsPerMinutePerAddress", "3"));

        using var client = throttled.CreateClient();
        var report = Report(site.Id, $"https://{site.Domain}/posts/hello");

        var accepted = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await PostAsync(client, report, site.Domain);
            accepted.Add(response.StatusCode);
            response.Dispose();
        }

        var refused = await PostAsync(client, report, site.Domain);

        accepted.Should().AllBeEquivalentTo(HttpStatusCode.NoContent);
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// An address read from a header anyone can write would flow straight into the visitor key and
    /// the network attribution, so nothing upstream is believed until it has been named.
    /// </summary>
    [Fact]
    public async Task A_Forwarded_Address_Is_Ignored_While_No_Hop_Has_Been_Declared_Trustworthy()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await stack.Server.SendAsync(
            context => Forwarded(context, site, ConnectionAddress, ClaimedAddress),
            Cancellation.Token);

        (await StoredAddressAsync(site.Id)).Should().Be(ConnectionAddress);
    }

    [Fact]
    public async Task A_Forwarded_Address_Is_Believed_Once_The_Hop_In_Front_Has_Been_Declared()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        using var behindProxy = stack.WithWebHostBuilder(builder =>
            builder.UseSetting("Dewiride:Network:TrustedProxies:0", ConnectionAddress));

        await behindProxy.Server.SendAsync(
            context => Forwarded(context, site, ConnectionAddress, ClaimedAddress),
            Cancellation.Token);

        (await StoredAddressAsync(site.Id)).Should().Be(ClaimedAddress);
    }

    private static void Forwarded(HttpContext context, Site site, string connection, string claimed)
    {
        var body = JsonSerializer.Serialize(Report(site.Id, $"https://{site.Domain}/posts/hello"));

        context.Request.Method = HttpMethods.Post;
        context.Request.Path = Collect;
        context.Request.ContentType = "application/json";
        context.Request.Headers.Origin = $"https://{site.Domain}";
        context.Request.Headers["X-Forwarded-For"] = claimed;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Connection.RemoteIpAddress = IPAddress.Parse(connection);
    }

    private async Task<string?> StoredAddressAsync(Guid siteId)
    {
        var rows = await TelemetryStore.RowsAsync(
            Client,
            "SELECT ip_address FROM events WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

        return rows.Should().ContainSingle().Subject["ip_address"] as string;
    }

    private IClickHouseClient Client => stack.Services.GetRequiredService<IClickHouseClient>();

    private static string Domain() => $"site-{Guid.NewGuid():n}.example";

    private static Dictionary<string, object?> Report(Guid siteId, string url) =>
        new(StringComparer.Ordinal)
        {
            ["siteId"] = siteId,
            ["kind"] = "pageview",
            ["url"] = url,
            ["language"] = "en-GB",
            ["viewportWidth"] = 1440,
            ["viewportHeight"] = 900,
        };

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        Dictionary<string, object?> report,
        string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Collect)
        {
            Content = JsonContent.Create(report),
        };

        request.Headers.Add(HeaderNames.Origin, $"https://{origin}");

        return await client.SendAsync(request, Cancellation.Token);
    }

    private async Task<string?> StoredControlAsync(Guid siteId) =>
        await TelemetryStore.ScalarAsync<string>(
            Client,
            "SELECT toString(action_control) FROM events WHERE site_id = {site_id:UUID} LIMIT 1",
            TelemetryStore.Bind("site_id", siteId));

    private async Task<ulong> StoredCountAsync(Guid siteId) =>
        await TelemetryStore.ScalarAsync<ulong>(
            Client,
            "SELECT count() FROM events WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));
}
