using Dewiride.Analytics.Application.Notifications;
using Dewiride.Analytics.Infrastructure.Notifications;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// The message every owner gets a week before a closed account is deleted.
/// </summary>
/// <remarks>
/// A closure that was a mistake is usually noticed quickly, but not always: the person who would
/// have noticed may have been away, or the message announcing it may have been read as routine.
/// One reminder, a week out, is the last chance said plainly. It is composed in the free product
/// and reads the same on both editions, so it promises nothing that only the hosted service does.
/// </remarks>
internal static class AccountDeletionReminderMessage
{
    /// <summary>What makes two sendings of the same reminder one message.</summary>
    private const string Kind = "account-deletion-reminder";

    /// <summary>
    /// Builds the message.
    /// </summary>
    /// <param name="to">The owner it goes to.</param>
    /// <param name="organizationId">The account, which with the closure instant is what makes this one message.</param>
    /// <param name="accountName">What the account is called.</param>
    /// <param name="closedAt">When it was closed.</param>
    /// <param name="deletionDue">When everything about it is deleted unless it is brought back first.</param>
    /// <param name="link">The address that lets an owner sign in and bring it back.</param>
    /// <returns>The message.</returns>
    public static EmailMessage For(
        Recipient to,
        Guid organizationId,
        string accountName,
        DateTimeOffset closedAt,
        DateTimeOffset deletionDue,
        string link)
    {
        var deletionDay = MailTemplate.Day(deletionDue);

        return MailTemplate.Compose(
            to,
            MailKeys.For(Kind, organizationId, closedAt),
            new MailContent
            {
                Subject = $"{accountName} will be deleted on {deletionDay}",
                Preheader = $"Bring it back before {deletionDay} or it is gone.",
                Paragraphs =
                [
                    $"{accountName} was closed and will be deleted on {deletionDay}, along with "
                    + "everything it measured and everyone in it. If that was a mistake, sign in "
                    + "and bring it back before then.",
                ],
                Facts =
                [
                    new MailFact("Closed on", MailTemplate.Day(closedAt)),
                    new MailFact("Deleted on", deletionDay),
                ],
                Action = "Bring it back",
                Link = link,
                Footnotes =
                [
                    "After that day nothing it measured can be brought back.",
                ],
            });
    }
}
