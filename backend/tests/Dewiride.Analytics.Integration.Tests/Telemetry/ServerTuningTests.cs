using ClickHouse.Driver;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves what the tuning shipped with the product keeps of the server's own record of its work.
/// </summary>
/// <remarks>
/// The server here is started with the same tuning files the compose stack mounts, so what it keeps
/// is what an installation keeps.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class ServerTuningTests(AnalyticsStackFixture stack)
{
    /// <summary>
    /// The engine asks this server questions around the clock. A row for every processor in every
    /// question's pipeline, and a span for every stage of one that carries a trace, would soon
    /// outweigh the telemetry they describe many times over, so the server is set to keep neither.
    /// Flushing its logs creates the table of every log it is set to keep, even one with nothing in
    /// it yet, so after a flush neither table exists — while the log of questions a self-hoster
    /// would actually read does.
    /// </summary>
    [Fact]
    public async Task The_Server_Keeps_Its_Log_Of_Questions_And_Neither_Profiles_Nor_Traces_Of_Them()
    {
        await Client.ExecuteNonQueryAsync("SYSTEM FLUSH LOGS", cancellationToken: Cancellation.Token);

        var kept = await TelemetryStore.RowsAsync(
            Client,
            """
            SELECT name
            FROM system.tables
            WHERE database = 'system'
              AND name IN ('query_log', 'processors_profile_log', 'opentelemetry_span_log')
            """);

        kept.Should().ContainSingle()
            .Which["name"].Should().Be("query_log");
    }

    private IClickHouseClient Client => stack.Services.GetRequiredService<IClickHouseClient>();
}
