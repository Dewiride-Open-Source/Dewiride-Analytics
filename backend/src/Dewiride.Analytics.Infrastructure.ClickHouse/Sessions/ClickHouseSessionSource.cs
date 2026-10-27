using System.Collections.Immutable;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;
using Column = Dewiride.Analytics.Infrastructure.ClickHouse.Sessions.SessionSqlCompiler.Column;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Sessions;

/// <summary>
/// Rebuilds visits from the telemetry store.
/// </summary>
/// <remarks>
/// <para>
/// Turns rows into the closed set of values the engine reasons about. Everything three-state stays
/// three-state across the boundary: a visit nobody could watch for pointer activity arrives with
/// that reading absent rather than false, because the difference between "nobody touched anything"
/// and "nothing was watching" is the difference between evidence and the absence of it.
/// </para>
/// <para>
/// One value comes back outside that set. The address a visit arrived from is carried beside the
/// evidence rather than within it, because what it is for is settling whose crawlers it belongs to
/// before the engine is asked anything — and a detector that could see an address would be a
/// detector that could reason about where somebody lives.
/// </para>
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
            found.Add(new ObservedSession(
                ToEvidence(reader),
                reader.GetBoolean(Column.IsClosed),
                NullIfEmpty(reader.GetString(Column.Address))));
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
        EngagedMs = Milliseconds(reader, Column.EngagedMs),
        LeftOpenMs = LeftOpen(reader),
        SoonestProgressMs = Milliseconds(reader, Column.SoonestProgressMs),
        MaxScrollDepthPercent = Reading<byte>(reader, Column.MaxScrollDepthPercent),
        HadPointerInteraction = Watched(reader, Column.PointerObserved, Column.PointerSeen),
        HadKeyboardInteraction = Watched(reader, Column.KeyboardObserved, Column.KeyboardSeen),
        DeclaredWebDriver = Watched(reader, Column.WebDriverObserved, Column.WebDriverSeen),
        AutonomousSystem = reader.GetFieldValue<uint>(Column.AutonomousSystem),
        NetworkOwner = NullIfEmpty(reader.GetString(Column.NetworkOwner)),
        ConfirmedOperator = NullIfEmpty(reader.GetString(Column.ConfirmedOperator)),
        SentBy = SendingSites.KindOf(reader.GetString(Column.SentBy)),
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
    /// Reads a length of time the statement worked out from what the tracker reported.
    /// </summary>
    /// <remarks>
    /// Held to what the reading can express rather than trusted. The collector accepts an
    /// implausible engaged time on purpose — a report that does not add up is itself evidence
    /// about what produced it — but a visitor who sends a hundred of them would otherwise overflow
    /// a total and hand the engine a negative one, which no detector is built to read.
    /// </remarks>
    /// <param name="reader">The open reader.</param>
    /// <param name="column">Where the statement put it.</param>
    /// <returns>The length, or <see langword="null"/> when nothing measured any.</returns>
    private static int? Milliseconds(ClickHouseDataReader reader, int column)
    {
        var total = Reading<long>(reader, column);

        return total is null ? null : (int)Math.Clamp(total.Value, 0, int.MaxValue);
    }

    /// <summary>
    /// Works out how much of the time the pages reported they did not say as they were being left.
    /// </summary>
    /// <remarks>
    /// Taken from the total and from what was said as pages were left, each held to what a reading
    /// can express first. Working it out as the difference of two totals held separately would let
    /// one impossible report on one page swallow what every other page said as it was left.
    /// </remarks>
    /// <param name="reader">The open reader.</param>
    /// <returns>The time, or <see langword="null"/> when nothing measured any.</returns>
    private static int? LeftOpen(ClickHouseDataReader reader)
    {
        var engaged = Milliseconds(reader, Column.EngagedMs);

        return engaged is null ? null : Math.Max(0, engaged.Value - (Milliseconds(reader, Column.ReadMs) ?? 0));
    }

    private static T? Reading<T>(ClickHouseDataReader reader, int column)
        where T : struct =>
        reader.IsDBNull(column) ? null : reader.GetFieldValue<T>(column);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
