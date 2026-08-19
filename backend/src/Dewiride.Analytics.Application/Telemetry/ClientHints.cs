namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// What a browser volunteered about itself, apart from its user-agent string.
/// </summary>
/// <remarks>
/// <para>
/// Chromium browsers send these three of their own accord on every request, cross-origin
/// included, without the measured site having to delegate anything. They are the low-entropy
/// hints — the ones deemed to say so little about an individual that they need no permission.
/// The high-entropy ones, which would name the exact model and build a visitor could be picked
/// out by, are never asked for.
/// </para>
/// <para>
/// Written by the client, so hostile like everything else on the ingest path. Nothing here is
/// ever stored as it arrived: each value is matched against a closed catalogue and what gets
/// written is the catalogue's own word. A visitor cannot put a string of their choosing into the
/// store through this route, which matters because the columns these feed are held as small sets
/// of repeated values and one arbitrary entry per request would ruin that for a whole site.
/// </para>
/// </remarks>
public sealed record ClientHints
{
    /// <summary>Nothing was sent. Every browser that does not implement these looks like this.</summary>
    public static ClientHints None { get; } = new();

    /// <summary>
    /// Whether the client says it is on a handheld device, from <c>Sec-CH-UA-Mobile</c>.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> when the header was absent or unreadable, which is the ordinary
    /// case for browsers outside the Chromium family and says nothing about the device.
    /// </remarks>
    public bool? Mobile { get; init; }

    /// <summary>Platform the client named, from <c>Sec-CH-UA-Platform</c>, exactly as sent.</summary>
    public string? Platform { get; init; }

    /// <summary>The brand list from <c>Sec-CH-UA</c>, exactly as sent.</summary>
    public string? Brands { get; init; }
}
