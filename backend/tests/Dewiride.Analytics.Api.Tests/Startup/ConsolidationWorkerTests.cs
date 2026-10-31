using Dewiride.Analytics.Api.Startup;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
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

    private readonly WatchedClock _clock = new(Start);
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
        await EventuallyAsync(() => _clock.Timers == 1);

        _clock.Advance(StartDelay - TimeSpan.FromSeconds(1));
        await QuietAsync();

        Surveyed(First.Id).Should().Be(0);

        _clock.Advance(TimeSpan.FromSeconds(2));

        await EventuallyAsync(() => Surveyed(First.Id) == 1 && Surveyed(Second.Id) == 1);
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
        await EventuallyAsync(() => _clock.Timers == 1);

        _clock.Advance(StartDelay);
        await EventuallyAsync(() => Surveyed(Second.Id) == 1 && _clock.Timers == 2);

        _clock.Advance(Interval);
        await EventuallyAsync(() => Surveyed(First.Id) == 2 && Surveyed(Second.Id) == 2);

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
        await EventuallyAsync(() => _clock.Timers == 1);

        _clock.Advance(StartDelay);
        await EventuallyAsync(() => _logger.Collector.GetSnapshot().Any(record => record.Id.Id == 5207) && _clock.Timers == 2);

        _clock.Advance(Interval);
        await EventuallyAsync(() => Surveyed(First.Id) == 1);

        _worker!.ExecuteTask!.IsFaulted.Should().BeFalse();
    }

    /// <summary>
    /// Switched off, nothing is kept, and it says so: a month whose reports expire meanwhile is lost.
    /// </summary>
    [Fact]
    public async Task Nothing_Is_Counted_While_Switched_Off()
    {
        await StartAsync(enabled: false);

        _clock.Advance(StartDelay + Interval);
        await QuietAsync();

        _worker!.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
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

    /// <summary>
    /// A clock the test moves, which counts the timers set on it, so the test moves it only once the
    /// work has set the timer it is waiting on: the work starts on a thread of its own, and a clock
    /// moved before a timer is set never fires it.
    /// </summary>
    /// <param name="start">The moment it starts at.</param>
    private sealed class WatchedClock(DateTimeOffset start) : FakeTimeProvider(start)
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
    }

    /// <summary>How many times one website's calendar was read.</summary>
    private int Surveyed(Guid siteId) =>
        _months.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == nameof(IMonthConsolidation.SurveyAsync)
            && (Guid)call.GetArguments()[0]! == siteId);

    /// <summary>
    /// Gives anything the clock just set going the chance to run, so a check that nothing happened is
    /// a check that nothing happened rather than that it had not happened yet.
    /// </summary>
    private static Task QuietAsync() => Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

    /// <summary>Waits, in real time, for something the clock set going to have happened.</summary>
    private static async Task EventuallyAsync(Func<bool> happened)
    {
        var began = TimeProvider.System.GetTimestamp();

        while (!happened())
        {
            TimeProvider.System.GetElapsedTime(began).Should().BeLessThan(TimeSpan.FromSeconds(10), "what the clock set going should have happened by now");
            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        }
    }
}
