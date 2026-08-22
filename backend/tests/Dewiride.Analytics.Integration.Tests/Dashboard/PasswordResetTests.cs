using System.Net;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Api.Endpoints;
using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Proves what somebody who has forgotten their password can and cannot do.
/// </summary>
/// <remarks>
/// Two things are being held at once. The way back in has to work — an owner locked out of their
/// own analytics with no way to return is the failure this exists to prevent — and asking for it
/// must say nothing whatever about who has an account here, because the form is open to anyone
/// who can reach the installation.
/// </remarks>
/// <param name="stack">The running stack, whose stores this install shares.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class PasswordResetTests(AnalyticsStackFixture stack)
{
    private const string Ask = "/api/password-reset";
    private const string Complete = "/api/password-reset/complete";

    [Fact]
    public async Task Asking_For_A_Link_Is_Accepted()
    {
        await using var install = MailboxInstall.Start(stack);
        var address = await RegisteredAsync();
        using var browser = await Browser.OpenAsync(install);

        var response = await browser.PostAsync(Ask, new ForgotPasswordRequest { EmailAddress = address });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        install.Mailbox.LastTo(address).Should().NotBeNull();
    }

    /// <summary>
    /// The one property this endpoint exists to have: an address with an account and one without
    /// are answered identically, so the form cannot be used to find out who has an account here.
    /// </summary>
    [Fact]
    public async Task An_Address_Nobody_Has_Registered_Is_Answered_Exactly_Like_One_That_Is_In_Use()
    {
        await using var install = MailboxInstall.Start(stack);
        var registered = await RegisteredAsync();
        var stranger = SignedIn.Address();
        using var browser = await Browser.OpenAsync(install);

        var known = await browser.PostAsync(Ask, new ForgotPasswordRequest { EmailAddress = registered });
        var unknown = await browser.PostAsync(Ask, new ForgotPasswordRequest { EmailAddress = stranger });

        unknown.StatusCode.Should().Be(known.StatusCode);
        (await unknown.Content.ReadAsStringAsync(Cancellation.Token))
            .Should().Be(await known.Content.ReadAsStringAsync(Cancellation.Token));

        install.Mailbox.CountTo(stranger).Should().Be(0);
    }

    [Fact]
    public async Task The_Message_Points_At_The_Address_This_Installation_Is_Published_On()
    {
        await using var install = MailboxInstall.Start(stack);
        var address = await RegisteredAsync();
        using var browser = await Browser.OpenAsync(install);

        await browser.PostAsync(Ask, new ForgotPasswordRequest { EmailAddress = address });

        var message = install.Mailbox.LastTo(address);

        message.Should().NotBeNull();

        var link = ResetLink.In(message);

        link.GetLeftPart(UriPartial.Authority).Should().Be(MailboxInstall.PublicAddress);
        link.AbsolutePath.Should().Be("/app/reset-password");
        ResetLink.AddressIn(message).Should().Be(address);
        // Read back the way a mail client would. The address carries two values, so the ampersand
        // between them is escaped in the HTML — as it has to be, or the link breaks halfway.
        WebUtility.HtmlDecode(message.Html).Should().Contain(link.AbsoluteUri);
    }

    [Fact]
    public async Task A_Link_Sets_A_New_Password_And_Retires_The_Old_One()
    {
        await using var install = MailboxInstall.Start(stack);
        var address = await RegisteredAsync();
        using var browser = await Browser.OpenAsync(install);

        var response = await browser.PostAsync(Complete, await LinkFollowedAsync(install, browser, address));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await SigningInAsync(install, address, Passwords.Replacement))
            .Should().Be(HttpStatusCode.OK);
        (await SigningInAsync(install, address, Passwords.Acceptable))
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Setting a password rotates the account's security stamp, and the stamp is sealed inside the
    /// token — so the link stops working without anything having had to record that it was used.
    /// </summary>
    [Fact]
    public async Task A_Link_Works_Once()
    {
        await using var install = MailboxInstall.Start(stack);
        var address = await RegisteredAsync();
        using var browser = await Browser.OpenAsync(install);

        var followed = await LinkFollowedAsync(install, browser, address);

        (await browser.PostAsync(Complete, followed)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var again = await browser.PostAsync(Complete, followed);

        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(again))
            .Should().Contain(AccountEndpoints.ResetLinkNotUsableCode);
    }

    [Fact]
    public async Task A_Link_Meant_For_Somebody_Else_Changes_Nothing()
    {
        await using var install = MailboxInstall.Start(stack);
        var theirs = await RegisteredAsync();
        var mine = await RegisteredAsync();
        using var browser = await Browser.OpenAsync(install);

        var followed = await LinkFollowedAsync(install, browser, theirs);

        var response = await browser.PostAsync(Complete, followed with { EmailAddress = mine });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SigningInAsync(install, mine, Passwords.Acceptable)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Made_Up_Link_Is_Refused_Without_Saying_Why()
    {
        await using var install = MailboxInstall.Start(stack);
        var address = await RegisteredAsync();
        using var browser = await Browser.OpenAsync(install);

        var response = await browser.PostAsync(
            Complete,
            new ResetPasswordRequest
            {
                EmailAddress = address,
                Token = "not-a-token",
                Password = Passwords.Replacement,
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(response))
            .Should().Equal(AccountEndpoints.ResetLinkNotUsableCode);
    }

    /// <summary>
    /// A link sent to an address that has no account is answered exactly like an expired one, so
    /// following it says nothing about whether the address is registered.
    /// </summary>
    [Fact]
    public async Task A_Link_For_An_Address_With_No_Account_Is_Answered_Like_An_Expired_One()
    {
        await using var install = MailboxInstall.Start(stack);
        var address = await RegisteredAsync();
        using var browser = await Browser.OpenAsync(install);

        var followed = await LinkFollowedAsync(install, browser, address);

        var stranger = await browser.PostAsync(
            Complete,
            followed with { EmailAddress = SignedIn.Address() });
        var expired = await browser.PostAsync(
            Complete,
            followed with { Token = "not-a-token" });

        stranger.StatusCode.Should().Be(expired.StatusCode);
        (await Refusal.ReasonsOfAsync(stranger))
            .Should().Equal(await Refusal.ReasonsOfAsync(expired));
    }

    [Fact]
    public async Task A_Password_Anyone_Could_Guess_Is_Refused_And_The_Old_One_Still_Works()
    {
        await using var install = MailboxInstall.Start(stack);
        var address = await RegisteredAsync();
        using var browser = await Browser.OpenAsync(install);

        var followed = await LinkFollowedAsync(install, browser, address);

        var response = await browser.PostAsync(
            Complete,
            followed with { Password = "aaaaaaaaaaaaaaaaaa" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(response))
            .Should().Contain(PredictablePasswordValidator.ErrorCode);
        (await SigningInAsync(install, address, Passwords.Acceptable)).Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Somebody who guessed at their own password until the account was paused is exactly the
    /// person who then asks for a reset, so the pause has to end with it.
    /// </summary>
    [Fact]
    public async Task An_Account_Paused_By_Failed_Attempts_Can_Sign_In_Again_After_A_Reset()
    {
        await using var install = MailboxInstall.Start(stack);
        var address = await RegisteredAsync();
        using var browser = await Browser.OpenAsync(install);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await SigningInAsync(install, address, "not the password at all");
        }

        (await SigningInAsync(install, address, Passwords.Acceptable))
            .Should().Be(HttpStatusCode.Unauthorized);

        var followed = await LinkFollowedAsync(install, browser, address);

        (await browser.PostAsync(Complete, followed)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SigningInAsync(install, address, Passwords.Replacement)).Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Receiving the link proves the mailbox, which is the whole of what confirming an address
    /// attests. Leaving it unconfirmed would leave somebody who took this route rather than the
    /// confirmation link unable to sign in with the password they had just chosen.
    /// </summary>
    [Fact]
    public async Task A_Completed_Reset_Confirms_The_Address_It_Was_Sent_To()
    {
        await using var install = MailboxInstall.Start(stack);
        var address = await RegisteredAsync();
        using var browser = await Browser.OpenAsync(install);

        await using (var before = install.Services.CreateAsyncScope())
        {
            var database = before.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var user = await database.Users
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Email == address, Cancellation.Token);

            user.EmailConfirmed.Should().BeFalse("the suite creates accounts that never confirmed one");
        }

        var followed = await LinkFollowedAsync(install, browser, address);

        (await browser.PostAsync(Complete, followed)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var after = install.Services.CreateAsyncScope();
        var confirmed = await after.ServiceProvider
            .GetRequiredService<ControlPlaneDbContext>()
            .Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Email == address, Cancellation.Token);

        confirmed.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task A_Reset_That_Cannot_Prove_Where_It_Came_From_Changes_Nothing()
    {
        await using var install = MailboxInstall.Start(stack);
        var address = await RegisteredAsync();
        using var browser = await Browser.OpenAsync(install);

        var followed = await LinkFollowedAsync(install, browser, address);

        var response = await browser.PostWithoutProofAsync(Complete, followed);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SigningInAsync(install, address, Passwords.Acceptable)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Asking_Without_An_Address_Is_Refused()
    {
        await using var install = MailboxInstall.Start(stack);
        using var browser = await Browser.OpenAsync(install);

        var response = await browser.PostAsync(Ask, new ForgotPasswordRequest { EmailAddress = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Asking_For_A_Link_Without_Proof_Of_Origin_Sends_Nothing()
    {
        await using var install = MailboxInstall.Start(stack);
        var address = await RegisteredAsync();
        using var browser = await Browser.OpenAsync(install);

        var response = await browser.PostWithoutProofAsync(
            Ask,
            new ForgotPasswordRequest { EmailAddress = address });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        install.Mailbox.CountTo(address).Should().Be(0);
    }

    /// <summary>Creates an account nothing else in the run will touch.</summary>
    private async Task<string> RegisteredAsync()
    {
        var address = SignedIn.Address();
        var (created, _) = await ControlPlaneSeed.AddAccountAsync(stack, address, Passwords.Acceptable);

        created.Succeeded.Should().BeTrue();

        return address;
    }

    /// <summary>
    /// Asks for a link and reads back exactly what a browser would send after following it.
    /// </summary>
    private static async Task<ResetPasswordRequest> LinkFollowedAsync(
        MailboxInstall install,
        Browser browser,
        string address)
    {
        var asked = await browser.PostAsync(Ask, new ForgotPasswordRequest { EmailAddress = address });

        asked.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var message = install.Mailbox.LastTo(address);

        message.Should().NotBeNull();

        return new ResetPasswordRequest
        {
            EmailAddress = ResetLink.AddressIn(message),
            Token = ResetLink.TokenIn(message),
            Password = Passwords.Replacement,
        };
    }

    /// <summary>Attempts a sign-in from a browser that has done nothing else.</summary>
    private static async Task<HttpStatusCode> SigningInAsync(
        MailboxInstall install,
        string address,
        string password)
    {
        using var browser = await Browser.OpenAsync(install);

        var response = await browser.PostAsync(
            "/api/session",
            new SignInRequest { EmailAddress = address, Password = password });

        return response.StatusCode;
    }
}
