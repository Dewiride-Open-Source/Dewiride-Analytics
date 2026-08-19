namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// Removes everything the telemetry store holds for one site.
/// </summary>
/// <remarks>
/// <para>
/// A port rather than a call, because the two stores are implemented in different assemblies and
/// only one of them may speak to the telemetry store. Removing a site is a control-plane decision
/// carried out where the control plane lives, and it needs the telemetry gone as well; expressing
/// that need as an interface is what lets it be met without the control plane taking a reference
/// on the telemetry driver.
/// </para>
/// <para>
/// It takes a site identifier rather than a <see cref="Dewiride.Analytics.Application.Tenancy.TenantScope"/>,
/// unlike everything on <see cref="ITelemetryQueries"/>. A scope is proof that somebody may read a
/// site, and reading is what it guards; this is reached only after the control plane has satisfied
/// itself that the caller owns the site and is about to delete the row that a scope would be
/// resolved from. Requiring one here would mean resolving proof of a right to read in order to
/// exercise a right to destroy, which is a weaker check dressed as a stronger one.
/// </para>
/// </remarks>
public interface ITelemetryPurge
{
    /// <summary>
    /// Deletes every row the telemetry store holds for a site.
    /// </summary>
    /// <remarks>
    /// Returning is the guarantee: the call completes once the rows have stopped answering
    /// queries, so the caller may treat a successful return as the telemetry being gone rather
    /// than as the deletion having been accepted for later.
    /// </remarks>
    /// <param name="siteId">The site whose telemetry is to be removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when nothing is left to read.</returns>
    Task PurgeSiteAsync(Guid siteId, CancellationToken cancellationToken);
}
