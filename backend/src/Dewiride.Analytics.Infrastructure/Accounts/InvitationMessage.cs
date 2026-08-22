using Dewiride.Analytics.Application.Notifications;
using Dewiride.Analytics.Infrastructure.Notifications;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// The message somebody gets when they are asked to join an account.
/// </summary>
/// <remarks>
/// It names the account and nothing else about it. The recipient may be a stranger, a colleague or
/// somebody who was invited by mistake, and until they take it up they are entitled to know only
/// what they have to decide about.
/// </remarks>
internal static class InvitationMessage
{
    /// <summary>
    /// Builds the message.
    /// </summary>
    /// <param name="toAddress">The mailbox it goes to.</param>
    /// <param name="organizationName">The account they have been asked to join.</param>
    /// <param name="invitedBy">What to call whoever asked them.</param>
    /// <param name="link">The address that lets them take it up.</param>
    /// <returns>The message.</returns>
    public static EmailMessage For(
        string toAddress,
        string organizationName,
        string invitedBy,
        string link) =>
        MailTemplate.Compose(
            new Recipient(toAddress, null),
            $"Join {organizationName} on {MailTemplate.ProductName}",
            [
                $"{invitedBy} has asked you to join {organizationName} on "
                    + $"{MailTemplate.ProductName}, where they measure how their websites are read.",
                "The link below works for the next 7 days.",
                "If you were not expecting this, you can ignore it. Nothing happens until you "
                    + "open the link.",
            ],
            link,
            "Join the account");
}
