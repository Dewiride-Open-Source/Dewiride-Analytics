using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves who may add a website, and what a new one is.
/// </summary>
/// <remarks>
/// Adding a website decides what an installation collects and who can see it, so it takes the role
/// that already carries that responsibility. Which organisation the new one joins is taken from a
/// website the person already owns rather than from anything they send, so nobody can put a
/// website into an organisation belonging to somebody else by asking.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class AddSiteTests(AnalyticsStackFixture stack)
{
    [Fact]
    public async Task An_Owner_Can_Add_A_Website_And_Owns_It()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var added = await AddAsync(browser, "shop.example.com");

            added.Domain.Should().Be("shop.example.com");
            added.DisplayName.Should().Be("shop.example.com");
            added.TimeZoneId.Should().Be("Asia/Kolkata");
            added.Role.Should().Be("owner");
            added.Id.Should().NotBe(site.Id);
        }
    }

    /// <summary>
    /// The point of the whole thing: it is there to be switched to the moment it exists.
    /// </summary>
    [Fact]
    public async Task An_Added_Website_Is_In_The_List_Straight_Away()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var added = await AddAsync(browser, "docs.example.com");
            var listed = await ListAsync(browser);

            listed.Select(one => one.Id).Should().Contain(added.Id);
            listed.Select(one => one.Domain).Should().Contain("docs.example.com");
        }
    }

    /// <summary>
    /// A new website joins the organisation of one the person already owns, which is what keeps an
    /// installation a single organisation rather than a scattering of them.
    /// </summary>
    [Fact]
    public async Task An_Added_Website_Joins_The_One_Its_Owner_Already_Has()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var added = await AddAsync(browser, "second.example.com");

            var (existing, arrived) = await OrganizationsOfAsync(site.Id, added.Id);

            arrived.Should().Be(existing);
        }
    }

    [Theory]
    [InlineData(SiteRole.Viewer)]
    [InlineData(SiteRole.Editor)]
    public async Task Somebody_Who_Owns_No_Website_Cannot_Add_One(SiteRole role)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, role, site.Id);

        using (browser)
        {
            var response = await browser.PostAsync(Sites, new AddSiteRequest
            {
                Domain = "shop.example.com",
                TimeZoneId = "Etc/UTC",
            });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    /// <summary>
    /// Two rows for one address would split its traffic between two entries nobody could tell
    /// apart, and the refusal names itself so the dashboard can say which one it means.
    /// </summary>
    [Fact]
    public async Task A_Website_That_Is_Already_Here_Is_Refused_By_Name()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            await AddAsync(browser, "shop.example.com");

            var again = await browser.PostAsync(Sites, new AddSiteRequest
            {
                Domain = "shop.example.com",
                TimeZoneId = "Etc/UTC",
            });

            again.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await Refusal.ReasonsOfAsync(again)).Should().Contain("SiteAlreadyMeasured");
        }
    }

    /// <summary>
    /// The same address in different letters is the same address, so it is checked against the
    /// hostname as it will be stored rather than as it was typed.
    /// </summary>
    [Fact]
    public async Task The_Same_Website_Written_Differently_Is_Still_The_Same_Website()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            await AddAsync(browser, "shop.example.com");

            var again = await browser.PostAsync(Sites, new AddSiteRequest
            {
                Domain = "  SHOP.Example.com.  ",
                TimeZoneId = "Etc/UTC",
            });

            again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }

    [Theory]
    [InlineData("", "Etc/UTC")]
    [InlineData("   ", "Etc/UTC")]
    [InlineData("shop.example.com", "")]
    [InlineData("shop.example.com", "Mars/Olympus_Mons")]
    public async Task A_Website_That_Cannot_Be_Built_Is_Refused_By_Name(string domain, string zone)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var response = await browser.PostAsync(Sites, new AddSiteRequest
            {
                Domain = domain,
                TimeZoneId = zone,
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Refusal.ReasonsOfAsync(response)).Should().Contain("SiteDetailsRejected");
        }
    }

    /// <summary>
    /// An address longer than a hostname is allowed to be is refused for what it is, not left to
    /// fail against the column it would not fit in — where the only thing it could produce is a
    /// failure with nothing in it anybody could act on.
    /// </summary>
    [Fact]
    public async Task A_Website_Whose_Address_Is_Too_Long_Is_Refused_By_Name()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var response = await browser.PostAsync(Sites, new AddSiteRequest
            {
                Domain = $"{new string('a', 250)}.example.com",
                TimeZoneId = "Etc/UTC",
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Refusal.ReasonsOfAsync(response)).Should().Contain("SiteDetailsRejected");
        }
    }

    [Fact]
    public async Task Nobody_Signed_In_Cannot_Add_A_Website()
    {
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.PostAsync(Sites, new AddSiteRequest
        {
            Domain = "shop.example.com",
            TimeZoneId = "Etc/UTC",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A cookie the browser returns on its own is not proof that this page meant to send the
    /// request, so adding a website carries the pair the engine issued or it does not happen.
    /// </summary>
    [Fact]
    public async Task Adding_Without_Proof_Of_Where_It_Came_From_Is_Refused()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedIn.AsAsync(stack, SiteRole.Owner, site.Id);

        using (browser)
        {
            var response = await browser.PostWithoutProofAsync(Sites, new AddSiteRequest
            {
                Domain = "shop.example.com",
                TimeZoneId = "Etc/UTC",
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ListAsync(browser)).Should().ContainSingle();
        }
    }

    /// <summary>The organisations two sites belong to, so a test can assert they are the same one.</summary>
    private async Task<(Guid Existing, Guid Arrived)> OrganizationsOfAsync(Guid existing, Guid arrived)
    {
        await using var work = stack.Services.CreateAsyncScope();

        var database = work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var organizations = await database.Sites
            .AsNoTracking()
            .Where(site => site.Id == existing || site.Id == arrived)
            .ToDictionaryAsync(site => site.Id, site => site.OrganizationId, Cancellation.Token);

        organizations.Should().HaveCount(2);

        return (organizations[existing], organizations[arrived]);
    }

    private const string Sites = "/api/sites";

    private static async Task<SiteSummary> AddAsync(Browser browser, string domain)
    {
        var response = await browser.PostAsync(Sites, new AddSiteRequest
        {
            Domain = domain,
            TimeZoneId = "Asia/Kolkata",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var added = await response.Content.ReadFromJsonAsync<SiteSummary>(Cancellation.Token);

        added.Should().NotBeNull();

        return added;
    }

    private static async Task<IReadOnlyList<SiteSummary>> ListAsync(Browser browser)
    {
        var response = await browser.GetAsync(Sites);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<SiteSummary>>(Cancellation.Token);

        listed.Should().NotBeNull();

        return listed;
    }

    private static string Domain() => $"add-{Guid.NewGuid():n}.example";
}
