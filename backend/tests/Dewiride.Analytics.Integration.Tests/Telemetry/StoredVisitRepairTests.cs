using System.Globalization;
using System.Text;
using ClickHouse.Driver;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.ClickHouse.Migrations;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves the repair that removes stored verdicts on activity that is no longer a visit.
/// </summary>
/// <remarks>
/// Reads take the highest ruleset present for each visit, so a verdict on something that will never
/// be reconstructed again is never superseded and is counted for ever. The repair is the only thing
/// that can remove one, and it has to recognise them from the activity alone — a stored verdict says
/// nothing about whether anybody arrived.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class StoredVisitRepairTests(AnalyticsStackFixture stack)
{
    private const string Repair = "0008_visits_that_never_began.sql";

    private static readonly DateTimeOffset Midnight = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The shape the repair exists for: a departure report that opened a visit of its own because
    /// the key it arrived under had never announced anything.
    /// </summary>
    [Fact]
    public async Task A_Visit_Nobody_Arrived_At_Is_Removed()
    {
        var siteId = Guid.NewGuid();
        var at = Midnight.AddHours(1);

        await WriteAsync(Left(siteId, at, "phantom", "/posts/hello"));
        await StoreAsync(siteId, Visit("phantom", at, at, IngestSurface.BrowserTracker));

        await RepairAsync();

        (await StoredKeysAsync(siteId)).Should().BeEmpty();
    }

    /// <summary>
    /// A visit whose first arrival is later than the instant it is recorded as beginning is named
    /// differently now, so it gains a row rather than having one replaced.
    /// </summary>
    [Fact]
    public async Task A_Visit_Whose_Name_No_Longer_Matches_Its_Arrival_Is_Removed()
    {
        var siteId = Guid.NewGuid();
        var at = Midnight.AddHours(2);

        await WriteAsync(
            Left(siteId, at, "renamed", "/posts/hello"),
            Arrived(siteId, at.AddMinutes(5), "renamed", "/"));

        await StoreAsync(siteId, Visit("renamed", at, at.AddMinutes(5), IngestSurface.BrowserTracker));

        await RepairAsync();

        (await StoredKeysAsync(siteId)).Should().BeEmpty();
    }

    /// <summary>
    /// A visit that began where somebody arrived is what the engine still reconstructs, and its
    /// verdict is superseded by every later ruleset rather than removed.
    /// </summary>
    [Fact]
    public async Task A_Visit_Somebody_Arrived_At_Is_Left_Alone()
    {
        var siteId = Guid.NewGuid();
        var at = Midnight.AddHours(3);

        await WriteAsync(
            Arrived(siteId, at, "reader", "/posts/hello"),
            Left(siteId, at.AddMinutes(4), "reader", "/posts/hello"));

        await StoreAsync(siteId, Visit("reader", at, at.AddMinutes(4), IngestSurface.BrowserTracker));

        await RepairAsync();

        (await StoredKeysAsync(siteId)).Should().ContainSingle();
    }

    /// <summary>
    /// Nothing in the request path reports a page being read, and those are the keys the
    /// reconciliation rewrites — so for a visit any of them watched, the name is not the key its
    /// reports carry and the activity cannot answer the question.
    /// </summary>
    [Fact]
    public async Task A_Visit_A_Reporter_On_The_Site_Watched_Is_Left_Alone()
    {
        var siteId = Guid.NewGuid();
        var at = Midnight.AddHours(4);

        await WriteAsync(Left(siteId, at, "reconciled", "/posts/hello"));
        await StoreAsync(siteId, Visit("reconciled", at, at, IngestSurface.CloudflareWorker));

        await RepairAsync();

        (await StoredKeysAsync(siteId)).Should().ContainSingle();
    }

    /// <summary>
    /// Raw activity is dropped at twelve months, as verdicts are. A visit whose reports have already
    /// gone cannot be judged either way, and guessing would throw away a year-old answer to save a
    /// row.
    /// </summary>
    [Fact]
    public async Task A_Visit_Whose_Activity_Has_Already_Gone_Is_Left_Alone()
    {
        var siteId = Guid.NewGuid();
        var at = Midnight.AddHours(5);

        await StoreAsync(siteId, Visit("forgotten", at, at.AddMinutes(3), IngestSurface.BrowserTracker));

        await RepairAsync();

        (await StoredKeysAsync(siteId)).Should().ContainSingle();
    }

    /// <summary>Runs the repair exactly as it ships, one statement read out of the assembly.</summary>
    private async Task RepairAsync()
    {
        var assembly = typeof(ClickHouseMigrationRunner).Assembly;

        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(Repair, StringComparison.Ordinal));

        await using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var script = (await reader.ReadToEndAsync(Cancellation.Token).ConfigureAwait(false))
            .TrimEnd()
            .TrimEnd(';');

        await Client.ExecuteNonQueryAsync(script, cancellationToken: Cancellation.Token)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> StoredKeysAsync(Guid siteId)
    {
        var rows = await TelemetryStore.RowsAsync(
            Client,
            "SELECT session_key FROM session_classifications WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

        return [.. rows.Select(row => (string)row["session_key"]!)];
    }

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    private Task StoreAsync(Guid siteId, SessionEvidence visit) =>
        stack.Services.GetRequiredService<IClassificationStore>()
            .SaveAsync(
                siteId,
                [new SessionJudgement(visit, ClassificationVerdict.Insufficient(RulesetVersion.Current))],
                Midnight.AddDays(1),
                Cancellation.Token);

    private IClickHouseClient Client => stack.Services.GetRequiredService<IClickHouseClient>();

    /// <summary>A visit as it was stored, named the way the engine names one.</summary>
    private static SessionEvidence Visit(
        string visitor,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        IngestSurface surface) =>
        new()
        {
            SessionKey = string.Create(
                CultureInfo.InvariantCulture,
                $"{visitor}:{startedAt.ToUnixTimeMilliseconds()}"),
            StartedAt = startedAt,
            EndedAt = endedAt,
            Requests = [new ObservedRequest(startedAt, "/posts/hello", null)],
            Surfaces = [surface],
        };

    /// <summary>A page being delivered.</summary>
    private static RawEvent Arrived(Guid siteId, DateTimeOffset at, string visitor, string path) =>
        Reported(siteId, at, visitor, path, EventKind.PageView);

    /// <summary>A page announcing that it is being left.</summary>
    private static RawEvent Left(Guid siteId, DateTimeOffset at, string visitor, string path) =>
        Reported(siteId, at, visitor, path, EventKind.Exit) with
        {
            EngagedMs = 900_000,
            ScrollDepthPercent = 80,
        };

    private static RawEvent Reported(
        Guid siteId,
        DateTimeOffset at,
        string visitor,
        string path,
        EventKind kind) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = kind,
            Surface = IngestSurface.BrowserTracker,
            ServerTimestamp = at,
            VisitorKey = visitor,
            Host = "example.com",
            Path = path,
        };
}
