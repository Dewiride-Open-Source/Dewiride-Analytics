using System.Text;
using Dewiride.Analytics.Application.Notifications;

namespace Dewiride.Analytics.MailTests;

/// <summary>
/// One message, written out so that a person reviewing a change can see what changed.
/// </summary>
/// <remarks>
/// Both bodies in one file, because the failure this exists to catch is one of them being edited
/// and the other not. Reading them side by side in a diff is the only way that shows up before
/// somebody with a plain-text client receives a message describing a button they cannot see.
/// </remarks>
internal static class MailReport
{
    /// <summary>
    /// Renders a message for approval.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The report.</returns>
    public static string Render(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var report = new StringBuilder();

        Section(report, "To", message.ToAddress);
        Section(report, "Greeted as", message.ToName ?? "(nothing)");
        Section(report, "Subject", message.Subject);
        Section(report, "Idempotency key", message.IdempotencyKey);
        Section(report, "Plain text", message.PlainText);
        Section(report, "HTML", message.Html);

        return report.ToString();
    }

    private static void Section(StringBuilder report, string heading, string body)
    {
        report.Append("--- ").Append(heading).AppendLine(" ---");
        report.AppendLine(body);
        report.AppendLine();
    }
}
