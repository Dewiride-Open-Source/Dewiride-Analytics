using System.Text;
using ClickHouse.Driver;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.ClickHouse.Migrations;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves the two migrations that take out of the store what should never have been kept in it:
/// what followed the path of a referrer, and the name stored with a press on a field.
/// </summary>
/// <remarks>
/// The rows are written the way an earlier collector wrote them, straight to the store, because
/// the collector this build runs cuts a referrer on the way in and never names a text area, so it
/// could not produce them.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class StoredReferrerAndFieldNameRepairTests(AnalyticsStackFixture stack)
{
    private const string CameFrom = "0012_came_from_without_what_followed.sql";

    private const string FieldNames = "0013_presses_on_fields_without_names.sql";

    /// <summary>Well inside the store's retention, and to the millisecond the store keeps.</summary>
    private DateTimeOffset At => DateTimeOffset.FromUnixTimeMilliseconds(
        stack.Services.GetRequiredService<TimeProvider>().GetUtcNow().AddDays(-1).ToUnixTimeMilliseconds());

    [Fact]
    public async Task A_Stored_Referrer_Loses_What_Followed_Its_Path_And_Keeps_Where_It_Came_From()
    {
        var siteId = Guid.NewGuid();

        await DescribedTraffic.WriteAsync(
            stack,
            Viewed(siteId, "searched", "https://search.example.org/find?q=jane+doe+refund", "search.example.org"),
            Viewed(siteId, "linked", "https://news.example.org/story/42", "news.example.org"),
            Viewed(siteId, "searched-by-hash", "https://blog.example.org/search#gsc.q=jane+doe", "blog.example.org"),
            Viewed(siteId, "routed-by-hash", "https://app.example.org/a#b?c=d", "app.example.org"));

        await RunAsync(CameFrom);

        (await ReferrersAsync(siteId)).Should().BeEquivalentTo(
        [
            ("https://search.example.org/find", "search.example.org"),
            ("https://news.example.org/story/42", "news.example.org"),
            ("https://blog.example.org/search", "blog.example.org"),
            ("https://app.example.org/a", "app.example.org"),
        ]);
    }

    [Fact]
    public async Task A_Press_On_A_Field_Keeps_Everything_But_Its_Name()
    {
        var siteId = Guid.NewGuid();

        await DescribedTraffic.WriteAsync(
            stack,
            Pressed(siteId, "writer", ControlKind.Field, "Dear Jane, my account is 12345678"),
            Pressed(siteId, "subscriber", ControlKind.Button, "Subscribe"));

        await RunAsync(FieldNames);

        (await PressesAsync(siteId)).Should().BeEquivalentTo(
        [
            ("Field", "", "/contact"),
            ("Button", "Subscribe", "/contact"),
        ]);
    }

    /// <summary>
    /// A migration that fails part-way is run again from the start, so running both again has to
    /// leave what the first run left.
    /// </summary>
    [Fact]
    public async Task Running_Them_Again_Changes_Nothing_More()
    {
        var siteId = Guid.NewGuid();

        await DescribedTraffic.WriteAsync(
            stack,
            Viewed(siteId, "searched", "https://search.example.org/find?q=jane", "search.example.org"),
            Pressed(siteId, "writer", ControlKind.Field, "my draft"));

        foreach (var migration in new[] { CameFrom, FieldNames, CameFrom, FieldNames })
        {
            await RunAsync(migration);
        }

        (await ReferrersAsync(siteId)).Should().ContainSingle()
            .Which.Should().Be(("https://search.example.org/find", "search.example.org"));
        (await PressesAsync(siteId)).Should().ContainSingle()
            .Which.Should().Be(("Field", "", "/contact"));
    }

    /// <summary>
    /// Runs a migration exactly as it ships, one statement read out of the assembly, and waits for
    /// the store to finish it — which the engine, starting up, does not.
    /// </summary>
    /// <param name="name">The migration's file name.</param>
    /// <returns>A task that completes once the store has applied it.</returns>
    private async Task RunAsync(string name)
    {
        var assembly = typeof(ClickHouseMigrationRunner).Assembly;

        var resource = assembly.GetManifestResourceNames()
            .Single(candidate => candidate.EndsWith(name, StringComparison.Ordinal));

        await using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var statement = (await reader.ReadToEndAsync(Cancellation.Token).ConfigureAwait(false))
            .TrimEnd()
            .TrimEnd(';');

        await Client.ExecuteNonQueryAsync(
                $"{statement}\nSETTINGS mutations_sync = 2",
                cancellationToken: Cancellation.Token)
            .ConfigureAwait(false);
    }

    private async Task<List<(string Referrer, string Domain)>> ReferrersAsync(Guid siteId)
    {
        var rows = await TelemetryStore.RowsAsync(
            Client,
            """
            SELECT referrer, referrer_domain
            FROM events
            WHERE site_id = {site_id:UUID} AND kind = 'PageView'
            ORDER BY event_id
            """,
            TelemetryStore.Bind("site_id", siteId));

        return [.. rows.Select(row => ((string)row["referrer"]!, (string)row["referrer_domain"]!))];
    }

    private async Task<List<(string Control, string Name, string Path)>> PressesAsync(Guid siteId)
    {
        var rows = await TelemetryStore.RowsAsync(
            Client,
            """
            SELECT toString(action_control) AS control, action_label, path
            FROM events
            WHERE site_id = {site_id:UUID} AND kind = 'Action'
            ORDER BY event_id
            """,
            TelemetryStore.Bind("site_id", siteId));

        return [.. rows.Select(row => ((string)row["control"]!, (string)row["action_label"]!, (string)row["path"]!))];
    }

    private IClickHouseClient Client => stack.Services.GetRequiredService<IClickHouseClient>();

    /// <summary>A page view an earlier collector stored with its referrer as reported.</summary>
    private RawEvent Viewed(Guid siteId, string visitor, string referrer, string domain) =>
        DescribedTraffic.Arrived(siteId, At.AddSeconds(visitor.Length), "/posts/hello") with
        {
            VisitorKey = visitor,
            Referrer = referrer,
            ReferrerDomain = domain,
        };

    /// <summary>A press an earlier collector stored with the name it was reported under.</summary>
    private RawEvent Pressed(Guid siteId, string visitor, ControlKind control, string name) =>
        DescribedTraffic.Arrived(siteId, At.AddSeconds(visitor.Length), "/contact") with
        {
            Kind = EventKind.Action,
            VisitorKey = visitor,
            ActionControl = control,
            ActionLabel = name,
        };
}
