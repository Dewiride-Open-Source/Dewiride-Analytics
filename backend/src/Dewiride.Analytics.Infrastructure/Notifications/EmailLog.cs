using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.Notifications;

/// <summary>
/// What sending a message records.
/// </summary>
/// <remarks>
/// A message that went to a mail server records its subject and nothing else. A recipient's
/// address written into a log is personal data kept somewhere with a different lifetime from the
/// database it came from, and the body carries the very link the message exists to deliver.
/// </remarks>
internal static partial class EmailLog
{
    [LoggerMessage(
        EventId = 3201,
        Level = LogLevel.Debug,
        Message = "Sent a message: {Subject}")]
    public static partial void Sent(ILogger logger, string subject);

    [LoggerMessage(
        EventId = 3202,
        Level = LogLevel.Warning,
        Message = "No mail server is configured, so this message was written to the log instead "
            + "of being sent. Anyone who can read this log can use the links in it. Subject: "
            + "{Subject}\n{PlainText}")]
    public static partial void WrittenHereInstead(ILogger logger, string subject, string plainText);

    [LoggerMessage(
        EventId = 3203,
        Level = LogLevel.Error,
        Message = "A mail server would not take a message, so nobody received it. Subject: {Subject}")]
    public static partial void CouldNotHandOver(ILogger logger, string subject, Exception exception);
}
