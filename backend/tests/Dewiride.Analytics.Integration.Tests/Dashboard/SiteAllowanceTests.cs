using System.Net;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves what happens when an account has no room left for another website.
/// </summary>
/// <remarks>
/// An installation somebody runs themselves has no such limit, so this is the open-source half of
/// something only the hosted service reaches: the endpoint takes an answer from the allowance and
/// turns it into a refusal a person can read, in words that say nothing about plans — those belong
/// to the edition that sells the room and are said on its own screen, where they can be said
/// accurately.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SiteAllowanceTests(AnalyticsStackFixture stack)
{
    private const string Sites = "/api/sites";

    /// <summary>The code the dashboard looks its own sentence up by.</summary>
    private const string LimitReached = "SiteLimitReached";

    [Fact]
    public async Task An_Account_With_No_Room_Left_Is_Refused_With_A_Reason_It_Can_Read()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        await ControlPlaneSeed.AddAccountAsync(stack, Address(), Passwords.Acceptable);

        await using var install = FullInstall.Start(stack);
        using var browser = await SignedInAsync(install, site.Id);

        var response = await AddAsync(browser, "another.example.com");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Refusal.ReasonsOfAsync(response)).Should().Equal(LimitReached);
    }

    /// <summary>
    /// Nothing is written on the way to saying no. A site row left behind by a refused request
    /// would be one nobody could see and nobody could remove, and it would count against the
    /// allowance that refused it.
    /// </summary>
    [Fact]
    public async Task Nothing_Is_Written_When_A_Website_Is_Refused()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var refused = Domain();

        await using var install = FullInstall.Start(stack);
        using var browser = await SignedInAsync(install, site.Id);

        await AddAsync(browser, refused);

        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        (await database.Sites.AnyAsync(one => one.Domain == refused, Cancellation.Token))
            .Should().BeFalse();
    }

    /// <summary>
    /// Adding a website already on the list is answered as being already there, whatever room is
    /// left. The answer somebody needs is the one about the website they named.
    /// </summary>
    [Fact]
    public async Task A_Website_Already_Here_Is_Still_Answered_As_Being_Already_Here()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await using var install = FullInstall.Start(stack);
        using var browser = await SignedInAsync(install, site.Id);

        var response = await AddAsync(browser, site.Domain);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Refusal.ReasonsOfAsync(response)).Should().Equal("SiteAlreadyMeasured");
    }

    /// <summary>
    /// An address the product cannot store is answered as that, not as an allowance that is full.
    /// Being told the account is out of room when what was typed was a mistake would send somebody
    /// to the wrong screen.
    /// </summary>
    [Fact]
    public async Task An_Address_That_Cannot_Be_Used_Is_Still_Answered_As_That()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await using var install = FullInstall.Start(stack);
        using var browser = await SignedInAsync(install, site.Id);

        var response = await AddAsync(browser, new string('a', 300) + ".example.com");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(response)).Should().Equal("SiteDetailsRejected");
    }

    private static Task<HttpResponseMessage> AddAsync(Browser browser, string domain) =>
        browser.PostAsync(Sites, new AddSiteRequest
        {
            Domain = domain,
            TimeZoneId = "Asia/Kolkata",
        });

    /// <summary>
    /// Signs somebody in on the install with no room, owning a site written through the stack the
    /// two hosts share.
    /// </summary>
    private async Task<Browser> SignedInAsync(FullInstall install, Guid siteId)
    {
        var address = Address();
        var (created, account) = await ControlPlaneSeed
            .AddAccountAsync(stack, address, Passwords.Acceptable);

        created.Succeeded.Should().BeTrue();
        await ControlPlaneSeed.GrantAsync(stack, siteId, account.Id, SiteRole.Owner);

        var browser = await Browser.OpenAsync(install);
        var response = await browser.PostAsync(
            "/api/session",
            new SignInRequest { EmailAddress = address, Password = Passwords.Acceptable });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await browser.DescribeAsync();

        return browser;
    }

    private static string Address() => $"room-{Guid.NewGuid():n}@example.com";

    private static string Domain() => $"room-{Guid.NewGuid():n}.example";
}
