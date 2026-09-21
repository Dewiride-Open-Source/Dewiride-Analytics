using Dewiride.Analytics.Application.Notifications;
using Dewiride.Analytics.Infrastructure.Notifications;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// The message every owner gets when a closed account is brought back.
/// </summary>
/// <remarks>
/// The closure was announced to every owner, so its reversal is too: an owner who read the first
/// message and did nothing, because somebody else was going to deal with it, should not have to
/// sign in to find out whether they did.
/// </remarks>
internal static class AccountRestoredMessage
{
    /// <summary>What makes two sendings of the same restoration one message.</summary>
    private const string Kind = "account-restored";

    /// <summary>
    /// Builds the message.
    /// </summary>
    /// <param name="to">The owner it goes to.</param>
    /// <param name="organizationId">The account, which with the restoration instant is what makes this one message.</param>
    /// <param name="accountName">What the account is called.</param>
    /// <param name="restoredBy">What to call whoever brought it back.</param>
    /// <param name="restoredAt">When it was brought back.</param>
    /// <param name="link">The address of the account's screens.</param>
    /// <returns>The message.</returns>
    public static EmailMessage For(
        Recipient to,
        Guid organizationId,
        string accountName,
        string restoredBy,
        DateTimeOffset restoredAt,
        string link) =>
        MailTemplate.Compose(
            to,
            MailKeys.For(Kind, organizationId, restoredAt),
            new MailContent
            {
                Subject = "Your account is back",
                Preheader = $"{accountName} is open again.",
                Paragraphs =
                [
                    $"{restoredBy} brought {accountName} back on {MailTemplate.Day(restoredAt)}. "
                    + "Everything it measured is where it was, and measurement has resumed.",
                ],
                Action = "Open your account",
                Link = link,
            });
}
