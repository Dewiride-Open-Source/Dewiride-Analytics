using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Reads the sitemaps websites name, in the background.
/// </summary>
/// <remarks>
/// <para>
/// A few readers, each going from one website to the next for as long as any website's time has come, and
/// waiting a while when none has. The database says whose time has come, so nothing is lost if the process
/// stops part-way through and nothing has to be replayed when it starts again. The first look waits, so
/// the process is serving requests before it goes out to anybody's website.
/// </para>
/// <para>
/// A pass that fails as a whole ends there, the failure is written down, and that reader waits before
/// it looks again. A statement the client gives up on is a failure like any other: only the host shutting
/// down ends a reader, because an exception that leaves a background service stops the host, and with it
/// the collector.
/// </para>
/// </remarks>
/// <param name="passes">Runs one pass at a time.</param>
/// <param name="options">How many readers, and how long they wait.</param>
/// <param name="timeProvider">Source of the waits.</param>
/// <param name="logger">Log.</param>
public sealed class SitemapReadingService(
    SitemapPass passes,
    IOptions<SitemapOptions> options,
    TimeProvider timeProvider,
    ILogger<SitemapReadingService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            SitemapLog.Disabled(logger);

            return;
        }

        if (settings.SitesAtOnce == 0)
        {
            SitemapLog.LeftToOthers(logger);

            return;
        }

        var readers = Enumerable.Range(0, settings.SitesAtOnce).Select(_ => ReadAsync(settings.Interval, stoppingToken));

        await Task.WhenAll(readers).ConfigureAwait(false);
    }

    private async Task ReadAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(interval, timeProvider, stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await TryPassAsync(stoppingToken).ConfigureAwait(false))
                {
                    await Task.Delay(interval, timeProvider, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down part-way through, whether the work was told to stop or a store went down with
            // the machine first. Whatever the pass reached has already been written, and the next start
            // resumes from there.
        }
    }

    /// <summary>
    /// Runs one pass.
    /// </summary>
    /// <returns>Whether there might be more to do at once.</returns>
    private async Task<bool> TryPassAsync(CancellationToken stoppingToken)
    {
        try
        {
            return await passes.RunNextAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (!stoppingToken.IsCancellationRequested)
        {
            SitemapLog.RunFailed(logger, failure);

            return false;
        }
    }
}
