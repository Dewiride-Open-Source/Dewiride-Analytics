using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// Changes the name somebody is shown under, and their password.
/// </summary>
/// <param name="accounts">Account store.</param>
/// <param name="logger">Log.</param>
public sealed class AccountProfile(
    UserManager<ApplicationUser> accounts,
    ILogger<AccountProfile> logger) : IAccountProfile
{
    /// <summary>Longest name an account may be shown under.</summary>
    private const int MaxDisplayNameLength = 100;

    /// <summary>
    /// The code the account store reports when the current password does not match.
    /// </summary>
    /// <remarks>
    /// Taken from the describer this installation is using rather than written out, so that it
    /// stays correct if the describer is ever replaced.
    /// </remarks>
    private static string WrongPasswordCode { get; } = new IdentityErrorDescriber().PasswordMismatch().Code;

    /// <inheritdoc />
    public async Task<ProfileOutcome> RenameAsync(
        Guid userId,
        string displayName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var trimmed = displayName?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxDisplayNameLength)
        {
            return ProfileOutcome.Refused(ProfileStatus.NameRejected);
        }

        var user = await accounts.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

        if (user is null)
        {
            return ProfileOutcome.Refused(ProfileStatus.NoSuchAccount);
        }

        user.DisplayName = trimmed;

        var updated = await accounts.UpdateAsync(user).ConfigureAwait(false);

        // The store refuses this only where the account changed underneath us, which is two people
        // editing one account at the same moment. Its own words are carried out rather than
        // replaced, because there is nothing useful this side could say about it.
        return updated.Succeeded
            ? ProfileOutcome.Changed()
            : new ProfileOutcome(
                ProfileStatus.NameRejected,
                [.. updated.Errors.Select(error => new AccountProblem(error.Code, error.Description))]);
    }

    /// <inheritdoc />
    public async Task<ProfileOutcome> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string replacement,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await accounts.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

        if (user is null)
        {
            return ProfileOutcome.Refused(ProfileStatus.NoSuchAccount);
        }

        var changed = await accounts
            .ChangePasswordAsync(user, currentPassword, replacement)
            .ConfigureAwait(false);

        if (changed.Succeeded)
        {
            AccountProfileLog.PasswordChanged(logger);

            return ProfileOutcome.Changed();
        }

        // The store checks the current password first and stops there, so the two can never both
        // be the reason. Which one it was decides what the screen asks somebody to do next.
        if (changed.Errors.Any(error =>
            string.Equals(error.Code, WrongPasswordCode, StringComparison.Ordinal)))
        {
            return ProfileOutcome.Refused(ProfileStatus.CurrentPasswordWrong);
        }

        return new ProfileOutcome(
            ProfileStatus.PasswordRejected,
            [.. changed.Errors.Select(error => new AccountProblem(error.Code, error.Description))]);
    }
}

/// <summary>What changing an account records.</summary>
/// <remarks>
/// No address and no name. Which account it was is not written either: a log line saying somebody
/// changed their password is enough to notice a pattern, and naming them adds only personal data
/// with a lifetime nobody is managing.
/// </remarks>
internal static partial class AccountProfileLog
{
    [LoggerMessage(
        EventId = 3501,
        Level = LogLevel.Information,
        Message = "A password was changed from the account screen. Every other sign-in it had "
            + "open stops working at the next check.")]
    public static partial void PasswordChanged(ILogger logger);
}
