using Dewiride.Analytics.Application.Notifications;
using Dewiride.Analytics.Infrastructure.Notifications;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// The message every owner gets when their account is closed.
/// </summary>
/// <remarks>
/// Written for the owner who did not press the button. The one who did knows what happened; the
/// others need to know who did it, that nothing is gone yet, and by when they have to act if it
/// was a mistake — which is why the deletion day is both a fact row and the one instruction. The
/// fact rows carry the two days and nothing else: the panel is set for figures, and a name in it
/// reads as a code.
/// </remarks>
internal static class AccountClosedMessage
{
    /// <summary>What makes two sendings of the same closure one message.</summary>
    private const string Kind = "account-closed";

    /// <summary>
    /// Builds the message.
    /// </summary>
    /// <param name="to">The owner it goes to.</param>
    /// <param name="organizationId">The account, which with the closure instant is what makes this one message.</param>
    /// <param name="accountName">What the account is called.</param>
    /// <param name="closedBy">What to call whoever closed it.</param>
    /// <param name="closedAt">When it was closed.</param>
    /// <param name="deletionDue">When everything about it is deleted unless it is brought back first.</param>
    /// <param name="link">The address that lets an owner sign in and bring it back.</param>
    /// <returns>The message.</returns>
    public static EmailMessage For(
        Recipient to,
        Guid organizationId,
        string accountName,
        string closedBy,
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
                Subject = "Your account has been closed",
                Preheader = $"Everything is kept until {deletionDay}. Any owner can bring it back.",
                Paragraphs =
                [
                    $"{closedBy} closed the account {accountName} on {MailTemplate.Day(closedAt)}. "
                    + "Measurement has stopped for every website in it, and nobody can open its "
                    + "screens.",
                    $"Nothing has been deleted yet. Before {deletionDay} any owner can sign in and "
                    + "bring it back, with everything as it was.",
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
                    $"If nobody brings it back before {deletionDay}, everything it measured and "
                    + "everyone in it is deleted.",
                ],
            });
    }
}
