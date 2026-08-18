namespace Dewiride.Analytics.Api.Contracts;

/// <summary>
/// One report from the browser tracker.
/// </summary>
/// <remarks>
/// <para>
/// Everything on this type is written by the page being measured and is therefore under the
/// control of whoever is visiting it, including someone who would rather not be counted
/// honestly. It is validated, bound as a parameter wherever it reaches the telemetry store, and
/// never treated as an instruction.
/// </para>
/// <para>
/// The names are spelled out rather than abbreviated. This is the shape every capture surface
/// posts, including ones other people write, and a saving of a hundred bytes before compression
/// is not worth a wire format nobody can read.
/// </para>
/// </remarks>
public sealed record CollectRequest
{
    /// <summary>The site's public identifier, as it appears in the tracker snippet.</summary>
    public Guid SiteId { get; init; }

    /// <summary>What is being reported: <c>pageview</c>, <c>engagement</c> or <c>exit</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>Absolute URL of the page.</summary>
    public string? Url { get; init; }

    /// <summary>Referring URL, where the browser supplied one.</summary>
    public string? Referrer { get; init; }

    /// <summary>The client's own clock in Unix milliseconds. Recorded as a difference, never used as a time.</summary>
    public long? ClientTimestamp { get; init; }

    /// <summary>Viewport width in CSS pixels.</summary>
    public int? ViewportWidth { get; init; }

    /// <summary>Viewport height in CSS pixels.</summary>
    public int? ViewportHeight { get; init; }

    /// <summary>Primary language the browser declares.</summary>
    public string? Language { get; init; }

    /// <summary>The client's offset from UTC in minutes.</summary>
    public short? TimezoneOffsetMinutes { get; init; }

    /// <summary>Milliseconds accrued while the tab was actually visible.</summary>
    public int? EngagedMs { get; init; }

    /// <summary>Furthest scroll depth reached, as a percentage of document height.</summary>
    public byte? ScrollDepthPercent { get; init; }

    /// <summary>Whether any pointer interaction happened. Presence only — never coordinates.</summary>
    public bool? PointerInteraction { get; init; }

    /// <summary>Whether any keyboard interaction happened. Presence only — never what was typed.</summary>
    public bool? KeyboardInteraction { get; init; }

    /// <summary>Whether the browser reports itself as being driven by automation.</summary>
    public bool? WebDriver { get; init; }

    /// <summary>Identifier stamped into the served page, echoed back so the two can be matched.</summary>
    public string? CorrelationId { get; init; }
}
