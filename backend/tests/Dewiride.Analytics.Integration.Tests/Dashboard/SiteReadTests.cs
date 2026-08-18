using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves that reading a site's numbers requires a role on that site, and that the numbers are
/// the ones that were collected.
/// </summary>
/// <remarks>
/// A site identifier is printed in the source of every page it measures, so these endpoints are
/// asked for identifiers their caller has no business with as a matter of routine. A site that
/// does not exist and a site the caller has no role on must be answered identically.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SiteReadTests(AnalyticsStackFixture stack)
{
    private const string Password = Passwords.Acceptable;

    [Fact]
    public async Task Somebody_Sees_The_Sites_They_Hold_A_Role_On_And_No_Others()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: "Asia/Kolkata");
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(mine.Id, SiteRole.Editor);

        using (browser)
        {
            var response = await browser.GetAsync("/api/sites");
            var listed = await response.Content.ReadFromJsonAsync<IReadOnlyList<SiteSummary>>(Cancellation.Token);

            listed.Should().ContainSingle();
            listed[0].Id.Should().Be(mine.Id);
            listed[0].TimeZoneId.Should().Be("Asia/Kolkata");
            listed[0].Role.Should().Be("editor");
            listed.Should().NotContain(site => site.Id == theirs.Id);
        }
    }

    [Fact]
    public async Task A_Member_Can_Read_The_Headline_Totals()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/overview");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var totals = await response.Content.ReadFromJsonAsync<OverviewResponse>(Cancellation.Token);

            totals.Should().NotBeNull();
            totals.PageViews.Should().Be(0);
            totals.To.Should().BeAfter(totals.From);
        }
    }

    [Fact]
    public async Task A_Member_Can_Read_A_Measure_In_Daily_Buckets()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync(
                $"/api/sites/{site.Id}/series?metric=PageViews&granularity=Day");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var series = await response.Content.ReadFromJsonAsync<SeriesResponse>(Cancellation.Token);

            series.Should().NotBeNull();
            series.Metric.Should().Be("pageviews");
            series.Granularity.Should().Be("day");
            series.Points.Should().NotBeEmpty();
            series.Points.Should().BeInAscendingOrder(point => point.BucketStart);
        }
    }

    /// <summary>
    /// The same answer as a site that was never created, so the endpoint cannot be used to test
    /// which identifiers on an install are real.
    /// </summary>
    [Fact]
    public async Task A_Site_Somebody_Has_No_Role_On_Is_Answered_As_Though_It_Did_Not_Exist()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(mine.Id, SiteRole.Owner);

        using (browser)
        {
            var otherSite = await browser.GetAsync($"/api/sites/{theirs.Id}/overview");
            var noSite = await browser.GetAsync($"/api/sites/{Guid.NewGuid()}/overview");

            otherSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
            noSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Nobody_Signed_In_Is_Refused_A_Site_They_Would_Otherwise_Be_Allowed()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.GetAsync($"/api/sites/{site.Id}/overview");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("metric=nonsense&granularity=day")]
    [InlineData("metric=pageviews&granularity=fortnight")]
    [InlineData("metric=pageviews&granularity=hour&from=2020-01-01T00:00:00Z&to=2024-01-01T00:00:00Z")]
    [InlineData("metric=pageviews&granularity=day&from=2020-01-01T00:00:00Z&to=2024-01-01T00:00:00Z")]
    [InlineData("metric=pageviews&granularity=day&from=2024-01-02T00:00:00Z&to=2024-01-01T00:00:00Z")]
    public async Task A_Question_That_Cannot_Be_Answered_As_Asked_Is_Refused(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/series?{query}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    /// <summary>
    /// Checked before the site is looked up, so a refused window cannot be used to find out
    /// whether a site identifier is real.
    /// </summary>
    [Fact]
    public async Task An_Impossible_Window_Is_Refused_Even_For_A_Site_That_Does_Not_Exist()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync(
                $"/api/sites/{Guid.NewGuid()}/overview?from=2024-01-02T00:00:00Z&to=2024-01-01T00:00:00Z");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
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

    private static string Domain() => $"read-{Guid.NewGuid():n}.example";
}
