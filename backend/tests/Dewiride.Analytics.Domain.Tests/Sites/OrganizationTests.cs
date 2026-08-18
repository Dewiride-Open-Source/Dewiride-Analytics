using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Domain.Tests.Sites;

/// <summary>
/// Covers the organisation aggregate.
/// </summary>
public sealed class OrganizationTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Trims_The_Name()
    {
        var organization = new Organization(Guid.NewGuid(), "  Example Media  ", CreatedAt);

        organization.Name.Should().Be("Example Media");
    }

    [Fact]
    public void Constructor_Records_The_Supplied_Creation_Time()
    {
        var organization = new Organization(Guid.NewGuid(), "Example Media", CreatedAt);

        organization.CreatedAt.Should().Be(CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Rejects_A_Blank_Name(string blank)
    {
        var act = () => new Organization(Guid.NewGuid(), blank, CreatedAt);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void Constructor_Leaves_The_Site_List_Empty()
    {
        var organization = new Organization(Guid.NewGuid(), "Example Media", CreatedAt);

        organization.Sites.Should().BeEmpty();
    }

    [Fact]
    public void Rename_Trims_The_New_Name()
    {
        var organization = new Organization(Guid.NewGuid(), "Example Media", CreatedAt);

        organization.Rename("  Example Publishing  ");

        organization.Name.Should().Be("Example Publishing");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_Rejects_A_Blank_Name(string blank)
    {
        var organization = new Organization(Guid.NewGuid(), "Example Media", CreatedAt);

        var act = () => organization.Rename(blank);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }
}
