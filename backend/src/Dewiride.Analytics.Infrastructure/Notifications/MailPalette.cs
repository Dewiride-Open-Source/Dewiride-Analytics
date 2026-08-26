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
/// <para>
/// The neutrals carry a trace of the accent rather than being grey. A message is one flat surface
/// filling a reading pane, and a true grey at that size reads as a wash laid over the page instead
/// of as a colour somebody chose. Three surfaces — the ground, the card, and the panel a figure
/// sits in — give it depth without a second hue.
/// </para>
/// <para>
/// The accent has two values in each theme because a fill and a letterform are not the same
/// problem: the violet that carries white lettering at button size is too light to read as small
/// text on the same ground. Every pairing here clears the contrast ratio its size and weight
/// require.
/// </para>
/// </remarks>
internal static class MailPalette
{
    /// <summary>Behind the card.</summary>
    public const string Page = "#f1eff9";

    /// <summary>The card itself.</summary>
    public const string Surface = "#ffffff";

    /// <summary>Behind a block of figures, so it reads as one object rather than three lines.</summary>
    public const string Panel = "#f7f5fd";

    /// <summary>Hairlines and separators.</summary>
    public const string Border = "#e3dff1";

    /// <summary>Anything somebody reads closely.</summary>
    public const string Text = "#17161f";

    /// <summary>The small print, and a figure's name beside it.</summary>
    public const string Muted = "#514f61";

    /// <summary>The quietest thing on the page: the footer, and the label above a copied link.</summary>
    public const string Subtle = "#6b6880";

    /// <summary>The one thing to do.</summary>
    public const string Accent = "#7456e0";

    /// <summary>The accent as lettering: the product's own mark, and a link.</summary>
    public const string AccentText = "#5b3bc4";

    /// <summary>Lettering on the accent.</summary>
    public const string OnAccent = "#ffffff";

    /// <summary>
    /// What lifts the card off the ground.
    /// </summary>
    /// <remarks>
    /// The product's own glow rather than a grey drop-shadow: a hairline of shade for the edge and
    /// a wide violet bloom beneath it. Ignored by the clients that lay a message out with Word,
    /// which is why the card also has a border.
    /// </remarks>
    public const string CardShadow =
        "0 1px 2px rgba(23,22,31,0.05),0 14px 32px -10px rgba(116,86,224,0.22)";

    /// <summary>Behind the card, for a reader in a dark client.</summary>
    public const string DarkPage = "#08070e";

    /// <summary>The card, for a reader in a dark client.</summary>
    public const string DarkSurface = "#161526";

    /// <summary>Behind a block of figures, for a reader in a dark client.</summary>
    public const string DarkPanel = "#1e1c33";

    /// <summary>Hairlines, for a reader in a dark client.</summary>
    public const string DarkBorder = "#332f52";

    /// <summary>Reading text, for a reader in a dark client.</summary>
    public const string DarkText = "#f3f2f9";

    /// <summary>The small print, for a reader in a dark client.</summary>
    public const string DarkMuted = "#b0adc4";

    /// <summary>The footer, for a reader in a dark client.</summary>
    public const string DarkSubtle = "#9a97b0";

    /// <summary>
    /// The accent as a fill, lifted for a dark ground.
    /// </summary>
    /// <remarks>
    /// The lighter of the two, because the violet that reads as deliberate on white is nearly
    /// black on a dark card and the button disappears into it.
    /// </remarks>
    public const string DarkAccent = "#a78bfa";

    /// <summary>The accent as lettering, for a reader in a dark client.</summary>
    public const string DarkAccentText = "#c4b0ff";

    /// <summary>Lettering on the accent, for a reader in a dark client.</summary>
    public const string DarkOnAccent = "#150a33";
}
