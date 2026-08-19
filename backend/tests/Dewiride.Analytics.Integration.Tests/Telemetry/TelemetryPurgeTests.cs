using ClickHouse.Driver;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that emptying the telemetry store of one site empties it of that site and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Both site-scoped tables are partitioned by month and sorted by site, so there is no partition
/// belonging to a single site and nothing to drop wholesale. What is left is deleting by
/// predicate, and a predicate is exactly the sort of thing that can be right about the rows it
/// removes and wrong about the rows it leaves. A store shared by every site on an installation
/// makes that the more expensive half of the mistake.
/// </para>
/// <para>
/// The call is expected to return only once the rows have stopped answering queries, which is what
/// lets the control-plane row be deleted afterwards on the understanding that the telemetry is
/// already gone. Every assertion here reads the store back immediately, with nothing waiting in
/// between, so a deletion merely accepted for later would fail these tests.
/// </para>
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class TelemetryPurgeTests(AnalyticsStackFixture stack)
{
    private static readonly DateTimeOffset Midnight = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Everything_Measured_For_A_Site_Is_Deleted()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Page(siteId, Midnight.AddHours(1), "visitor-a", "/"),
            Page(siteId, Midnight.AddHours(2), "visitor-b", "/posts/hello"),
            Page(siteId, Midnight.AddHours(3), "visitor-c", "/pricing"));

        (await ActivityCountAsync(siteId)).Should().Be(3);

        await PurgeAsync(siteId);

        (await ActivityCountAsync(siteId)).Should().Be(0);
    }

    /// <summary>
    /// One store holds every site on an installation, so the predicate that empties one of them is
    /// the only thing keeping the rest. A removal that took a neighbour's traffic with it would be
    /// invisible to the person who asked for it and unrecoverable for the person who did not.
    /// </summary>
    [Fact]
    public async Task Nothing_Belonging_To_Another_Site_Is_Touched()
    {
        var going = Guid.NewGuid();
        var staying = Guid.NewGuid();

        await WriteAsync(
            Page(going, Midnight.AddHours(1), "visitor-a", "/"),
            Page(going, Midnight.AddHours(2), "visitor-b", "/posts/hello"),
            Page(staying, Midnight.AddHours(1), "neighbour", "/"),
            Page(staying, Midnight.AddHours(2), "neighbour", "/posts/hello"),
            Page(staying, Midnight.AddHours(3), "passer-by", "/pricing"));

        await PurgeAsync(going);

        (await ActivityCountAsync(going)).Should().Be(0);
        (await ActivityCountAsync(staying)).Should().Be(3);
    }

    /// <summary>
    /// A site added and removed before anything reported for it is an ordinary thing to do, and
    /// the removal it is part of must not fail on the way through.
    /// </summary>
    [Fact]
    public async Task A_Site_Nothing_Was_Measured_For_Is_Purged_Without_Complaint()
    {
        var siteId = Guid.NewGuid();

        await PurgeAsync(siteId);

        (await ActivityCountAsync(siteId)).Should().Be(0);
    }

    /// <summary>
    /// Purging what has already been purged is what a retry looks like, and a removal is retried
    /// whenever the first attempt was interrupted after the telemetry went and before the
    /// control-plane row did.
    /// </summary>
    [Fact]
    public async Task Purging_The_Same_Site_Twice_Leaves_The_Same_Answer()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(Page(siteId, Midnight.AddHours(1), "visitor-a", "/"));

        await PurgeAsync(siteId);
        await PurgeAsync(siteId);

        (await ActivityCountAsync(siteId)).Should().Be(0);
    }

    private Task PurgeAsync(Guid siteId) =>
        stack.Services.GetRequiredService<ITelemetryPurge>().PurgeSiteAsync(siteId, Cancellation.Token);

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    private async Task<ulong> ActivityCountAsync(Guid siteId) =>
        await TelemetryStore.ScalarAsync<ulong>(
            stack.Services.GetRequiredService<IClickHouseClient>(),
            "SELECT count() FROM events WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

    private static RawEvent Page(Guid siteId, DateTimeOffset at, string visitorKey, string path) => new()
    {
        EventId = Guid.CreateVersion7(at),
        SiteId = siteId,
        Kind = EventKind.PageView,
        Surface = IngestSurface.BrowserTracker,
        ServerTimestamp = at,
        VisitorKey = visitorKey,
        Host = "example.com",
        Path = path,
    };
}
