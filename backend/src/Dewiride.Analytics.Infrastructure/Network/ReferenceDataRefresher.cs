using System.Globalization;
using System.IO.Compression;
using System.Net;
using MaxMind.Db;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Network;

/// <summary>
/// Keeps the place and network data on disk, and keeps what is loaded in step with it.
/// </summary>
/// <remarks>
/// <para>
/// Runs after the host is up rather than during startup. The place database is a hundred and
/// twenty megabytes, and an install whose first boot waited on that download would look broken
/// for a quarter of an hour on somebody's home connection. Traffic is measured throughout;
/// what it lacks until the download lands is the country, which the interface already reports
/// honestly as not known.
/// </para>
/// <para>
/// Whatever is already on disk is loaded first, on the first pass, so a restart is instant and an
/// install with no way out to the internet works exactly as well as one with — provided somebody
/// has put the files there.
/// </para>
/// <para>
/// Nothing is ever published without having been read first. A truncated download, an error page
/// served instead of a database, a file half-written when the machine lost power: each is caught
/// by opening the candidate before it replaces anything, and each leaves the previous data in
/// service rather than taking the product's answers away.
/// </para>
/// </remarks>
/// <param name="store">Where loaded data is published.</param>
/// <param name="options">Where the data comes from and how often to look.</param>
/// <param name="clients">Supplies the client the downloads are made with.</param>
/// <param name="timeProvider">Source of the current release name and of the delay timer.</param>
/// <param name="logger">Log sink.</param>
internal sealed partial class ReferenceDataRefresher(
    ReferenceDataStore store,
    IOptions<ReferenceDataOptions> options,
    IHttpClientFactory clients,
    TimeProvider timeProvider,
    ILogger<ReferenceDataRefresher> logger) : BackgroundService
{
    /// <summary>Name the download client is registered under.</summary>
    public const string HttpClientName = "reference-data";

    /// <summary>What every release of the place database is called on disk, before its month.</summary>
    private const string PlacesPrefix = "dbip-city-lite-";

    /// <summary>And after it.</summary>
    private const string PlacesSuffix = ".mmdb";

    /// <summary>The two together, as a pattern for finding every release present.</summary>
    private const string PlacesSearch = PlacesPrefix + "*" + PlacesSuffix;

    /// <summary>What the network table is called on disk.</summary>
    private const string NetworksName = "ip2asn-combined.tsv.gz";

    /// <summary>Stands in for the release month in the published address.</summary>
    private const string ReleaseToken = "{release}";

    /// <summary>Suffix a download carries until it has been proven readable.</summary>
    private const string PartialSuffix = ".part";

    private string _loadedPlaces = string.Empty;
    private DateTime _loadedNetworks = DateTime.MinValue;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        using var timer = new PeriodicTimer(settings.RefreshInterval, timeProvider);

        do
        {
            try
            {
                Directory.CreateDirectory(settings.Directory);

                await RefreshPlacesAsync(settings, stoppingToken).ConfigureAwait(false);
                await RefreshNetworksAsync(settings, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Whatever went wrong, the next interval retries and measurement continues meanwhile.
            catch (Exception exception)
            {
                Log.RefreshFailed(logger, exception);
            }
#pragma warning restore CA1031
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Brings the place database up to the current release, then loads whatever is newest.
    /// </summary>
    /// <remarks>
    /// The month just begun is tried first and the one before it second. A release is published
    /// during its own month rather than before it, so on the first of the month the newer address
    /// does not exist yet, and treating that as a failure would leave a fresh install with nothing
    /// for a day.
    /// </remarks>
    private async Task RefreshPlacesAsync(ReferenceDataOptions settings, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        if (settings.AutoDownload && !File.Exists(PlacesPath(settings, Release(now))))
        {
            var current = await TryFetchPlacesAsync(settings, Release(now), cancellationToken)
                .ConfigureAwait(false);

            if (!current)
            {
                _ = await TryFetchPlacesAsync(settings, Release(now.AddMonths(-1)), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var newest = NewestPlaces(settings);

        if (newest is null || string.Equals(newest, _loadedPlaces, StringComparison.Ordinal))
        {
            return;
        }

        store.PublishPlaces(new Reader(newest));
        _loadedPlaces = newest;

        Log.PlacesLoaded(logger, newest);
        RemoveSupersededPlaces(settings, newest);
    }

    /// <summary>
    /// Brings the network table up to date, then loads it if it has changed.
    /// </summary>
    /// <remarks>
    /// Published hourly at one unchanging address, so freshness is judged by the local copy's own
    /// age rather than by its name.
    /// </remarks>
    private async Task RefreshNetworksAsync(ReferenceDataOptions settings, CancellationToken cancellationToken)
    {
        var path = Path.Combine(settings.Directory, NetworksName);
        var due = !File.Exists(path)
            || File.GetLastWriteTimeUtc(path) < timeProvider.GetUtcNow().UtcDateTime - settings.RefreshInterval;

        if (settings.AutoDownload && due)
        {
            await TryFetchNetworksAsync(settings, path, cancellationToken).ConfigureAwait(false);
        }

        if (!File.Exists(path))
        {
            return;
        }

        var written = File.GetLastWriteTimeUtc(path);

        if (written == _loadedNetworks)
        {
            return;
        }

        var table = await AutonomousSystemTable.LoadAsync(path, cancellationToken).ConfigureAwait(false);

        if (table.Count == 0)
        {
            Log.NetworksUnreadable(logger, path);
            return;
        }

        store.PublishNetworks(table);
        _loadedNetworks = written;

        Log.NetworksLoaded(logger, table.Count);
    }

    /// <summary>
    /// Fetches one release of the place database and proves it can be read before keeping it.
    /// </summary>
    /// <returns>Whether a usable file now sits at the release's path.</returns>
    private async Task<bool> TryFetchPlacesAsync(
        ReferenceDataOptions settings,
        string release,
        CancellationToken cancellationToken)
    {
        var destination = PlacesPath(settings, release);
        var partial = destination + PartialSuffix;
        var address = settings.PlacesUrl.Replace(ReleaseToken, release, StringComparison.Ordinal);

        try
        {
            await DownloadAsync(settings, address, partial, expand: true, cancellationToken).ConfigureAwait(false);

            // Opened and closed before the file is moved into place, so a download that finished
            // cleanly but is not a database never becomes the database in service. Opening is a
            // real check rather than a formality: this format keeps its description at the end of
            // the file, so a truncated download has nothing to open and a web page served in its
            // stead has nothing to find. The reader maps the file, so it has to be let go before
            // anything can rename it.
            using (var candidate = new Reader(partial))
            {
                // Walking the tree as well as opening the file. Opening reads the description,
                // which this format keeps at the end, so a truncated download or a web page
                // served in the database's stead fails here; searching then proves the tree
                // between the two is intact. The answer is of no interest — the address is not
                // in any geolocation database — only that asking did not throw.
                _ = candidate.Find<PlaceRecord>(IPAddress.Loopback);
            }

            File.Move(partial, destination, overwrite: true);
            return true;
        }
#pragma warning disable CA1031 // Any failure means this release is unavailable; the caller tries the one before.
        catch (Exception exception)
        {
            Log.PlacesFetchFailed(logger, release, exception);
            Discard(partial);
            return false;
        }
#pragma warning restore CA1031
    }

    private async Task TryFetchNetworksAsync(
        ReferenceDataOptions settings,
        string destination,
        CancellationToken cancellationToken)
    {
        var partial = destination + PartialSuffix;

        try
        {
            await DownloadAsync(settings, settings.NetworksUrl, partial, expand: false, cancellationToken)
                .ConfigureAwait(false);

            var candidate = await AutonomousSystemTable.LoadAsync(partial, cancellationToken).ConfigureAwait(false);

            if (candidate.Count == 0)
            {
                Log.NetworksUnreadable(logger, partial);
                Discard(partial);
                return;
            }

            File.Move(partial, destination, overwrite: true);
        }
#pragma warning disable CA1031 // The previous copy stays in service and the next interval retries.
        catch (Exception exception)
        {
            Log.NetworksFetchFailed(logger, exception);
            Discard(partial);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Downloads to a temporary name, optionally unpacking on the way.
    /// </summary>
    /// <param name="settings">Where to fetch from and how long to allow.</param>
    /// <param name="address">The published address.</param>
    /// <param name="destination">Where to write, which is never the name the file will end up with.</param>
    /// <param name="expand">
    /// Whether to unpack. The place database is read from a plain file and the network table from
    /// a packed one, so one is unpacked here and the other kept as it arrived.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task DownloadAsync(
        ReferenceDataOptions settings,
        string address,
        string destination,
        bool expand,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.DownloadTimeout);

        var client = clients.CreateClient(HttpClientName);

        using var response = await client
            .GetAsync(new Uri(address), HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var incoming = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        await using var file = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);

        if (!expand)
        {
            await incoming.CopyToAsync(file, timeout.Token).ConfigureAwait(false);
            return;
        }

        await using var expanded = new GZipStream(incoming, CompressionMode.Decompress);
        await expanded.CopyToAsync(file, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// The newest release present on disk, whether or not it is the current one.
    /// </summary>
    /// <remarks>
    /// Releases are named after their month, so their names sort into the order they were
    /// published in and the last one is the newest without a date having to be read.
    /// </remarks>
    private static string? NewestPlaces(ReferenceDataOptions settings) =>
        Directory.Exists(settings.Directory)
            ? Directory.EnumerateFiles(settings.Directory, PlacesSearch)
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .FirstOrDefault()
            : null;

    /// <summary>
    /// Deletes every release except the one in service.
    /// </summary>
    /// <remarks>
    /// Each is a few hundred megabytes and a new one arrives monthly, so an install left alone
    /// for a year would otherwise fill its volume with eleven copies nothing will ever open.
    /// </remarks>
    private static void RemoveSupersededPlaces(ReferenceDataOptions settings, string keep)
    {
        var superseded = Directory.EnumerateFiles(settings.Directory, PlacesSearch)
            .Where(path => !string.Equals(path, keep, StringComparison.Ordinal));

        foreach (var path in superseded)
        {
            Discard(path);
        }
    }

    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Still held open, or gone already. Neither is worth interrupting a refresh over.
        }
        catch (UnauthorizedAccessException)
        {
            // The directory is not ours to tidy.
        }
    }

    private static string PlacesPath(ReferenceDataOptions settings, string release) =>
        Path.Combine(settings.Directory, PlacesPrefix + release + PlacesSuffix);

    private static string Release(DateTimeOffset moment) =>
        moment.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1101,
            Level = LogLevel.Information,
            Message = "Loaded visitor place data from {File}.")]
        public static partial void PlacesLoaded(ILogger logger, string file);

        [LoggerMessage(
            EventId = 1102,
            Level = LogLevel.Information,
            Message = "Loaded {Ranges} network ranges.")]
        public static partial void NetworksLoaded(ILogger logger, int ranges);

        [LoggerMessage(
            EventId = 1103,
            Level = LogLevel.Warning,
            Message = "Could not fetch the {Release} release of the visitor place data.")]
        public static partial void PlacesFetchFailed(ILogger logger, string release, Exception exception);

        [LoggerMessage(
            EventId = 1104,
            Level = LogLevel.Warning,
            Message = "Could not fetch the network table.")]
        public static partial void NetworksFetchFailed(ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 1105,
            Level = LogLevel.Warning,
            Message = "The network table at {File} held no usable ranges and was not used.")]
        public static partial void NetworksUnreadable(ILogger logger, string file);

        [LoggerMessage(
            EventId = 1106,
            Level = LogLevel.Error,
            Message = "Refreshing the visitor reference data failed. Retrying on the next interval.")]
        public static partial void RefreshFailed(ILogger logger, Exception exception);
    }
}
