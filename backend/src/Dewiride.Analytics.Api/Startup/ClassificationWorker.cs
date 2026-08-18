using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Api.Startup;

/// <summary>
/// Judges finished visits in the background.
/// </summary>
/// <remarks>
/// <para>
/// A timer rather than a queue, because there is nothing to enqueue: what needs judging is
/// whatever has happened since the last bookmark, and the events themselves are the record of
/// that. Nothing is lost if the process stops mid-run, and nothing has to be replayed when it
/// starts again.
/// </para>
/// <para>
/// A failure on one site does not stop the others and does not stop the loop. The stores are
/// remote services, and a run that gave up permanently the first time one of them was briefly
/// unreachable would leave an installation silently no longer classifying anything.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates the scope each run's database work is resolved from.</param>
/// <param name="options">How often to run, and how much to work through.</param>
/// <param name="timeProvider">Source of the timer.</param>
/// <param name="logger">Log sink.</param>
internal sealed partial class ClassificationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ClassificationOptions> options,
    TimeProvider timeProvider,
    ILogger<ClassificationWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            Log.Disabled(logger);
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval, timeProvider);

        try
        {
            // The first tick waits, so the process is serving requests before it starts reading a
            // columnar store — which on a small machine is the difference between a slow start-up
            // and a failed health probe.
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down part-way through. Whatever the run reached has already been recorded,
            // and the next start resumes from there.
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var sites = scope.ServiceProvider.GetRequiredService<ISiteRoster>();
        var classifier = scope.ServiceProvider.GetRequiredService<SessionClassifier>();

        foreach (var site in await sites.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await classifier.CatchUpAsync(site.Id, site.AddedAt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                Log.SiteFailed(logger, failure, site.Id);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 5001,
            Level = LogLevel.Warning,
            Message = "Judging traffic is switched off. Visits are still collected, and nothing "
                + "already judged is affected, but no new verdicts will be reached.")]
        public static partial void Disabled(ILogger logger);

        [LoggerMessage(
            EventId = 5002,
            Level = LogLevel.Error,
            Message = "Judging traffic on site {SiteId} failed. The next run resumes from the same point.")]
        public static partial void SiteFailed(ILogger logger, Exception failure, Guid siteId);
    }
}
