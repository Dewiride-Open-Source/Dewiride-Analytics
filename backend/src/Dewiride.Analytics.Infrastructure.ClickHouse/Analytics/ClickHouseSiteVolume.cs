using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Dewiride.Analytics.Application.Analytics;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Counts what a set of sites delivered, from the telemetry store.
/// </summary>
/// <param name="client">Telemetry store client.</param>
internal sealed class ClickHouseSiteVolume(IClickHouseClient client) : ISiteVolume
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SiteVolume>> CountAsync(
        SiteVolumeWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        // No sites means no statement. Sent as written it would still be a valid question with an
        // empty answer, but it is one query per organisation that has not added a site yet, on a
        // path that runs for every organisation on the installation.
        if (window.SiteIds.IsDefaultOrEmpty)
        {
            return [];
        }

        var statement = AnalyticsSqlCompiler.CompileVolume(window);
        var parameters = new ClickHouseParameterCollection();

        foreach (var parameter in statement.Parameters)
        {
            parameters.AddParameter(parameter.Name, parameter.Value);
        }

        var counted = new List<SiteVolume>(window.SiteIds.Length);

        await using var reader = await client
            .ExecuteReaderAsync(statement.Sql, parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counted.Add(new SiteVolume(reader.GetGuid(0), reader.GetInt64(1)));
        }

        return counted;
    }
}
