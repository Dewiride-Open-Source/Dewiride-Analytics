using System.Collections.Immutable;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;
using Column = Dewiride.Analytics.Infrastructure.ClickHouse.Sessions.SessionSqlCompiler.Column;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Sessions;

/// <summary>
/// Rebuilds visits from the telemetry store.
/// </summary>
/// <remarks>
/// Turns rows into the closed set of values the engine reasons about, and nothing more. Everything
/// three-state stays three-state across the boundary: a visit nobody could watch for pointer
/// activity arrives with that reading absent rather than false, because the difference between
/// "nobody touched anything" and "nothing was watching" is the difference between evidence and
/// the absence of it.
/// </remarks>
/// <param name="client">Telemetry store client.</param>
internal sealed class ClickHouseSessionSource(IClickHouseClient client) : ISessionSource
{
    /// <inheritdoc />
    public async Task<ImmutableArray<ObservedSession>> ReadAsync(
        SessionWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        var statement = SessionSqlCompiler.Compile(window);
        var parameters = new ClickHouseParameterCollection();

        foreach (var parameter in statement.Parameters)
        {
            parameters.AddParameter(parameter.Name, parameter.Value);
        }

        var found = ImmutableArray.CreateBuilder<ObservedSession>();

        await using var reader = await client
            .ExecuteReaderAsync(statement.Sql, parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            found.Add(new ObservedSession(ToEvidence(reader), reader.GetBoolean(Column.IsClosed)));
        }

        return found.DrainToImmutable();
    }

    private static SessionEvidence ToEvidence(ClickHouseDataReader reader) => new()
    {
        SessionKey = reader.GetString(Column.SessionKey),
        StartedAt = reader.GetDateTimeOffset(Column.StartedAt),
        EndedAt = reader.GetDateTimeOffset(Column.EndedAt),
        PageCount = (int)reader.GetFieldValue<uint>(Column.PageCount),
        Requests = ToRequests(reader.GetFieldValue<Tuple<long, string, short?>[]>(Column.Requests)),
        Surfaces = ToSurfaces(reader.GetFieldValue<string[]>(Column.Surfaces)),
        UserAgent = NullIfEmpty(reader.GetString(Column.UserAgent)),
        Language = NullIfEmpty(reader.GetString(Column.Language)),
        ViewportWidth = Reading<int>(reader, Column.ViewportWidth),
        EngagedMs = Attention(reader),
        MaxScrollDepthPercent = Reading<byte>(reader, Column.MaxScrollDepthPercent),
        HadPointerInteraction = Watched(reader, Column.PointerObserved, Column.PointerSeen),
        HadKeyboardInteraction = Watched(reader, Column.KeyboardObserved, Column.KeyboardSeen),
        DeclaredWebDriver = Watched(reader, Column.WebDriverObserved, Column.WebDriverSeen),
        AutonomousSystem = reader.GetFieldValue<uint>(Column.AutonomousSystem),
        NetworkOwner = NullIfEmpty(reader.GetString(Column.NetworkOwner)),
    };

    private static ImmutableArray<ObservedRequest> ToRequests(Tuple<long, string, short?>[] rows) =>
    [
        .. rows.Select(row => new ObservedRequest(
            DateTimeOffset.FromUnixTimeMilliseconds(row.Item1),
            row.Item2,
            row.Item3)),
    ];

    /// <summary>
    /// Maps stored surface names back onto the enumeration.
    /// </summary>
    /// <remarks>
    /// A name this build has never heard of comes back as unattributed rather than throwing. Rows
    /// outlive the code that wrote them, and refusing to judge a whole site because one visit was
    /// recorded by a newer build would be a worse answer than judging it without knowing which
    /// surface saw it.
    /// </remarks>
    private static ImmutableArray<IngestSurface> ToSurfaces(string[] names) =>
    [
        .. names.Select(name =>
            StoredNames.Surfaces.TryGetValue(name, out var surface) ? surface : IngestSurface.Unknown),
    ];

    /// <summary>
    /// Resolves a reading that some surfaces can take and others cannot.
    /// </summary>
    /// <param name="reader">The open reader.</param>
    /// <param name="observedColumn">How many reports could take the reading.</param>
    /// <param name="seenColumn">How many of those found something.</param>
    /// <returns>
    /// <see langword="null"/> when nothing was in a position to watch, which must never be weighed
    /// as evidence that nothing happened.
    /// </returns>
    private static bool? Watched(ClickHouseDataReader reader, int observedColumn, int seenColumn) =>
        reader.GetFieldValue<uint>(observedColumn) == 0
            ? null
            : reader.GetFieldValue<uint>(seenColumn) > 0;

    /// <summary>
    /// Totals the time the pages were in front of somebody.
    /// </summary>
    /// <remarks>
    /// Held to what the reading can express rather than trusted. The collector accepts an
    /// implausible engaged time on purpose — a report that does not add up is itself evidence
    /// about what produced it — but a visitor who sends a hundred of them would otherwise overflow
    /// the total and hand the engine a negative one, which no detector is built to read.
    /// </remarks>
    /// <param name="reader">The open reader.</param>
    /// <returns>The total, or <see langword="null"/> when nothing measured any.</returns>
    private static int? Attention(ClickHouseDataReader reader)
    {
        var total = Reading<long>(reader, Column.EngagedMs);

        return total is null ? null : (int)Math.Clamp(total.Value, 0, int.MaxValue);
    }

    private static T? Reading<T>(ClickHouseDataReader reader, int column)
        where T : struct =>
        reader.IsDBNull(column) ? null : reader.GetFieldValue<T>(column);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
