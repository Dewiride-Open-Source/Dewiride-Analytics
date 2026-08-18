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

    /// <summary>
    /// Largest accepted body from a server-side reporter, in bytes.
    /// </summary>
    /// <remarks>
    /// Far larger than a single report, because a reporter sends batches. Kept as its own setting
    /// rather than raised for everyone: the browser collector takes writes from anybody who knows
    /// a site identifier, and it has no business accepting a quarter of a megabyte.
    /// </remarks>
    [Range(4096, 4194304)]
    public int MaxServerBatchBytes { get; init; } = 262144;

    /// <summary>
    /// Most observations accepted in one batch from a server-side reporter.
    /// </summary>
    [Range(1, 1000)]
    public int MaxEventsPerBatch { get; init; } = 100;

    /// <summary>
    /// Batches accepted per minute from one network address.
    /// </summary>
    /// <remarks>
    /// Counted in batches rather than observations, because the address is all that is known
    /// before the body is read. A reporter is one machine rather than a shared office connection,
    /// so this can be tighter per address than the browser allowance while carrying far more.
    /// </remarks>
    [Range(1, 1000000)]
    public int ServerBatchesPerMinutePerAddress { get; init; } = 600;
}
