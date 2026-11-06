using Microsoft.Extensions.Time.Testing;

namespace Dewiride.Analytics.Api.Tests.Startup;

/// <summary>
/// A clock a test moves, which counts the timers set on it, so the test moves it only once the work
/// has set the timer it is waiting on: background work starts on a thread of its own, and a clock
/// moved before a timer is set never fires it.
/// </summary>
/// <param name="start">The moment it starts at.</param>
internal sealed class WorkerClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private int _timers;

    /// <summary>How many timers have been set on it.</summary>
    public int Timers => Volatile.Read(ref _timers);

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _timers);

        return timer;
    }

    /// <summary>
    /// Gives anything the clock just set going the chance to run, so a check that nothing happened is
    /// a check that nothing happened rather than that it had not happened yet.
    /// </summary>
    /// <returns>A task that completes once the chance has passed.</returns>
    public static Task QuietAsync() => Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

    /// <summary>Waits until a token is cancelled, and returns rather than throwing.</summary>
    /// <param name="token">The token.</param>
    /// <returns>A task that completes once it is cancelled.</returns>
    public static Task UntilCancelledAsync(CancellationToken token)
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => cancelled.TrySetResult());

        return cancelled.Task;
    }

    /// <summary>Waits, in real time, for something the clock set going to have happened.</summary>
    /// <param name="happened">Whether it has.</param>
    /// <returns>A task that completes once it has.</returns>
    public static async Task EventuallyAsync(Func<bool> happened)
    {
        var began = System.GetTimestamp();

        while (!happened())
        {
            System.GetElapsedTime(began).Should().BeLessThan(TimeSpan.FromSeconds(10), "what the clock set going should have happened by now");
            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        }
    }
}
