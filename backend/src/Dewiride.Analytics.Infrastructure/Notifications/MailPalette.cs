namespace Dewiride.Analytics.Infrastructure.Notifications;

/// <summary>
/// The product's colours, written the one way a mail client understands them.
/// </summary>
/// <remarks>
/// <para>
/// The interface states its palette in a perceptual colour space, which is right for a browser and
/// unreadable to every mail client in use. These are the same colours converted once, so that a
/// message and the screen it leads to are recognisably the same product rather than approximately
/// the same violet.
/// </para>
/// <para>
/// Both themes, because a reader in a dark client is shown the dark set and a message that ignores
/// it arrives as a white slab in a dark inbox. Clients that strip the rules simply keep the light
/// set, which is why every colour here is also written onto the element itself.
/// </para>
/// </remarks>
internal static class MailPalette
{
    /// <summary>Behind the card.</summary>
    public const string Page = "#f2f2f7";

    /// <summary>The card itself.</summary>
    public const string Surface = "#ffffff";

    /// <summary>Hairlines and separators.</summary>
    public const string Border = "#dddde4";

    /// <summary>Anything somebody reads closely.</summary>
    public const string Text = "#1e1d25";

    /// <summary>The small print, and a figure's name beside it.</summary>
    public const string Muted = "#676671";

    /// <summary>The quietest thing on the page: the footer, and a link written out in full.</summary>
    public const string Subtle = "#888892";

    /// <summary>The one thing to do, and the product's own mark.</summary>
    public const string Accent = "#7456e0";

    /// <summary>Lettering on the accent.</summary>
    public const string OnAccent = "#ffffff";

    /// <summary>Behind the card, for a reader in a dark client.</summary>
    public const string DarkPage = "#0a0a10";

    /// <summary>The card, for a reader in a dark client.</summary>
    public const string DarkSurface = "#111117";

    /// <summary>Hairlines, for a reader in a dark client.</summary>
    public const string DarkBorder = "#383844";

    /// <summary>Reading text, for a reader in a dark client.</summary>
    public const string DarkText = "#f1f1f4";

    /// <summary>The small print, for a reader in a dark client.</summary>
    public const string DarkMuted = "#a4a3ad";

    /// <summary>
    /// The accent, lifted for a dark ground.
    /// </summary>
    /// <remarks>
    /// The lighter of the two, because the violet that reads as deliberate on white is nearly
    /// black on a dark card and the button disappears into it.
    /// </remarks>
    public const string DarkAccent = "#9f87ff";

    /// <summary>Lettering on the accent, for a reader in a dark client.</summary>
    public const string DarkOnAccent = "#0d091f";
}
