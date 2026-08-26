using System.Text;

namespace Dewiride.Analytics.Infrastructure.Notifications;

/// <summary>
/// The rules that only some clients honour, and that no message depends on.
/// </summary>
/// <remarks>
/// <para>
/// Everything structural is written onto the elements themselves, because a mail client may strip
/// this block entirely and several do. What is here only ever improves a message that is already
/// correct without it: the dark set for a reader in a dark client, and the narrow arrangement for
/// a phone.
/// </para>
/// <para>
/// <c>color-scheme</c> is declared as a property as well as in the document's head. A client that
/// finds neither assumes the message knows nothing about dark mode and inverts it channel by
/// channel — which turns a white card and a tinted ground into two similar greys, and is the usual
/// reason a carefully coloured message arrives looking as though something had been laid over it.
/// The property is honoured where the meta element is not.
/// </para>
/// <para>
/// The dark set is written twice from one description. Outlook.com performs that inversion whatever
/// the message says, and marks what it touched with <c>data-ogsc</c> where it changed lettering and
/// <c>data-ogsb</c> where it changed a background; matching those attributes is the only way to put
/// the intended colours back. The same declarations reached by two routes have to agree, so they
/// are stated once and rendered under both.
/// </para>
/// <para>
/// Composed rather than written out, so that every colour comes from <see cref="MailPalette"/> and
/// a message that does not follow the palette cannot exist. Written with a raw interpolated literal
/// whose holes are doubled, so that the braces a stylesheet is made of stay single and a rule
/// cannot be lost to an escaping mistake nobody sees until a message arrives looking wrong.
/// </para>
/// </remarks>
internal static class MailStyles
{
    /// <summary>
    /// The rules every reader gets, whatever their client decided about colour.
    /// </summary>
    /// <remarks>
    /// Word lays out a message in Outlook on Windows and ignores line height unless told to take it
    /// exactly, which is what closes up a paragraph there and nowhere else.
    /// </remarks>
    private static readonly string Base = $$"""
        :root { color-scheme:light dark; supported-color-schemes:light dark; }
        body,table,td,p,a { -webkit-text-size-adjust:100%; -ms-text-size-adjust:100%; }
        table,td { mso-table-lspace:0pt; mso-table-rspace:0pt; }
        p,td,div { mso-line-height-rule:exactly; }
        img { -ms-interpolation-mode:bicubic; border:0; outline:none; text-decoration:none; }
        a { color:{{MailPalette.AccentText}}; }

        """;

    /// <summary>
    /// What each part of a message becomes for a reader in a dark client.
    /// </summary>
    /// <remarks>
    /// The card keeps an accent edge as well as a border. On a dark ground the card and the page
    /// beneath it are a few per cent apart in lightness, and that step alone does not give the card
    /// a shape.
    /// </remarks>
    private static readonly (string Selector, string Declarations)[] DarkRules =
    [
        (".dw-page", $"background:{MailPalette.DarkPage} !important;"),
        (".dw-card",
            $"background:{MailPalette.DarkSurface} !important;" +
            $"border-color:{MailPalette.DarkBorder} !important;" +
            "box-shadow:0 0 0 1px rgba(167,139,250,0.12),0 18px 40px -14px rgba(0,0,0,0.8) !important;"),
        (".dw-panel",
            $"background:{MailPalette.DarkPanel} !important;" +
            $"border-color:{MailPalette.DarkBorder} !important;"),
        (".dw-heading", $"color:{MailPalette.DarkText} !important;"),
        (".dw-text", $"color:{MailPalette.DarkText} !important;"),
        (".dw-figure", $"color:{MailPalette.DarkText} !important;"),
        (".dw-muted", $"color:{MailPalette.DarkMuted} !important;"),
        (".dw-subtle", $"color:{MailPalette.DarkSubtle} !important;"),
        (".dw-rule", $"border-color:{MailPalette.DarkBorder} !important;"),
        (".dw-mark", $"color:{MailPalette.DarkAccentText} !important;"),
        (".dw-link", $"color:{MailPalette.DarkAccentText} !important;"),
        (".dw-button",
            $"background:{MailPalette.DarkAccent} !important;" +
            $"color:{MailPalette.DarkOnAccent} !important;"),
    ];

    /// <summary>
    /// How Outlook.com marks what it recoloured.
    /// </summary>
    /// <remarks>
    /// Matched on the element it changed and on an ancestor of it, because which of the two carries
    /// the attribute depends on where the colour it replaced was written.
    /// </remarks>
    private static readonly string[] ForcedMarkers = ["data-ogsc", "data-ogsb"];

    private const string Narrow = """

        @media only screen and (max-width: 620px) {
          .dw-shell { width:100% !important; }
          .dw-pad { padding-left:24px !important; padding-right:24px !important; }
          .dw-panel-pad { padding-left:18px !important; padding-right:18px !important; }
          .dw-heading { font-size:22px !important; line-height:30px !important; }
        }
        """;

    /// <summary>The rules, ready to be placed in the document's head.</summary>
    public static string Sheet { get; } = Compose();

    private static string Compose()
    {
        var sheet = new StringBuilder(Base);

        sheet.AppendLine("@media (prefers-color-scheme: dark) {");

        foreach (var (selector, declarations) in DarkRules)
        {
            sheet.Append("  ").Append(selector).Append(" { ").Append(declarations).AppendLine(" }");
        }

        sheet.AppendLine("}");

        foreach (var (selector, declarations) in DarkRules)
        {
            sheet.Append(string.Join(",", Forced(selector)))
                .Append(" { ")
                .Append(declarations)
                .AppendLine(" }");
        }

        return sheet.Append(Narrow).ToString();
    }

    private static IEnumerable<string> Forced(string selector) =>
        ForcedMarkers.SelectMany(marker =>
            new[] { $"[{marker}] {selector}", $"{selector}[{marker}]" });
}
