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
    /// <param name="invitationId">Which invitation this is, which is what makes it one message.</param>
    /// <param name="organizationName">The account they have been asked to join.</param>
    /// <param name="invitedBy">What to call whoever asked them.</param>
    /// <param name="link">The address that lets them take it up.</param>
    /// <returns>The message.</returns>
    public static EmailMessage For(
        string toAddress,
        Guid invitationId,
        string organizationName,
        string invitedBy,
        string link) =>
        MailTemplate.Compose(
            new Recipient(toAddress, null),
            MailKeys.For("invitation", invitationId),
            new MailContent
            {
                Subject = $"Join {organizationName} on {MailTemplate.ProductName}",
                Preheader = $"{invitedBy} has asked you to join. The invitation lasts 7 days.",
                Paragraphs =
                [
                    $"{invitedBy} has asked you to join {organizationName} on "
                    + $"{MailTemplate.ProductName}, where they measure who is really reading their "
                    + "websites — people, search engines, AI crawlers, and traffic nobody asked "
                    + "for.",
                    "Opening the link sets up your own sign-in for the account. Nothing is created "
                    + "in your name before that.",
                ],
                Action = "Join the account",
                Link = link,
                Footnotes =
                [
                    "The invitation works for the next 7 days.",
                    "If you were not expecting this, you can ignore it and nothing will happen.",
                ],
            });
}
