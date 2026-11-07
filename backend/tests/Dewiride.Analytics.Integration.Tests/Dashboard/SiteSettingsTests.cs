using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Infrastructure.Sites;
using Dewiride.Analytics.Infrastructure.Tenancy;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
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
    /// Off until somebody who knows the site asks for it. On a site that shows its visitors their
    /// own messages, names and accounts, the words on its buttons and links are those things.
    /// </summary>
    [Fact]
    public async Task A_New_Site_Records_Nothing_Its_Visitors_Operate_Until_Asked()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

        using (browser)
        {
            var settings = await ReadAsync(browser, site.Id);

            settings.CaptureClicks.Should().BeFalse();
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
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: Domain(),
            configure: created => created.SetClickCapture(true));
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);

        using (browser)
        {
            var off = await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { CaptureClicks = false });
            off.CaptureClicks.Should().BeFalse();

            var reread = await ReadAsync(browser, site.Id);
            reread.CaptureClicks.Should().BeFalse();

            var on = await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { CaptureClicks = true });
            on.CaptureClicks.Should().BeTrue();
            (await ReadAsync(browser, site.Id)).CaptureClicks.Should().BeTrue();
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
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: Domain(),
            configure: created => created.SetClickCapture(true));
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
                new UpdateSiteSettingsRequest { CaptureClicks = true });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var unchanged = await ReadAsync(browser, site.Id);
            unchanged.CaptureClicks.Should().BeFalse();
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
            await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { CaptureClicks = true });

            var response = await browser.PutAsync(Settings(site.Id), new UpdateSiteSettingsRequest());

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var settings = await ReadAsync(browser, site.Id);
            settings.CaptureClicks.Should().BeTrue();
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
            await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { CaptureClicks = true });

            var renamed = await ApplyAsync(
                browser,
                site.Id,
                new UpdateSiteSettingsRequest { DisplayName = "The Reading Room" });

            renamed.TimeZoneId.Should().Be("Europe/London");
            renamed.CaptureClicks.Should().BeTrue();

            var moved = await ApplyAsync(
                browser,
                site.Id,
                new UpdateSiteSettingsRequest { TimeZoneId = "Asia/Kolkata" });

            moved.DisplayName.Should().Be("The Reading Room");
            moved.CaptureClicks.Should().BeTrue();

            var stopped = await ApplyAsync(
                browser,
                site.Id,
                new UpdateSiteSettingsRequest { CaptureClicks = false });

            stopped.DisplayName.Should().Be("The Reading Room");
            stopped.TimeZoneId.Should().Be("Asia/Kolkata");
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
                new UpdateSiteSettingsRequest { DisplayName = "Not This Way", CaptureClicks = true });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var unchanged = await ReadAsync(browser, site.Id);
            unchanged.CaptureClicks.Should().BeFalse();
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

    /// <summary>
    /// What comes back is the one spelling it is kept in, because that is what the panel then shows
    /// and what saving the same sitemap again has to find unchanged.
    /// </summary>
    [Fact]
    public async Task An_Editor_Can_Name_The_Sitemap()
    {
        var domain = Domain();
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: domain);
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);

        using (browser)
        {
            var named = await ApplyAsync(
                browser,
                site.Id,
                new UpdateSiteSettingsRequest { SitemapAddress = $"  HTTPS://{domain}:443/sitemap.xml#top  " });

            named.SitemapAddress.Should().Be($"https://{domain}/sitemap.xml");
            (await ReadAsync(browser, site.Id)).SitemapAddress.Should().Be($"https://{domain}/sitemap.xml");
        }
    }

    [Fact]
    public async Task A_New_Website_Has_No_Sitemap()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

        using (browser)
        {
            (await ReadAsync(browser, site.Id)).SitemapAddress.Should().BeNull();
        }
    }

    /// <summary>
    /// Asserted by code rather than by status, because the code is what the dashboard writes its
    /// own sentence from, and a sentence about the name or the zone would send somebody to the wrong
    /// field. Whatever it was, the setting stays as it was.
    /// </summary>
    [Theory]
    [InlineData("https://example.org/sitemap.xml")]
    [InlineData("ftp://{0}/sitemap.xml")]
    [InlineData("https://{0}:8443/sitemap.xml")]
    [InlineData("https://user:secret@{0}/sitemap.xml")]
    [InlineData("http://127.0.0.1/sitemap.xml")]
    [InlineData("/sitemap.xml")]
    public async Task A_Sitemap_Elsewhere_Is_Refused_By_Code(string addressTemplate)
    {
        var domain = Domain();
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: domain);
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        var elsewhere = addressTemplate.Replace("{0}", domain, StringComparison.Ordinal);

        using (browser)
        {
            var response = await browser.PutAsync(
                Settings(site.Id),
                new UpdateSiteSettingsRequest { SitemapAddress = elsewhere });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Refusal.ReasonsOfAsync(response)).Should().Equal(SitemapRejected);
            (await ReadAsync(browser, site.Id)).SitemapAddress.Should().BeNull();
        }
    }

    /// <summary>
    /// A host a name server cannot be asked about parses and then throws when it is asked for its
    /// ASCII form. Somebody who pasted one is told the address cannot be used, like anybody else with
    /// a bad address, rather than being shown that the server fell over.
    /// </summary>
    [Fact]
    public async Task A_Sitemap_On_A_Name_With_No_Ascii_Form_Is_Refused_By_Code()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var response = await browser.PutAsync(
                Settings(site.Id),
                new UpdateSiteSettingsRequest { SitemapAddress = "https://exam‍ple.com/sitemap.xml" });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Refusal.ReasonsOfAsync(response)).Should().Equal(SitemapRejected);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_Empty_Sitemap_Address_Stops_It_Being_Read(string empty)
    {
        var domain = Domain();
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: domain,
            configure: created => created.SetSitemapAddress($"https://{domain}/sitemap.xml"));
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);

        using (browser)
        {
            var stopped = await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { SitemapAddress = empty });

            stopped.SitemapAddress.Should().BeNull();
            (await ReadAsync(browser, site.Id)).SitemapAddress.Should().BeNull();
        }
    }

    /// <summary>
    /// Naming a sitemap has this engine fetch from the address named, so it takes the same role as
    /// anything else that changes how a website is measured.
    /// </summary>
    [Fact]
    public async Task Somebody_Who_May_Only_Read_Cannot_Name_A_Sitemap()
    {
        var domain = Domain();
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: domain);
        var browser = await SignedIn.AsAsync(stack, SiteRole.Viewer, site.Id);

        using (browser)
        {
            var response = await browser.PutAsync(
                Settings(site.Id),
                new UpdateSiteSettingsRequest { SitemapAddress = $"https://{domain}/sitemap.xml" });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ReadAsync(browser, site.Id)).SitemapAddress.Should().BeNull();
        }
    }

    /// <summary>
    /// Null means unchanged here as it does for every other setting, so a save from a panel that
    /// has never heard of a sitemap cannot stop one being read.
    /// </summary>
    [Fact]
    public async Task A_Change_That_Does_Not_Name_The_Sitemap_Leaves_It_As_It_Was()
    {
        var domain = Domain();
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: domain,
            configure: created => created.SetSitemapAddress($"https://{domain}/sitemap.xml"));
        var browser = await SignedIn.AsAsync(stack, SiteRole.Editor, site.Id);

        using (browser)
        {
            var renamed = await ApplyAsync(
                browser,
                site.Id,
                new UpdateSiteSettingsRequest { DisplayName = "The Reading Room" });

            renamed.SitemapAddress.Should().Be($"https://{domain}/sitemap.xml");
        }
    }

    /// <summary>
    /// A change of sitemap saves differently from any other change, and everything else named beside
    /// it has to arrive all the same, the collector's cached copy being thrown away included.
    /// </summary>
    [Fact]
    public async Task A_Change_Naming_A_Sitemap_Applies_Everything_Else_It_Names()
    {
        var domain = Domain();
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: domain,
            configure: created => created.SetClickCapture(true));
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            (await CollectorSeesAsync(site.Id)).Should().BeTrue();

            var changed = await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest
            {
                DisplayName = "The Reading Room",
                CaptureClicks = false,
                SitemapAddress = $"https://{domain}/sitemap.xml",
            });

            changed.DisplayName.Should().Be("The Reading Room");
            changed.CaptureClicks.Should().BeFalse();
            changed.SitemapAddress.Should().Be($"https://{domain}/sitemap.xml");

            (await CollectorSeesAsync(site.Id)).Should().BeFalse();
        }
    }

    /// <summary>
    /// One refused part refuses the whole change, whichever part it was, and the sitemap is settled
    /// after the name and the zone, so somebody who got all three wrong is told about the first.
    /// </summary>
    [Fact]
    public async Task A_Refused_Sitemap_Leaves_Every_Setting_As_It_Was()
    {
        var domain = Domain();
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: domain, timeZoneId: "Etc/UTC");
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var refused = await browser.PutAsync(Settings(site.Id), new UpdateSiteSettingsRequest
            {
                DisplayName = "The Reading Room",
                TimeZoneId = "Asia/Kolkata",
                CaptureClicks = true,
                SitemapAddress = "https://example.org/sitemap.xml",
            });

            (await Refusal.ReasonsOfAsync(refused)).Should().Equal(SitemapRejected);

            var unchanged = await ReadAsync(browser, site.Id);
            unchanged.DisplayName.Should().Be(domain);
            unchanged.TimeZoneId.Should().Be("Etc/UTC");
            unchanged.CaptureClicks.Should().BeFalse();
            unchanged.SitemapAddress.Should().BeNull();

            var first = await browser.PutAsync(Settings(site.Id), new UpdateSiteSettingsRequest
            {
                TimeZoneId = "Mars/Olympus_Mons",
                SitemapAddress = "https://example.org/sitemap.xml",
            });

            (await Refusal.ReasonsOfAsync(first)).Should().Equal(TimeZoneRejected);
        }
    }

    /// <summary>
    /// How reading the old address went says nothing about the new one, and deleting it is what stops
    /// a reading still under way. The posts are kept: they are keyed by their paths, so the new sitemap
    /// lists them again or a complete reading of it unlists them, and nobody's calendar empties in the
    /// meantime. Another website's reading is nothing to do with any of this.
    /// </summary>
    [Fact]
    public async Task Changing_The_Sitemap_Forgets_How_The_Old_One_Was_Read_And_Keeps_The_Posts()
    {
        var (site, neighbour, browser) = await TwoWebsitesBeingReadAsync();

        using (browser)
        {
            await ApplyAsync(
                browser,
                site.Id,
                new UpdateSiteSettingsRequest { SitemapAddress = $"https://{site.Domain}/sitemap_index.xml" });

            (await SitemapSeed.LeftAsync(stack, site.Id)).Should().Be(new SitemapSeed.Left(0, 0, 1));
            (await SitemapSeed.LeftAsync(stack, neighbour.Id)).Should().Be(new SitemapSeed.Left(1, 1, 1));
        }
    }

    /// <summary>
    /// With no sitemap there is nothing that could ever list these posts again or say they had gone,
    /// so they would otherwise be counted for ever. Another website's posts stay where they are.
    /// </summary>
    [Fact]
    public async Task Clearing_The_Sitemap_Forgets_Everything_Read_From_It()
    {
        var (site, neighbour, browser) = await TwoWebsitesBeingReadAsync();

        using (browser)
        {
            await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { SitemapAddress = string.Empty });

            (await SitemapSeed.LeftAsync(stack, site.Id)).Should().Be(new SitemapSeed.Left(0, 0, 0));
            (await SitemapSeed.LeftAsync(stack, neighbour.Id)).Should().Be(new SitemapSeed.Left(1, 1, 1));
        }
    }

    /// <summary>
    /// A save that leaves the sitemap where it was writes nothing about it, whatever else it changes
    /// and however the same address was spelled this time. Forgetting a reading is what stops it,
    /// so anything less careful would have a change of name start a website's reading again.
    /// </summary>
    [Fact]
    public async Task A_Change_That_Leaves_The_Sitemap_Alone_Leaves_Its_Reading_Alone()
    {
        var (site, _, browser) = await TwoWebsitesBeingReadAsync();

        using (browser)
        {
            await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { DisplayName = "The Reading Room" });
            (await SitemapSeed.LeftAsync(stack, site.Id)).Should().Be(new SitemapSeed.Left(1, 1, 1));

            var respelled = $"HTTPS://{site.Domain.ToUpperInvariant()}/sitemap.xml#top";

            await ApplyAsync(browser, site.Id, new UpdateSiteSettingsRequest { SitemapAddress = respelled });

            (await SitemapSeed.LeftAsync(stack, site.Id)).Should().Be(new SitemapSeed.Left(1, 1, 1));
        }
    }

    /// <summary>
    /// A change the database did not take leaves everything as it was. Forgetting how a sitemap was
    /// read before the new address was saved would leave a website read from nowhere, with a reading
    /// under way for it that nothing would stop.
    /// </summary>
    [Fact]
    public async Task A_Change_Of_Sitemap_That_Cannot_Be_Saved_Forgets_Nothing()
    {
        var (site, _, browser) = await TwoWebsitesBeingReadAsync();

        browser.Dispose();

        await using var work = stack.Services.CreateAsyncScope();
        await using var failing = new ControlPlaneDbContext(
            new DbContextOptionsBuilder<ControlPlaneDbContext>()
                .UseNpgsql(stack.ControlPlaneConnectionString)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new RefusingSave())
                .Options);

        var settings = new SiteSettings(failing, work.ServiceProvider.GetRequiredService<HybridCache>());

        var act = () => settings.ApplyAsync(
            site.Id,
            new SiteAmendment(null, null, null, string.Empty),
            Cancellation.Token);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(RefusingSave.Refusal);

        (await SitemapSeed.LeftAsync(stack, site.Id)).Should().Be(new SitemapSeed.Left(1, 1, 1));
    }

    /// <summary>Names the reason a name is not one a website can be shown under.</summary>
    private const string NameRejected = "SiteNameRejected";

    /// <summary>Names the reason a time zone is not one this installation knows.</summary>
    private const string TimeZoneRejected = "SiteTimeZoneRejected";

    /// <summary>Names the reason an address is not one a website's posts may be read from.</summary>
    private const string SitemapRejected = "SiteSitemapRejected";

    private static string Settings(Guid siteId) => $"/api/sites/{siteId}/settings";

    /// <summary>
    /// Two websites with a sitemap each and a reading of it behind them, and somebody signed in who
    /// owns the first.
    /// </summary>
    /// <returns>The website the test is about, its neighbour, and the browser.</returns>
    private async Task<(Site Site, Site Neighbour, Browser Browser)> TwoWebsitesBeingReadAsync()
    {
        var site = await WebsiteBeingReadAsync();
        var neighbour = await WebsiteBeingReadAsync();
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        return (site, neighbour, browser);
    }

    private async Task<Site> WebsiteBeingReadAsync()
    {
        var domain = Domain();
        var address = $"https://{domain}/sitemap.xml";
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: domain,
            configure: created => created.SetSitemapAddress(address));

        await SitemapSeed.AddReadingAsync(stack, site.Id, address);

        return site;
    }

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

        var scopes = new MembershipTenantScopeProvider(
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

    /// <summary>A database that takes every statement up to the point of saving a change, and then does not.</summary>
    private sealed class RefusingSave : SaveChangesInterceptor
    {
        /// <summary>What the refusal says, so a test can tell it from any other failure.</summary>
        public const string Refusal = "The database did not take this change.";

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Refusal);
    }
}
