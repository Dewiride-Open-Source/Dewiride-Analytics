using ClickHouse.Driver;
using Dewiride.Analytics.Infrastructure.ClickHouse;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Covers what the store's driver is allowed to write in the engine's log.
/// </summary>
/// <remarks>
/// The driver writes any statement that ends in an exception as an error. A statement somebody
/// stopped waiting for is not one: a reader left the screen, or the engine was told to stop. A log
/// that calls those errors sends whoever reads it looking for a fault that is not there, and these
/// hold the engine to saying nothing of them while every real failure is still written.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class StoreDriverLoggingTests(AnalyticsStackFixture stack)
{
    private const string DriverLogs = "ClickHouse.Driver";

    /// <summary>
    /// A statement in hand when whoever asked goes away ends without a word from the driver, on the
    /// engine's own client of the store.
    /// </summary>
    [Fact]
    public async Task A_Statement_Somebody_Stopped_Waiting_For_Is_Not_Written()
    {
        using var written = new FakeLoggerProvider();
        using var engine = stack.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddLogging(logging => logging.AddProvider(written))));
        var store = engine.Services.GetRequiredService<IClickHouseClient>();
        using var gone = new CancellationTokenSource();

        var asking = store.ExecuteReaderAsync("SELECT sleep(3)", cancellationToken: gone.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), Cancellation.Token);
        await gone.CancelAsync();

        var ending = async () =>
        {
            await using var reader = await asking;
        };

        await ending.Should().ThrowAsync<OperationCanceledException>();
        FromTheDriver(written).Should().BeEmpty();
    }

    /// <summary>
    /// A statement the store refuses is still written by the driver, with the statement's identifier:
    /// the same log, on the same client, so the silence above is about cancellation and nothing else.
    /// </summary>
    [Fact]
    public async Task A_Statement_The_Store_Refuses_Is_Still_Written()
    {
        using var written = new FakeLoggerProvider();
        using var engine = stack.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddLogging(logging => logging.AddProvider(written))));
        var store = engine.Services.GetRequiredService<IClickHouseClient>();

        var asking = async () =>
        {
            await using var reader = await store.ExecuteReaderAsync(
                "SELECT nothing_of_the_kind FROM events",
                cancellationToken: Cancellation.Token);
        };

        await asking.Should().ThrowAsync<Exception>();
        FromTheDriver(written).Should().ContainSingle()
            .Which.Message.Should().Contain("QueryId");
    }

    /// <summary>
    /// A statement the engine gave up on because it ran out of the time it is allowed is a failure,
    /// though it arrives as a cancellation: a client out of time says so inside it.
    /// </summary>
    [Fact]
    public void A_Statement_That_Ran_Out_Of_Time_Is_Not_One_Somebody_Abandoned()
    {
        var outOfTime = new TaskCanceledException("The request was cancelled.", new TimeoutException());

        StoreDriverLogging.IsAbandoned(outOfTime).Should().BeFalse();
        StoreDriverLogging.IsAbandoned(new TaskCanceledException()).Should().BeTrue();
        StoreDriverLogging.IsAbandoned(new OperationCanceledException()).Should().BeTrue();
        StoreDriverLogging.IsAbandoned(new InvalidOperationException()).Should().BeFalse();
        StoreDriverLogging.IsAbandoned(null).Should().BeFalse();
    }

    /// <summary>
    /// What the driver says with no failure attached, and of failures, goes through at the level it
    /// was said at; only the abandoned statement is left out.
    /// </summary>
    [Fact]
    public void Everything_Else_The_Driver_Says_Is_Written_As_It_Was_Said()
    {
        using var written = new FakeLoggerProvider();
        using var host = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(written));
        using var driver = new StoreDriverLogging(host);
        var log = driver.CreateLogger($"{DriverLogs}.Command");

        Say(log, LogLevel.Debug, "Executing a statement.");
        Say(log, LogLevel.Error, "A statement failed.", new InvalidOperationException("Refused."));
        Say(log, LogLevel.Error, "A statement ran out of time.", new TaskCanceledException("Out of time.", new TimeoutException()));
        Say(log, LogLevel.Error, "A statement was abandoned.", new TaskCanceledException("Abandoned."));

        written.Collector.GetSnapshot().Select(line => (line.Level, line.Message)).Should().Equal(
            (LogLevel.Debug, "Executing a statement."),
            (LogLevel.Error, "A statement failed."),
            (LogLevel.Error, "A statement ran out of time."));
        written.Collector.GetSnapshot().Should().OnlyContain(line => line.Category == $"{DriverLogs}.Command");
    }

    /// <summary>Writes one line as the driver would, with what its statement ended in.</summary>
    private static void Say(ILogger log, LogLevel level, string message, Exception? failure = null) =>
        log.Log(level, default, message, failure, static (said, _) => said);

    /// <summary>What the driver wrote as a warning or worse.</summary>
    private static FakeLogRecord[] FromTheDriver(FakeLoggerProvider written) =>
        [
            .. written.Collector.GetSnapshot().Where(line =>
                line.Category is { } category
                && category.StartsWith(DriverLogs, StringComparison.Ordinal)
                && line.Level >= LogLevel.Warning),
        ];
}
