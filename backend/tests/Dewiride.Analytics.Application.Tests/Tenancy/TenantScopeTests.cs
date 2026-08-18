using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Application.Tests.Tenancy;

/// <summary>
/// Covers the authorisation decision every telemetry read is bound to.
/// </summary>
public sealed class TenantScopeTests
{
    /// <summary>
    /// The time zone travels with the scope so that a day-bucketed query reports the owner's days
    /// rather than UTC's, and so the value reaching a query is always the one stored against the
    /// site rather than one a caller chose.
    /// </summary>
    [Fact]
    public void Carries_The_Site_Its_Owner_And_The_Zone_Its_Days_Are_Cut_In()
    {
        var siteId = Guid.Parse("0197c0de-0000-7000-8000-000000000010");
        var organizationId = Guid.Parse("0197c0de-0000-7000-8000-000000000011");

        var scope = new TenantScope(siteId, organizationId, SiteRole.Editor, "Asia/Kolkata");

        scope.SiteId.Should().Be(siteId);
        scope.OrganizationId.Should().Be(organizationId);
        scope.Role.Should().Be(SiteRole.Editor);
        scope.TimeZoneId.Should().Be("Asia/Kolkata");
    }
}
