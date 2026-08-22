using Dewiride.Analytics.Application.Notifications;
using Dewiride.Analytics.Infrastructure.Notifications;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// The message somebody gets when they ask for a way back into their account.
/// </summary>
internal static class PasswordResetMessage
{
    /// <summary>
    /// Builds the message.
    /// </summary>
    /// <param name="toAddress">The mailbox it goes to.</param>
    /// <param name="name">The name to greet, which falls back to the address.</param>
    /// <param name="link">The address that lets them choose a new password.</param>
    /// <returns>The message.</returns>
    public static EmailMessage For(string toAddress, string? name, string link) =>
        MailTemplate.Compose(
            new Recipient(toAddress, name),
            $"Reset your {MailTemplate.ProductName} password",
            [
                "Someone asked to reset the password on this account.",
                "The link below works for the next 24 hours, and once only.",
                "If this was not you, nothing has changed and there is nothing you need to do.",
            ],
            link,
            "Choose a new password");
}
