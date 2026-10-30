using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.Telemetry;

/// <summary>
/// Keeps the visitor-key salt current and deletes expired ones.
/// </summary>
/// <remarks>
/// Runs at startup and hourly rather than at midnight, so that a process started at any time has a
/// usable salt immediately. Every run also makes the next day's salt, so a long-running process
/// holds it hours before midnight, wherever in the hour its runs happen to fall — and a run that
/// fails is followed by many more before the salt is needed.
/// </remarks>
/// <param name="saltStore">The store to rotate.</param>
/// <param name="timeProvider">Source of the delay timer.</param>
/// <param name="logger">Log sink.</param>
public sealed partial class VisitorKeySaltRotationService(
    VisitorKeySaltStore saltStore,
    TimeProvider timeProvider,
    ILogger<VisitorKeySaltRotationService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);

        do
        {
            try
            {
                await saltStore.RotateAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed rotation must not stop the service; the next tick retries.
            catch (Exception exception)
            {
                Log.RotationFailed(logger, exception);
            }
#pragma warning restore CA1031
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Error,
            Message = "Visitor-key salt rotation failed. Retrying on the next interval.")]
        public static partial void RotationFailed(ILogger logger, Exception exception);
    }
}
