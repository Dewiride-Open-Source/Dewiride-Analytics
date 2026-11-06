using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Sites;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Api.Startup;

/// <summary>
/// Keeps every website's finished months in the background.
/// </summary>
/// <remarks>
/// <para>
/// A timer rather than a queue, on the terms judging runs on: what needs counting is whatever month
/// has finished, changed or not been counted since the last run, and the store itself is the record
/// of that. Nothing is lost if the process stops part-way through, because nothing is written until
/// a month has been counted whole.
/// </para>
/// <para>
/// The first run waits a minute rather than a whole interval, so an installation's history is kept
/// soon after it starts, but not before it is serving requests. A failure on one website does not
/// stop the others, a run that fails as a whole does not stop the loop, and a statement the client
/// gives up on is a failure like any other rather than a reason to stop.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates the scope each run's work is resolved from.</param>
/// <param name="options">When and how often to run.</param>
/// <param name="timeProvider">Source of the timer.</param>
/// <param name="logger">Log sink.</param>
internal sealed partial class ConsolidationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ConsolidationOptions> options,
    TimeProvider timeProvider,
    ILogger<ConsolidationWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            Log.Disabled(logger);
            return;
        }

        try
        {
            await Task.Delay(settings.StartDelay, timeProvider, stoppingToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(settings.Interval, timeProvider);

            do
            {
                try
                {
                    await RunAsync(settings, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception failure) when (!stoppingToken.IsCancellationRequested)
                {
                    // The stores are remote services. A run that could not even list the websites
                    // ends there, and the next one tries again: stopping here would stop the whole
                    // engine, collector included, over a database that was restarting for a moment.
                    Log.RunFailed(logger, failure);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down part-way through, whether the work was told to stop or a store went down
            // with the machine first. Every month already written was counted whole, and the next
            // start counts whatever this run did not reach.
        }
    }

    private async Task RunAsync(ConsolidationOptions settings, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();

        await using var scope = scopeFactory.CreateAsyncScope();

        var roster = scope.ServiceProvider.GetRequiredService<ISiteRoster>();
        var consolidation = scope.ServiceProvider.GetRequiredService<MonthlyConsolidation>();
        var sites = await roster.ListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var site in sites)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await consolidation.CatchUpAsync(site, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
            {
                Log.SiteFailed(logger, failure, site.Id);
            }
        }

        var elapsed = timeProvider.GetElapsedTime(started);

        if (elapsed > settings.Interval)
        {
            Log.Overran(logger, sites.Count, elapsed, settings.Interval);
        }
        else
        {
            Log.Ran(logger, sites.Count, elapsed);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 5201,
            Level = LogLevel.Debug,
            Message = "Kept the finished months of {Sites} websites in {Elapsed}.")]
        public static partial void Ran(ILogger logger, int sites, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 5202,
            Level = LogLevel.Warning,
            Message = "Keeping each website's monthly totals is switched off. Nothing already kept is "
                + "affected, but a month whose reports expire while it is off is never counted, and its "
                + "totals are lost with them.")]
        public static partial void Disabled(ILogger logger);

        [LoggerMessage(
            EventId = 5204,
            Level = LogLevel.Error,
            Message = "Keeping the months of website {SiteId} failed. The next run tries again.")]
        public static partial void SiteFailed(ILogger logger, Exception failure, Guid siteId);

        [LoggerMessage(
            EventId = 5205,
            Level = LogLevel.Warning,
            Message = "Keeping the finished months of {Sites} websites took {Elapsed}, longer than the "
                + "{Interval} between runs, so runs now follow one another without a pause.")]
        public static partial void Overran(ILogger logger, int sites, TimeSpan elapsed, TimeSpan interval);

        [LoggerMessage(
            EventId = 5207,
            Level = LogLevel.Error,
            Message = "Keeping the finished months could not run. The next run tries again.")]
        public static partial void RunFailed(ILogger logger, Exception failure);
    }
}
