using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves the screen that answers who is on a site now, and what it refuses to say.
/// </summary>
/// <remarks>
/// The one answer in this product that takes no window, renews itself while somebody watches, and
/// deliberately says nothing about most of the people in it. All three are properties a change
/// could quietly remove, so all three are asserted here rather than described anywhere.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class LiveReadTests(AnalyticsStackFixture stack)
{
    private const string Password = Passwords.Acceptable;

    /// <summary>What the crawler that gathers training material actually sends.</summary>
    private const string GptBot = "Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko); compatible; "
        + "GPTBot/1.2; +https://openai.com/gptbot";

    [Fact]
    public async Task A_Member_Sees_Who_Is_On_Their_Site()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        await WriteAsync(
            Arrived(site.Id, "reader", "/"),
            Arrived(site.Id, "reader", "/pricing"));

        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var live = await ReadAsync(browser, site.Id);

            live.VisitorsSeen.Should().Be(1);
            live.Visitors.Should().ContainSingle();
            live.Visitors[0].CurrentPath.Should().Be("/pricing");
            live.Visitors[0].PageCount.Should().Be(2);
            live.Pages.Select(page => page.Path).Should().BeEquivalentTo("/", "/pricing");
        }
    }

    /// <summary>
    /// The whole point of the screen: a crawler whose owner vouches for the address it arrived from
    /// is named the moment it arrives, and named as verified, because nothing it does next can
    /// unmake that.
    /// </summary>
    [Fact]
    public async Task A_Crawler_Whose_Owner_Vouches_For_It_Is_Named_While_It_Is_Still_Crawling()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        await WriteAsync(
            Vouched(Arrived(site.Id, "crawler", "/robots.txt"), "OpenAI", GptBot),
            Vouched(Arrived(site.Id, "crawler", "/sitemap.xml"), "OpenAI", GptBot));

        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var live = await ReadAsync(browser, site.Id);

            live.Visitors.Should().ContainSingle();
            live.Visitors[0].Category.Should().Be("known-ai-crawler");
            live.Visitors[0].Strength.Should().Be("verified");
            live.Visitors[0].Operator.Should().Be("OpenAI");
            live.Visitors[0].Supporting.Should().NotBeEmpty();
        }
    }

    /// <summary>
    /// Somebody reading is counted and listed and called nothing at all. On a site reported by both
    /// its own server and the browser, the honest reading of a person one page in is machinery — so
    /// a screen that showed the engine's answer here would put a real reader in red and take it back
    /// a second later.
    /// </summary>
    [Fact]
    public async Task Somebody_Who_Is_Still_Reading_Is_Counted_And_Not_Named()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        await WriteAsync(Arrived(site.Id, "reader", "/posts/hello"));

        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var live = await ReadAsync(browser, site.Id);

            live.VisitorsSeen.Should().Be(1);
            live.Visitors[0].Category.Should().BeNull();
            live.Visitors[0].Strength.Should().BeNull();
            live.Visitors[0].Ruleset.Should().BeNull();
            live.Visitors[0].Supporting.Should().BeEmpty();
            live.Visitors[0].Contradicting.Should().BeEmpty();
        }
    }

    /// <summary>
    /// An answer about this moment is out of date the instant it is given, and nothing in the
    /// address it was asked at says who asked. Some of these answers carry a renewed sign-in with
    /// them, so a shared cache holding one would be handing over a session rather than a stale
    /// figure.
    /// </summary>
    [Fact]
    public async Task A_Reading_Of_Now_Is_Never_Kept_By_Anything_In_Between()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/live");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
            response.Headers.Vary.Should().Contain("Cookie");
        }
    }

    /// <summary>
    /// Every relative time on the screen is measured against the moment the engine took the reading,
    /// so a machine whose clock is an hour out does not report visitors arriving in the future.
    /// </summary>
    [Fact]
    public async Task A_Reading_Says_When_It_Was_Taken_And_What_It_Covers()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var live = await ReadAsync(browser, site.Id);

            live.At.Should().BeAfter(live.From);
            (live.At - live.From).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(30));
            live.Minutes.Should().NotBeEmpty();
            live.Minutes.Should().BeInAscendingOrder(minute => minute.Start);
        }
    }

    /// <summary>
    /// The same answer as a site that was never created, so the address cannot be used to test which
    /// identifiers on an install are real.
    /// </summary>
    [Fact]
    public async Task A_Site_Somebody_Has_No_Role_On_Is_Answered_As_Though_It_Did_Not_Exist()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(mine.Id, SiteRole.Owner);

        using (browser)
        {
            var otherSite = await browser.GetAsync($"/api/sites/{theirs.Id}/live");
            var noSite = await browser.GetAsync($"/api/sites/{Guid.NewGuid()}/live");

            otherSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
            noSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Nobody_Signed_In_Is_Refused()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.GetAsync($"/api/sites/{site.Id}/live");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The whole of the second half of this screen: open a row and see where that visitor has been.
    /// It reads the same stretch of minutes the row was drawn from, so the pages it shows and the
    /// number the row printed are one reading of one window.
    /// </summary>
    [Fact]
    public async Task A_Member_Opens_A_Visitor_And_Sees_Where_They_Have_Been()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        await WriteAsync(
            Arrived(site.Id, "2f8a1c0b4d6e7f905a1b2c3d4e5f6071", "/"),
            Arrived(site.Id, "2f8a1c0b4d6e7f905a1b2c3d4e5f6071", "/pricing"));

        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var live = await ReadAsync(browser, site.Id);
            var trail = await TrailAsync(browser, site.Id, live.Visitors[0].Visitor);

            trail.Visitor.Should().Be(live.Visitors[0].Visitor);
            trail.Steps.Select(step => step.Path).Should().Equal("/", "/pricing");
            trail.Steps.Should().HaveCount(live.Visitors[0].PageCount);
        }
    }

    /// <summary>
    /// A visitor who has left since their row was drawn is an empty trail rather than a refusal,
    /// because a visitor going is what happens to every visitor and a panel open on them has to be
    /// able to say so.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Who_Is_No_Longer_Here_Is_An_Empty_Trail_Rather_Than_A_Refusal()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var trail = await TrailAsync(browser, site.Id, "2f8a1c0b4d6e7f905a1b2c3d4e5f6071");

            trail.Steps.Should().BeEmpty();
        }
    }

    /// <summary>
    /// The key arrives from an address somebody typed. Anything that could not name a visitor is
    /// turned away where it arrives, in the same words whatever it was, and nothing of what was
    /// written comes back.
    /// </summary>
    [Theory]
    [InlineData("2F8A1C0B4D6E7F905A1B2C3D4E5F6071")]
    [InlineData("nobody")]
    [InlineData("2f8a1c0b4d6e7f905a1b2c3d4e5f6071aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Anything_That_Could_Not_Name_A_Visitor_Is_Refused(string visitorKey)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/live/{visitorKey}/trail");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var body = await response.Content.ReadAsStringAsync(Cancellation.Token);

            body.Should().NotContain(visitorKey);
        }
    }

    /// <summary>
    /// A trail is renewed on every beat for as long as a row is open, on the same terms as the
    /// reading it was opened from, so nothing in between may keep one.
    /// </summary>
    [Fact]
    public async Task A_Trail_Is_Never_Kept_By_Anything_In_Between()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync(
                $"/api/sites/{site.Id}/live/2f8a1c0b4d6e7f905a1b2c3d4e5f6071/trail");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
            response.Headers.Vary.Should().Contain("Cookie");
        }
    }

    [Fact]
    public async Task A_Trail_On_A_Site_Somebody_Has_No_Role_On_Is_Answered_As_Though_It_Did_Not_Exist()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(mine.Id, SiteRole.Owner);

        using (browser)
        {
            var response = await browser.GetAsync(
                $"/api/sites/{theirs.Id}/live/2f8a1c0b4d6e7f905a1b2c3d4e5f6071/trail");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Nobody_Signed_In_Is_Refused_A_Trail()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.GetAsync(
            $"/api/sites/{site.Id}/live/2f8a1c0b4d6e7f905a1b2c3d4e5f6071/trail");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<LiveTrailResponse> TrailAsync(Browser browser, Guid siteId, string visitor)
    {
        var response = await browser.GetAsync($"/api/sites/{siteId}/live/{visitor}/trail");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var trail = await response.Content.ReadFromJsonAsync<LiveTrailResponse>(Cancellation.Token);

        trail.Should().NotBeNull();

        return trail;
    }

    private static async Task<LiveResponse> ReadAsync(Browser browser, Guid siteId)
    {
        var response = await browser.GetAsync($"/api/sites/{siteId}/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var live = await response.Content.ReadFromJsonAsync<LiveResponse>(Cancellation.Token);

        live.Should().NotBeNull();

        return live;
    }

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    /// <summary>
    /// A page asked for a moment ago, so it falls inside whatever stretch the engine reads when the
    /// question is put to it.
    /// </summary>
    private RawEvent Arrived(Guid siteId, string visitorKey, string path)
    {
        var at = stack.Services.GetRequiredService<TimeProvider>().GetUtcNow().AddSeconds(-30);

        return new RawEvent
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = EventKind.PageView,
            Surface = IngestSurface.NextJsMiddleware,
            ServerTimestamp = at,
            VisitorKey = visitorKey,
            Host = "example.com",
            Path = path,
        };
    }

    private static RawEvent Vouched(RawEvent observed, string operatorName, string userAgent) =>
        observed with { ConfirmedOperator = operatorName, UserAgent = userAgent };

    private async Task<Browser> SignedInAsync(Guid siteId, SiteRole role)
    {
        var address = $"reader-{Guid.NewGuid():n}@example.com";
        var (_, user) = await ControlPlaneSeed.AddAccountAsync(stack, address, Password);
        await ControlPlaneSeed.GrantAsync(stack, siteId, user.Id, role);

        var browser = await Browser.OpenAsync(stack);
        var response = await browser.PostAsync(
            "/api/session",
            new SignInRequest { EmailAddress = address, Password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await browser.DescribeAsync();

        return browser;
    }

    private static string Domain() => $"live-{Guid.NewGuid():n}.example";
}
