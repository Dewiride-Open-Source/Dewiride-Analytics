using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Ingest;

/// <summary>
/// What a capture surface reports about one observed activity.
/// </summary>
/// <remarks>
/// Everything here is untrusted. It arrives from a public unauthenticated endpoint and, for the
/// browser surfaces, from a script the site's visitor can modify. Nothing on this type may be
/// interpolated into SQL, rendered as HTML, or placed in a model prompt as instructions.
/// </remarks>
public sealed record IngestCommand
{
    /// <summary>Public identifier of the site the activity belongs to.</summary>
    public required Guid SiteId { get; init; }

    /// <summary>What is being reported.</summary>
    public required EventKind Kind { get; init; }

    /// <summary>Absolute URL of the page, as the surface saw it.</summary>
    public required string Url { get; init; }

    /// <summary>Referring URL, where one was present.</summary>
    public string? Referrer { get; init; }

    /// <summary>Client's claimed time in Unix milliseconds. Never trusted; used to derive clock skew.</summary>
    public long? ClientTimestampUnixMs { get; init; }

    /// <summary>Viewport width in CSS pixels.</summary>
    public int? ViewportWidth { get; init; }

    /// <summary>Viewport height in CSS pixels.</summary>
    public int? ViewportHeight { get; init; }

    /// <summary>Primary language the client declared.</summary>
    public string? Language { get; init; }

    /// <summary>Client's UTC offset in minutes.</summary>
    public short? TimezoneOffsetMinutes { get; init; }

    /// <summary>Engaged milliseconds accrued while the tab was visible.</summary>
    public int? EngagedMs { get; init; }

    /// <summary>Furthest scroll depth reached, as a percentage of document height.</summary>
    public byte? ScrollDepthPercent { get; init; }

    /// <summary>Whether any pointer interaction occurred. Presence only.</summary>
    public bool? HadPointerInteraction { get; init; }

    /// <summary>Whether any keyboard interaction occurred. Presence only, never content.</summary>
    public bool? HadKeyboardInteraction { get; init; }

    /// <summary>Whether the client reported itself as being under automation control.</summary>
    public bool? DeclaredWebDriver { get; init; }

    /// <summary>Correlation identifier echoed back from the served HTML.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// What the server itself observed about the request carrying an <see cref="IngestCommand"/>.
/// </summary>
/// <remarks>
/// These values come from the transport rather than the payload, so they are the ones a client
/// cannot simply assert. They are kept separate from the command for exactly that reason —
/// mixing trusted and untrusted values into one bag is how the distinction gets lost.
/// </remarks>
public sealed record IngestContext
{
    /// <summary>Which capture surface produced the request.</summary>
    public required IngestSurface Surface { get; init; }

    /// <summary>User-agent header as received.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Network address the request came from.</summary>
    public string? IpAddress { get; init; }

    /// <summary>Origin or Referer host, used to check the request came from the site it claims.</summary>
    public string? RequestOrigin { get; init; }

    /// <summary>HTTP status the site returned, where the surface can observe it.</summary>
    public short? StatusCode { get; init; }

    /// <summary>Response content type, where the surface can observe it.</summary>
    public string? ContentType { get; init; }

    /// <summary>Response size in bytes, where the surface can observe it.</summary>
    public long? ResponseBytes { get; init; }
}

/// <summary>Why an ingest attempt ended the way it did.</summary>
public enum IngestOutcome
{
    /// <summary>The event was accepted and written.</summary>
    Accepted = 0,

    /// <summary>
    /// No such site, or the request did not come from an origin the site permits. Deliberately
    /// one outcome: reporting them separately would let anyone probe which site identifiers are
    /// real.
    /// </summary>
    Rejected = 1,

    /// <summary>The payload was malformed.</summary>
    Invalid = 2,
}
