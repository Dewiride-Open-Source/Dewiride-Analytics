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

    [Fact]
    public void A_New_Organisation_Is_Open()
    {
        var organization = new Organization(Guid.NewGuid(), "Example Media", CreatedAt);

        organization.IsClosed.Should().BeFalse();
        organization.ClosedAt.Should().BeNull();
        organization.ClosedByUserId.Should().BeNull();
        organization.DeletionDue.Should().BeNull();
    }

    [Fact]
    public void Closing_Records_Who_And_When()
    {
        var organization = new Organization(Guid.NewGuid(), "Example Media", CreatedAt);
        var owner = Guid.NewGuid();
        var closedAt = CreatedAt.AddDays(40);

        organization.Close(owner, closedAt);

        organization.IsClosed.Should().BeTrue();
        organization.ClosedAt.Should().Be(closedAt);
        organization.ClosedByUserId.Should().Be(owner);
    }

    /// <summary>
    /// Everything about a closed organisation is deleted thirty days on, unless it is brought back.
    /// </summary>
    [Fact]
    public void Deletion_Is_Due_Thirty_Days_After_Closing()
    {
        var organization = new Organization(Guid.NewGuid(), "Example Media", CreatedAt);
        var closedAt = CreatedAt.AddDays(40);

        organization.Close(Guid.NewGuid(), closedAt);

        Organization.ClosureRetention.Should().Be(TimeSpan.FromDays(30));
        organization.DeletionDue.Should().Be(closedAt.AddDays(30));
    }

    /// <summary>
    /// The owners are reminded a week before deletion, which is what the two constants add up to.
    /// </summary>
    [Fact]
    public void The_Reminder_Comes_A_Week_Before_Deletion()
    {
        Organization.DeletionReminderLead.Should().Be(TimeSpan.FromDays(7));
        (Organization.ClosureRetention - Organization.DeletionReminderLead).Should().Be(TimeSpan.FromDays(23));
    }

    /// <summary>
    /// Two owners pressing the same button, or one request retried, must not move the instant the
    /// deletion date and the announcement hang off.
    /// </summary>
    [Fact]
    public void Closing_Twice_Keeps_The_First_Instant_And_Closer()
    {
        var organization = new Organization(Guid.NewGuid(), "Example Media", CreatedAt);
        var first = Guid.NewGuid();
        var closedAt = CreatedAt.AddDays(40);

        organization.Close(first, closedAt);
        organization.Close(Guid.NewGuid(), closedAt.AddHours(2));

        organization.ClosedAt.Should().Be(closedAt);
        organization.ClosedByUserId.Should().Be(first);
    }

    [Fact]
    public void Reopening_Clears_Every_Trace_Of_The_Closure()
    {
        var organization = new Organization(Guid.NewGuid(), "Example Media", CreatedAt);
        organization.Close(Guid.NewGuid(), CreatedAt.AddDays(40));

        organization.Reopen();

        organization.IsClosed.Should().BeFalse();
        organization.ClosedAt.Should().BeNull();
        organization.ClosedByUserId.Should().BeNull();
        organization.DeletionReminderSentAt.Should().BeNull();
        organization.DeletionDue.Should().BeNull();
    }

    [Fact]
    public void Reopening_An_Open_Organisation_Changes_Nothing()
    {
        var organization = new Organization(Guid.NewGuid(), "Example Media", CreatedAt);

        organization.Reopen();

        organization.IsClosed.Should().BeFalse();
        organization.Name.Should().Be("Example Media");
    }
}
