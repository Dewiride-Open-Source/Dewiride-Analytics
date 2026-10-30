using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClickHouse.Driver;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Dewiride.Analytics.Integration.Tests.Collector;

/// <summary>
/// Proves that an installation configured not to keep visits from a website keeps none: not the
/// page view that names it, not what that page goes on to send, and not by any way a visit can be
/// reported — while answering the sender exactly as it answers every report it does not keep.
/// </summary>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class IgnoredSenderTests(AnalyticsStackFixture stack)
{
    private const string Collect = "/collect";

    private const string ServerCollect = "/collect/server";

    private const string Setting = "Dewiride:Collector:IgnoredSenders:0";

    /// <summary>The address the visit from the ignored website comes from.</summary>
    private const string Visitor = "203.0.113.7";

    /// <summary>The address somebody else comes from, at the same time.</summary>
    private const string Somebody = "198.51.100.99";

    private const string Browser =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36";

    /// <summary>
    /// The page view, the reading it reports fifteen seconds later and the page being left are all
    /// answered with nothing, as a report for a website that does not exist is — and none is kept.
    /// </summary>
    [Fact]
    public async Task A_Visit_From_An_Ignored_Website_Is_Answered_As_Any_Report_And_None_Of_It_Is_Kept()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var host = Ignoring("aocr.org");

        int[] answers =
        [
            await SendAsync(host, site, Report(site, "pageview", referrer: "https://www.aocr.org/"), Visitor),
            await SendAsync(host, site, Report(site, "engagement", engagedMs: 15_000), Visitor),
            await SendAsync(host, site, Report(site, "exit", engagedMs: 16_000), Visitor),
        ];

        answers.Should().AllBeEquivalentTo(StatusCodes.Status204NoContent);
        (await StoredCountAsync(site.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Somebody_Else_Arriving_Meanwhile_Is_Kept()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var host = Ignoring("aocr.org");

        await SendAsync(host, site, Report(site, "pageview", referrer: "https://www.aocr.org/"), Visitor);
        await SendAsync(host, site, Report(site, "pageview", referrer: "https://www.google.com/"), Somebody);

        (await StoredSendersAsync(site.Id)).Should().Equal("www.google.com");
    }

    /// <summary>
    /// A website's own server reports the visitor's referrer as the browser does, and is told what
    /// became of its batch, so the visit it reported from an ignored website is counted as not kept.
    /// </summary>
    [Fact]
    public async Task A_Server_Reporting_A_Visit_From_An_Ignored_Website_Is_Told_It_Was_Not_Kept()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var secret = await ControlPlaneSeed.AddServerKeyAsync(stack, site.Id);
        using var host = Ignoring("aocr.org");
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, ServerCollect)
        {
            Content = JsonContent.Create(new ServerCollectRequest
            {
                Surface = "wordpress-plugin",
                Events =
                [
                    Observation(site, "https://www.aocr.org/", Visitor),
                    Observation(site, "https://www.google.com/", Somebody),
                ],
            }),
        };
        request.Headers.Add(HeaderNames.Authorization, $"Bearer {secret}");

        using var answer = await client.SendAsync(request, Cancellation.Token);
        var counted = await answer.Content.ReadFromJsonAsync<ServerCollectResponse>(Cancellation.Token);

        answer.StatusCode.Should().Be(HttpStatusCode.OK);
        counted.Should().Be(new ServerCollectResponse(Accepted: 1, Rejected: 1));
        (await StoredSendersAsync(site.Id)).Should().Equal("www.google.com");
    }

    /// <summary>
    /// A list nobody could match against is a configuration mistake, and an installation that
    /// started with one would quietly keep the visits it was told to turn away.
    /// </summary>
    [Fact]
    public void An_Installation_Does_Not_Start_With_Anything_But_Host_Names_On_The_List()
    {
        using var host = Ignoring("https://aocr.org/");

        var starting = () => host.CreateClient();

        starting.Should().Throw<OptionsValidationException>().WithMessage("*IgnoredSenders*");
    }

    private WebApplicationFactory<Program> Ignoring(string sender) =>
        stack.WithWebHostBuilder(builder => builder.UseSetting(Setting, sender));

    private static async Task<int> SendAsync(
        WebApplicationFactory<Program> host,
        Site site,
        Dictionary<string, object?> report,
        string address)
    {
        var body = JsonSerializer.Serialize(report);

        var answered = await host.Server.SendAsync(
            context =>
            {
                context.Request.Method = HttpMethods.Post;
                context.Request.Path = Collect;
                context.Request.ContentType = "application/json";
                context.Request.Headers.Origin = $"https://{site.Domain}";
                context.Request.Headers.UserAgent = Browser;
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
                context.Connection.RemoteIpAddress = IPAddress.Parse(address);
            },
            Cancellation.Token);

        return answered.Response.StatusCode;
    }

    private static Dictionary<string, object?> Report(
        Site site,
        string kind,
        string? referrer = null,
        int? engagedMs = null) =>
        new(StringComparer.Ordinal)
        {
            ["siteId"] = site.Id,
            ["kind"] = kind,
            ["url"] = $"https://{site.Domain}/posts/hello",
            ["referrer"] = referrer,
            ["engagedMs"] = engagedMs,
        };

    private static ServerObservation Observation(Site site, string referrer, string address) =>
        new()
        {
            Kind = "pageview",
            Url = $"https://{site.Domain}/posts/hello",
            Referrer = referrer,
            IpAddress = address,
            UserAgent = Browser,
            StatusCode = 200,
        };

    private IClickHouseClient Client => stack.Services.GetRequiredService<IClickHouseClient>();

    private static string Domain() => $"ignoring-{Guid.NewGuid():n}.example";

    private async Task<ulong> StoredCountAsync(Guid siteId) =>
        await TelemetryStore.ScalarAsync<ulong>(
            Client,
            "SELECT count() FROM events WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

    private async Task<List<string?>> StoredSendersAsync(Guid siteId) =>
        (await TelemetryStore.RowsAsync(
            Client,
            "SELECT referrer_domain FROM events WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId)))
        .Select(row => row["referrer_domain"] as string)
        .ToList();
}
