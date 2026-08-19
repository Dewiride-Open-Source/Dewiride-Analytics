using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Infrastructure.Tenancy;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves who may change how a website is set up, and that a change reaches everything it decides.
/// </summary>
/// <remarks>
/// <para>
/// Three things are settled here and they fail in three different ways. A name is what somebody
/// picks their website out by; a zone is where every day boundary the numbers are cut on sits; and
/// what is collected reaches the collector, which resolves a website out of a cache on every
/// report. A setting that takes a minute to take effect is a setting that lies for a minute, so
/// the write path throws away what the collector had cached rather than waiting for it to expire.
/// </para>
/// <para>
/// The promise the endpoint makes, and the one most easily broken, is that a setting the caller
/// did not name is left exactly as it stands. Several tests here exist for nothing else.
/// </para>
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
        var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

        using (browser)
        {
            var settings = await ReadAsync(browser, site.Id);

            settings.CaptureClicks.Should().BeTrue();
        }
    }

    /// <summary>
    /// A website is set up under the name and the zone it was added with, so the panel opens on
    /// what is stored rather than on a guess it would then save over.
    /// </summary>
    [Fact]
    public async Task Settings_Report_The_Name_And_The_Zone_A_Website_Was_Added_With()
    {
        var domain = Domain();
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: domain, timeZoneId: "Asia/Kolkata");
        var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

        using (browser)
        {
            var settings = await ReadAsync(browser, site.Id);

            settings.DisplayName.Should().Be(domain);
            settings.TimeZoneId.Should().Be("Asia/Kolkata");
        }
    }

    [Fact]
    public async Task An_Editor_Can_Turn_Recording_Off_And_On_Again()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);

        using (browser)
        {
            var off = await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { CaptureClicks = false });
            off.CaptureClicks.Should().BeFalse();

            var reread = await ReadAsync(browser, site.Id);
            reread.CaptureClicks.Should().BeFalse();

            var on = await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { CaptureClicks = true });
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
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            // Resolved first, so the collector is holding the old answer when the change lands.
            (await CollectorSeesAsync(site.Id)).Should().BeTrue();

            await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { CaptureClicks = false });

            (await CollectorSeesAsync(site.Id)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task An_Editor_Can_Rename_A_Website()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);

        using (browser)
        {
            var renamed = await ApplyAsync(
                browser,
                site.Id,
                new UpdateSiteSettingsRequest { DisplayName = "  The Reading Room  " });

            renamed.DisplayName.Should().Be("The Reading Room");
            (await ReadAsync(browser, site.Id)).DisplayName.Should().Be("The Reading Room");
        }
    }

    /// <summary>
    /// The reason anybody renames a website: to pick it out of the list at the top of the screen.
    /// A new name that only the settings panel knows about would have changed nothing.
    /// </summary>
    [Fact]
    public async Task A_Renamed_Website_Is_Listed_Under_Its_New_Name()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);

        using (browser)
        {
            await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { DisplayName = "The Reading Room" });

            var listed = await ListAsync(browser);

            listed.Should().ContainSingle().Which.DisplayName.Should().Be("The Reading Room");
        }
    }

    /// <summary>
    /// A name is what a website is called; its address is what the collector matches every report
    /// against. Moving one with the other would silently stop a website being measured at all.
    /// </summary>
    [Fact]
    public async Task Renaming_A_Website_Leaves_Its_Address_Alone()
    {
        var domain = Domain();
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: domain);
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);

        using (browser)
        {
            await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { DisplayName = "Something Else" });

            var listed = await ListAsync(browser);

            listed.Should().ContainSingle().Which.Domain.Should().Be(domain);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_Website_Cannot_Be_Left_Without_A_Name(string blank)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);

        using (browser)
        {
            var response = await browser.PutAsync(
                Settings(site.Id),
                new UpdateSiteSettingsRequest { DisplayName = blank });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Refusal.ReasonsOfAsync(response)).Should().Contain(NameRejected);
        }
    }

    /// <summary>
    /// The limit is the width the column is declared at. Refusing it here is what turns an
    /// over-long name into an answer somebody can act on rather than a save that fails on its way
    /// into the database with nothing to tell them.
    /// </summary>
    [Fact]
    public async Task A_Name_Longer_Than_A_Website_Accepts_Is_Refused_By_Name()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);

        using (browser)
        {
            var response = await browser.PutAsync(
                Settings(site.Id),
                new UpdateSiteSettingsRequest { DisplayName = new string('n', Site.MaxDisplayNameLength + 1) });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Refusal.ReasonsOfAsync(response)).Should().Contain(NameRejected);
        }
    }

    [Fact]
    public async Task A_Name_As_Long_As_A_Website_Accepts_Is_Kept()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);
        var longest = new string('n', Site.MaxDisplayNameLength);

        using (browser)
        {
            var renamed = await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { DisplayName = longest });

            renamed.DisplayName.Should().Be(longest);
            (await ReadAsync(browser, site.Id)).DisplayName.Should().Be(longest);
        }
    }

    [Fact]
    public async Task An_Editor_Can_Change_The_Zone_A_Websites_Days_Are_Counted_In()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: "Etc/UTC");
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);

        using (browser)
        {
            var moved = await ApplyAsync(
                browser,
                site.Id,
                new UpdateSiteSettingsRequest { TimeZoneId = "Asia/Kolkata" });

            moved.TimeZoneId.Should().Be("Asia/Kolkata");
            (await ReadAsync(browser, site.Id)).TimeZoneId.Should().Be("Asia/Kolkata");
        }
    }

    /// <summary>
    /// The identifier reaches the telemetry store as the zone the daily buckets are cut in, so one
    /// this installation cannot resolve has to be refused where it is chosen rather than surfacing
    /// later as a screen that will not load.
    /// </summary>
    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("GMT+5")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_Zone_This_Installation_Does_Not_Know_Is_Refused_By_Name(string unknown)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: "Etc/UTC");
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);

        using (browser)
        {
            var response = await browser.PutAsync(
                Settings(site.Id),
                new UpdateSiteSettingsRequest { TimeZoneId = unknown });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Refusal.ReasonsOfAsync(response)).Should().Contain(TimeZoneRejected);
            (await ReadAsync(browser, site.Id)).TimeZoneId.Should().Be("Etc/UTC");
        }
    }

    /// <summary>
    /// The zone travels with the authorisation scope every reading is made under, which is the
    /// point of changing it at all. A scope still carrying the old zone would go on cutting the
    /// days where they used to be cut while the panel said otherwise.
    /// </summary>
    [Fact]
    public async Task A_Reading_Is_Cut_On_The_Zone_As_It_Now_Stands()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: "Etc/UTC");
        var address = SignedIn.Address();
        var (created, account) = await ControlPlaneSeed.AddAccountAsync(stack, address, Passwords.Acceptable);

        created.Succeeded.Should().BeTrue();
        await ControlPlaneSeed.GrantAsync(stack, site.Id, account.Id, SiteRole.Owner);

        using var browser = await SignedIn.AsAccountAsync(stack, address);

        await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { TimeZoneId = "Asia/Kolkata" });

        var scope = await ScopeOfAsync(site.Id, account.Id);

        scope.Should().NotBeNull();
        scope.TimeZoneId.Should().Be("Asia/Kolkata");
    }

    /// <summary>
    /// A change to what is collected is a change to what the numbers mean, so it takes the same
    /// role as handing out a key that can write them.
    /// </summary>
    [Fact]
    public async Task Somebody_Who_May_Only_Read_Cannot_Change_What_Is_Collected()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

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
    /// The name and the zone are behind the same gate as everything else on the panel. A website
    /// somebody can only read is a website they cannot rename or move the days of either.
    /// </summary>
    [Fact]
    public async Task Somebody_Who_May_Only_Read_Cannot_Rename_A_Website_Or_Move_Its_Days()
    {
        var domain = Domain();
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: domain, timeZoneId: "Etc/UTC");
        var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

        using (browser)
        {
            var renaming = await browser.PutAsync(
                Settings(site.Id),
                new UpdateSiteSettingsRequest { DisplayName = "Not Theirs To Name" });

            var moving = await browser.PutAsync(
                Settings(site.Id),
                new UpdateSiteSettingsRequest { TimeZoneId = "Asia/Kolkata" });

            renaming.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            moving.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var unchanged = await ReadAsync(browser, site.Id);
            unchanged.DisplayName.Should().Be(domain);
            unchanged.TimeZoneId.Should().Be("Etc/UTC");
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
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { CaptureClicks = false });

            var response = await browser.PutAsync(Settings(site.Id), new UpdateSiteSettingsRequest());

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var settings = await ReadAsync(browser, site.Id);
            settings.CaptureClicks.Should().BeFalse();
        }
    }

    /// <summary>
    /// The promise the panel is written against: it sends only what somebody actually altered, and
    /// each of the three settings has to survive a change to either of the others untouched. This
    /// is the property that breaks the moment anything reconstructs a whole website from a partial
    /// request, and the failure is silent — a day boundary moves and nobody is told.
    /// </summary>
    [Fact]
    public async Task A_Change_That_Names_One_Setting_Leaves_The_Others_As_They_Were()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: "Europe/London");
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { CaptureClicks = false });

            var renamed = await ApplyAsync(
                browser,
                site.Id,
                new UpdateSiteSettingsRequest { DisplayName = "The Reading Room" });

            renamed.TimeZoneId.Should().Be("Europe/London");
            renamed.CaptureClicks.Should().BeFalse();

            var moved = await ApplyAsync(
                browser,
                site.Id,
                new UpdateSiteSettingsRequest { TimeZoneId = "Asia/Kolkata" });

            moved.DisplayName.Should().Be("The Reading Room");
            moved.CaptureClicks.Should().BeFalse();

            var recording = await ApplyAsync(
                browser,
                site.Id,
                new UpdateSiteSettingsRequest { CaptureClicks = true });

            recording.DisplayName.Should().Be("The Reading Room");
            recording.TimeZoneId.Should().Be("Asia/Kolkata");
        }
    }

    /// <summary>
    /// One refused part refuses the whole thing. Storing the acceptable half would leave somebody
    /// told their change was rejected while half of it had already happened.
    /// </summary>
    [Fact]
    public async Task A_Refused_Change_Leaves_Every_Setting_As_It_Was()
    {
        var domain = Domain();
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: domain, timeZoneId: "Etc/UTC");
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var response = await browser.PutAsync(Settings(site.Id), new UpdateSiteSettingsRequest
            {
                DisplayName = "The Reading Room",
                TimeZoneId = "Mars/Olympus_Mons",
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var unchanged = await ReadAsync(browser, site.Id);
            unchanged.DisplayName.Should().Be(domain);
            unchanged.TimeZoneId.Should().Be("Etc/UTC");
        }
    }

    /// <summary>
    /// Two things wrong at once names the first of them, so the sentence somebody reads is about
    /// the field their eye is already on rather than about whichever check happened to run first.
    /// </summary>
    [Fact]
    public async Task A_Refusal_Names_The_First_Thing_That_Is_Actually_Wrong()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var response = await browser.PutAsync(Settings(site.Id), new UpdateSiteSettingsRequest
            {
                DisplayName = new string('n', Site.MaxDisplayNameLength + 1),
                TimeZoneId = "Mars/Olympus_Mons",
            });

            var reasons = await Refusal.ReasonsOfAsync(response);

            reasons.Should().Contain(NameRejected);
            reasons.Should().NotContain(TimeZoneRejected);
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
        var domain = Domain();
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: domain);
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var response = await browser.PutWithoutProofAsync(
                Settings(site.Id),
                new UpdateSiteSettingsRequest { DisplayName = "Not This Way", CaptureClicks = false });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var unchanged = await ReadAsync(browser, site.Id);
            unchanged.CaptureClicks.Should().BeTrue();
            unchanged.DisplayName.Should().Be(domain);
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
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, mine.Id);

        using (browser)
        {
            var existing = await browser.GetAsync(Settings(theirs.Id));
            var invented = await browser.GetAsync(Settings(Guid.NewGuid()));

            existing.StatusCode.Should().Be(HttpStatusCode.NotFound);
            invented.StatusCode.Should().Be(existing.StatusCode);
        }
    }

    /// <summary>Names the reason a name is not one a website can be shown under.</summary>
    private const string NameRejected = "SiteNameRejected";

    /// <summary>Names the reason a time zone is not one this installation knows.</summary>
    private const string TimeZoneRejected = "SiteTimeZoneRejected";

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

    /// <summary>
    /// The scope a reading of this site would be made under, resolved as a request would resolve
    /// it.
    /// </summary>
    /// <remarks>
    /// Built here rather than taken from the container because the registered one reads the
    /// identity of the request it is serving, and there is no request in hand. What is being
    /// proved is what the provider reads out of the control plane, which is the same either way.
    /// </remarks>
    /// <param name="siteId">The site.</param>
    /// <param name="userId">The person asking.</param>
    /// <returns>The scope, or nothing where they have no role on it.</returns>
    private async Task<TenantScope?> ScopeOfAsync(Guid siteId, Guid userId)
    {
        await using var work = stack.Services.CreateAsyncScope();

        var scopes = new SingleTenantScopeProvider(
            work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>(),
            new Caller(userId));

        return await scopes.ResolveAsync(siteId, Cancellation.Token);
    }

    private static async Task<SiteSettingsResponse> ReadAsync(Browser browser, Guid siteId)
    {
        var response = await browser.GetAsync(Settings(siteId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await response.Content.ReadFromJsonAsync<SiteSettingsResponse>(Cancellation.Token);

        settings.Should().NotBeNull();

        return settings;
    }

    private static async Task<SiteSettingsResponse> ApplyAsync(
        Browser browser,
        Guid siteId,
        UpdateSiteSettingsRequest change)
    {
        var response = await browser.PutAsync(Settings(siteId), change);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await response.Content.ReadFromJsonAsync<SiteSettingsResponse>(Cancellation.Token);

        settings.Should().NotBeNull();

        return settings;
    }

    private static async Task<IReadOnlyList<SiteSummary>> ListAsync(Browser browser)
    {
        var response = await browser.GetAsync("/api/sites");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = await response.Content.ReadFromJsonAsync<IReadOnlyList<SiteSummary>>(Cancellation.Token);

        listed.Should().NotBeNull();

        return listed;
    }

    private static string Domain() => $"settings-{Guid.NewGuid():n}.example";

    /// <summary>The signed-in person, as the scope provider asks the request for them.</summary>
    /// <param name="userId">Their account.</param>
    private sealed class Caller(Guid userId) : ICurrentPrincipalAccessor
    {
        /// <inheritdoc />
        public Guid? GetUserId() => userId;
    }
}
