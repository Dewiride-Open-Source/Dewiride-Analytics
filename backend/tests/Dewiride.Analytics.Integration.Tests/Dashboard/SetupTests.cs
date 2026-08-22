using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Accounts;
using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves what happens the first time somebody opens a freshly installed copy.
/// </summary>
/// <remarks>
/// This is the only moment in an install's life when somebody who is not signed in may create an
/// account, so it is the one place where getting it wrong hands the install to a stranger. Each
/// test runs against a control-plane database created for it and never used before.
/// </remarks>
/// <param name="stack">The running stack, whose servers these installs borrow.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SetupTests(AnalyticsStackFixture stack)
{
    private const string Setup = "/api/setup";
    private const string GoodPassword = Passwords.Acceptable;

    [Fact]
    public async Task An_Install_Nobody_Has_Claimed_Says_So_And_Hands_Out_A_Token()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var browser = await Browser.OpenAsync(install);

        var session = await browser.DescribeAsync();

        session.SetupCompleted.Should().BeFalse();
        session.User.Should().BeNull();
        session.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_First_Person_To_Arrive_Becomes_The_Owner_And_Is_Signed_In()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var browser = await Browser.OpenAsync(install);

        var response = await browser.PostAsync(Setup, Details());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await response.Content.ReadFromJsonAsync<SetupResponse>(Cancellation.Token);

        created.Should().NotBeNull();
        created.SiteId.Should().NotBe(Guid.Empty);
        created.User.EmailAddress.Should().Be("owner@example.com");

        var sites = await browser.GetAsync("/api/sites");
        var listed = await sites.Content.ReadFromJsonAsync<IReadOnlyList<SiteSummary>>(Cancellation.Token);

        listed.Should().ContainSingle();
        listed[0].Id.Should().Be(created.SiteId);
        listed[0].Domain.Should().Be("first.example");
        listed[0].Role.Should().Be("owner");
    }

    /// <summary>
    /// The account created on first run owns the organisation as well as the site it named.
    /// </summary>
    /// <remarks>
    /// Both grants are written because they answer different questions: one names a role on a
    /// particular website, the other a standing across everything the account owns. An install
    /// claimed with only the first would produce an owner whose standing nothing recorded.
    /// </remarks>
    [Fact]
    public async Task The_First_Person_Also_Owns_The_Organisation_They_Created()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var browser = await Browser.OpenAsync(install);

        var response = await browser.PostAsync(Setup, Details());
        var created = await response.Content.ReadFromJsonAsync<SetupResponse>(Cancellation.Token);

        created.Should().NotBeNull();

        await using var scope = install.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var site = await database.Sites.AsNoTracking().SingleAsync(Cancellation.Token);
        var standing = await database.OrganizationMemberships
            .AsNoTracking()
            .SingleAsync(Cancellation.Token);

        standing.UserId.Should().Be(created.User.Id);
        standing.OrganizationId.Should().Be(site.OrganizationId);
        standing.Role.Should().Be(OrganizationRole.Owner);
    }

    [Fact]
    public async Task Setting_Up_An_Install_That_Already_Has_An_Owner_Is_Refused()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var first = await Browser.OpenAsync(install);
        using var second = await Browser.OpenAsync(install);

        (await first.PostAsync(Setup, Details())).StatusCode.Should().Be(HttpStatusCode.OK);

        var later = await second.PostAsync(Setup, Details("someone.else@example.com"));

        later.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Two people opening the setup screen at the same moment must not both become the owner.
    /// </summary>
    [Fact]
    public async Task Two_People_Arriving_At_Once_Produce_Exactly_One_Owner()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var first = await Browser.OpenAsync(install);
        using var second = await Browser.OpenAsync(install);

        var attempts = await Task.WhenAll(
            first.PostAsync(Setup, Details("first@example.com")),
            second.PostAsync(Setup, Details("second@example.com")));

        attempts.Select(attempt => attempt.StatusCode)
            .Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
    }

    [Fact]
    public async Task A_Setup_Request_That_Cannot_Prove_Where_It_Came_From_Is_Refused()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var browser = await Browser.OpenAsync(install);

        var response = await browser.PostWithoutProofAsync(Setup, Details());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await browser.DescribeAsync()).SetupCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task A_Password_Anyone_Could_Guess_Creates_Nothing()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var browser = await Browser.OpenAsync(install);

        var response = await browser.PostAsync(Setup, Details() with { Password = "aaaaaaaaaaaaaaaaaa" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodesAsync(response)).Should().Contain(PredictablePasswordValidator.ErrorCode);
        (await browser.DescribeAsync()).SetupCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task A_Time_Zone_This_Machine_Has_Never_Heard_Of_Creates_Nothing()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var browser = await Browser.OpenAsync(install);

        var response = await browser.PostAsync(Setup, Details() with { TimeZoneId = "Mars/Olympus_Mons" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodesAsync(response)).Should().Contain(Installation.SiteRejectedCode);
        (await browser.DescribeAsync()).SetupCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Setup_Details_With_A_Missing_Answer_Are_Refused()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var browser = await Browser.OpenAsync(install);

        var response = await browser.PostAsync(Setup, Details() with { SiteDomain = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await browser.DescribeAsync()).SetupCompleted.Should().BeFalse();
    }

    private static SetupRequest Details(string emailAddress = "owner@example.com") => new()
    {
        EmailAddress = emailAddress,
        Password = GoodPassword,
        DisplayName = "The Owner",
        OrganizationName = "First Organisation",
        SiteDomain = "first.example",
        TimeZoneId = "Europe/London",
    };

    /// <summary>
    /// Reads the reasons a refusal carried, which is what lets the interface explain itself.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ProblemCodesAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(Cancellation.Token));

        return
        [
            .. document.RootElement.GetProperty("problems")
                .EnumerateArray()
                .Select(problem => problem.GetProperty("code").GetString() ?? string.Empty),
        ];
    }
}
