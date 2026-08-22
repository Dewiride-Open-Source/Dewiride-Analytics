using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Covers the two things somebody may change about their own account.
/// </summary>
/// <remarks>
/// Nothing here names an account, so what is worth proving is what changing a password does to
/// everything else: the sign-in that made the change carries on, and the old password stops being
/// one anybody can sign in with. That is the whole point of being able to change it in a hurry.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class ProfileTests(AnalyticsStackFixture stack)
{
    private const string Account = "/api/account";
    private const string Password = "/api/account/password";

    private const string Replacement = "sequoia harbour lantern drift";

    [Fact]
    public async Task Somebody_Can_Change_The_Name_They_Are_Shown_Under()
    {
        var address = SignedIn.Address();
        using var browser = await AccountAsync(address).ConfigureAwait(true);

        using var response = await browser
            .PatchAsync(Account, new RenameAccountRequest { DisplayName = "  Ada Lovelace  " })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await response.Content
            .ReadFromJsonAsync<SignedInUser>(Cancellation.Token)
            .ConfigureAwait(true);

        user!.DisplayName.Should().Be("Ada Lovelace");

        var session = await browser.DescribeAsync().ConfigureAwait(true);

        session.User!.DisplayName.Should().Be("Ada Lovelace");
    }

    [Fact]
    public async Task An_Empty_Name_Is_Refused_With_A_Reason_The_Screen_Can_Explain()
    {
        using var browser = await AccountAsync(SignedIn.Address()).ConfigureAwait(true);

        using var response = await browser
            .PatchAsync(Account, new RenameAccountRequest { DisplayName = "   " })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true))
            .Should().Contain(OrganizationEndpointCodes.AccountNameRejected);
    }

    [Fact]
    public async Task Somebody_Can_Replace_Their_Password_And_Sign_In_With_The_New_One()
    {
        var address = SignedIn.Address();
        using var browser = await AccountAsync(address).ConfigureAwait(true);

        using var changed = await browser
            .PutAsync(
                Password,
                new ChangePasswordRequest
                {
                    CurrentPassword = Passwords.Acceptable,
                    NewPassword = Replacement,
                })
            .ConfigureAwait(true);

        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var elsewhere = await Browser.OpenAsync(stack).ConfigureAwait(true);
        using var signedIn = await elsewhere
            .PostAsync("/api/session", new SignInRequest { EmailAddress = address, Password = Replacement })
            .ConfigureAwait(true);

        signedIn.StatusCode.Should().Be(HttpStatusCode.OK);

        using var withTheOldOne = await Browser.OpenAsync(stack).ConfigureAwait(true);
        using var refused = await withTheOldOne
            .PostAsync(
                "/api/session",
                new SignInRequest { EmailAddress = address, Password = Passwords.Acceptable })
            .ConfigureAwait(true);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The reason somebody changes a password in a hurry is that they think it is known. Being
    /// signed out of the screen they did it on, while whoever they were worried about stayed signed
    /// in, would be the wrong way round.
    /// </summary>
    [Fact]
    public async Task Changing_A_Password_Keeps_The_Sign_In_That_Changed_It()
    {
        var address = SignedIn.Address();
        using var browser = await AccountAsync(address).ConfigureAwait(true);

        using var changed = await browser
            .PutAsync(
                Password,
                new ChangePasswordRequest
                {
                    CurrentPassword = Passwords.Acceptable,
                    NewPassword = Replacement,
                })
            .ConfigureAwait(true);

        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var session = await browser.DescribeAsync().ConfigureAwait(true);

        session.User.Should().NotBeNull();
        session.User.EmailAddress.Should().Be(address);
    }

    [Fact]
    public async Task A_Wrong_Current_Password_Changes_Nothing_And_Says_What_To_Do()
    {
        var address = SignedIn.Address();
        using var browser = await AccountAsync(address).ConfigureAwait(true);

        using var response = await browser
            .PutAsync(
                Password,
                new ChangePasswordRequest
                {
                    CurrentPassword = "not the one they use",
                    NewPassword = Replacement,
                })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true))
            .Should().Contain(OrganizationEndpointCodes.CurrentPasswordWrong);

        using var elsewhere = await Browser.OpenAsync(stack).ConfigureAwait(true);
        using var stillWorks = await elsewhere
            .PostAsync(
                "/api/session",
                new SignInRequest { EmailAddress = address, Password = Passwords.Acceptable })
            .ConfigureAwait(true);

        stillWorks.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_New_Password_This_Installation_Will_Not_Take_Is_Refused_With_Its_Reasons()
    {
        using var browser = await AccountAsync(SignedIn.Address()).ConfigureAwait(true);

        using var response = await browser
            .PutAsync(
                Password,
                new ChangePasswordRequest { CurrentPassword = Passwords.Acceptable, NewPassword = "short" })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Nobody_Signed_In_Can_Change_Anything()
    {
        using var browser = await Browser.OpenAsync(stack).ConfigureAwait(true);

        using var response = await browser
            .PatchAsync(Account, new RenameAccountRequest { DisplayName = "Nobody" })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_Change_Without_Proof_Of_Where_It_Came_From_Is_Refused()
    {
        using var browser = await AccountAsync(SignedIn.Address()).ConfigureAwait(true);

        using var response = await browser
            .PatchWithoutProofAsync(Account, new RenameAccountRequest { DisplayName = "Nope" })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// An account with a website, so that it is a person the product would recognise rather than a
    /// row with nothing attached.
    /// </summary>
    private async Task<Browser> AccountAsync(string address)
    {
        var site = await ControlPlaneSeed
            .AddSiteAsync(stack, domain: $"profile-{Guid.NewGuid():n}.example")
            .ConfigureAwait(false);

        var (created, user) = await ControlPlaneSeed
            .AddAccountAsync(stack, address, Passwords.Acceptable)
            .ConfigureAwait(false);

        created.Succeeded.Should().BeTrue();

        await ControlPlaneSeed
            .GrantAsync(stack, site.Id, user.Id, SiteRole.Owner)
            .ConfigureAwait(false);

        return await SignedIn.AsAccountAsync(stack, address).ConfigureAwait(false);
    }
}
