using Dewiride.Analytics.Application.Notifications;
using Dewiride.Analytics.Infrastructure.Notifications;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// The message somebody gets when they ask for a way back into their account.
/// </summary>
/// <remarks>
/// Written for two readers at once: the one who asked, who wants the button and nothing else, and
/// the one who did not, who wants to know whether anything has happened to their account. The
/// second reader is why the reassurance is stated plainly rather than left implied.
/// </remarks>
internal static class PasswordResetMessage
{
    /// <summary>
    /// Builds the message.
    /// </summary>
    /// <param name="toAddress">The mailbox it goes to.</param>
    /// <param name="name">The name to greet, which falls back to the address.</param>
    /// <param name="link">The address that lets them choose a new password.</param>
    /// <param name="token">
    /// What the link carries. Never sent and never shown — it is what makes one attempt to send
    /// this message the same message as another attempt to send it.
    /// </param>
    /// <returns>The message.</returns>
    public static EmailMessage For(string toAddress, string? name, string link, string token) =>
        MailTemplate.Compose(
            new Recipient(toAddress, name),
            MailKeys.ForSecret("password-reset", token),
            new MailContent
            {
                // Not the same words as the button below it. The heading and the action sat one
                // above the other saying "Choose a new password" twice, which reads as a mistake
                // rather than as emphasis — and this is the wording an inbox has always used for
                // this message, so it is the one somebody recognises without reading.
                Subject = "Reset your password",
                Preheader = "The link lasts 24 hours and can be used once.",
                Paragraphs =
                [
                    "Someone asked for a way back into this account. If it was you, the button "
                    + "below takes you straight to choosing a new password.",
                ],
                Action = "Choose a new password",
                Link = link,
                Footnotes =
                [
                    "The link works for the next 24 hours, and once only.",
                    "If this was not you, nothing has changed and there is nothing you need to do. "
                    + "Your current password still works.",
                ],
            });
}
