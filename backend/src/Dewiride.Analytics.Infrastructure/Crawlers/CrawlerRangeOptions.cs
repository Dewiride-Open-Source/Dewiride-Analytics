namespace Dewiride.Analytics.Infrastructure.Crawlers;

/// <summary>
/// Where the crawler address files come from, and where they are kept.
/// </summary>
/// <remarks>
/// <para>
/// The addresses themselves are not configured. Which companies publish a file, and at which
/// address, is part of the catalogue the engine reasons with, because a file listed here decides
/// whose name goes beside a customer's traffic and that is not a deployment setting. What is
/// configurable is whether this installation may fetch them, how often, and where the copies live.
/// </para>
/// <para>
/// Nothing is vendored into the repository. The files change whenever a company commissions a new
/// machine, and a copy shipped with a release would be as old as the release — which for the
/// purpose they serve is worse than useless, since a stale file does not produce a wrong answer, it
/// produces a visit that quietly stops being recognised.
/// </para>
/// </remarks>
public sealed class CrawlerRangeOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:CrawlerRanges";

    /// <summary>
    /// Directory the downloaded files are kept in.
    /// </summary>
    /// <remarks>
    /// Beneath the reference-data directory by default, so the volume already mounted for that
    /// carries these too and an install keeps recognising crawlers across a restart without anybody
    /// having to add anything. Together they come to a few hundred kilobytes.
    /// </remarks>
    public string Directory { get; init; } = "/var/lib/dewiride/reference/crawlers";

    /// <summary>
    /// Whether the files may be fetched when they are missing or stale.
    /// </summary>
    /// <remarks>
    /// Turn this off for an install with no way out to the internet, put the files in
    /// <see cref="Directory"/> yourself, and everything else behaves identically. An install that
    /// fetches nothing and has nothing on disk still measures every visit; what it cannot do is
    /// tell a company's own crawler from something using its name, and it says so rather than
    /// guessing.
    /// </remarks>
    public bool AutoDownload { get; init; } = true;

    /// <summary>How often to look for a newer version of each file.</summary>
    /// <remarks>
    /// Twice a day. A company adding machines republishes without warning, and a crawler that
    /// arrives from an address published this morning should be recognised today rather than next
    /// week — but the files are small enough that this costs a few hundred kilobytes either way.
    /// </remarks>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromHours(12);

    /// <summary>How long a single download may take before it is abandoned and retried later.</summary>
    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Largest file accepted.
    /// </summary>
    /// <remarks>
    /// The largest of these is a few tens of kilobytes. This is not a tuning knob but a bound on
    /// what somebody else's web server can make this process allocate: the addresses are fetched
    /// from third parties, and a redirect to something enormous should cost a failed refresh
    /// rather than the machine.
    /// </remarks>
    public int LargestFileBytes { get; init; } = 4 * 1024 * 1024;
}
