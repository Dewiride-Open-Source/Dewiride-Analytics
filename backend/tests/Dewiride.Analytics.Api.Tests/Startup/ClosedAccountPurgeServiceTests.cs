using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Dashboard;
using Dewiride.Analytics.Infrastructure.Accounts;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Dewiride.Analytics.Api.Tests.Startup;

/// <summary>
/// Covers what keeps the hourly sweep of closed accounts running.
/// </summary>
/// <remarks>
/// An exception that leaves a background service stops the host, and the sweep starts by reading the
/// control plane, so a statement that takes longer than the client will wait is the likeliest thing
/// to end a pass. What a pass does when it runs is covered against real stores elsewhere.
/// </remarks>
public sealed class ClosedAccountPurgeServiceTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>
    /// A pass that fails, whether outright or because a client stopped waiting, ends there, is written
    /// down, and the next pass runs.
    /// </summary>
    /// <param name="failure">How the pass fails.</param>
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_Pass_That_Fails_Does_Not_Stop_The_Engine(Exception failure)
    {
        var clock = new WorkerClock(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));
        var logger = new FakeLogger<ClosedAccountPurgeService>();
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(_ => throw failure);

        var sweep = new ClosedAccountPurge(
            scopes,
            Substitute.For<ITelemetryPurge>(),
            Substitute.For<HybridCache>(),
            Options.Create(new DashboardOptions()),
            clock,
            NullLogger<ClosedAccountPurge>.Instance);

        using var service = new ClosedAccountPurgeService(sweep, clock, logger);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WorkerClock.EventuallyAsync(() => clock.Timers == 1);

        clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => logger.Collector.Count == 1);

        clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => logger.Collector.Count == 2);

        service.ExecuteTask!.IsCompleted.Should().BeFalse();
        logger.LatestRecord.Id.Id.Should().Be(3608);
        logger.LatestRecord.Level.Should().Be(LogLevel.Error);

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The host shutting down part-way through a pass is not a failure, whether the statement in hand
    /// was cancelled or the store it was asking went down with the machine: the service says nothing
    /// of it, and ends rather than waiting for the next hour.
    /// </summary>
    /// <param name="ending">How the statement in hand ends once the host is stopping.</param>
    [Theory]
    [MemberData(nameof(Endings))]
    public async Task Shutting_Down_Part_Way_Through_A_Pass_Is_Not_A_Failure(Exception ending)
    {
        var clock = new WorkerClock(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));
        var logger = new FakeLogger<ClosedAccountPurgeService>();
        var scopes = Substitute.For<IServiceScopeFactory>();

        var sweep = new ClosedAccountPurge(
            scopes,
            Substitute.For<ITelemetryPurge>(),
            Substitute.For<HybridCache>(),
            Options.Create(new DashboardOptions()),
            clock,
            NullLogger<ClosedAccountPurge>.Instance);

        using var service = new ClosedAccountPurgeService(sweep, clock, logger);

        // The host stops while the statement is in hand and the statement then fails, which is the
        // order a machine going down puts them in. Stopping cancels the service's token before it
        // returns. Nothing here may block: the work runs on the thread that moves the clock.
        scopes.CreateScope().Returns(call =>
        {
            _ = service.StopAsync(TestContext.Current.CancellationToken);

            throw ending;
        });

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WorkerClock.EventuallyAsync(() => clock.Timers == 1);

        clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => service.ExecuteTask!.IsCompleted);

        service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        logger.Collector.Count.Should().Be(0);
    }

    /// <summary>How a statement ends once the host is stopping: cancelled, or its store gone.</summary>
    public static TheoryData<Exception> Endings =>
    [
        new OperationCanceledException("The host is stopping."),
        new InvalidOperationException("The store went down with the machine."),
    ];

    /// <summary>The ways a store fails: refusing outright, and a client giving up on a statement.</summary>
    public static TheoryData<Exception> Failures =>
    [
        new InvalidOperationException("The store went away."),
        new TaskCanceledException("The client stopped waiting for the store."),
    ];
}
