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

    [Fact]
    public async Task A_Member_Can_Read_The_Busiest_Pages()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/pages");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var pages = await response.Content.ReadFromJsonAsync<PagesResponse>(Cancellation.Token);

            pages.Should().NotBeNull();
            pages.Pages.Should().BeEmpty();
            pages.PageViews.Should().Be(0);
            pages.TotalPaths.Should().Be(0);
            pages.To.Should().BeAfter(pages.From);
        }
    }

    /// <summary>
    /// The whole list is read a slice at a time, so a caller may start anywhere along it — and
    /// past the end of a short one answers empty rather than refusing.
    /// </summary>
    [Fact]
    public async Task A_Member_Can_Start_The_Page_List_Further_Along()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/pages?limit=10&offset=20");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var pages = await response.Content.ReadFromJsonAsync<PagesResponse>(Cancellation.Token);

            pages.Should().NotBeNull();
            pages.Pages.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=-1")]
    [InlineData("offset=-1")]
    public async Task Asking_For_An_Impossible_Number_Of_Pages_Is_Refused(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/pages?{query}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("?grouping=country")]
    [InlineData("?grouping=town")]
    [InlineData("?grouping=TOWN")]
    public async Task A_Member_Can_Read_Where_The_Audience_Was(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/locations{query}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var places = await response.Content.ReadFromJsonAsync<LocationsResponse>(Cancellation.Token);

            places.Should().NotBeNull();
            places.Places.Should().BeEmpty();
            places.Visitors.Should().Be(0);
            places.TotalPlaces.Should().Be(0);
            places.To.Should().BeAfter(places.From);
        }
    }

    /// <summary>
    /// The answer names what it grouped by rather than repeating the caller's spelling, so two
    /// requests differing only in case produce identical documents.
    /// </summary>
    [Theory]
    [InlineData("Country", "country")]
    [InlineData("TOWN", "town")]
    public async Task A_Place_List_Names_What_It_Grouped_By(string asked, string expected)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/locations?grouping={asked}");
            var places = await response.Content.ReadFromJsonAsync<LocationsResponse>(Cancellation.Token);

            places.Should().NotBeNull();
            places.Grouping.Should().Be(expected);
        }
    }

    [Theory]
    [InlineData("grouping=continent")]
    [InlineData("grouping=country_code%3B+DROP+TABLE+events")]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=-1")]
    [InlineData("offset=-1")]
    public async Task Asking_For_An_Impossible_Place_List_Is_Refused(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/locations?{query}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task A_Member_Can_Read_What_The_Audience_Read_On()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/devices");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var devices = await response.Content.ReadFromJsonAsync<DevicesResponse>(Cancellation.Token);

            devices.Should().NotBeNull();
            devices.Devices.Should().BeEmpty();
            devices.Visitors.Should().Be(0);
            devices.To.Should().BeAfter(devices.From);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("?grouping=control")]
    [InlineData("?grouping=destination")]
    [InlineData("?grouping=DESTINATION")]
    public async Task A_Member_Can_Read_What_The_Audience_Operated(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/actions{query}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var actions = await response.Content.ReadFromJsonAsync<ActionsResponse>(Cancellation.Token);

            actions.Should().NotBeNull();
            actions.Controls.Should().BeEmpty();
            actions.Presses.Should().Be(0);
            actions.TotalControls.Should().Be(0);
            actions.To.Should().BeAfter(actions.From);
        }
    }

    /// <summary>
    /// The answer names what it gathered by rather than repeating the caller's spelling, on the
    /// same terms as a place list.
    /// </summary>
    [Theory]
    [InlineData("Control", "control")]
    [InlineData("DESTINATION", "destination")]
    public async Task A_Control_List_Names_What_It_Gathered_By(string asked, string expected)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/actions?grouping={asked}");
            var actions = await response.Content.ReadFromJsonAsync<ActionsResponse>(Cancellation.Token);

            actions.Should().NotBeNull();
            actions.Grouping.Should().Be(expected);
        }
    }

    [Theory]
    [InlineData("grouping=element")]
    [InlineData("grouping=action_label%3B+DROP+TABLE+events")]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=-1")]
    [InlineData("offset=-1")]
    public async Task Asking_For_An_Impossible_Control_List_Is_Refused(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/actions?{query}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("?grouping=browser")]
    [InlineData("?grouping=system")]
    [InlineData("?grouping=SYSTEM")]
    public async Task A_Member_Can_Read_What_Software_The_Audience_Used(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/software{query}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var software = await response.Content.ReadFromJsonAsync<SoftwareResponse>(Cancellation.Token);

            software.Should().NotBeNull();
            software.Names.Should().BeEmpty();
            software.Visitors.Should().Be(0);
            software.TotalNames.Should().Be(0);
            software.To.Should().BeAfter(software.From);
        }
    }

    /// <summary>
    /// The answer names what it grouped by rather than repeating the caller's spelling, on the
    /// same terms as a place list.
    /// </summary>
    [Theory]
    [InlineData("Browser", "browser")]
    [InlineData("SYSTEM", "system")]
    public async Task A_Software_List_Names_What_It_Grouped_By(string asked, string expected)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/software?grouping={asked}");
            var software = await response.Content.ReadFromJsonAsync<SoftwareResponse>(Cancellation.Token);

            software.Should().NotBeNull();
            software.Grouping.Should().Be(expected);
        }
    }

    [Theory]
    [InlineData("grouping=device")]
    [InlineData("grouping=browser_family%3B+DROP+TABLE+events")]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=-1")]
    [InlineData("offset=-1")]
    public async Task Asking_For_An_Impossible_Software_List_Is_Refused(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/software?{query}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    /// <summary>
    /// How many readings could be measured is answered beside every figure, so a website with no
    /// browser tracker on it reads as unmeasured rather than as an audience that did nothing.
    /// </summary>
    [Fact]
    public async Task A_Member_Can_Read_How_The_Pages_Were_Read()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/engagement");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var reading = await response.Content.ReadFromJsonAsync<EngagementResponse>(Cancellation.Token);

            reading.Should().NotBeNull();
            reading.Readings.Should().Be(0);
            reading.Measured.Should().Be(0);
            reading.MedianEngagedMs.Should().Be(0);
            reading.Depths.Should().NotBeNull();
            reading.To.Should().BeAfter(reading.From);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("?ranking=attention")]
    [InlineData("?ranking=depth")]
    [InlineData("?ranking=DEPTH")]
    public async Task A_Member_Can_Read_Which_Pages_Held_Attention(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/engagement/pages{query}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var pages = await response.Content.ReadFromJsonAsync<PageEngagementResponse>(Cancellation.Token);

            pages.Should().NotBeNull();
            pages.Pages.Should().BeEmpty();
            pages.TotalPages.Should().Be(0);
            pages.LongestMedianEngagedMs.Should().Be(0);
            pages.To.Should().BeAfter(pages.From);
        }
    }

    /// <summary>
    /// The answer names what it ordered by rather than repeating the caller's spelling, on the
    /// same terms as a place list names what it grouped by.
    /// </summary>
    [Theory]
    [InlineData("Attention", "attention")]
    [InlineData("DEPTH", "depth")]
    public async Task A_Reading_List_Names_What_It_Ordered_By(string asked, string expected)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync(
                $"/api/sites/{site.Id}/engagement/pages?ranking={asked}");

            var pages = await response.Content.ReadFromJsonAsync<PageEngagementResponse>(Cancellation.Token);

            pages.Should().NotBeNull();
            pages.Ranking.Should().Be(expected);
        }
    }

    [Theory]
    [InlineData("ranking=visitors")]
    [InlineData("ranking=median_engaged_ms%3B+DROP+TABLE+events")]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=-1")]
    [InlineData("offset=-1")]
    public async Task Asking_For_An_Impossible_Reading_List_Is_Refused(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/engagement/pages?{query}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task A_Member_Can_Read_How_The_Visits_Went()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/visits/totals");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var totals = await response.Content.ReadFromJsonAsync<VisitTotalsResponse>(Cancellation.Token);

            totals.Should().NotBeNull();
            totals.Visits.Should().Be(0);
            totals.SinglePageVisits.Should().Be(0);
            totals.PageViews.Should().Be(0);
            totals.To.Should().BeAfter(totals.From);
        }
    }

    [Theory]
    [InlineData("", "entry")]
    [InlineData("?position=entry", "entry")]
    [InlineData("?position=exit", "exit")]
    [InlineData("?position=EXIT", "exit")]
    public async Task A_Member_Can_Read_Where_Visits_Began_And_Ended(string query, string expected)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/visits/pages{query}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var pages = await response.Content.ReadFromJsonAsync<VisitPagesResponse>(Cancellation.Token);

            pages.Should().NotBeNull();
            pages.Position.Should().Be(expected);
            pages.Pages.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("position=middle")]
    [InlineData("position=exit_path%3B+DROP+TABLE+events")]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("offset=-1")]
    public async Task Asking_For_An_Impossible_Arrival_List_Is_Refused(string query)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/visits/pages?{query}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task A_Member_Can_Read_The_Pages_One_Visit_Went_Through()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);
        const string visit = "2f8a1c0b4d6e7f905a1b2c3d4e5f6071:1777628415250";

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/visits/{visit}/journey");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var journey = await response.Content.ReadFromJsonAsync<VisitJourneyResponse>(Cancellation.Token);

            journey.Should().NotBeNull();
            journey.Visit.Should().Be(visit);
            journey.Steps.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A visit's identity arrives from an address somebody typed, so anything that is not one is
    /// refused where it arrives rather than answered with an empty list.
    /// </summary>
    [Theory]
    [InlineData("nonsense")]
    [InlineData("2f8a1c0b4d6e7f905a1b2c3d4e5f6071")]
    [InlineData("2f8a1c0b4d6e7f905a1b2c3d4e5f6071:yesterday")]
    [InlineData("2f8a1c0b4d6e7f905a1b2c3d4e5f6071%3B+DROP+TABLE+events%3A1777628415250")]
    public async Task Asking_For_Something_That_Is_Not_A_Visit_Is_Refused(string visit)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.GetAsync($"/api/sites/{site.Id}/visits/{visit}/journey");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    /// <summary>
    /// Every address behind the dashboard's door is shut to somebody who has not opened it, and a
    /// new one is protected unless it says otherwise — so the list grows with the endpoints rather
    /// than being written once and forgotten.
    /// </summary>
    [Theory]
    [InlineData("overview")]
    [InlineData("pages")]
    [InlineData("actions")]
    [InlineData("locations")]
    [InlineData("devices")]
    [InlineData("software")]
    [InlineData("engagement")]
    [InlineData("engagement/pages")]
    [InlineData("visits")]
    [InlineData("visits/totals")]
    [InlineData("visits/pages")]
    [InlineData("visits/2f8a1c0b4d6e7f905a1b2c3d4e5f6071:1777628415250/journey")]
    [InlineData("traffic")]
    public async Task Nobody_Signed_In_Is_Refused_A_Site_They_Would_Otherwise_Be_Allowed(string question)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.GetAsync($"/api/sites/{site.Id}/{question}");

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
