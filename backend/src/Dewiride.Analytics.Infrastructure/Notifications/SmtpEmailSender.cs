using Dewiride.Analytics.Application.Notifications;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Dewiride.Analytics.Infrastructure.Notifications;

/// <summary>
/// Hands messages to a mail server.
/// </summary>
/// <remarks>
/// <para>
/// MailKit rather than the framework's own client, which Microsoft does not recommend for new
/// work because it does not speak enough of the modern protocol to be secured properly. MailKit
/// is the alternative Microsoft names, and it is MIT-licensed.
/// </para>
/// <para>
/// A client is created for each message and disposed with it. MailKit's client holds one
/// connection and is not safe to use from two places at once, so a shared one would either need a
/// lock in front of it — turning every send into a queue — or would corrupt the conversation with
/// the server under any concurrency at all.
/// </para>
/// </remarks>
/// <param name="settings">Where and how to submit.</param>
/// <param name="logger">Log.</param>
public sealed class SmtpEmailSender(
    IOptionsMonitor<EmailOptions> settings,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Read at the moment of use rather than captured once. The settings are a singleton's
        // dependency, and one that copied them into a field would keep the first values it ever
        // saw for the life of the process.
        var current = settings.CurrentValue;

        using var composed = Compose(message, current);
        using var client = new SmtpClient { Timeout = (int)current.Timeout.TotalMilliseconds };

        await client
            .ConnectAsync(current.Host, current.Port, SocketOptions(current.Security), cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrEmpty(current.UserName))
        {
            await client
                .AuthenticateAsync(current.UserName, current.Password, cancellationToken)
                .ConfigureAwait(false);
        }

        await client.SendAsync(composed, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);

        EmailLog.Sent(logger, message.Subject);
    }

    private static MimeMessage Compose(EmailMessage message, EmailOptions settings)
    {
        var composed = new MimeMessage();
        composed.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        composed.To.Add(new MailboxAddress(message.ToName ?? message.ToAddress, message.ToAddress));
        composed.Subject = message.Subject;

        // Both forms, in the order a reader's client is meant to consider them: the last part of
        // a multipart/alternative is the one it prefers, so the plain text goes first.
        composed.Body = new MultipartAlternative
        {
            new TextPart(TextFormat.Plain) { Text = message.PlainText },
            new TextPart(TextFormat.Html) { Text = message.Html },
        };

        return composed;
    }

    private static SecureSocketOptions SocketOptions(EmailSecurity security) => security switch
    {
        EmailSecurity.StartTls => SecureSocketOptions.StartTls,
        EmailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        EmailSecurity.None => SecureSocketOptions.None,
        _ => throw new ArgumentOutOfRangeException(nameof(security)),
    };
}

