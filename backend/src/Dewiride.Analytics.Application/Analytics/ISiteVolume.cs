using System.Collections.Immutable;

namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// Counts how much a set of sites delivered over a window.
/// </summary>
/// <remarks>
/// <para>
/// For the parts of the product that work on behalf of the system rather than on behalf of a
/// person, on the same terms as <see cref="Sites.ISiteRoster"/>: the sites come from the control
/// plane rather than from a request, so there is no caller whose membership could be checked and
/// no <see cref="Tenancy.TenantScope"/> to take. Nothing reachable from a request may use this —
/// the questions a person asks are the ones in <see cref="ITelemetryQueries"/>, every one of which
/// demands proof of a role on the site it reads.
/// </para>
/// <para>
/// It is declared here, in the open-source product, because the telemetry store is only ever
/// reached from the one project that holds its driver. A count of what an installation delivered
/// therefore has to be asked for through a port the whole product can see, whichever edition is
/// the one that needs the answer.
/// </para>
/// <para>
/// What it counts is pages delivered, on exactly the same arithmetic as the headline totals a
/// dashboard shows. Two figures derived two ways would eventually disagree, and a customer whose
/// dashboard and whose allowance say different numbers has no way to tell which one is wrong.
/// </para>
/// </remarks>
public interface ISiteVolume
{
    /// <summary>
    /// Counts pages delivered, per site, over a window.
    /// </summary>
    /// <param name="window">Which sites, and which stretch of time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One row per site that delivered anything, in a fixed order. A site that delivered nothing
    /// is absent rather than present with a nought, because the store has no rows to report it
    /// from.
    /// </returns>
    Task<IReadOnlyList<SiteVolume>> CountAsync(SiteVolumeWindow window, CancellationToken cancellationToken);
}

/// <summary>
/// Which sites to count, and over what stretch of time.
/// </summary>
public sealed record SiteVolumeWindow
{
    /// <summary>The window to count over.</summary>
    public required TimeRange Range { get; init; }

    /// <summary>
    /// The sites to count, which the caller has already established belong together.
    /// </summary>
    /// <remarks>
    /// Asked for together rather than one at a time because the answer is a sum across all of
    /// them, and a question per site would put one statement per site on the store every time
    /// anybody's usage was worked out.
    /// </remarks>
    public required ImmutableArray<Guid> SiteIds { get; init; }
}

/// <summary>
/// What one site delivered over the window.
/// </summary>
/// <param name="SiteId">The site.</param>
/// <param name="PageViews">Pages delivered, counted as the dashboard counts them.</param>
public readonly record struct SiteVolume(Guid SiteId, long PageViews);
