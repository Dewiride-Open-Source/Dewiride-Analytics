using System.Globalization;
using System.Runtime.CompilerServices;
using ClickHouse.Driver;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// When this installation began keeping months: the moment its store gained the table they are
/// kept in.
/// </summary>
/// <remarks>
/// <para>
/// Read from the store's own record of the changes made to it, which is written once, when the
/// change is applied, and never again — so the answer never moves, however often months are counted
/// again or websites are removed. Counting begins a minute after the engine starts, so the two are
/// the same moment wherever counting is left on, which it is unless an operator switches it off.
/// </para>
/// <para>
/// Read once and remembered for the life of the process, since nothing changes it.
/// </para>
/// </remarks>
/// <param name="client">Telemetry store client.</param>
internal sealed class KeepingOrigin(IClickHouseClient client)
{
    /// <summary>The change that gave the store somewhere to keep months.</summary>
    internal const uint KeptMonthsMigration = 14;

    private static readonly string Sql = string.Create(
        CultureInfo.InvariantCulture,
        $"SELECT applied_at FROM schema_migrations WHERE version = {KeptMonthsMigration} ORDER BY applied_at LIMIT 1");

    private StrongBox<DateTimeOffset>? since;

    /// <summary>
    /// When this installation began keeping months.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The moment, or nothing where the store has no record of it.</returns>
    public async Task<DateTimeOffset?> SinceAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref since) is { } known)
        {
            return known.Value;
        }

        await using var reader = await client
            .ExecuteReaderAsync(Sql, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var began = reader.GetDateTimeOffset(0);
        Volatile.Write(ref since, new StrongBox<DateTimeOffset>(began));

        return began;
    }
}
