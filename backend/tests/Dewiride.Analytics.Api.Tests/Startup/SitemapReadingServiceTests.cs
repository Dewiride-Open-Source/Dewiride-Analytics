using Dewiride.Analytics.Infrastructure.Posts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Dewiride.Analytics.Api.Tests.Startup;

/// <summary>
/// Covers what keeps the background work that reads sitemaps running.
/// </summary>
/// <remarks>
/// An exception that leaves a background service stops the host, and with it the collector, and the reader
/// spends its life waiting on other people's websites and on a database that may be restarting. What a pass
/// does when it runs is covered against real stores elsewhere.
/// </remarks>
public sealed class SitemapReadingServiceTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly WorkerClock _clock = new(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeLogger<SitemapReadingService> _logger = new();
    private readonly IServiceScopeFactory _scopes = Substitute.For<IServiceScopeFactory>();

    /// <summary>
    /// A pass that fails, whether outright or because a client stopped waiting for a statement, ends there,
    /// is written down, and the reader looks again after its usual wait. It does not stop the engine, and it
    /// does not go round again at once and fill the log.
    /// </summary>
    /// <param name="failure">How the pass fails.</param>
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_Pass_That_Fails_Does_Not_Stop_The_Engine(Exception failure)
    {
        _scopes.CreateScope().Returns(_ => throw failure);

        using var service = Service(Settings());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WorkerClock.EventuallyAsync(() => _clock.Timers == 1);

        _clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => _logger.Collector.Count == 1);

        await WorkerClock.QuietAsync();
        _logger.Collector.Count.Should().Be(1);

        await WorkerClock.EventuallyAsync(() => _clock.Timers == 2);
        _clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => _logger.Collector.Count == 2);

        service.ExecuteTask!.IsCompleted.Should().BeFalse();
        _logger.LatestRecord.Id.Id.Should().Be(1304);
        _logger.LatestRecord.Level.Should().Be(LogLevel.Error);

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The host shutting down part-way through a pass is not a failure, whether the work in hand was
    /// cancelled or the store it was asking went down with the machine: the service says nothing of it, and
    /// ends rather than waiting for the next look.
    /// </summary>
    /// <param name="ending">How the work in hand ends once the host is stopping.</param>
    [Theory]
    [MemberData(nameof(Endings))]
    public async Task Shutting_Down_Part_Way_Through_A_Pass_Is_Not_A_Failure(Exception ending)
    {
        using var service = Service(Settings());

        // The host stops while the pass is in hand and the pass then ends, which is the order a machine
        // going down puts them in. Stopping cancels the service's token before it returns. Nothing here
        // may block: the work runs on the thread that moves the clock.
        _scopes.CreateScope().Returns(call =>
        {
            _ = service.StopAsync(TestContext.Current.CancellationToken);

            throw ending;
        });

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WorkerClock.EventuallyAsync(() => _clock.Timers == 1);

        _clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => service.ExecuteTask!.IsCompleted);

        service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        _logger.Collector.Count.Should().Be(0);
    }

    /// <summary>
    /// Nothing goes out to anybody's website before the first wait is over, so the process is serving
    /// requests first.
    /// </summary>
    [Fact]
    public async Task The_First_Look_Waits()
    {
        using var service = Service(Settings());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WorkerClock.EventuallyAsync(() => _clock.Timers == 1);
        await WorkerClock.QuietAsync();

        _scopes.ReceivedCalls().Should().BeEmpty();

        _scopes.CreateScope().Returns(_ => throw new InvalidOperationException("The store went away."));

        _clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => _scopes.ReceivedCalls().Any());

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>However many readers an engine has, each goes its own way, and one failing does not stop another.</summary>
    [Fact]
    public async Task Every_Reader_Runs_And_Fails_On_Its_Own()
    {
        _scopes.CreateScope().Returns(_ => throw new InvalidOperationException("The store went away."));

        using var service = Service(Settings(readers: 3));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WorkerClock.EventuallyAsync(() => _clock.Timers == 3);

        _clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => _logger.Collector.Count == 3);

        service.ExecuteTask!.IsCompleted.Should().BeFalse();

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Switched off, nothing is read, and it says so.</summary>
    [Fact]
    public async Task Nothing_Is_Read_While_Switched_Off()
    {
        using var service = Service(Settings(enabled: false));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        _clock.Advance(Interval);
        await WorkerClock.QuietAsync();

        _scopes.ReceivedCalls().Should().BeEmpty();
        _logger.LatestRecord.Id.Id.Should().Be(1307);
        _logger.LatestRecord.Level.Should().Be(LogLevel.Warning);
    }

    /// <summary>
    /// An engine given no readers leaves reading to the others sharing its database, which is a choice
    /// somebody made and not a fault, so it says so quietly and is still running.
    /// </summary>
    [Fact]
    public async Task An_Engine_With_No_Readers_Leaves_It_To_The_Others()
    {
        using var service = Service(Settings(readers: 0));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        _clock.Advance(Interval);
        await WorkerClock.QuietAsync();

        _scopes.ReceivedCalls().Should().BeEmpty();
        _logger.LatestRecord.Id.Id.Should().Be(1308);
        _logger.LatestRecord.Level.Should().Be(LogLevel.Information);
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

    private static IOptions<SitemapOptions> Settings(bool enabled = true, int readers = 1) =>
        Options.Create(new SitemapOptions
        {
            Enabled = enabled,
            SitesAtOnce = readers,
            Interval = Interval,
        });

    private SitemapReadingService Service(IOptions<SitemapOptions> settings) =>
        new(new SitemapPass(_scopes, settings, _clock, NullLogger<SitemapPass>.Instance), settings, _clock, _logger);
}
