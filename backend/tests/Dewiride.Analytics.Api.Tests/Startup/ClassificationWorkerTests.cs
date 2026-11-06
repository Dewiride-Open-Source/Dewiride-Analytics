using Dewiride.Analytics.Api.Startup;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Dewiride.Analytics.Api.Tests.Startup;

/// <summary>
/// Covers what keeps the background work that judges visits running.
/// </summary>
/// <remarks>
/// An exception that leaves a background service stops the host, so anything this lets out stops the
/// whole engine, collector included. Every host the stores are tested against switches it off, so it
/// is driven here on its own, over substitutes for the stores and a clock the test moves.
/// </remarks>
public sealed class ClassificationWorkerTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private static readonly SiteRegistration First =
        new(Guid.Parse("0197c0de-0000-7000-8000-000000000011"), "first.example", Start.AddMonths(-3), "Etc/UTC");

    private static readonly SiteRegistration Second =
        new(Guid.Parse("0197c0de-0000-7000-8000-000000000012"), "second.example", Start.AddMonths(-3), "Etc/UTC");

    private readonly WorkerClock _clock = new(Start);
    private readonly ISiteRoster _roster = Substitute.For<ISiteRoster>();
    private readonly IClassificationProgressStore _progress = Substitute.For<IClassificationProgressStore>();
    private readonly FakeLogger<ClassificationWorker> _logger = new();
    private ServiceProvider? _services;
    private ClassificationWorker? _worker;

    /// <summary>Two websites, each already judged as far as there is anything to judge.</summary>
    public ClassificationWorkerTests()
    {
        _roster.ListAsync(Arg.Any<CancellationToken>()).Returns([First, Second]);
        _progress.ResumeFromAsync(Arg.Any<Guid>(), Arg.Any<RulesetVersion>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ClassificationBookmark(Start, Start));
    }

    /// <summary>
    /// A website that fails stops neither the websites after it nor the runs after it, whether it
    /// fails outright or a statement takes longer than the client will wait, which reaches here as a
    /// cancellation nobody asked for.
    /// </summary>
    /// <param name="failure">How the website fails.</param>
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_Website_That_Fails_Does_Not_Stop_The_Next_Or_The_Loop(Exception failure)
    {
        _progress.ResumeFromAsync(First.Id, Arg.Any<RulesetVersion>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ClassificationBookmark>(failure));

        await StartAsync(enabled: true);
        await WorkerClock.EventuallyAsync(() => _clock.Timers == 1);

        _clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => Reached(Second.Id) == 1);

        _clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => Reached(First.Id) == 2 && Reached(Second.Id) == 2);

        _worker!.ExecuteTask!.IsCompleted.Should().BeFalse();
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Id.Id == 5002 && record.Level == LogLevel.Error);
    }

    /// <summary>
    /// A run that cannot even list the websites ends there, and the next one tries again, whether the
    /// control plane refused or a statement took longer than the client will wait. Letting it out
    /// would stop the whole engine over a database that was restarting for a moment.
    /// </summary>
    /// <param name="failure">How the run fails.</param>
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_Run_That_Cannot_List_The_Websites_Does_Not_Stop_The_Engine(Exception failure)
    {
        _roster.ListAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromException<IReadOnlyList<SiteRegistration>>(failure),
            Task.FromResult<IReadOnlyList<SiteRegistration>>([First]));

        await StartAsync(enabled: true);
        await WorkerClock.EventuallyAsync(() => _clock.Timers == 1);

        _clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => _logger.Collector.GetSnapshot().Any(record => record.Id.Id == 5003));

        _clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => Reached(First.Id) == 1);

        _worker!.ExecuteTask!.IsCompleted.Should().BeFalse();
    }

    /// <summary>
    /// The host shutting down part-way through a run is not a failure, whether the statement in hand
    /// was cancelled or the store it was asking went down with the machine: this work says nothing of
    /// it, and ends rather than waiting for the next tick.
    /// </summary>
    /// <param name="ending">How the statement in hand ends once the host is stopping.</param>
    [Theory]
    [MemberData(nameof(Endings))]
    public async Task Shutting_Down_Part_Way_Through_A_Run_Is_Not_A_Failure(Exception ending)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _roster.ListAsync(Arg.Any<CancellationToken>()).Returns(async call =>
        {
            reached.TrySetResult();
            await WorkerClock.UntilCancelledAsync(call.Arg<CancellationToken>());

            throw ending;
#pragma warning disable CS0162 // A lambda that only ever throws still needs to say what it would have returned.
            return (IReadOnlyList<SiteRegistration>)[First];
#pragma warning restore CS0162
        });

        await StartAsync(enabled: true);
        await WorkerClock.EventuallyAsync(() => _clock.Timers == 1);

        _clock.Advance(Interval);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await _worker!.StopAsync(TestContext.Current.CancellationToken);

        _worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        _logger.Collector.GetSnapshot().Should().BeEmpty();
    }

    /// <summary>Switched off, nothing is judged, and it says so.</summary>
    [Fact]
    public async Task Nothing_Is_Judged_While_Switched_Off()
    {
        await StartAsync(enabled: false);
        await _worker!.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        _clock.Advance(Interval);
        await WorkerClock.QuietAsync();

        _roster.ReceivedCalls().Should().BeEmpty();
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Id.Id == 5001 && record.Level == LogLevel.Warning);
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_worker is not null)
        {
            await _worker.StopAsync(CancellationToken.None);
            _worker.Dispose();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
    }

    private async Task StartAsync(bool enabled)
    {
        var settings = Options.Create(new ClassificationOptions
        {
            Enabled = enabled,
            Interval = Interval,
        });

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton(_roster);
        services.AddSingleton(_progress);
        services.AddSingleton(Substitute.For<ISessionSource>());
        services.AddSingleton(Substitute.For<IClassificationStore>());
        services.AddSingleton(Substitute.For<INameConfirmationStore>());
        services.AddSingleton(Substitute.For<ICrawlerNameLookup>());
        services.AddSingleton(TrafficClassifier.Current());
        services.AddSingleton(settings);
        services.AddLogging();
        services.AddScoped<SessionClassifier>();

        _services = services.BuildServiceProvider();
        _worker = new ClassificationWorker(
            _services.GetRequiredService<IServiceScopeFactory>(),
            settings,
            _clock,
            _logger);

        await _worker.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>How many times judging one website began.</summary>
    private int Reached(Guid siteId) =>
        _progress.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == nameof(IClassificationProgressStore.ResumeFromAsync)
            && (Guid)call.GetArguments()[0]! == siteId);
}
