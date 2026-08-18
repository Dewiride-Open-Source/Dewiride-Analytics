using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Reads the telemetry store back in tests.
/// </summary>
/// <remarks>
/// Values are bound here for the same reason they are bound in the product: a test that
/// interpolates a value into a statement is a test that has stopped resembling the thing it is
/// checking.
/// </remarks>
internal static class TelemetryStore
{
    /// <summary>
    /// Binds one value.
    /// </summary>
    /// <param name="name">Placeholder name, without braces.</param>
    /// <param name="value">The value to bind.</param>
    /// <returns>The parameter collection.</returns>
    public static ClickHouseParameterCollection Bind(string name, object value)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter(name, value);

        return parameters;
    }

    /// <summary>
    /// Reads the first column of the first row.
    /// </summary>
    /// <typeparam name="T">The column's type.</typeparam>
    /// <param name="client">Telemetry store client.</param>
    /// <param name="sql">The statement.</param>
    /// <param name="parameters">Bound values, or null when the statement takes none.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">The statement returned no rows.</exception>
    public static async Task<T> ScalarAsync<T>(
        IClickHouseClient client,
        string sql,
        ClickHouseParameterCollection? parameters = null)
    {
        await using var reader = await client
            .ExecuteReaderAsync(sql, parameters, cancellationToken: Cancellation.Token)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(Cancellation.Token).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"The telemetry store returned no rows for: {sql}");
        }

        return await reader.GetFieldValueAsync<T>(0, Cancellation.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads every row, keyed by column name.
    /// </summary>
    /// <param name="client">Telemetry store client.</param>
    /// <param name="sql">The statement.</param>
    /// <param name="parameters">Bound values, or null when the statement takes none.</param>
    /// <returns>The rows, in the order the store returned them.</returns>
    public static async Task<List<Dictionary<string, object?>>> RowsAsync(
        IClickHouseClient client,
        string sql,
        ClickHouseParameterCollection? parameters = null)
    {
        var rows = new List<Dictionary<string, object?>>();

        await using var reader = await client
            .ExecuteReaderAsync(sql, parameters, cancellationToken: Cancellation.Token)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(Cancellation.Token).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);

            for (var column = 0; column < reader.FieldCount; column++)
            {
                var absent = await reader.IsDBNullAsync(column, Cancellation.Token).ConfigureAwait(false);

                row[reader.GetName(column)] = absent ? null : reader.GetValue(column);
            }

            rows.Add(row);
        }

        return rows;
    }
}
