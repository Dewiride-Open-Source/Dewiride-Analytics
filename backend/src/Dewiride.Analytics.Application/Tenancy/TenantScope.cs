using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Application.Tenancy;

/// <summary>
/// Proof that the current caller is authorised to read a particular site's telemetry.
/// </summary>
/// <remarks>
/// <para>
/// Every method on <see cref="Analytics.ITelemetryQueries"/> requires one of these. That is
/// the mechanism by which tenant isolation is enforced: it is not possible to express a
/// telemetry query without first having obtained a scope, and a scope can only be produced
/// by <see cref="ITenantScopeProvider"/>, which checks membership.
/// </para>
/// <para>
/// The constructor is deliberately internal. If any caller holding an arbitrary site
/// identifier could construct a scope, the type would document an intention rather than
/// enforce a rule.
/// </para>
/// </remarks>
public sealed record TenantScope
{
    /// <summary>The site the caller is authorised to read.</summary>
    public Guid SiteId { get; }

    /// <summary>The organisation that owns the site.</summary>
    public Guid OrganizationId { get; }

    /// <summary>The role the caller holds on the site.</summary>
    public SiteRole Role { get; }

    /// <summary>
    /// IANA time zone the site's numbers are reported in.
    /// </summary>
    /// <remarks>
    /// It travels with the scope so that a day-bucketed query reports the owner's days rather
    /// than UTC's, without a second lookup on every read. Carrying it here also means the value
    /// that reaches a query is always the one stored against the site, never one a caller chose.
    /// </remarks>
    public string TimeZoneId { get; }

    internal TenantScope(Guid siteId, Guid organizationId, SiteRole role, string timeZoneId)
    {
        SiteId = siteId;
        OrganizationId = organizationId;
        Role = role;
        TimeZoneId = timeZoneId;
    }
}
