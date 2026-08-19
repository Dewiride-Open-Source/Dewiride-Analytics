using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Dewiride.Analytics.Application.Analytics;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Removes everything the telemetry store holds for one site.
/// </summary>
/// <remarks>
/// <para>
/// Both tables are partitioned by month and sorted by site, so there is no partition that belongs
/// to a single site and nothing to drop wholesale. What is left is deleting by predicate, and the
/// lightweight form is the one the store's own documentation recommends for that: it marks the
/// matching rows and leaves the parts alone, where <c>ALTER TABLE … DELETE</c> rewrites every part
/// a match was found in — on a store holding a year of a busy site's traffic, the difference is
/// between a statement that returns and one that occupies the server for the rest of the day.
/// </para>
/// <para>
/// The statement waits, at the store's own default, until the rows have stopped answering queries.
/// That is what lets a caller treat a successful return as the telemetry being gone, which is the
/// whole reason the control-plane row is only deleted afterwards.
/// </para>
/// <para>
/// Nothing a caller supplied reaches the statement text. The table names are written here and the
/// site identifier is bound, so the only value crossing the wire is a UUID the store parses for
/// itself.
/// </para>
/// </remarks>
/// <param name="client">Telemetry store client.</param>
internal sealed class ClickHouseTelemetryPurge(IClickHouseClient client) : ITelemetryPurge
{
    /// <summary>Placeholder the site identifier binds to.</summary>
    private const string SiteIdParameter = "site_id";

    /// <summary>
    /// The tables holding rows that belong to one site.
    /// </summary>
    /// <remarks>
    /// Compiler-authored literals, and the list a new per-site table has to be added to. A table
    /// missing from here would leave a removed site's rows behind with nothing left to name them
    /// by, since every read is scoped through a site that no longer exists.
    /// </remarks>
    private static readonly string[] SiteScopedTables = ["events", "session_classifications"];

    /// <inheritdoc />
    public async Task PurgeSiteAsync(Guid siteId, CancellationToken cancellationToken)
    {
        foreach (var table in SiteScopedTables)
        {
            var parameters = new ClickHouseParameterCollection();
            parameters.AddParameter(SiteIdParameter, siteId);

            await client.ExecuteNonQueryAsync(
                    $"DELETE FROM {table} WHERE site_id = {{{SiteIdParameter}:UUID}}",
                    parameters,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
