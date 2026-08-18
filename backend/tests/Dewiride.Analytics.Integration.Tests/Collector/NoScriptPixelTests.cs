using System.Net;
using ClickHouse.Driver;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Dewiride.Analytics.Integration.Tests.Collector;

/// <summary>
/// Proves what the no-script image records, and what it refuses to reveal.
/// </summary>
/// <remarks>
/// The image is on somebody else's page, so it has to be returned whatever the request turned out
/// to mean. A broken image would tell every visitor to that page something about an installation
/// that is not theirs — and would make a real site identifier distinguishable from an invented
/// one, which is precisely what the collector is built not to do.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class NoScriptPixelTests(AnalyticsStackFixture stack)
{
    private const string Pixel = "/collect/pixel.gif";

    /// <summary>How many bytes a whole one-pixel transparent image takes.</summary>
    private const int WholeImage = 43;

    [Fact]
    public async Task A_Request_From_The_Measured_Site_Is_Recorded_As_A_Page_View()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        var response = await GetAsync(client, site.Id, referrer: $"https://{site.Domain}/posts/hello");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await RowsAsync(site.Id);

        stored.Should().ContainSingle();
        stored[0]["path"].Should().Be("/posts/hello");
        stored[0]["kind"].Should().Be("PageView");
        stored[0]["surface"].Should().Be("NoScriptPixel");
    }

    /// <summary>
    /// The whole point of the surface: what it cannot see stays distinguishable from what it saw
    /// and found to be nothing. A nought here would assert that nobody touched a page nobody was
    /// ever watching.
    /// </summary>
    [Fact]
    public async Task What_The_Image_Cannot_Observe_Is_Recorded_As_Unobserved()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        await GetAsync(client, site.Id, referrer: $"https://{site.Domain}/posts/hello");

        var stored = await RowsAsync(site.Id);

        stored[0]["had_pointer_interaction"].Should().Be("Unobserved");
        stored[0]["had_keyboard_interaction"].Should().Be("Unobserved");
        stored[0]["declared_web_driver"].Should().Be("Unobserved");
        stored[0]["engaged_ms"].Should().BeNull();
        stored[0]["scroll_depth_percent"].Should().BeNull();
    }

    /// <summary>
    /// A surface that rendered the page itself knows the address and says so. A snippet somebody
    /// pasted cannot, and asks the browser to name the page instead.
    /// </summary>
    [Fact]
    public async Task An_Address_Given_Outright_Is_Preferred_To_The_One_The_Browser_Names()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        await GetAsync(
            client,
            site.Id,
            referrer: $"https://{site.Domain}/",
            url: $"https://{site.Domain}/guide/getting-started");

        var stored = await RowsAsync(site.Id);

        stored[0]["path"].Should().Be("/guide/getting-started");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("not-a-guid", "https://example.com/")]
    [InlineData(null, "javascript:alert(1)")]
    public async Task A_Request_That_Records_Nothing_Still_Returns_The_Image(string? site, string? url)
    {
        using var client = stack.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Address(site, url));

        var response = await client.SendAsync(request, Cancellation.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Cancellation.Token)).Should().HaveCount(WholeImage);
    }

    [Fact]
    public async Task A_Request_For_A_Site_That_Does_Not_Exist_Is_Answered_The_Same_Way()
    {
        var unknown = Guid.NewGuid();
        using var client = stack.CreateClient();

        var response = await GetAsync(client, unknown, referrer: "https://example.com/posts/hello");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RowsAsync(unknown)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Request_Made_From_Another_Website_Records_Nothing()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        var response = await GetAsync(client, site.Id, referrer: "https://attacker.test/posts/hello");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RowsAsync(site.Id)).Should().BeEmpty();
    }

    /// <summary>
    /// An image the browser holds on to is a page view that happens once and is then counted no
    /// more, which would quietly turn every returning reader into a single visit.
    /// </summary>
    [Fact]
    public async Task The_Image_Is_Never_Held_By_Anything_In_Between()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        var response = await GetAsync(client, site.Id, referrer: $"https://{site.Domain}/");

        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/gif");
    }

    /// <summary>
    /// A browser that is told a response is a file offers to save it, which on somebody else's
    /// page is a download prompt appearing for no reason the reader can see.
    /// </summary>
    [Fact]
    public async Task The_Image_Is_Never_Offered_As_A_Download()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var client = stack.CreateClient();

        var response = await GetAsync(client, site.Id, referrer: $"https://{site.Domain}/");

        response.Content.Headers.ContentDisposition.Should().BeNull();
    }

    private IClickHouseClient Client => stack.Services.GetRequiredService<IClickHouseClient>();

    private static string Domain() => $"site-{Guid.NewGuid():n}.example";

    private static string Address(string? site, string? url)
    {
        var query = new List<string>();

        if (site is not null)
        {
            query.Add($"site={Uri.EscapeDataString(site)}");
        }

        if (url is not null)
        {
            query.Add($"u={Uri.EscapeDataString(url)}");
        }

        return query.Count == 0 ? Pixel : $"{Pixel}?{string.Join('&', query)}";
    }

    private static async Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        Guid siteId,
        string referrer,
        string? url = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Address(siteId.ToString(), url));

        request.Headers.Add(HeaderNames.Referer, referrer);

        return await client.SendAsync(request, Cancellation.Token);
    }

    private async Task<List<Dictionary<string, object?>>> RowsAsync(Guid siteId) =>
        await TelemetryStore.RowsAsync(
            Client,
            "SELECT path, kind, surface, had_pointer_interaction, had_keyboard_interaction, "
            + "declared_web_driver, engaged_ms, scroll_depth_percent "
            + "FROM events WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));
}
