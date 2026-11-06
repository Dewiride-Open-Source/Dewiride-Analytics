using Dewiride.Analytics.Api.Startup;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Dewiride.Analytics.Api.Tests.Startup;

/// <summary>
/// Covers the background work that keeps every website's finished months.
/// </summary>
/// <remarks>
/// It is the only thing that keeps a month at all, and a month it never keeps is lost for good once
/// its reports go — while All time keeps looking right for a year, because a month nothing was kept
/// of is answered from its reports until then. Every host the stores are tested against switches it
/// off, so it is driven here on its own, over substitutes for the stores and a clock the test moves.
/// </remarks>
public sealed class ConsolidationWorkerTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private static readonly SiteRegistration First =
        new(Guid.Parse("0197c0de-0000-7000-8000-000000000001"), "first.example", Start.AddMonths(-3), "Etc/UTC");

    private static readonly SiteRegistration Second =
        new(Guid.Parse("0197c0de-0000-7000-8000-000000000002"), "second.example", Start.AddMonths(-3), "Etc/UTC");

    private readonly WorkerClock _clock = new(Start);
    private readonly ISiteRoster _roster = Substitute.For<ISiteRoster>();
    private readonly IMonthConsolidation _months = Substitute.For<IMonthConsolidation>();
    private readonly IClassificationProgressStore _progress = Substitute.For<IClassificationProgressStore>();
    private readonly FakeLogger<ConsolidationWorker> _logger = new();
    private ServiceProvider? _services;
    private ConsolidationWorker? _worker;

    public ConsolidationWorkerTests()
    {
        _roster.ListAsync(Arg.Any<CancellationToken>()).Returns([First, Second]);
        _months.SurveyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new MonthSurvey(null, []));
    }

    /// <summary>
    /// The first run waits a minute rather than a whole interval, so an installation's history is kept
    /// soon after it starts but not before it is serving requests.
    /// </summary>
    [Fact]
    public async Task The_First_Run_Comes_After_The_Start_Delay_And_Before_A_Whole_Interval()
    {
        await StartAsync(enabled: true);
        await WorkerClock.EventuallyAsync(() => _clock.Timers == 1);

        _clock.Advance(StartDelay - TimeSpan.FromSeconds(1));
        await WorkerClock.QuietAsync();

        Surveyed(First.Id).Should().Be(0);

        _clock.Advance(TimeSpan.FromSeconds(2));

        await WorkerClock.EventuallyAsync(() => Surveyed(First.Id) == 1 && Surveyed(Second.Id) == 1);
    }

    /// <summary>
    /// A website whose months fail to count every time stops neither the websites after it nor the
    /// runs after it: without that, one such website would stop the whole engine a minute after
    /// every start.
    /// </summary>
    [Fact]
    public async Task A_Website_That_Fails_Does_Not_Stop_The_Next_Or_The_Loop()
    {
        _months.SurveyAsync(First.Id, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MonthSurvey>(new InvalidOperationException("The store went away.")));

        await StartAsync(enabled: true);
        await WorkerClock.EventuallyAsync(() => _clock.Timers == 1);

        _clock.Advance(StartDelay);
        await WorkerClock.EventuallyAsync(() => Surveyed(Second.Id) == 1 && _clock.Timers == 2);

        _clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => Surveyed(First.Id) == 2 && Surveyed(Second.Id) == 2);

        _worker!.ExecuteTask!.IsFaulted.Should().BeFalse();
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Id.Id == 5204 && record.Level == LogLevel.Error);
    }

    /// <summary>
    /// A run that cannot even list the websites — the control plane restarting for a moment — ends
    /// there, and the next one tries again. Letting it out would stop the whole engine, collector
    /// included.
    /// </summary>
    [Fact]
    public async Task A_Run_That_Cannot_List_The_Websites_Does_Not_Stop_The_Engine()
    {
        _roster.ListAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromException<IReadOnlyList<SiteRegistration>>(new InvalidOperationException("The control plane is restarting.")),
            Task.FromResult<IReadOnlyList<SiteRegistration>>([First]));

        await StartAsync(enabled: true);
        await WorkerClock.EventuallyAsync(() => _clock.Timers == 1);

        _clock.Advance(StartDelay);
        await WorkerClock.EventuallyAsync(() => _logger.Collector.GetSnapshot().Any(record => record.Id.Id == 5207) && _clock.Timers == 2);

        _clock.Advance(Interval);
        await WorkerClock.EventuallyAsync(() => Surveyed(First.Id) == 1);

        _worker!.ExecuteTask!.IsFaulted.Should().BeFalse();
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

        _clock.Advance(StartDelay);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await _worker!.StopAsync(TestContext.Current.CancellationToken);

        _worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        _logger.Collector.GetSnapshot().Should().BeEmpty();
    }

    /// <summary>How a statement ends once the host is stopping: cancelled, or its store gone.</summary>
    public static TheoryData<Exception> Endings =>
    [
        new OperationCanceledException("The host is stopping."),
        new InvalidOperationException("The store went down with the machine."),
    ];

    /// <summary>
    /// Switched off, nothing is kept, and it says so: a month whose reports expire meanwhile is lost.
    /// </summary>
    [Fact]
    public async Task Nothing_Is_Counted_While_Switched_Off()
    {
        await StartAsync(enabled: false);
        await _worker!.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        _clock.Advance(StartDelay + Interval);
        await WorkerClock.QuietAsync();

        _months.ReceivedCalls().Should().BeEmpty();
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Id.Id == 5202 && record.Level == LogLevel.Warning);
    }

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
        var settings = Options.Create(new ConsolidationOptions
        {
            Enabled = enabled,
            StartDelay = StartDelay,
            Interval = Interval,
        });

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton(_roster);
        services.AddSingleton(_months);
        services.AddSingleton(_progress);
        services.AddSingleton(settings);
        services.AddSingleton(Options.Create(new ClassificationOptions()));
        services.AddLogging();
        services.AddScoped<MonthlyConsolidation>();

        _services = services.BuildServiceProvider();
        _worker = new ConsolidationWorker(
            _services.GetRequiredService<IServiceScopeFactory>(),
            settings,
            _clock,
            _logger);

        await _worker.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>How many times one website's calendar was read.</summary>
    private int Surveyed(Guid siteId) =>
        _months.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == nameof(IMonthConsolidation.SurveyAsync)
            && (Guid)call.GetArguments()[0]! == siteId);
}
