using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Domain.Tests.Sites;

/// <summary>
/// Covers the grant of a role on a site.
/// </summary>
public sealed class SiteMembershipTests
{
    private static readonly DateTimeOffset GrantedAt = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Records_What_Was_Granted_To_Whom()
    {
        var siteId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var membership = new SiteMembership(Guid.NewGuid(), siteId, userId, SiteRole.Editor, GrantedAt);

        membership.SiteId.Should().Be(siteId);
        membership.UserId.Should().Be(userId);
        membership.Role.Should().Be(SiteRole.Editor);
        membership.GrantedAt.Should().Be(GrantedAt);
    }

    [Fact]
    public void ChangeRole_Replaces_The_Granted_Role()
    {
        var membership = new SiteMembership(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SiteRole.Viewer,
            GrantedAt);

        membership.ChangeRole(SiteRole.Owner);

        membership.Role.Should().Be(SiteRole.Owner);
    }

    /// <summary>
    /// Roles are ordered so that a check can ask whether someone holds at least a given level
    /// rather than enumerating every role that qualifies.
    /// </summary>
    [Fact]
    public void Roles_Are_Ordered_From_Least_To_Most_Permitted()
    {
        var ordered = Enum.GetValues<SiteRole>().OrderBy(role => (int)role);

        ordered.Should().Equal(SiteRole.Viewer, SiteRole.Editor, SiteRole.Owner);
    }
}
