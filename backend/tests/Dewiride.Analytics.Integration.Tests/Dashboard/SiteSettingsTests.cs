using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves who may change what a site collects, and that a change reaches the collector.
/// </summary>
/// <remarks>
/// A setting that takes a minute to take effect is a setting that lies for a minute: somebody
/// turns recording off, is told it is off, and their visitors go on being recorded. So the write
/// path throws away what the collector had cached rather than waiting for it to expire.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SiteSettingsTests(AnalyticsStackFixture stack)
{
    /// <summary>
    /// On, so that a site added before this existed behaves like one added after it. A difference
    /// nobody could see the cause of is worse than either answer.
    /// </summary>
    [Fact]
    public async Task A_New_Site_Records_What_Its_Visitors_Operate()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var settings = await ReadAsync(browser, site.Id);

            settings.CaptureClicks.Should().BeTrue();
        }
    }

    [Fact]
    public async Task An_Editor_Can_Turn_Recording_Off_And_On_Again()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Editor);

        using (browser)
        {
            var off = await ApplyAsync(browser, site.Id, capture: false);
            off.CaptureClicks.Should().BeFalse();

            var reread = await ReadAsync(browser, site.Id);
            reread.CaptureClicks.Should().BeFalse();

            var on = await ApplyAsync(browser, site.Id, capture: true);
            on.CaptureClicks.Should().BeTrue();
        }
    }

    /// <summary>
    /// The property the whole switch rests on. The collector resolves a site out of a cache on
    /// every report, so a save that left the old answer in it would go on recording presses the
    /// dashboard has already said are not being recorded.
    /// </summary>
    [Fact]
    public async Task Turning_Recording_Off_Stops_The_Collector_At_Once()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Owner);

        using (browser)
        {
            // Resolved first, so the collector is holding the old answer when the change lands.
            (await CollectorSeesAsync(site.Id)).Should().BeTrue();

            await ApplyAsync(browser, site.Id, capture: false);

            (await CollectorSeesAsync(site.Id)).Should().BeFalse();
        }
    }

    /// <summary>
    /// A change to what is collected is a change to what the numbers mean, so it takes the same
    /// role as handing out a key that can write them.
    /// </summary>
    [Fact]
    public async Task Somebody_Who_May_Only_Read_Cannot_Change_What_Is_Collected()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var response = await browser.PutAsync(
                Settings(site.Id),
                new UpdateSiteSettingsRequest { CaptureClicks = false });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var unchanged = await ReadAsync(browser, site.Id);
            unchanged.CaptureClicks.Should().BeTrue();
        }
    }

    /// <summary>
    /// A setting left out is left as it was, so a caller that has never heard of a setting cannot
    /// switch it off by omission.
    /// </summary>
    [Fact]
    public async Task A_Setting_That_Is_Not_Named_Is_Left_As_It_Was()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Owner);

        using (browser)
        {
            await ApplyAsync(browser, site.Id, capture: false);

            var response = await browser.PutAsync(Settings(site.Id), new UpdateSiteSettingsRequest());

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var settings = await ReadAsync(browser, site.Id);
            settings.CaptureClicks.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Nobody_Signed_In_Can_Neither_Read_Nor_Change_What_Is_Collected()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var browser = await Browser.OpenAsync(stack);

        var reading = await browser.GetAsync(Settings(site.Id));
        var writing = await browser.PutAsync(
            Settings(site.Id),
            new UpdateSiteSettingsRequest { CaptureClicks = false });

        reading.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        writing.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A cookie the browser returns on its own is not proof that this page meant to send the
    /// request, so a change carries the pair the engine issued or it does not happen.
    /// </summary>
    [Fact]
    public async Task A_Change_Without_Proof_Of_Where_It_Came_From_Is_Refused()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Owner);

        using (browser)
        {
            var response = await browser.PutWithoutProofAsync(
                Settings(site.Id),
                new UpdateSiteSettingsRequest { CaptureClicks = false });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var unchanged = await ReadAsync(browser, site.Id);
            unchanged.CaptureClicks.Should().BeTrue();
        }
    }

    /// <summary>
    /// A site that does not exist and a site the caller has no role on answer identically, so this
    /// cannot be used to find out which identifiers on an install are real.
    /// </summary>
    [Fact]
    public async Task Somebody_Elses_Site_Answers_As_Though_It_Were_Not_There()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(mine.Id, SiteRole.Owner);

        using (browser)
        {
            var existing = await browser.GetAsync(Settings(theirs.Id));
            var invented = await browser.GetAsync(Settings(Guid.NewGuid()));

            existing.StatusCode.Should().Be(HttpStatusCode.NotFound);
            invented.StatusCode.Should().Be(existing.StatusCode);
        }
    }

    private static string Settings(Guid siteId) => $"/api/sites/{siteId}/settings";

    /// <summary>Whether the collector would record a press for this site, as it stands now.</summary>
    private async Task<bool> CollectorSeesAsync(Guid siteId)
    {
        using var work = stack.Services.CreateScope();

        var found = await work.ServiceProvider
            .GetRequiredService<ISiteCatalog>()
            .FindAsync(siteId, Cancellation.Token);

        found.Should().NotBeNull();

        return found.CaptureClicks;
    }

    private static async Task<SiteSettingsResponse> ReadAsync(Browser browser, Guid siteId)
    {
        var response = await browser.GetAsync(Settings(siteId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await response.Content.ReadFromJsonAsync<SiteSettingsResponse>(Cancellation.Token);

        settings.Should().NotBeNull();

        return settings;
    }

    private static async Task<SiteSettingsResponse> ApplyAsync(Browser browser, Guid siteId, bool capture)
    {
        var response = await browser.PutAsync(
            Settings(siteId),
            new UpdateSiteSettingsRequest { CaptureClicks = capture });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await response.Content.ReadFromJsonAsync<SiteSettingsResponse>(Cancellation.Token);

        settings.Should().NotBeNull();

        return settings;
    }

    private async Task<Browser> SignedInAsync(Guid siteId, SiteRole role)
    {
        var address = $"settings-{Guid.NewGuid():n}@example.com";
        var (_, user) = await ControlPlaneSeed.AddAccountAsync(stack, address, Passwords.Acceptable);
        await ControlPlaneSeed.GrantAsync(stack, siteId, user.Id, role);

        var browser = await Browser.OpenAsync(stack);
        var response = await browser.PostAsync(
            "/api/session",
            new SignInRequest { EmailAddress = address, Password = Passwords.Acceptable });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await browser.DescribeAsync();

        return browser;
    }

    private static string Domain() => $"settings-{Guid.NewGuid():n}.example";
}
