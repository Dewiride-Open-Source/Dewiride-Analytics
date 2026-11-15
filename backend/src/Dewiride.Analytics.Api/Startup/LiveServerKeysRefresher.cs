using Dewiride.Analytics.Api.Ingest;
using Dewiride.Analytics.Application.Sites;

namespace Dewiride.Analytics.Api.Startup;

/// <summary>
/// Reads which server keys are live, once before the collector takes a batch and every minute
/// after.
/// </summary>
/// <remarks>
/// The first reading is made while the host is starting, so the first batch after a restart is
/// already counted against its key. A reading that fails leaves the last one in place and is tried
/// again at the next minute: the list decides only whose allowance a batch is counted against, and
/// whether a key is accepted is decided elsewhere, so a stale list costs some batches an allowance
/// they share and never lets one in.
/// </remarks>
/// <param name="live">Where the reading is kept.</param>
/// <param name="scopeFactory">Supplies the store the reading is made from.</param>
/// <param name="timeProvider">Clock the minute is kept by.</param>
/// <param name="logger">Log sink.</param>
internal sealed partial class LiveServerKeysRefresher(
    LiveServerKeys live,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<LiveServerKeysRefresher> logger) : BackgroundService
{
    /// <summary>How often the list is read again.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RefreshAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping, which is the only way this loop ends.
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var keys = await scope.ServiceProvider.GetRequiredService<IIngestKeyCatalog>()
                .ListLiveAsync(cancellationToken)
                .ConfigureAwait(false);

            live.Replace(keys);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            // The control-plane database is a remote service. A reading that fails keeps the last
            // one, and the next minute tries again: stopping here would stop the collector over a
            // database that was restarting for a moment.
            Log.RefreshFailed(logger, failure);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 5301,
            Level = LogLevel.Warning,
            Message = "Could not read which server keys are live; the last reading stays in use.")]
        public static partial void RefreshFailed(ILogger logger, Exception exception);
    }
}
