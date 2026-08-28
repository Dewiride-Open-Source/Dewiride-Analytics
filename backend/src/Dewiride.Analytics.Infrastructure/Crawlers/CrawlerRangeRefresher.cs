using System.Collections.Immutable;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Crawlers;

/// <summary>
/// Keeps a copy of every published crawler address file, and keeps what is loaded in step with it.
/// </summary>
/// <remarks>
/// <para>
/// Runs after the host is up. Together the files come to a few hundred kilobytes, so unlike the
/// place database there is no download worth waiting on — but they are fetched from a dozen other
/// companies' web servers, and none of them should be able to hold up a start.
/// </para>
/// <para>
/// Whatever is already on disk is loaded on the first pass, before anything is fetched, so a
/// restart recognises crawlers immediately and an install with no way out to the internet works
/// exactly as well as one with, provided somebody has put the files there.
/// </para>
/// <para>
/// Nothing is put into service without having been read first. A redirect to a sign-in page, a
/// truncated response, a file that has become something other than a list of addresses: each is
/// caught by parsing the candidate before it replaces anything, and each leaves the previous copy
/// working rather than taking the product's answers away. A file that fails every time simply
/// stops being refreshed, and the company it belongs to keeps being recognised from the last good
/// copy until somebody notices the warning.
/// </para>
/// </remarks>
/// <param name="store">Where the loaded addresses are published.</param>
/// <param name="options">Whether to fetch, how often, and where copies live.</param>
/// <param name="clients">Supplies the client the downloads are made with.</param>
/// <param name="timeProvider">Source of the staleness comparison and of the delay timer.</param>
/// <param name="logger">Log sink.</param>
internal sealed partial class CrawlerRangeRefresher(
    CrawlerRangeStore store,
    IOptions<CrawlerRangeOptions> options,
    IHttpClientFactory clients,
    TimeProvider timeProvider,
    ILogger<CrawlerRangeRefresher> logger) : BackgroundService
{
    /// <summary>Name the download client is registered under.</summary>
    public const string HttpClientName = "crawler-ranges";

    /// <summary>Suffix a download carries until it has been proven readable.</summary>
    private const string PartialSuffix = ".part";

    private int _published;

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

                // Loaded before anything is fetched, so a restart is recognising crawlers within a
                // moment rather than after a round of downloads from a dozen other companies.
                Load(settings);

                if (settings.AutoDownload)
                {
                    await RefreshAsync(settings, stoppingToken).ConfigureAwait(false);
                    Load(settings);
                }
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
    /// Fetches every file whose copy is missing or has gone stale.
    /// </summary>
    /// <remarks>
    /// One company's web server being down, slow or rearranged costs that company's file and
    /// nothing else, which is why each is fetched in its own attempt rather than as a batch that
    /// could fail as one.
    /// </remarks>
    private async Task RefreshAsync(CrawlerRangeOptions settings, CancellationToken cancellationToken)
    {
        var stale = timeProvider.GetUtcNow().UtcDateTime - settings.RefreshInterval;

        foreach (var file in CrawlerRangeFiles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Path.Combine(settings.Directory, file.Name);

            if (File.Exists(path) && File.GetLastWriteTimeUtc(path) >= stale)
            {
                continue;
            }

            await TryFetchAsync(settings, file, path, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fetches one file and proves it can be read before keeping it.
    /// </summary>
    private async Task TryFetchAsync(
        CrawlerRangeOptions settings,
        CrawlerRangeFile file,
        string path,
        CancellationToken cancellationToken)
    {
        var partial = path + PartialSuffix;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(settings.DownloadTimeout);

            var client = clients.CreateClient(HttpClientName);
            var json = await client.GetStringAsync(new Uri(file.Address), timeout.Token).ConfigureAwait(false);

            // Read before it is kept, and kept only if it held addresses. A file that parses to
            // nothing is a file that would silently stop this product recognising the company, so
            // the copy already on disk stays in service instead.
            if (PublishedRangeFile.Read(json).IsEmpty)
            {
                Log.Unusable(logger, file.Address);
                return;
            }

            await File.WriteAllTextAsync(partial, json, cancellationToken).ConfigureAwait(false);
            File.Move(partial, path, overwrite: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // This file's own allowance running out, not the host stopping. One slow web server
            // costs its own company's addresses and must not cost the ones fetched after it.
            Log.FetchTimedOut(logger, file.Address);
            Discard(partial);
        }
#pragma warning disable CA1031 // Any failure means this file is unavailable; the copy on disk stays in service.
        catch (Exception exception)
        {
            Log.FetchFailed(logger, file.Address, exception);
            Discard(partial);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Builds a table from every copy on disk and puts it into service.
    /// </summary>
    /// <remarks>
    /// Rebuilt in full rather than amended, because a company's file is a statement about all of
    /// its machines: a block it has stopped using has to leave, and merging a new file into an old
    /// table would keep an address in service after its operator had disowned it.
    /// <para>
    /// The addresses established one at a time, from the names they answer to, go with it for the
    /// same reason. They were never published as a set and so cannot be compared against a newer
    /// one; letting them expire alongside the files is what stops an address a company has stopped
    /// using from carrying its name for as long as this process runs. Each is established again the
    /// next time a visit from it is judged.
    /// </para>
    /// </remarks>
    private void Load(CrawlerRangeOptions settings)
    {
        var claims = ImmutableArray.CreateBuilder<ClaimedBlock>();

        foreach (var file in CrawlerRangeFiles.All)
        {
            foreach (var block in ReadCopy(Path.Combine(settings.Directory, file.Name)))
            {
                claims.Add(new ClaimedBlock(file.Operator, block));
            }
        }

        var table = CrawlerRangeTable.Build(claims.DrainToImmutable());

        store.Forget();
        store.Publish(table);

        if (table.Count == _published)
        {
            return;
        }

        _published = table.Count;
        Log.Loaded(logger, table.Count);
    }

    private static ImmutableArray<AddressBlock> ReadCopy(string path)
    {
        try
        {
            return File.Exists(path) ? PublishedRangeFile.Read(File.ReadAllText(path)) : [];
        }
        catch (IOException)
        {
            // Being written by the other half of this pass, or gone. Either way the next interval
            // reads it, and until then the addresses in it are simply not recognised.
            return [];
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

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1201,
            Level = LogLevel.Information,
            Message = "Loaded {Blocks} published crawler address range(s).")]
        public static partial void Loaded(ILogger logger, int blocks);

        [LoggerMessage(
            EventId = 1202,
            Level = LogLevel.Warning,
            Message = "Could not fetch the crawler address ranges published at {Address}.")]
        public static partial void FetchFailed(ILogger logger, string address, Exception exception);

        [LoggerMessage(
            EventId = 1203,
            Level = LogLevel.Warning,
            Message = "The crawler address ranges published at {Address} held nothing readable and were not used.")]
        public static partial void Unusable(ILogger logger, string address);

        [LoggerMessage(
            EventId = 1205,
            Level = LogLevel.Warning,
            Message = "Fetching the crawler address ranges published at {Address} took too long and was abandoned.")]
        public static partial void FetchTimedOut(ILogger logger, string address);

        [LoggerMessage(
            EventId = 1204,
            Level = LogLevel.Error,
            Message = "Refreshing the published crawler address ranges failed. Retrying on the next interval.")]
        public static partial void RefreshFailed(ILogger logger, Exception exception);
    }
}
