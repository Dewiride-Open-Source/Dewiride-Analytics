using ClickHouse.Driver;
using Dewiride.Analytics.Application.Sessions;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Sessions;

/// <summary>
/// Writes answers settled by name to the telemetry store.
/// </summary>
/// <remarks>
/// Through the binary row format, as verdicts are, so nothing written is ever parsed as SQL. The
/// table replaces on the site, the visit's beginning and its derived identity, keeping the latest
/// answer — which is what makes a pass safe to repeat.
/// </remarks>
/// <param name="client">Telemetry store client.</param>
internal sealed class ClickHouseNameConfirmationStore(IClickHouseClient client) : INameConfirmationStore
{
    private const string TableName = "name_confirmations";

    /// <summary>Insert columns, in the order <see cref="ToRow"/> produces values for them.</summary>
    private static readonly string[] Columns =
    [
        "site_id",
        "session_key",
        "started_at",
        "operator",
        "settled_at",
    ];

    /// <inheritdoc />
    public Task RecordAsync(
        Guid siteId,
        IReadOnlyCollection<NameConfirmation> confirmations,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmations);

        return confirmations.Count == 0
            ? Task.CompletedTask
            : client.InsertBinaryAsync(
                TableName,
                Columns,
                confirmations.Select(confirmation => ToRow(siteId, confirmation, settledAt)),
                cancellationToken: cancellationToken);
    }

    private static object[] ToRow(Guid siteId, NameConfirmation confirmation, DateTimeOffset settledAt) =>
    [
        siteId,
        confirmation.SessionKey,
        confirmation.StartedAt.UtcDateTime,
        confirmation.Operator,
        settledAt.UtcDateTime,
    ];
}
