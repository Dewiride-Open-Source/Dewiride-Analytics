using System.Globalization;
using System.Net;
using Dewiride.Analytics.Application.Notifications;

namespace Dewiride.Analytics.Infrastructure.Notifications;

/// <summary>
/// The shape every message this product sends takes.
/// </summary>
/// <remarks>
/// <para>
/// The product's mark, a heading, a short paragraph or two, the figures if there are figures, one
/// thing to do, and the small print under it. Every message leaving here is about somebody's
/// account, and giving them one shape means a reader recognises the next one before they have read
/// it — which is most of what stops a genuine message being taken for a forgery.
/// </para>
/// <para>
/// Both forms are written from the same description. Composing them separately is how the plain
/// text quietly stops matching the HTML, and the plain text is what a reader whose client refuses
/// HTML actually sees.
/// </para>
/// <para>
/// English only, and composed here rather than taken from the interface's catalogues: nothing is
/// rendering this in a browser, so there is no reader whose language could be known. Everything
/// that came from a person is escaped before it reaches the HTML form.
/// </para>
/// <para>
/// The arrangement is tables with the styling written onto the elements, because a mail client may
/// strip a stylesheet and lay out a page of divs however it likes. The stylesheet that is included
/// only ever improves a message that is already correct without it.
/// </para>
/// </remarks>
public static class MailTemplate
{
    /// <summary>What the product calls itself in the messages it sends.</summary>
    public const string ProductName = "Dewiride Analytics";

    private const string BodyFont =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    private const string FigureFont = "SFMono-Regular,Consolas,'Liberation Mono',Menlo,monospace";

    /// <summary>
    /// Writes one message in both forms.
    /// </summary>
    /// <param name="to">Who it goes to.</param>
    /// <param name="idempotencyKey">
    /// What makes this message the same message if it is composed twice. Derived from the thing the
    /// message is about — the token being sent, the invitation being offered, the month being
    /// reported on — so that a pass which fails after sending and runs again does not send a second
    /// copy.
    /// </param>
    /// <param name="content">What it says.</param>
    /// <returns>The message.</returns>
    public static EmailMessage Compose(Recipient to, string idempotencyKey, MailContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new EmailMessage(
            to.Address,
            to.Greeting,
            content.Subject,
            PlainText(to, content),
            Page(to, content),
            idempotencyKey);
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
    /// The message for a mailbox that shows plain text, or a reader who prefers it.
    /// </summary>
    /// <remarks>
    /// Not a stripped copy of the HTML but the same description written out: the heading underlined
    /// the way a plain-text message has always marked one, the figures aligned in a block, and the
    /// link on a line of its own where it can be copied without picking up punctuation.
    /// </remarks>
    private static string PlainText(Recipient to, MailContent content)
    {
        var lines = new List<string>
        {
            content.Subject,
            new('=', content.Subject.Length),
            string.Empty,
            $"Hi {to.Greeting},",
        };

        foreach (var paragraph in content.Paragraphs)
        {
            lines.Add(string.Empty);
            lines.Add(paragraph);
        }

        AppendFacts(lines, content.Facts);

        lines.Add(string.Empty);
        lines.Add($"{content.Action}:");
        lines.Add(content.Link);

        foreach (var footnote in content.Footnotes)
        {
            lines.Add(string.Empty);
            lines.Add(footnote);
        }

        lines.Add(string.Empty);
        lines.Add("--");
        lines.Add($"{ProductName} · {Host(content.Link)}");
        lines.Add($"Sent to {to.Address} about the account it is used for.");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The figures, with their names padded so the values line up under one another.</summary>
    private static void AppendFacts(List<string> lines, IReadOnlyList<MailFact> facts)
    {
        if (facts.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);

        var width = facts.Max(fact => fact.Label.Length);

        lines.AddRange(facts.Select(fact => $"  {fact.Label.PadRight(width)}   {fact.Value}"));
    }

    /// <summary>The same message for a mailbox that shows HTML.</summary>
    private static string Page(Recipient to, MailContent content)
    {
        var link = WebUtility.HtmlEncode(content.Link);
        var subject = WebUtility.HtmlEncode(content.Subject);

        return $"""
            <!doctype html>
            <html lang="en" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="x-apple-disable-message-reformatting">
            <meta name="color-scheme" content="light dark">
            <meta name="supported-color-schemes" content="light dark">
            <title>{subject}</title>
            <!--[if mso]><xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml><![endif]-->
            <style>{MailStyles.Sheet}</style>
            </head>
            <body class="dw-page" style="margin:0;padding:0;width:100%;background:{MailPalette.Page};font-family:{BodyFont};">
            {Preheader(content.Preheader)}
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" class="dw-page" style="background:{MailPalette.Page};">
              <tr><td align="center" style="padding:32px 12px 40px;">
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="600" class="dw-shell" style="width:600px;max-width:600px;">
                  <tr><td style="padding:0 8px 16px;">
                    <span class="dw-mark" style="font-family:{BodyFont};font-size:15px;font-weight:700;letter-spacing:0.02em;color:{MailPalette.Accent};">{ProductName}</span>
                  </td></tr>
                  <tr><td class="dw-card" style="background:{MailPalette.Surface};border:1px solid {MailPalette.Border};border-radius:14px;">
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%">
                      <tr><td class="dw-pad" style="padding:36px 40px 40px;">
                        <h1 class="dw-heading" style="margin:0 0 20px;font-family:{BodyFont};font-size:25px;line-height:33px;font-weight:600;letter-spacing:-0.02em;color:{MailPalette.Text};">{subject}</h1>
                        <p class="dw-text" style="margin:0 0 18px;font-family:{BodyFont};font-size:16px;line-height:25px;color:{MailPalette.Text};">Hi {WebUtility.HtmlEncode(to.Greeting)},</p>
                        {Paragraphs(content.Paragraphs)}
                        {FactRows(content.Facts)}
                        {Button(content.Action, link)}
                        <p class="dw-subtle" style="margin:0;font-family:{BodyFont};font-size:13px;line-height:21px;color:{MailPalette.Subtle};word-break:break-word;overflow-wrap:anywhere;">Or copy this into your browser:<br><a href="{link}" class="dw-link" style="color:{MailPalette.Accent};text-decoration:underline;">{link}</a></p>
                        {Footnotes(content.Footnotes)}
                      </td></tr>
                    </table>
                  </td></tr>
                  <tr><td style="padding:20px 8px 0;">
                    <p class="dw-subtle" style="margin:0 0 4px;font-family:{BodyFont};font-size:12px;line-height:19px;color:{MailPalette.Subtle};">{ProductName} · {WebUtility.HtmlEncode(Host(content.Link))}</p>
                    <p class="dw-subtle" style="margin:0;font-family:{BodyFont};font-size:12px;line-height:19px;color:{MailPalette.Subtle};">Sent to {WebUtility.HtmlEncode(to.Address)} about the account it is used for.</p>
                  </td></tr>
                </table>
              </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// The line an inbox shows beside the subject.
    /// </summary>
    /// <remarks>
    /// Hidden, then padded with enough invisible characters that the client stops before it reaches
    /// the greeting. Without the padding the preview reads "…Hi Jagdish, Someone asked to reset",
    /// which is the default this exists to replace.
    /// </remarks>
    private static string Preheader(string text)
    {
        var padding = string.Concat(Enumerable.Repeat("&#8199;&#65279;", 40));

        return $"""<div style="display:none;font-size:1px;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;mso-hide:all;">{WebUtility.HtmlEncode(text)}{padding}</div>""";
    }

    private static string Paragraphs(IReadOnlyList<string> paragraphs) =>
        string.Concat(paragraphs.Select(paragraph =>
            $"""<p class="dw-text" style="margin:0 0 18px;font-family:{BodyFont};font-size:16px;line-height:25px;color:{MailPalette.Text};">{WebUtility.HtmlEncode(paragraph)}</p>"""));

    /// <summary>
    /// The figures the message is about, in a block a reader can find without reading.
    /// </summary>
    /// <remarks>
    /// Set in a monospaced face and aligned right, so that two figures under one another line up on
    /// their digits. A column of numbers that does not line up is read as a list of unrelated
    /// things.
    /// </remarks>
    private static string FactRows(IReadOnlyList<MailFact> facts)
    {
        if (facts.Count == 0)
        {
            return string.Empty;
        }

        var rows = string.Concat(facts.Select(FactRow));

        return $"""<table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="margin:2px 0 26px;">{rows}</table>""";
    }

    private static string FactRow(MailFact fact, int index)
    {
        var rule = index == 0 ? string.Empty : $"border-top:1px solid {MailPalette.Border};";

        return $"""
            <tr>
            <td class="dw-muted dw-rule" style="{rule}padding:11px 0;font-family:{BodyFont};font-size:14px;line-height:21px;color:{MailPalette.Muted};">{WebUtility.HtmlEncode(fact.Label)}</td>
            <td class="dw-text dw-rule" align="right" style="{rule}padding:11px 0;font-family:{FigureFont};font-size:14px;line-height:21px;font-weight:600;color:{MailPalette.Text};white-space:nowrap;">{WebUtility.HtmlEncode(fact.Value)}</td>
            </tr>
            """;
    }

    /// <summary>
    /// The one thing to do.
    /// </summary>
    /// <remarks>
    /// Twice: once as a shape Outlook's renderer understands, which ignores padding on a link and
    /// would otherwise draw the label with no button around it at all, and once as an ordinary link
    /// for every other client. Each is hidden from the other.
    /// </remarks>
    private static string Button(string action, string encodedLink)
    {
        var label = WebUtility.HtmlEncode(action);

        // Outlook's shape needs a width in pixels and cannot measure its own label, so one is
        // estimated from the label's length rather than fixed — a number that is right for today's
        // longest label is the kind that is wrong for the next one and nobody notices.
        var width = Math.Clamp((action.Length * 9) + 56, 190, 380);

        return $"""
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:8px 0 26px;">
            <tr><td>
            <!--[if mso]>
            <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{encodedLink}" style="height:46px;v-text-anchor:middle;width:{width}px;" arcsize="22%" stroke="f" fillcolor="{MailPalette.Accent}">
            <w:anchorlock/>
            <center style="color:{MailPalette.OnAccent};font-family:{BodyFont};font-size:16px;font-weight:600;">{label}</center>
            </v:roundrect>
            <![endif]-->
            <!--[if !mso]><!-->
            <a href="{encodedLink}" class="dw-button" style="display:inline-block;padding:14px 28px;border-radius:10px;background:{MailPalette.Accent};color:{MailPalette.OnAccent};font-family:{BodyFont};font-size:16px;line-height:18px;font-weight:600;text-decoration:none;">{label}</a>
            <!--<![endif]-->
            </td></tr>
            </table>
            """;
    }

    /// <summary>The small print, below a hairline so the message above it can stay short.</summary>
    private static string Footnotes(IReadOnlyList<string> footnotes)
    {
        if (footnotes.Count == 0)
        {
            return string.Empty;
        }

        var lines = string.Concat(footnotes.Select(footnote =>
            $"""<p class="dw-muted" style="margin:0 0 12px;font-family:{BodyFont};font-size:14px;line-height:22px;color:{MailPalette.Muted};">{WebUtility.HtmlEncode(footnote)}</p>"""));

        return $"""
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="margin:26px 0 0;">
            <tr><td class="dw-rule" style="border-top:1px solid {MailPalette.Border};padding:22px 0 0;">{lines}</td></tr>
            </table>
            """;
    }

    /// <summary>
    /// Where the message leads, as a reader would name it.
    /// </summary>
    /// <remarks>
    /// Taken from the link rather than from configuration, so that the installation named in the
    /// footer is provably the one the button goes to. A footer naming somewhere other than where
    /// the button leads is the shape of every phishing message ever written.
    /// </remarks>
    private static string Host(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var address) ? address.Host : ProductName;
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
