namespace Dewiride.Analytics.Domain.Telemetry;

/// <summary>
/// The kind of thing a visitor operated, for an <see cref="EventKind.Action"/>.
/// </summary>
/// <remarks>
/// A closed set of three, resolved on the way in from whatever the page called the element. The
/// page's own spelling is not kept: a site may name an element anything at all, and a column that
/// stores what the page said is a column whose values cannot be shown to anybody without either
/// leaking the site's markup or writing prose around an arbitrary string.
/// </remarks>
public enum ControlKind
{
    /// <summary>The page described the control in terms this product does not recognise.</summary>
    Unknown = 0,

    /// <summary>Something that takes the visitor somewhere.</summary>
    Link = 1,

    /// <summary>Something that makes the page do something.</summary>
    Button = 2,

    /// <summary>Something the visitor fills in. What they put in it is never observed.</summary>
    Field = 3,
}
