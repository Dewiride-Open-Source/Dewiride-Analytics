using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves the two screens' questions are answered, and answered only to somebody entitled to ask.
/// </summary>
/// <remarks>
/// What comes back here is what the dashboard renders, so the spellings matter as much as the
/// numbers: every category, band and reason is looked up in the message catalogue by the name in
/// this answer, and a name that changed would leave a screen showing nothing at all.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class TrafficReadTests(AnalyticsStackFixture stack)
{
    private const string Password = Passwords.Acceptable;

    private const string Scanner = "python-requests/2.32.3";

    [Fact]
    public async Task A_Member_Sees_What_Generated_Their_Traffic()
    {
        var site = await JudgedSiteAsync();
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/traffic");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var traffic = await response.Content.ReadFromJsonAsync<TrafficResponse>(Cancellation.Token);

            traffic.Should().NotBeNull();
            traffic.Sessions.Should().Be(1);
            traffic.PageViews.Should().Be(3);
            traffic.Groups.Should().ContainSingle();
            traffic.Groups[0].Category.Should().Be("security-scanner");
            traffic.Groups[0].Strength.Should().Be("strong");
        }
    }

    [Fact]
    public async Task A_Member_Sees_Why_Each_Visit_Was_Judged_The_Way_It_Was()
    {
        var site = await JudgedSiteAsync();
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/visits");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var visits = await response.Content.ReadFromJsonAsync<VisitsResponse>(Cancellation.Token);

            visits.Should().NotBeNull();
            visits.Visits.Should().ContainSingle();

            var visit = visits.Visits[0];

            visit.Category.Should().Be("security-scanner");
            visit.PageCount.Should().Be(3);
            visit.Ruleset.Should().Be("1.0");
            visit.IsProvisional.Should().BeFalse();
            visit.Surfaces.Should().Equal("cloudflare-worker");
            visit.Supporting.Should().Contain(reason => reason.Code == "probing.sensitive_paths");
            visit.Supporting.Should().OnlyContain(reason => reason.Direction == "toward-automation");
            visit.Supporting.Single(reason => reason.Code == "probing.sensitive_paths")
                .Values["attemptCount"].Should().Be("3");
        }
    }

    /// <summary>
    /// The same answer as a site that was never created, so neither endpoint can be used to test
    /// which identifiers on an install are real.
    /// </summary>
    [Theory]
    [InlineData("traffic")]
    [InlineData("visits")]
    public async Task A_Site_Somebody_Has_No_Role_On_Is_Answered_As_Though_It_Did_Not_Exist(string screen)
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(mine.Id, SiteRole.Owner);

        using (browser)
        {
            var otherSite = await browser.GetAsync($"/api/sites/{theirs.Id}/{screen}");
            var noSite = await browser.GetAsync($"/api/sites/{Guid.NewGuid()}/{screen}");

            otherSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
            noSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Theory]
    [InlineData("traffic")]
    [InlineData("visits")]
    public async Task Nobody_Signed_In_Is_Refused(string screen)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.GetAsync($"/api/sites/{site.Id}/{screen}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("visits?limit=0")]
    [InlineData("visits?limit=501")]
    [InlineData("visits?from=2024-01-02T00:00:00Z&to=2024-01-01T00:00:00Z")]
    [InlineData("traffic?from=2020-01-01T00:00:00Z&to=2024-01-01T00:00:00Z")]
    public async Task A_Question_That_Cannot_Be_Answered_As_Asked_Is_Refused(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/{query}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    /// <summary>
    /// A site nothing has visited yet answers with an empty breakdown rather than a refusal, so
    /// the screen can say "nothing yet" instead of "something went wrong".
    /// </summary>
    [Fact]
    public async Task A_Site_With_No_Judged_Traffic_Answers_With_Nothing_Rather_Than_A_Failure()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var traffic = await browser.GetAsync($"/api/sites/{site.Id}/traffic");
            var visits = await browser.GetAsync($"/api/sites/{site.Id}/visits");

            var groups = await traffic.Content.ReadFromJsonAsync<TrafficResponse>(Cancellation.Token);
            var listed = await visits.Content.ReadFromJsonAsync<VisitsResponse>(Cancellation.Token);

            groups.Should().NotBeNull();
            groups.Groups.Should().BeEmpty();
            groups.Sessions.Should().Be(0);
            listed.Should().NotBeNull();
            listed.Visits.Should().BeEmpty();
        }
    }

    /// <summary>
    /// Writes one recognisable visit and judges it, so what the screens read back has been through
    /// the whole path rather than been placed there.
    /// </summary>
    private async Task<Site> JudgedSiteAsync()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var clock = stack.Services.GetRequiredService<TimeProvider>();
        var at = clock.GetUtcNow().AddDays(-1);

        await stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(
            [
                Probe(site.Id, at, "/.env"),
                Probe(site.Id, at.AddSeconds(1), "/.git/config"),
                Probe(site.Id, at.AddSeconds(2), "/wp-login.php"),
            ],
            Cancellation.Token);

        await using var scope = stack.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SessionClassifier>()
            .CatchUpAsync(site.Id, site.CreatedAt.AddDays(-2), Cancellation.Token);

        return site;
    }

    private static RawEvent Probe(Guid siteId, DateTimeOffset at, string path) => new()
    {
        EventId = Guid.CreateVersion7(at),
        SiteId = siteId,
        Kind = EventKind.PageView,
        Surface = IngestSurface.CloudflareWorker,
        ServerTimestamp = at,
        VisitorKey = $"intruder-{siteId:n}",
        Host = "example.com",
        Path = path,
        UserAgent = Scanner,
        StatusCode = 404,
    };

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

    private static string Domain() => $"traffic-{Guid.NewGuid():n}.example";
}
