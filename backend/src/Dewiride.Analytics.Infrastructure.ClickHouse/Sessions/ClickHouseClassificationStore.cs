using System.Collections.Immutable;
using ClickHouse.Driver;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Classification;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Sessions;

/// <summary>
/// Writes verdicts to the telemetry store.
/// </summary>
/// <remarks>
/// <para>
/// Rows go in through the binary row format, as events do, so nothing a verdict carries is ever
/// parsed as SQL. That matters even though a verdict is written by this product rather than by a
/// visitor: the signal parameters are numbers the engine counted, but the discipline is worth
/// having by construction rather than by remembering which strings came from where.
/// </para>
/// <para>
/// The table replaces on the visit's own identity and the ruleset, so writing the same verdict
/// twice leaves one row. That is what makes a run safe to interrupt and safe to duplicate.
/// </para>
/// </remarks>
/// <param name="client">Telemetry store client.</param>
internal sealed class ClickHouseClassificationStore(IClickHouseClient client) : IClassificationStore
{
    private const string TableName = "session_classifications";

    /// <summary>Insert columns, in the order <see cref="ToRow"/> produces values for them.</summary>
    private static readonly string[] Columns =
    [
        "site_id",
        "session_key",
        "ruleset_major",
        "ruleset_minor",
        "started_at",
        "ended_at",
        "page_count",
        "surfaces",
        "category",
        "strength",
        "is_provisional",
        "signal_codes",
        "signal_directions",
        "signal_weights",
        "signal_supporting",
        "signal_parameters",
        "classified_at",
    ];

    /// <inheritdoc />
    public Task SaveAsync(
        Guid siteId,
        IReadOnlyCollection<SessionJudgement> judgements,
        DateTimeOffset classifiedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(judgements);

        return judgements.Count == 0
            ? Task.CompletedTask
            : client.InsertBinaryAsync(
                TableName,
                Columns,
                judgements.Select(judgement => ToRow(siteId, judgement, classifiedAt)),
                cancellationToken: cancellationToken);
    }

    private static object[] ToRow(Guid siteId, SessionJudgement judgement, DateTimeOffset classifiedAt)
    {
        var session = judgement.Session;
        var verdict = judgement.Verdict;
        var evidence = Flatten(verdict);

        return
        [
            siteId,
            session.SessionKey,
            (ushort)verdict.RulesetVersion.Major,
            (ushort)verdict.RulesetVersion.Minor,
            session.StartedAt.UtcDateTime,
            session.EndedAt.UtcDateTime,
            (uint)session.PageCount,
            session.Surfaces.Select(surface => StoredNames.SurfaceNames[surface]).ToArray(),
            StoredNames.CategoryNames[verdict.Category],
            StoredNames.StrengthNames[verdict.Strength],
            verdict.IsProvisional,
            evidence.Select(entry => entry.Signal.Code).ToArray(),
            evidence.Select(entry => StoredNames.DirectionNames[entry.Signal.Direction]).ToArray(),
            evidence.Select(entry => (byte)entry.Signal.Weight).ToArray(),
            evidence.Select(entry => entry.Supporting).ToArray(),
            evidence.Select(entry => entry.Signal.Parameters.ToDictionary(StringComparer.Ordinal)).ToArray(),
            classifiedAt.UtcDateTime,
        ];
    }

    /// <summary>
    /// Lays the evidence out as one list, marking which way each piece counted.
    /// </summary>
    /// <remarks>
    /// Supporting first, then the evidence that pointed the other way. Both are kept: a verdict
    /// that carries only what agrees with it is an argument rather than an assessment, and the
    /// screens are obliged to show the objection alongside the conclusion.
    /// </remarks>
    private static ImmutableArray<(Signal Signal, bool Supporting)> Flatten(ClassificationVerdict verdict) =>
    [
        .. verdict.Supporting.Select(signal => (signal, true)),
        .. verdict.Contradicting.Select(signal => (signal, false)),
    ];
}
