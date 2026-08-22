using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Covers asking somebody to join an account and letting them take it up.
/// </summary>
/// <remarks>
/// <para>
/// Runs against a host whose messages are kept rather than sent, because what is worth proving is
/// what arrives in a mailbox and what happens when somebody follows it. Everything else about the
/// installation — the account store, the password rules, the keys — is the product's own.
/// </para>
/// <para>
/// Two rules carry the weight. Nothing exists in the invited person's name until they follow the
/// link, so naming an address cannot claim it; and every link that will not do is answered
/// identically, so a spent one cannot be told from one that was never issued.
/// </para>
/// </remarks>
/// <param name="stack">The running stack, whose stores this host shares.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class InvitationTests(AnalyticsStackFixture stack)
{
    private const string Organization = "/api/organization";
    private const string Invitations = "/api/organization/invitations";
    private const string Preview = "/api/invitations/preview";
    private const string Accept = "/api/invitations/accept";

    [Fact]
    public async Task Inviting_Somebody_Sends_Them_A_Link_And_Creates_Nothing_In_Their_Name()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);
        var invited = SignedIn.Address();

        using var response = await InviteAsync(account.Owner, invited, "member").ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        install.Mailbox.CountTo(invited).Should().Be(1);

        // Nothing in their name yet. If an account had been created, they could sign in with a
        // password nobody chose and the address would be claimed by somebody who does not hold it.
        using var stillOutside = await Browser.OpenAsync(install).ConfigureAwait(true);
        using var refused = await stillOutside
            .PostAsync("/api/session", new SignInRequest { EmailAddress = invited, Password = Passwords.Acceptable })
            .ConfigureAwait(true);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_Invitation_Waiting_To_Be_Taken_Up_Is_Listed_For_The_Owner()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);
        var invited = SignedIn.Address();

        using var sent = await InviteAsync(account.Owner, invited, "admin").ConfigureAwait(true);
        sent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var described = await ReadAccountAsync(account.Owner).ConfigureAwait(true);

        described.Invitations.Should().ContainSingle();
        described.Invitations[0].EmailAddress.Should().Be(invited);
        described.Invitations[0].Role.Should().Be("admin");
    }

    /// <summary>
    /// Sending a second invitation is how one is sent again. A second row would show the same
    /// address twice and leave the older link working.
    /// </summary>
    [Fact]
    public async Task Asking_The_Same_Person_Again_Replaces_The_Invitation_And_Its_Link()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);
        var invited = SignedIn.Address();

        using var first = await InviteAsync(account.Owner, invited, "member").ConfigureAwait(true);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stale = TokenSentTo(install, invited);

        using var again = await InviteAsync(account.Owner, invited, "owner").ConfigureAwait(true);
        again.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var described = await ReadAccountAsync(account.Owner).ConfigureAwait(true);

        described.Invitations.Should().ContainSingle();
        described.Invitations[0].Role.Should().Be("owner");

        using var browser = await Browser.OpenAsync(install).ConfigureAwait(true);
        using var spent = await browser
            .PostAsync(Preview, new InvitationTokenRequest { Token = stale })
            .ConfigureAwait(true);

        spent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Somebody_Already_On_The_Account_Cannot_Be_Invited_To_It()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);

        using var response = await InviteAsync(account.Owner, account.OwnerAddress, "member")
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true))
            .Should().Contain(OrganizationEndpointCodes.AlreadyHere);
    }

    [Fact]
    public async Task An_Address_Nothing_Could_Be_Sent_To_Is_Refused()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);

        using var response = await InviteAsync(account.Owner, "not-an-address", "member")
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true))
            .Should().Contain(OrganizationEndpointCodes.AddressUnusable);
    }

    [Fact]
    public async Task Somebody_With_No_Account_Here_Chooses_A_Password_And_Is_Signed_In()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);
        var invited = SignedIn.Address();

        using var sent = await InviteAsync(account.Owner, invited, "member").ConfigureAwait(true);
        sent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var browser = await Browser.OpenAsync(install).ConfigureAwait(true);
        var token = TokenSentTo(install, invited);

        using var previewed = await browser
            .PostAsync(Preview, new InvitationTokenRequest { Token = token })
            .ConfigureAwait(true);

        previewed.StatusCode.Should().Be(HttpStatusCode.OK);

        var preview = await previewed.Content
            .ReadFromJsonAsync<InvitationPreviewResponse>(Cancellation.Token)
            .ConfigureAwait(true);

        preview.Should().NotBeNull();
        preview.NeedsAccount.Should().BeTrue();
        preview.EmailAddress.Should().Be(invited);

        using var joined = await browser
            .PostAsync(
                Accept,
                new AcceptInvitationRequest
                {
                    Token = token,
                    DisplayName = "Newly invited",
                    Password = Passwords.Acceptable,
                })
            .ConfigureAwait(true);

        joined.StatusCode.Should().Be(HttpStatusCode.OK);

        var outcome = await joined.Content
            .ReadFromJsonAsync<JoinResponse>(Cancellation.Token)
            .ConfigureAwait(true);

        outcome.Should().NotBeNull();
        outcome.SignedIn.Should().BeTrue();
        outcome.User!.DisplayName.Should().Be("Newly invited");

        var described = await ReadAccountAsync(account.Owner).ConfigureAwait(true);

        described.People.Should().Contain(person => person.EmailAddress == invited);
        described.Invitations.Should().BeEmpty();
    }

    /// <summary>
    /// The address is taken from the invitation rather than from the request, so somebody holding
    /// one cannot use it to create an account under a different address.
    /// </summary>
    [Fact]
    public async Task The_Account_Created_Belongs_To_The_Address_That_Was_Invited()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);
        var invited = SignedIn.Address();

        using var sent = await InviteAsync(account.Owner, invited, "member").ConfigureAwait(true);
        sent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var browser = await Browser.OpenAsync(install).ConfigureAwait(true);

        using var joined = await browser
            .PostAsync(
                Accept,
                new AcceptInvitationRequest
                {
                    Token = TokenSentTo(install, invited),
                    Password = Passwords.Acceptable,
                })
            .ConfigureAwait(true);

        joined.StatusCode.Should().Be(HttpStatusCode.OK);

        var outcome = await joined.Content
            .ReadFromJsonAsync<JoinResponse>(Cancellation.Token)
            .ConfigureAwait(true);

        outcome!.User!.EmailAddress.Should().Be(invited);
    }

    [Fact]
    public async Task Somebody_Who_Already_Has_An_Account_Joins_Without_Choosing_Anything()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);

        var address = SignedIn.Address();
        var (created, existing) = await ControlPlaneSeed
            .AddAccountAsync(stack, address, Passwords.Acceptable)
            .ConfigureAwait(true);

        created.Succeeded.Should().BeTrue();

        using var sent = await InviteAsync(account.Owner, address, "admin").ConfigureAwait(true);
        sent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var browser = await Browser.OpenAsync(install).ConfigureAwait(true);
        var token = TokenSentTo(install, address);

        using var previewed = await browser
            .PostAsync(Preview, new InvitationTokenRequest { Token = token })
            .ConfigureAwait(true);

        var preview = await previewed.Content
            .ReadFromJsonAsync<InvitationPreviewResponse>(Cancellation.Token)
            .ConfigureAwait(true);

        preview!.NeedsAccount.Should().BeFalse();

        using var joined = await browser
            .PostAsync(Accept, new AcceptInvitationRequest { Token = token })
            .ConfigureAwait(true);

        joined.StatusCode.Should().Be(HttpStatusCode.OK);

        var outcome = await joined.Content
            .ReadFromJsonAsync<JoinResponse>(Cancellation.Token)
            .ConfigureAwait(true);

        outcome!.SignedIn.Should().BeFalse();

        var described = await ReadAccountAsync(account.Owner).ConfigureAwait(true);

        described.People.Single(person => person.Id == existing.Id).Role.Should().Be("admin");
    }

    [Fact]
    public async Task An_Invitation_Cannot_Be_Taken_Up_Twice()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);
        var invited = SignedIn.Address();

        using var sent = await InviteAsync(account.Owner, invited, "member").ConfigureAwait(true);
        sent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var token = TokenSentTo(install, invited);

        using var first = await Browser.OpenAsync(install).ConfigureAwait(true);
        using var joined = await first
            .PostAsync(Accept, new AcceptInvitationRequest { Token = token, Password = Passwords.Acceptable })
            .ConfigureAwait(true);

        joined.StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = await Browser.OpenAsync(install).ConfigureAwait(true);
        using var again = await second
            .PostAsync(Accept, new AcceptInvitationRequest { Token = token, Password = Passwords.Acceptable })
            .ConfigureAwait(true);

        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(again).ConfigureAwait(true))
            .Should().Contain(OrganizationEndpointCodes.LinkNotUsable);
    }

    [Fact]
    public async Task A_Withdrawn_Invitation_Stops_Working()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);
        var invited = SignedIn.Address();

        using var sent = await InviteAsync(account.Owner, invited, "member").ConfigureAwait(true);
        sent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var token = TokenSentTo(install, invited);
        var described = await ReadAccountAsync(account.Owner).ConfigureAwait(true);

        using var revoked = await account.Owner
            .DeleteAsync($"{Invitations}/{described.Invitations[0].Id}")
            .ConfigureAwait(true);

        revoked.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var browser = await Browser.OpenAsync(install).ConfigureAwait(true);
        using var refused = await browser
            .PostAsync(Accept, new AcceptInvitationRequest { Token = token, Password = Passwords.Acceptable })
            .ConfigureAwait(true);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAccountAsync(account.Owner).ConfigureAwait(true)).Invitations.Should().BeEmpty();
    }

    /// <summary>
    /// A link that was never issued and one that has been spent have to be indistinguishable.
    /// Telling them apart would say whether somebody else had already used it.
    /// </summary>
    [Fact]
    public async Task A_Link_That_Was_Never_One_Of_Ours_Is_Answered_Like_A_Spent_One()
    {
        using var install = MailboxInstall.Start(stack);
        using var browser = await Browser.OpenAsync(install).ConfigureAwait(true);

        using var invented = await browser
            .PostAsync(Preview, new InvitationTokenRequest { Token = "dwi_not-a-real-secret" })
            .ConfigureAwait(true);

        invented.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(invented).ConfigureAwait(true))
            .Should().Contain(OrganizationEndpointCodes.LinkNotUsable);
    }

    [Fact]
    public async Task Joining_Without_Choosing_A_Password_Is_Refused_With_Something_To_Do_About_It()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);
        var invited = SignedIn.Address();

        using var sent = await InviteAsync(account.Owner, invited, "member").ConfigureAwait(true);
        sent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var browser = await Browser.OpenAsync(install).ConfigureAwait(true);
        using var response = await browser
            .PostAsync(Accept, new AcceptInvitationRequest { Token = TokenSentTo(install, invited) })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true))
            .Should().Contain(OrganizationEndpointCodes.DetailsMissing);
    }

    [Fact]
    public async Task A_Password_This_Installation_Will_Not_Take_Is_Refused_With_Its_Reasons()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);
        var invited = SignedIn.Address();

        using var sent = await InviteAsync(account.Owner, invited, "member").ConfigureAwait(true);
        sent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var browser = await Browser.OpenAsync(install).ConfigureAwait(true);
        using var response = await browser
            .PostAsync(
                Accept,
                new AcceptInvitationRequest { Token = TokenSentTo(install, invited), Password = "short" })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Somebody_Who_Does_Not_Own_The_Account_Cannot_Invite_Anybody_To_It()
    {
        using var install = MailboxInstall.Start(stack);
        using var account = await AccountWithOwnerAsync(install).ConfigureAwait(true);

        var address = SignedIn.Address();
        var (created, member) = await ControlPlaneSeed
            .AddAccountAsync(stack, address, Passwords.Acceptable)
            .ConfigureAwait(true);

        created.Succeeded.Should().BeTrue();

        await ControlPlaneSeed
            .GrantInOrganizationAsync(stack, account.OrganizationId, member.Id, OrganizationRole.Admin)
            .ConfigureAwait(true);

        using var browser = await SignedInOn(install, address).ConfigureAwait(true);

        using var response = await InviteAsync(browser, SignedIn.Address(), "member").ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> InviteAsync(Browser browser, string address, string role) =>
        browser.PostAsync(
            Invitations,
            new InvitePersonRequest { EmailAddress = address, Role = role });

    /// <summary>The secret from the most recent link sent to an address.</summary>
    private static string TokenSentTo(MailboxInstall install, string address)
    {
        var message = install.Mailbox.LastTo(address);

        message.Should().NotBeNull("an invitation has to reach the mailbox it was addressed to");

        return ResetLink.TokenIn(message);
    }

    private static async Task<OrganizationResponse> ReadAccountAsync(Browser browser)
    {
        using var response = await browser.GetAsync(Organization).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var described = await response.Content
            .ReadFromJsonAsync<OrganizationResponse>(Cancellation.Token)
            .ConfigureAwait(false);

        described.Should().NotBeNull();

        return described;
    }

    private static async Task<Browser> SignedInOn(MailboxInstall install, string address)
    {
        var browser = await Browser.OpenAsync(install).ConfigureAwait(false);

        using var response = await browser
            .PostAsync("/api/session", new SignInRequest { EmailAddress = address, Password = Passwords.Acceptable })
            .ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await browser.DescribeAsync().ConfigureAwait(false);

        return browser;
    }

    private async Task<Account> AccountWithOwnerAsync(MailboxInstall install)
    {
        var site = await ControlPlaneSeed
            .AddSiteAsync(stack, domain: $"invite-{Guid.NewGuid():n}.example")
            .ConfigureAwait(false);

        var address = SignedIn.Address();
        var (created, owner) = await ControlPlaneSeed
            .AddAccountAsync(stack, address, Passwords.Acceptable)
            .ConfigureAwait(false);

        created.Succeeded.Should().BeTrue();

        await ControlPlaneSeed
            .GrantInOrganizationAsync(stack, site.OrganizationId, owner.Id, OrganizationRole.Owner)
            .ConfigureAwait(false);

        var browser = await SignedInOn(install, address).ConfigureAwait(false);

        return new Account(site.OrganizationId, address, browser);
    }

    private sealed record Account(Guid OrganizationId, string OwnerAddress, Browser Owner) : IDisposable
    {
        public void Dispose() => Owner.Dispose();
    }
}
