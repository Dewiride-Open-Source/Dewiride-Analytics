using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Application.Dashboard;
using Dewiride.Analytics.Application.Notifications;
using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Infrastructure.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// Sends somebody a way back into their account, and lets them use it once.
/// </summary>
/// <remarks>
/// <para>
/// The token is issued and checked by the account store, which seals the account's identifier,
/// the purpose, the moment it was made and the account's security stamp into a value only this
/// installation's keys can open. Setting a password rotates that stamp, which is what makes a
/// used link worthless afterwards without anything having to record that it was used.
/// </para>
/// <para>
/// Those keys live in PostgreSQL rather than on a container's filesystem. That was decided for
/// sign-in cookies, and password reset depends on it just as much: keys that changed when the
/// engine restarted would break every outstanding link, and a second copy of the engine could not
/// open a link the first one issued.
/// </para>
/// </remarks>
/// <param name="accounts">Account store.</param>
/// <param name="email">How messages leave the building.</param>
/// <param name="dashboard">Where the screens are published, for the link.</param>
/// <param name="logger">Log.</param>
public sealed class PasswordReset(
    UserManager<ApplicationUser> accounts,
    IEmailSender email,
    IOptions<DashboardOptions> dashboard,
    ILogger<PasswordReset> logger) : IPasswordReset
{
    /// <summary>
    /// The screen the link opens.
    /// </summary>
    /// <remarks>
    /// The one address in the engine that names a screen. It has to be here: a link has to point
    /// somewhere, and the alternative — reading the hostname off the request that asked for it —
    /// is how a reset link ends up aimed at a server somebody else controls.
    /// </remarks>
    private const string ResetScreen = "app/reset-password";

    /// <summary>
    /// The code the account store reports when a link is expired, spent or forged.
    /// </summary>
    /// <remarks>
    /// Taken from the describer this installation is actually using rather than written out, so
    /// that it stays correct if the describer is ever replaced.
    /// </remarks>
    private static string InvalidTokenCode { get; } = new IdentityErrorDescriber().InvalidToken().Code;

    /// <inheritdoc />
    public async Task BeginAsync(string emailAddress, CancellationToken cancellationToken)
    {
        var user = await accounts.FindByEmailAsync(emailAddress).ConfigureAwait(false);

        if (user?.Email is null)
        {
            // Nothing is sent and nothing is said. The caller is answered identically either way,
            // so an address nobody has registered is indistinguishable from one that is in use.
            PasswordResetLog.NobodyToSendTo(logger);

            return;
        }

        var token = await accounts.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
        var message = PasswordResetMessage.For(user.Email, user.DisplayName, LinkFor(user.Email, token));

        await SendQuietlyAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PasswordResetOutcome> CompleteAsync(
        PasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await accounts.FindByEmailAsync(request.EmailAddress).ConfigureAwait(false);

        if (user is null || !AccountLinks.TryRead(request.Token, out var token))
        {
            return PasswordResetOutcome.LinkNotUsable;
        }

        var outcome = await accounts
            .ResetPasswordAsync(user, token, request.Password)
            .ConfigureAwait(false);

        if (!outcome.Succeeded)
        {
            return Refusal(outcome);
        }

        // Somebody who has just proved they hold the mailbox should be able to sign in at once.
        // Without this they would meet the lockout their own forgotten attempts caused, and a new
        // password that is refused reads as a reset that did not work.
        //
        // Receiving the link is also the whole of what confirming an address attests, so an
        // account that had never confirmed one is confirmed by it. Both writes are saved by the
        // calls below, which update the account.
        user.EmailConfirmed = true;

        await accounts.SetLockoutEndDateAsync(user, null).ConfigureAwait(false);
        await accounts.ResetAccessFailedCountAsync(user).ConfigureAwait(false);

        PasswordResetLog.Completed(logger);

        return PasswordResetOutcome.Reset;
    }

    /// <summary>
    /// Turns a refused attempt into an answer, keeping a bad link and a bad password apart.
    /// </summary>
    /// <remarks>
    /// The account store checks the link first and stops there, so the two can never both be the
    /// reason. A bad link is reported without its reasons: at that point nothing is known about
    /// whoever is holding it.
    /// </remarks>
    private static PasswordResetOutcome Refusal(IdentityResult outcome) =>
        outcome.Errors.Any(error => string.Equals(error.Code, InvalidTokenCode, StringComparison.Ordinal))
            ? PasswordResetOutcome.LinkNotUsable
            : PasswordResetOutcome.PasswordRejected(
                [.. outcome.Errors.Select(error => new AccountProblem(error.Code, error.Description))]);

    /// <summary>
    /// Sends the message, and says so in the log when nobody could be reached.
    /// </summary>
    /// <remarks>
    /// A failure must not reach the caller. The endpoint above answers a request for a link
    /// identically whether or not the address belongs to an account, and a fault that escaped
    /// here would be answered differently — which would say, to anyone who cared to look, which
    /// addresses have accounts on this installation.
    /// </remarks>
    private async Task SendQuietlyAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (!await QuietSend.TryAsync(email, message, logger, cancellationToken).ConfigureAwait(false))
        {
            PasswordResetLog.CouldNotSend(logger);
        }
    }

    /// <summary>
    /// Builds the address the message points at.
    /// </summary>
    /// <remarks>
    /// An installation that sends no mail has no address configured, and the message ends up in
    /// the log rather than in a mailbox. What is written then is relative, which is still the
    /// whole of what somebody needs to put after their own address.
    /// </remarks>
    private string LinkFor(string emailAddress, string token) =>
        AccountLinks.To(dashboard.Value.PublishedAt, ResetScreen, emailAddress, token);
}

/// <summary>
/// What asking for and using a way back in records.
/// </summary>
/// <remarks>
/// No address is written, on either path. The whole point of the endpoint above is that nothing
/// distinguishes an address that has an account from one that does not, and a log that named them
/// would be the list the endpoint refuses to produce.
/// </remarks>
internal static partial class PasswordResetLog
{
    [LoggerMessage(
        EventId = 3301,
        Level = LogLevel.Information,
        Message = "Somebody asked for a password reset for an address that has no account here. "
            + "Nothing was sent.")]
    public static partial void NobodyToSendTo(ILogger logger);

    [LoggerMessage(
        EventId = 3302,
        Level = LogLevel.Error,
        Message = "A password reset was asked for, but the message could not be handed to a mail "
            + "server. Whoever asked has been told a link is on its way and will not receive one.")]
    public static partial void CouldNotSend(ILogger logger);

    [LoggerMessage(
        EventId = 3303,
        Level = LogLevel.Information,
        Message = "A password was reset from a link.")]
    public static partial void Completed(ILogger logger);
}
