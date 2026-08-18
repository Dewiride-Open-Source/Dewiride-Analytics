using System.ComponentModel.DataAnnotations;

namespace Dewiride.Analytics.Api.Configuration;

/// <summary>
/// Limits applied to the public collection endpoint.
/// </summary>
/// <remarks>
/// The collector takes writes from anyone who knows a site identifier, and a site identifier is
/// printed in the page source of every page it measures. These limits are what stops that from
/// being an open write endpoint; they are configurable because a busy publisher and a personal
/// blog need very different numbers.
/// </remarks>
public sealed class CollectorOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:Collector";

    /// <summary>
    /// Largest accepted request body, in bytes. A report is a few hundred bytes; anything
    /// approaching this is not a report.
    /// </summary>
    [Range(512, 65536)]
    public int MaxRequestBytes { get; init; } = 8192;

    /// <summary>
    /// Reports accepted per minute from one network address.
    /// </summary>
    /// <remarks>
    /// Counted per address rather than per site because the address is the only thing known
    /// before the body is read. The default is generous: a single reader produces a handful of
    /// reports per page, while whole offices and mobile networks share one address, so a tight
    /// limit would discard real people long before it inconvenienced anyone.
    /// </remarks>
    [Range(1, 100000)]
    public int RequestsPerMinutePerAddress { get; init; } = 1200;
}
