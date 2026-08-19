namespace Dewiride.Analytics.Domain.Telemetry;

/// <summary>
/// Where an operated control pointed, for an <see cref="EventKind.Action"/>.
/// </summary>
/// <remarks>
/// <see cref="None"/> is a finding rather than an absence: a control that points nowhere is how
/// most of a page's buttons work, and it is a different fact from a link whose destination could
/// not be read.
/// </remarks>
public enum TargetKind
{
    /// <summary>The control pointed nowhere, or nowhere this product records.</summary>
    None = 0,

    /// <summary>Another page on the same site, kept in full.</summary>
    Internal = 1,

    /// <summary>Somewhere else, kept as the host alone.</summary>
    External = 2,

    /// <summary>
    /// An address to write to or ring. Recorded as having been used and never kept, because the
    /// address itself names a person.
    /// </summary>
    Contact = 3,
}
