using System.Net;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Collector;

/// <summary>
/// Proves the two probes answer different questions.
/// </summary>
/// <remarks>
/// Liveness asks whether this process can still handle a request at all, and the only correct
/// response to it failing is a restart — which is never the fix for a store being unreachable.
/// Readiness asks whether the process can do its work right now, and a store that has briefly
/// gone away is exactly the case for saying no. Wiring the store checks into liveness would turn
/// a ten-second database restart into a crash loop.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class HealthEndpointTests(AnalyticsStackFixture stack)
{
    [Fact]
    public async Task Liveness_Answers_While_Both_Stores_Are_Up()
    {
        using var client = stack.CreateClient();

        var response = await client.GetAsync("/health/live", Cancellation.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_Answers_Once_Both_Stores_Are_Reachable_And_Migrated()
    {
        using var client = stack.CreateClient();

        var response = await client.GetAsync("/health/ready", Cancellation.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Liveness runs no checks at all, so it cannot become slower or noisier as stores are added.
    /// </summary>
    [Fact]
    public async Task Liveness_Reports_Nothing_About_The_Stores()
    {
        using var client = stack.CreateClient();

        var response = await client.GetAsync("/health/live", Cancellation.Token);
        var body = await response.Content.ReadAsStringAsync(Cancellation.Token);

        body.Should().Be("Healthy");
    }
}
