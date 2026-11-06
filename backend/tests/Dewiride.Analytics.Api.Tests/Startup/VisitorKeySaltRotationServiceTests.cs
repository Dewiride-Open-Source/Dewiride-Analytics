using Dewiride.Analytics.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;

namespace Dewiride.Analytics.Api.Tests.Startup;

/// <summary>
/// Covers what keeps the hourly making of the visitor key's salt running.
/// </summary>
/// <remarks>
/// An exception that leaves a background service stops the host. A rotation begins by reading the
/// control plane, so that is what fails it, and it is driven here over a control plane that cannot be
/// reached. What a rotation makes and deletes is covered against a real one elsewhere.
/// </remarks>
public sealed class VisitorKeySaltRotationServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>
    /// A rotation that fails, whether outright or because a client stopped waiting, is written down
    /// and the next one runs.
    /// </summary>
    /// <param name="failure">How the rotation fails.</param>
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_Rotation_That_Fails_Does_Not_Stop_The_Engine(Exception failure)
    {
        var clock = new WorkerClock(Start);
        var logger = new FakeLogger<VisitorKeySaltRotationService>();
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(_ => throw failure);

        using var service = new VisitorKeySaltRotationService(
            new VisitorKeySaltStore(scopes, clock, NullLogger<VisitorKeySaltStore>.Instance),
            clock,
            logger);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WorkerClock.EventuallyAsync(() => logger.Collector.Count == 1 && clock.Timers == 1);

        clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => logger.Collector.Count == 2);

        service.ExecuteTask!.IsCompleted.Should().BeFalse();
        logger.LatestRecord.Id.Id.Should().Be(1003);
        logger.LatestRecord.Level.Should().Be(LogLevel.Error);

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The host shutting down part-way through a rotation is not a failure, whether the statement in
    /// hand was cancelled or the control plane went down with the machine: the service says nothing
    /// of it.
    /// </summary>
    /// <param name="ending">How the statement in hand ends once the host is stopping.</param>
    [Theory]
    [MemberData(nameof(Endings))]
    public async Task Shutting_Down_Part_Way_Through_A_Rotation_Is_Not_A_Failure(Exception ending)
    {
        var clock = new WorkerClock(Start);
        var logger = new FakeLogger<VisitorKeySaltRotationService>();
        var scopes = Substitute.For<IServiceScopeFactory>();

        using var service = new VisitorKeySaltRotationService(
            new VisitorKeySaltStore(scopes, clock, NullLogger<VisitorKeySaltStore>.Instance),
            clock,
            logger);

        // The host stops while the statement is in hand and the statement then fails, which is the
        // order a machine going down puts them in. Stopping cancels the service's token before it
        // returns.
        scopes.CreateScope().Returns(call =>
        {
            _ = service.StopAsync(TestContext.Current.CancellationToken);

            throw ending;
        });

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WorkerClock.EventuallyAsync(() => service.ExecuteTask!.IsCompleted);

        service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        logger.Collector.Count.Should().Be(0);
    }

    /// <summary>The ways a store fails: refusing outright, and a client giving up on a statement.</summary>
    public static TheoryData<Exception> Failures =>
    [
        new InvalidOperationException("The store went away."),
        new TaskCanceledException("The client stopped waiting for the store."),
    ];

    /// <summary>How a statement ends once the host is stopping: cancelled, or its store gone.</summary>
    public static TheoryData<Exception> Endings =>
    [
        new OperationCanceledException("The host is stopping."),
        new InvalidOperationException("The store went down with the machine."),
    ];
}
