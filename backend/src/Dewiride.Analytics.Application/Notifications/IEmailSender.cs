namespace Dewiride.Analytics.Application.Notifications;

/// <summary>
/// Sends a message to somebody's mailbox.
/// </summary>
/// <remarks>
/// <para>
/// Stated as a port because every message this product sends is a consequence of a use case —
/// somebody asking for a way back into their account, somebody confirming an address — and the
/// use case must not depend on how mail leaves the building.
/// </para>
/// <para>
/// An installation that has configured no mail server still has an implementation. A self-hosted
/// copy whose owner has forgotten their password and cannot be sent a link is locked out of their
/// own analytics with no way back, which is a defect rather than a consequence of not running a
/// mail server.
/// </para>
/// </remarks>
public interface IEmailSender
{
    /// <summary>
    /// Sends one message.
    /// </summary>
    /// <param name="message">What to send, and to whom.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the message has been handed over.</returns>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// One message, in both of the forms a mailbox may prefer.
/// </summary>
/// <remarks>
/// Both bodies are required rather than one being optional. A message sent as HTML alone is
/// unreadable in a client that shows plain text and is scored as spam by several large providers;
/// a message sent as text alone looks broken beside everything else in the inbox. Composing both
/// is the ordinary cost of sending mail, not a refinement.
/// </remarks>
/// <param name="ToAddress">Mailbox to send to.</param>
/// <param name="ToName">Name to address the recipient by, when one is known.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="PlainText">The message as plain text.</param>
/// <param name="Html">The message as HTML.</param>
/// <param name="IdempotencyKey">
/// <para>
/// What makes this the same message rather than another one. Derived from the thing the message is
/// about — the token being sent, the invitation being offered, the month being reported on — and
/// never from the moment of sending, which would make every attempt a new message.
/// </para>
/// <para>
/// Stated on the message rather than left to whatever delivers it, because only the caller knows
/// what "the same message" means. A pass that sends and then fails before recording that it sent
/// runs again, and the difference between one notice and two is this value. An implementation that
/// has nowhere to put it is free to ignore it.
/// </para>
/// </param>
public sealed record EmailMessage(
    string ToAddress,
    string? ToName,
    string Subject,
    string PlainText,
    string Html,
    string IdempotencyKey);
