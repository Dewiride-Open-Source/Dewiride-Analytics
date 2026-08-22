using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Domain.Tests.Sites;

/// <summary>
/// Covers what a standing in an organisation records and what it permits.
/// </summary>
/// <remarks>
/// The translation to a role on a site is the load-bearing part. It is what lets somebody added
/// to an account read the sites the account owns, so a mapping that quietly narrowed would show a
/// team an empty dashboard rather than an error anybody could act on.
/// </remarks>
public sealed class OrganizationMembershipTests
{
    private static readonly DateTimeOffset GrantedAt = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Records_What_Was_Granted_And_When()
    {
        var id = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var membership = new OrganizationMembership(
            id,
            organizationId,
            userId,
            OrganizationRole.Admin,
            GrantedAt);

        membership.Id.Should().Be(id);
        membership.OrganizationId.Should().Be(organizationId);
        membership.UserId.Should().Be(userId);
        membership.Role.Should().Be(OrganizationRole.Admin);
        membership.GrantedAt.Should().Be(GrantedAt);
    }

    [Fact]
    public void ChangeRole_Replaces_The_Standing_And_Leaves_Everything_Else()
    {
        var membership = NewMembership(OrganizationRole.Member);

        membership.ChangeRole(OrganizationRole.Owner);

        membership.Role.Should().Be(OrganizationRole.Owner);
        membership.GrantedAt.Should().Be(GrantedAt);
    }

    [Theory]
    [InlineData(OrganizationRole.Member, SiteRole.Viewer)]
    [InlineData(OrganizationRole.Admin, SiteRole.Editor)]
    [InlineData(OrganizationRole.Owner, SiteRole.Owner)]
    public void A_Standing_Permits_The_Matching_Role_On_Every_Site_The_Organisation_Owns(
        OrganizationRole standing,
        SiteRole expected)
    {
        standing.OnItsSites().Should().Be(expected);
    }

    /// <summary>
    /// Every standing this product defines has a translation. A value outside the enumeration can
    /// only arrive from a cast or from a row written by something that is not this product, and
    /// answering it with the narrowest role would silently grant a stranger a reader's access.
    /// </summary>
    [Fact]
    public void A_Standing_This_Product_Does_Not_Define_Permits_Nothing()
    {
        var invented = (OrganizationRole)99;

        var permitting = () => invented.OnItsSites();

        permitting.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// The rule every read of a site rests on. Somebody can hold a grant on the site, a standing in
    /// the organisation that owns it, or both, and the wider of the two is what they may do.
    /// </summary>
    [Theory]
    [InlineData(null, OrganizationRole.Member, SiteRole.Viewer)]
    [InlineData(null, OrganizationRole.Admin, SiteRole.Editor)]
    [InlineData(null, OrganizationRole.Owner, SiteRole.Owner)]
    [InlineData(SiteRole.Editor, null, SiteRole.Editor)]
    [InlineData(SiteRole.Owner, OrganizationRole.Member, SiteRole.Owner)]
    [InlineData(SiteRole.Editor, OrganizationRole.Member, SiteRole.Editor)]
    [InlineData(SiteRole.Viewer, OrganizationRole.Owner, SiteRole.Owner)]
    [InlineData(SiteRole.Viewer, OrganizationRole.Admin, SiteRole.Editor)]
    [InlineData(SiteRole.Owner, OrganizationRole.Admin, SiteRole.Owner)]
    public void Holding_Either_Claim_Gives_The_Wider_Of_Them(
        SiteRole? granted,
        OrganizationRole? standing,
        SiteRole expected)
    {
        OrganizationRoles.Widest(granted, standing).Should().Be(expected);
    }

    /// <summary>
    /// Neither claim is not the narrowest role. It is no role at all, and the difference is
    /// between a stranger being turned away and a stranger being handed somebody's figures.
    /// </summary>
    [Fact]
    public void Holding_Neither_Claim_Gives_Nothing()
    {
        OrganizationRoles.Widest(null, null).Should().BeNull();
    }

    private static OrganizationMembership NewMembership(OrganizationRole role) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), role, GrantedAt);
}
