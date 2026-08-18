using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves who may create a key that can write traffic into a site, and that the secret exists
/// exactly once.
/// </summary>
/// <remarks>
/// A key lets its holder assert the visitor's address on every event it reports, which is the
/// value most of a classification rests on. Handing one out is therefore a change to what the
/// numbers mean rather than a convenience, and the rules around it are worth proving rather than
/// intending.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class ServerKeyTests(AnalyticsStackFixture stack)
{
    [Fact]
    public async Task A_Created_Key_Is_Returned_Once_And_Then_Only_Described()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Editor);

        using (browser)
        {
            var issued = await IssueAsync(browser, site.Id, "Cloudflare");

            issued.Secret.Should().StartWith("dwk_");
            issued.Key.Name.Should().Be("Cloudflare");
            issued.Secret.Should().EndWith(issued.Key.Preview);

            var listed = await ListAsync(browser, site.Id);

            listed.Should().ContainSingle();
            listed[0].Id.Should().Be(issued.Key.Id);
            listed[0].Preview.Should().Be(issued.Key.Preview);
            listed[0].LastUsedAt.Should().BeNull();
        }
    }

    /// <summary>
    /// Two keys created a moment apart must not be the same secret, which is the one property
    /// the whole scheme rests on.
    /// </summary>
    [Fact]
    public async Task Two_Keys_Are_Never_The_Same()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Owner);

        using (browser)
        {
            var first = await IssueAsync(browser, site.Id, "Edge");
            var second = await IssueAsync(browser, site.Id, "Plugin");

            first.Secret.Should().NotBe(second.Secret);
            first.Key.Id.Should().NotBe(second.Key.Id);
        }
    }

    [Fact]
    public async Task A_Key_Must_Be_Given_A_Name()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Editor);

        using (browser)
        {
            var response = await browser.PostAsync(Keys(site.Id), new CreateServerKeyRequest("   "));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    /// <summary>
    /// A viewer may see what is reporting, because that is diagnosis rather than administration.
    /// Creating one is not: it changes what may be written into the site.
    /// </summary>
    [Fact]
    public async Task Somebody_Who_May_Only_Read_Cannot_Create_A_Key()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Viewer);

        using (browser)
        {
            var listing = await browser.GetAsync(Keys(site.Id));
            var creating = await browser.PostAsync(Keys(site.Id), new CreateServerKeyRequest("Mine"));

            listing.StatusCode.Should().Be(HttpStatusCode.OK);
            creating.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task A_Withdrawn_Key_Leaves_The_List()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Owner);

        using (browser)
        {
            var issued = await IssueAsync(browser, site.Id, "Retiring");

            var removed = await browser.DeleteAsync($"{Keys(site.Id)}/{issued.Key.Id}");
            var again = await browser.DeleteAsync($"{Keys(site.Id)}/{issued.Key.Id}");

            removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
            again.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await ListAsync(browser, site.Id)).Should().BeEmpty();
        }
    }

    /// <summary>
    /// Naming another site's key exactly must not remove it, and must not reveal that it was
    /// ever real.
    /// </summary>
    [Fact]
    public async Task A_Key_On_Another_Site_Cannot_Be_Reached_By_Naming_It()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        var owner = await SignedInAsync(theirs.Id, SiteRole.Owner);
        ServerKeySummary theirKey;

        using (owner)
        {
            theirKey = (await IssueAsync(owner, theirs.Id, "Theirs")).Key;
        }

        var intruder = await SignedInAsync(mine.Id, SiteRole.Owner);

        using (intruder)
        {
            var byTheirSite = await intruder.DeleteAsync($"{Keys(theirs.Id)}/{theirKey.Id}");
            var byMySite = await intruder.DeleteAsync($"{Keys(mine.Id)}/{theirKey.Id}");

            byTheirSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
            byMySite.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task A_Site_Somebody_Has_No_Role_On_Is_Answered_As_Though_It_Did_Not_Exist()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(mine.Id, SiteRole.Owner);

        using (browser)
        {
            var response = await browser.GetAsync(Keys(theirs.Id));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Nobody_Signed_In_Cannot_Create_A_Key()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        using var browser = await Browser.OpenAsync(stack);

        var response = await browser.PostAsync(Keys(site.Id), new CreateServerKeyRequest("Mine"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A request another site caused a signed-in person's browser to make carries their cookie,
    /// so the cookie alone cannot be what authorises creating a key.
    /// </summary>
    [Fact]
    public async Task A_Request_That_Cannot_Prove_Where_It_Came_From_Is_Refused()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var browser = await SignedInAsync(site.Id, SiteRole.Owner);

        using (browser)
        {
            var response = await browser.PostWithoutProofAsync(
                Keys(site.Id),
                new CreateServerKeyRequest("Forged"));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    private static string Keys(Guid siteId) => $"/api/sites/{siteId}/server-keys";

    private static async Task<IssuedServerKey> IssueAsync(Browser browser, Guid siteId, string name)
    {
        var response = await browser.PostAsync(Keys(siteId), new CreateServerKeyRequest(name));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var issued = await response.Content.ReadFromJsonAsync<IssuedServerKey>(Cancellation.Token);

        issued.Should().NotBeNull();

        return issued;
    }

    private static async Task<IReadOnlyList<ServerKeySummary>> ListAsync(Browser browser, Guid siteId)
    {
        var response = await browser.GetAsync(Keys(siteId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<ServerKeySummary>>(Cancellation.Token);

        listed.Should().NotBeNull();

        return listed;
    }

    private async Task<Browser> SignedInAsync(Guid siteId, SiteRole role)
    {
        var address = $"keys-{Guid.NewGuid():n}@example.com";
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

    private static string Domain() => $"keys-{Guid.NewGuid():n}.example";
}
