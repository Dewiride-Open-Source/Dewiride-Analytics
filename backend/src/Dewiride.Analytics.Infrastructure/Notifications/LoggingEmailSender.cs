using Dewiride.Analytics.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.Notifications;

/// <summary>
/// Writes messages to the log, for an installation that has no mail server to send them to.
/// </summary>
/// <remarks>
/// <para>
/// This is what a self-hosted copy gets before anybody configures mail, and it exists because the
/// alternative is worse. Without it, an owner who forgets their password is locked out of their
/// own analytics permanently, with the data still being collected and nobody able to look at it.
/// </para>
/// <para>
/// It does put a working password-reset link into the log, and that is a real consequence rather
/// than an oversight: whoever can read the log can use it. It is recorded as a warning rather than
/// as information so that it reads, in an ordinary log, as something to fix — and anybody who can
/// read the log can already read the database password out of the same machine's environment, so
/// this widens who can reach the account by nobody.
/// </para>
/// </remarks>
/// <param name="logger">Log.</param>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        EmailLog.WrittenHereInstead(logger, message.Subject, message.PlainText);

        return Task.CompletedTask;
    }
}
