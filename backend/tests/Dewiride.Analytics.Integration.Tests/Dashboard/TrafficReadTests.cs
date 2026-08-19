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
    /// The list is a slice, so the count beside it has to describe the period rather than the slice.
    /// A list reporting its own length would tell somebody with a hundred visits that they had two,
    /// and would stop without admitting there was anything behind it.
    /// </summary>
    [Fact]
    public async Task Every_Visit_A_Period_Holds_Can_Be_Reached_A_Slice_At_A_Time()
    {
        var site = await JudgedSiteAsync(visitors: 5);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var first = await ReadVisitsAsync(browser, site.Id, "limit=2&offset=0");
            var second = await ReadVisitsAsync(browser, site.Id, "limit=2&offset=2");
            var last = await ReadVisitsAsync(browser, site.Id, "limit=2&offset=4");

            first.TotalVisits.Should().Be(5);
            second.TotalVisits.Should().Be(5);
            last.TotalVisits.Should().Be(5);

            first.Visits.Should().HaveCount(2);
            second.Visits.Should().HaveCount(2);
            last.Visits.Should().ContainSingle();

            // The ordering is total, so walking the slices reaches every visit exactly once. A tie
            // broken arbitrarily would show one of them twice and leave another unreachable.
            IEnumerable<string> walked =
            [
                .. first.Visits.Select(visit => visit.Id),
                .. second.Visits.Select(visit => visit.Id),
                .. last.Visits.Select(visit => visit.Id),
            ];

            walked.Should().OnlyHaveUniqueItems().And.HaveCount(5);
            first.Visits.Should().BeInDescendingOrder(visit => visit.StartedAt);
        }
    }

    /// <summary>
    /// Newest first, so the list opens on what just happened rather than on the oldest thing the
    /// period still holds.
    /// </summary>
    [Fact]
    public async Task The_Visit_List_Opens_On_The_Most_Recent_Visit()
    {
        var site = await JudgedSiteAsync(visitors: 3);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var everything = await ReadVisitsAsync(browser, site.Id, "limit=10&offset=0");
            var opening = await ReadVisitsAsync(browser, site.Id, "limit=1&offset=0");

            opening.Visits.Should().ContainSingle();
            opening.Visits[0].StartedAt.Should().Be(everything.Visits.Max(visit => visit.StartedAt));
        }
    }

    /// <summary>
    /// Asked past the end, the answer is a slice with nothing in it rather than a refusal: reaching
    /// the end of a list is an ordinary thing to have done, not a mistake.
    /// </summary>
    [Fact]
    public async Task Asking_Past_The_End_Of_The_Visit_List_Answers_With_An_Empty_Slice()
    {
        var site = await JudgedSiteAsync(visitors: 2);
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var past = await ReadVisitsAsync(browser, site.Id, "limit=10&offset=500");

            past.Visits.Should().BeEmpty();
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
    [InlineData("visits?offset=-1")]
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
    /// <summary>
    /// A site whose traffic has been judged.
    /// </summary>
    /// <param name="visitors">
    /// How many separate visitors to seed. Sessions are cut per visitor, so this is how many judged
    /// visits the site ends up with — which is what a list read a slice at a time needs more than
    /// one of.
    /// </param>
    /// <returns>The site.</returns>
    private async Task<Site> JudgedSiteAsync(int visitors = 1)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var clock = stack.Services.GetRequiredService<TimeProvider>();
        var at = clock.GetUtcNow().AddDays(-1);

        // Spaced an hour apart so the visits are in a definite order rather than one the store is
        // free to choose, which is what makes walking through them meaningful to assert.
        var probes = Enumerable.Range(0, visitors)
            .SelectMany(visitor =>
            {
                var began = at.AddHours(visitor);

                return new[]
                {
                    Probe(site.Id, began, "/.env", visitor),
                    Probe(site.Id, began.AddSeconds(1), "/.git/config", visitor),
                    Probe(site.Id, began.AddSeconds(2), "/wp-login.php", visitor),
                };
            });

        await stack.Services.GetRequiredService<IEventSink>()
            .WriteBatchAsync([.. probes], Cancellation.Token);

        await using var scope = stack.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SessionClassifier>()
            .CatchUpAsync(site.Id, site.CreatedAt.AddDays(-2), Cancellation.Token);

        return site;
    }

    private static RawEvent Probe(Guid siteId, DateTimeOffset at, string path, int visitor = 0) => new()
    {
        EventId = Guid.CreateVersion7(at),
        SiteId = siteId,
        Kind = EventKind.PageView,
        Surface = IngestSurface.CloudflareWorker,
        ServerTimestamp = at,
        VisitorKey = $"intruder-{visitor}-{siteId:n}",
        Host = "example.com",
        Path = path,
        UserAgent = Scanner,
        StatusCode = 404,
    };

    private static async Task<VisitsResponse> ReadVisitsAsync(Browser browser, Guid siteId, string slice)
    {
        var response = await browser.GetAsync($"/api/sites/{siteId}/visits?{slice}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var visits = await response.Content.ReadFromJsonAsync<VisitsResponse>(Cancellation.Token);

        visits.Should().NotBeNull();

        return visits;
    }

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
