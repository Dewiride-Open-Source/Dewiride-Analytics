using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// Runs the closed-account sweep on a timer.
/// </summary>
/// <remarks>
/// Hourly, and the first tick waits: a closed account has thirty days before anything is deleted
/// and a week's notice before that, so nothing needs the first hour of a process's life, and the
/// process is serving requests before it starts deleting anything. A pass that fails is logged and
/// the next pass starts from the facts as they then are, since every account is chosen afresh
/// each time.
/// </remarks>
/// <param name="purge">The sweep.</param>
/// <param name="timeProvider">Source of the timer.</param>
/// <param name="logger">Log.</param>
public sealed partial class ClosedAccountPurgeService(
    ClosedAccountPurge purge,
    TimeProvider timeProvider,
    ILogger<ClosedAccountPurgeService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await purge.RunAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    Log.PassFailed(logger, failure);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down part-way through. Every account the pass finished is finished, and the
            // next start chooses the rest again.
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 3608,
            Level = LogLevel.Error,
            Message = "The closed-account sweep did not complete. It runs again on the next interval.")]
        public static partial void PassFailed(ILogger logger, Exception failure);
    }
}
