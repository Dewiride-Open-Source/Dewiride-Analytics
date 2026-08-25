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
/// Held apart from the page it is inserted into so that the braces a stylesheet is made of do not
/// have to be doubled through an interpolated literal, which is how a stylesheet ends up with a
/// missing rule nobody sees until a message arrives looking wrong.
/// </para>
/// </remarks>
internal static class MailStyles
{
    /// <summary>The rules, ready to be placed in the document's head.</summary>
    public const string Sheet = """
        /* Outlook renders with Word, which ignores line-height unless told to take it exactly. */
        body,table,td,p,a { -webkit-text-size-adjust:100%; -ms-text-size-adjust:100%; }
        table,td { mso-table-lspace:0pt; mso-table-rspace:0pt; }
        p,td,div { mso-line-height-rule:exactly; }
        img { -ms-interpolation-mode:bicubic; border:0; outline:none; text-decoration:none; }
        a { color:#7456e0; }

        @media (prefers-color-scheme: dark) {
          .dw-page { background:#0a0a10 !important; }
          .dw-card { background:#111117 !important; border-color:#383844 !important; }
          .dw-text, .dw-text a { color:#f1f1f4 !important; }
          .dw-heading { color:#f1f1f4 !important; }
          .dw-muted { color:#a4a3ad !important; }
          .dw-subtle { color:#a4a3ad !important; }
          .dw-rule { border-color:#383844 !important; }
          .dw-mark { color:#9f87ff !important; }
          .dw-button { background:#9f87ff !important; color:#0d091f !important; }
          .dw-link { color:#9f87ff !important; }
        }

        @media only screen and (max-width: 620px) {
          .dw-shell { width:100% !important; }
          .dw-pad { padding-left:24px !important; padding-right:24px !important; }
          .dw-heading { font-size:22px !important; line-height:30px !important; }
        }
        """;
}
