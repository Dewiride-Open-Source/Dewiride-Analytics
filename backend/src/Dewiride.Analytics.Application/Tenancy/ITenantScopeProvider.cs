namespace Dewiride.Analytics.Application.Tenancy;

/// <summary>
/// Resolves the authorisation scope for a telemetry request.
/// </summary>
/// <remarks>
/// Two implementations exist and they are the single point at which the two editions differ
/// on tenancy. The Community implementation resolves against the one organisation that
/// exists in a self-hosted install; the commercial implementation resolves the caller's
/// membership across organisations. Everything downstream — every query, every screen — is
/// written once against the resulting <see cref="TenantScope"/>.
/// </remarks>
public interface ITenantScopeProvider
{
    /// <summary>
    /// Resolves the scope for the current caller against a site.
    /// </summary>
    /// <param name="siteId">The site being requested.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The scope, or <see langword="null"/> when the site does not exist or the caller has no
    /// role on it. Callers must treat both cases identically and respond as though the site
    /// does not exist, so that the API cannot be used to test which site identifiers are real.
    /// </returns>
    Task<TenantScope?> ResolveAsync(Guid siteId, CancellationToken cancellationToken);
}
