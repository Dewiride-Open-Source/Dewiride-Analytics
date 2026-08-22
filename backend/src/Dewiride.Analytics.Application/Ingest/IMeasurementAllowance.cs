using Dewiride.Analytics.Application.Sites;

namespace Dewiride.Analytics.Application.Ingest;

/// <summary>
/// Whether an installation is still measuring what a site reports.
/// </summary>
/// <remarks>
/// <para>
/// Nothing to do with what the site itself is set to collect — that is decided by the site's own
/// settings and applied beside this. This answers the question that only exists where somebody
/// else is running the service: whether the account the site belongs to is still entitled to be
/// measured at all.
/// </para>
/// <para>
/// A self-hosted installation is measuring whatever its owner points at it, so the open-source
/// edition answers yes to everything. The seam exists here rather than in the edition that needs
/// it because the collector is the open-source product's own code and there is exactly one place
/// a report can be turned away.
/// </para>
/// <para>
/// Asked once per accepted report, on the busiest path in the product, so an implementation must
/// answer from memory rather than from a database.
/// </para>
/// </remarks>
public interface IMeasurementAllowance
{
    /// <summary>
    /// Decides whether this site's reports are still being taken in.
    /// </summary>
    /// <param name="site">The site the report is for, already resolved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> to store the report. A refusal is silent to the sender: the
    /// collector's answer is the same either way, because it is the same answer it gives to a site
    /// that does not exist.
    /// </returns>
    ValueTask<bool> AllowsAsync(SiteSnapshot site, CancellationToken cancellationToken);
}
