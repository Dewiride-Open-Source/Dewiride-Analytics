using System.Globalization;
using System.Text;
using ClickHouse.Driver;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.ClickHouse.Migrations;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves the migration that keeps every answer the stored verdicts already carry.
/// </summary>
/// <remarks>
/// <para>
/// Before answers settled by name were kept on their own, the verdict was the only place one
/// lived, and a visit judged again after its address was cleared went back to being a claim. The
/// migration reads the answers back out of the verdicts, so what it has to get right is which
/// company a verdict names — the one that vouched for the visit, never the one the visitor named —
/// and that an answer one set of rules reached and a later set lost is still found.
/// </para>
/// <para>
/// The verdicts are reached by the engine itself rather than written by hand, so the codes and the
/// parameters the migration reads are the ones the engine actually stores.
/// </para>
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class NameConfirmationBackfillTests(AnalyticsStackFixture stack)
{
    private const string Backfill = "0011_answers_already_reached_by_name.sql";

    private const string YandexBot = "Mozilla/5.0 (compatible; YandexBot/3.0; +http://yandex.com/bots)";

    private const string Googlebot = "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)";

    /// <summary>An earlier set of rules, as an installation that has been running a while holds.</summary>
    private static readonly RulesetVersion Earlier = new(8, 0);

    /// <summary>
    /// Well inside the verdicts' retention, and to the millisecond, which is as finely as the store
    /// keeps an instant.
    /// </summary>
    private DateTimeOffset Yesterday => DateTimeOffset.FromUnixTimeMilliseconds(
        stack.Services.GetRequiredService<TimeProvider>().GetUtcNow().AddDays(-1).ToUnixTimeMilliseconds());

    [Fact]
    public async Task A_Visit_Confirmed_By_Name_Keeps_The_Company_That_Vouched_For_It()
    {
        var siteId = Guid.NewGuid();
        var at = Yesterday;
        var visit = Visit("yandex", at, YandexBot, settledAs: "Yandex");

        await StoreAsync(siteId, visit, RulesetVersion.Current, at.AddHours(1));

        await BackfillAsync();

        (await KeptAsync(siteId)).Should().Equal(new Kept(visit.SessionKey, "Yandex", at.AddHours(1)));
    }

    /// <summary>
    /// A visitor naming one company while arriving from another's address is caught by the company
    /// the address belongs to, and that is what is kept. The name it gave is a claim, and keeping
    /// it would make the claim the thing that outlives the evidence against it.
    /// </summary>
    [Fact]
    public async Task A_False_Claim_Keeps_The_Company_The_Address_Belonged_To()
    {
        var siteId = Guid.NewGuid();
        var at = Yesterday;
        var visit = Visit("impostor", at, Googlebot, settledAs: "Yandex");

        await StoreAsync(siteId, visit, RulesetVersion.Current, at.AddHours(1));

        await BackfillAsync();

        (await KeptAsync(siteId)).Should().ContainSingle().Which.Operator.Should().Be("Yandex");
    }

    /// <summary>
    /// A verdict on a visitor that only named a company carries that company too, as the claim it
    /// is. Nothing was ever established about it, and nothing is kept.
    /// </summary>
    [Fact]
    public async Task A_Claim_Alone_Keeps_Nothing()
    {
        var siteId = Guid.NewGuid();
        var at = Yesterday;

        await StoreAsync(siteId, Visit("claimant", at, YandexBot), RulesetVersion.Current, at.AddHours(1));

        await BackfillAsync();

        (await KeptAsync(siteId)).Should().BeEmpty();
    }

    /// <summary>
    /// The case the migration exists for most of all. Earlier rules reached the answer while the
    /// address was still kept; later rules judged the visit again once it had gone, and stored it as
    /// a claim. The answer is still in the earlier verdict, and that is where it is found.
    /// </summary>
    [Fact]
    public async Task An_Answer_A_Later_Set_Of_Rules_Lost_Is_Found_Again()
    {
        var siteId = Guid.NewGuid();
        var at = Yesterday;
        var settled = Visit("yandex", at, YandexBot, settledAs: "Yandex");
        var lost = settled with { ConfirmedOperator = null };

        await StoreAsync(siteId, settled, Earlier, at.AddHours(1));
        await StoreAsync(siteId, lost, RulesetVersion.Current, at.AddHours(2));

        await BackfillAsync();

        (await KeptAsync(siteId)).Should().Equal(new Kept(settled.SessionKey, "Yandex", at.AddHours(1)));
    }

    /// <summary>
    /// Several verdicts naming a company are one answer, stamped with when the first of them was
    /// reached — which is what makes the stamp the same however many times the migration runs.
    /// </summary>
    [Fact]
    public async Task Several_Verdicts_Naming_A_Company_Are_One_Answer_Stamped_When_It_Was_First_Reached()
    {
        var siteId = Guid.NewGuid();
        var at = Yesterday;
        var visit = Visit("yandex", at, YandexBot, settledAs: "Yandex");

        await StoreAsync(siteId, visit, Earlier, at.AddHours(1));
        await StoreAsync(siteId, visit, RulesetVersion.Current, at.AddHours(3));

        await BackfillAsync();

        (await KeptAsync(siteId)).Should().Equal(new Kept(visit.SessionKey, "Yandex", at.AddHours(1)));
    }

    /// <summary>
    /// A migration that fails part-way is run again from the start, so a second run has to leave
    /// what the first one did.
    /// </summary>
    [Fact]
    public async Task Running_It_Again_Keeps_One_Answer_Per_Visit()
    {
        var siteId = Guid.NewGuid();
        var at = Yesterday;

        await StoreAsync(
            siteId,
            Visit("yandex", at, YandexBot, settledAs: "Yandex"),
            RulesetVersion.Current,
            at.AddHours(1));

        await BackfillAsync();
        await BackfillAsync();

        (await KeptAsync(siteId)).Should().ContainSingle();
    }

    /// <summary>Runs the migration exactly as it ships, one statement read out of the assembly.</summary>
    private async Task BackfillAsync()
    {
        var assembly = typeof(ClickHouseMigrationRunner).Assembly;

        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(Backfill, StringComparison.Ordinal));

        await using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var script = (await reader.ReadToEndAsync(Cancellation.Token).ConfigureAwait(false))
            .TrimEnd()
            .TrimEnd(';');

        await Client.ExecuteNonQueryAsync(script, cancellationToken: Cancellation.Token)
            .ConfigureAwait(false);
    }

    /// <summary>The answers kept for a site, one per visit as a reader sees them.</summary>
    private async Task<IReadOnlyList<Kept>> KeptAsync(Guid siteId)
    {
        var rows = await TelemetryStore.RowsAsync(
            Client,
            """
            SELECT session_key, operator, settled_at
            FROM name_confirmations FINAL
            WHERE site_id = {site_id:UUID}
            ORDER BY session_key
            """,
            TelemetryStore.Bind("site_id", siteId));

        return
        [
            .. rows.Select(row => new Kept(
                (string)row["session_key"]!,
                (string)row["operator"]!,
                new DateTimeOffset(DateTime.SpecifyKind((DateTime)row["settled_at"]!, DateTimeKind.Utc)))),
        ];
    }

    /// <summary>Has the engine judge a visit under the given rules and stores what it concluded.</summary>
    private Task StoreAsync(Guid siteId, SessionEvidence visit, RulesetVersion ruleset, DateTimeOffset classifiedAt) =>
        stack.Services.GetRequiredService<IClassificationStore>()
            .SaveAsync(
                siteId,
                [new SessionJudgement(visit, new TrafficClassifier(TrafficClassifier.Standard, ruleset).Classify(visit))],
                classifiedAt,
                Cancellation.Token);

    private IClickHouseClient Client => stack.Services.GetRequiredService<IClickHouseClient>();

    /// <summary>A crawler's visit as the engine is handed it, named the way the engine names one.</summary>
    /// <param name="visitor">Whose visit it is.</param>
    /// <param name="startedAt">When it began.</param>
    /// <param name="userAgent">What it called itself.</param>
    /// <param name="settledAs">Whose crawler its address was settled as, where it was.</param>
    /// <returns>The visit.</returns>
    private static SessionEvidence Visit(
        string visitor,
        DateTimeOffset startedAt,
        string userAgent,
        string? settledAs = null) =>
        new()
        {
            SessionKey = string.Create(
                CultureInfo.InvariantCulture,
                $"{visitor}:{startedAt.ToUnixTimeMilliseconds()}"),
            StartedAt = startedAt,
            EndedAt = startedAt.AddMinutes(2),
            PageCount = 2,
            Requests =
            [
                new ObservedRequest(startedAt, "/", 200),
                new ObservedRequest(startedAt.AddMinutes(2), "/posts/hello", 200),
            ],
            Surfaces = [IngestSurface.CloudflareWorker],
            UserAgent = userAgent,
            ConfirmedOperator = settledAs,
        };

    /// <summary>One kept answer, as read back.</summary>
    private sealed record Kept(string SessionKey, string Operator, DateTimeOffset SettledAt);
}
