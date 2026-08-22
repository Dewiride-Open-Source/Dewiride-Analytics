using System.Globalization;
using System.Net;
using Dewiride.Analytics.Application.Notifications;

namespace Dewiride.Analytics.Infrastructure.Notifications;

/// <summary>
/// The shape every message this product sends takes.
/// </summary>
/// <remarks>
/// <para>
/// One greeting, a short paragraph or two, one thing to do, and the product's name. Every message
/// leaving here is about somebody's account, and giving them one shape means a reader recognises
/// the next one before they have read it.
/// </para>
/// <para>
/// Both forms are written from the same paragraphs. Composing them separately is how the plain
/// text quietly stops matching the HTML, and the plain text is what a reader whose client refuses
/// HTML actually sees.
/// </para>
/// <para>
/// English only, and composed here rather than taken from the interface's catalogues: nothing is
/// rendering this in a browser, so there is no reader whose language could be known. Everything
/// that came from a person is escaped before it reaches the HTML form.
/// </para>
/// </remarks>
public static class MailTemplate
{
    /// <summary>What the product calls itself in the messages it sends.</summary>
    public const string ProductName = "Dewiride Analytics";

    /// <summary>
    /// Writes one message in both forms.
    /// </summary>
    /// <param name="to">Who it goes to.</param>
    /// <param name="subject">The subject line, which is also the message's own heading.</param>
    /// <param name="paragraphs">What it says.</param>
    /// <param name="link">Where the one thing to do about it happens.</param>
    /// <param name="action">What that one thing is called.</param>
    /// <returns>The message.</returns>
    public static EmailMessage Compose(
        Recipient to,
        string subject,
        IReadOnlyList<string> paragraphs,
        string link,
        string action)
    {
        var greeting = to.Greeting;

        var plain = string.Join(
            Environment.NewLine + Environment.NewLine,
            [
                $"Hi {greeting},",
                .. paragraphs,
                $"{action}:{Environment.NewLine}{link}",
                ProductName,
            ]);

        return new EmailMessage(to.Address, greeting, subject, plain, Page(subject, greeting, paragraphs, link, action));
    }

    /// <summary>
    /// A count as somebody reads it rather than as it is stored.
    /// </summary>
    /// <remarks>
    /// Grouped in the way the larger part of the world reads a number. Every message here is in
    /// English and there is no reader whose conventions could be known, so one form is chosen and
    /// used consistently rather than guessed at.
    /// </remarks>
    /// <param name="value">The count.</param>
    /// <returns>The count, written out.</returns>
    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A date somebody can act on, without a time nobody needs.</summary>
    /// <param name="instant">The moment.</param>
    /// <returns>The day it falls on.</returns>
    public static string Day(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("d MMMM", CultureInfo.InvariantCulture);

    /// <summary>
    /// The same message for a mailbox that shows HTML.
    /// </summary>
    /// <remarks>
    /// One table with styles on the elements themselves. Mail clients strip a style block, ignore
    /// most of a stylesheet and lay out a page of divs unpredictably, so the arrangement that
    /// survives everywhere is the plainest one.
    /// </remarks>
    private static string Page(
        string title,
        string greeting,
        IReadOnlyList<string> paragraphs,
        string link,
        string action)
    {
        var body = string.Concat(
            paragraphs.Select(paragraph =>
                $"""<p style="margin:0 0 20px;font-size:16px;line-height:1.5;">{WebUtility.HtmlEncode(paragraph)}</p>"""));

        var safeLink = WebUtility.HtmlEncode(link);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><title>{WebUtility.HtmlEncode(title)}</title></head>
            <body style="margin:0;padding:24px;background:#f5f5f7;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:#1c1c21;">
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="max-width:520px;margin:0 auto;background:#ffffff;border-radius:12px;border:1px solid #e4e4e9;">
                <tr><td style="padding:32px;">
                  <p style="margin:0 0 20px;font-size:16px;line-height:1.5;">Hi {WebUtility.HtmlEncode(greeting)},</p>
                  {body}
                  <p style="margin:0 0 24px;">
                    <a href="{safeLink}" style="display:inline-block;padding:12px 22px;border-radius:8px;background:#6d4aff;color:#ffffff;font-size:16px;font-weight:600;text-decoration:none;">{WebUtility.HtmlEncode(action)}</a>
                  </p>
                  <p style="margin:0;font-size:13px;line-height:1.5;color:#8a8a96;word-break:break-all;">Or paste this into your browser:<br><a href="{safeLink}" style="color:#6d4aff;">{safeLink}</a></p>
                </td></tr>
              </table>
            </body>
            </html>
            """);
    }
}

/// <summary>
/// Somebody a message is being sent to.
/// </summary>
/// <param name="Address">Their mailbox.</param>
/// <param name="Name">What to call them, where anything is known.</param>
public readonly record struct Recipient(string Address, string? Name)
{
    /// <summary>
    /// What to greet them by.
    /// </summary>
    /// <remarks>
    /// Their address where no name was given. An account created by somebody who left the name
    /// blank is greeted by the thing they did give rather than by a blank space or by "there".
    /// </remarks>
    public string Greeting => string.IsNullOrWhiteSpace(Name) ? Address : Name.Trim();
}
