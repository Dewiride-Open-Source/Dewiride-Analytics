namespace Dewiride.Analytics.Infrastructure.Network;

/// <summary>
/// Where the address-to-place and address-to-network data comes from, and where it is kept.
/// </summary>
/// <remarks>
/// <para>
/// Neither dataset is committed to this repository. One is a hundred and twenty megabytes and
/// republished every month, and vendoring it would mean an install's answers were as old as the
/// release it was installed from. They are fetched instead, into a directory that outlives the
/// container.
/// </para>
/// <para>
/// Both are free to redistribute and neither needs an account or a key, which is why they were
/// chosen over the better-known alternatives — a lookup that stops working when somebody's free
/// tier lapses is not something to build a measurement on. What they do carry is an attribution
/// requirement, and it reaches the interface rather than a file: see <c>NOTICE</c>.
/// </para>
/// </remarks>
public sealed class ReferenceDataOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:ReferenceData";

    /// <summary>
    /// Directory the downloaded files are kept in.
    /// </summary>
    /// <remarks>
    /// Mount a volume here. Left inside the container the files are fetched again on every
    /// deployment, which is a hundred and twenty megabytes each time for data that changes
    /// monthly.
    /// </remarks>
    public string Directory { get; init; } = "/var/lib/dewiride/reference";

    /// <summary>
    /// Whether the files may be fetched when they are missing or stale.
    /// </summary>
    /// <remarks>
    /// Turn this off for an install with no way out to the internet, put the files in
    /// <see cref="Directory"/> yourself, and everything else behaves identically.
    /// </remarks>
    public bool AutoDownload { get; init; } = true;

    /// <summary>
    /// Where the place data is published, with <c>{release}</c> standing for its month.
    /// </summary>
    /// <remarks>
    /// DB-IP Lite, under a Creative Commons Attribution licence. Released monthly and named after
    /// the month, so the current release's address is derived rather than configured.
    /// </remarks>
    public string PlacesUrl { get; init; } = "https://download.db-ip.com/free/dbip-city-lite-{release}.mmdb.gz";

    /// <summary>Where the network data is published. iptoasn.com, in the public domain.</summary>
    public string NetworksUrl { get; init; } = "https://iptoasn.com/data/ip2asn-combined.tsv.gz";

    /// <summary>How often to check whether a newer release has been published.</summary>
    /// <remarks>
    /// Daily. The place data is monthly and the network data hourly, and neither moves fast
    /// enough for a country to change under a visitor between one check and the next.
    /// </remarks>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromHours(24);

    /// <summary>How long a single download may take before it is abandoned and retried later.</summary>
    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromMinutes(20);
}
