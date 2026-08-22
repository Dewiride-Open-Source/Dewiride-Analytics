using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Domain.Tests.Sites;

/// <summary>
/// Covers the rules an invitation enforces about itself.
/// </summary>
/// <remarks>
/// Which state it is in is the whole of what decides whether a link works, and it is decided from
/// an instant handed in rather than read from the machine — so an invitation that has run out can
/// be observed without waiting a week for one to.
/// </remarks>
public sealed class OrganizationInvitationTests
{
    private static readonly DateTimeOffset Sent = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Keeps_The_Address_As_It_Was_Typed_And_Matches_On_The_Other_Form()
    {
        var invitation = NewInvitation(address: "  Ada@Example.com  ");

        invitation.EmailAddress.Should().Be("Ada@Example.com");
        invitation.NormalizedEmailAddress.Should().Be("ADA@EXAMPLE.COM");
    }

    [Fact]
    public void Constructor_Sets_The_Invitation_To_Run_Out_A_Week_Later()
    {
        var invitation = NewInvitation();

        invitation.ExpiresAt.Should().Be(Sent + OrganizationInvitation.Life);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Refuses_An_Address_That_Is_Not_One(string address)
    {
        var refused = () => NewInvitation(address: address);

        refused.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_Refuses_An_Empty_Digest()
    {
        var refused = () => NewInvitation(tokenHash: "  ");

        refused.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_Fresh_Invitation_Is_Waiting_To_Be_Taken_Up()
    {
        var invitation = NewInvitation();

        invitation.StateAt(Sent.AddDays(6)).Should().Be(InvitationState.Pending);
    }

    [Fact]
    public void An_Invitation_Left_Long_Enough_Stops_Working()
    {
        var invitation = NewInvitation();

        invitation.StateAt(Sent + OrganizationInvitation.Life).Should().Be(InvitationState.Expired);
    }

    [Fact]
    public void An_Invitation_That_Was_Taken_Up_Stays_Taken_Up()
    {
        var invitation = NewInvitation();

        invitation.Accept(Sent.AddHours(2));

        invitation.StateAt(Sent.AddHours(3)).Should().Be(InvitationState.Accepted);
        invitation.StateAt(Sent.AddYears(1)).Should().Be(InvitationState.Accepted);
    }

    /// <summary>
    /// Taking one up twice is a link opened twice, which happens whenever a mail client fetches it.
    /// The second must not move the moment it was taken up.
    /// </summary>
    [Fact]
    public void Being_Taken_Up_Again_Changes_Nothing()
    {
        var invitation = NewInvitation();

        invitation.Accept(Sent.AddHours(2));
        invitation.Accept(Sent.AddHours(5));

        invitation.AcceptedAt.Should().Be(Sent.AddHours(2));
    }

    [Fact]
    public void A_Withdrawn_Invitation_Stops_Working()
    {
        var invitation = NewInvitation();

        invitation.Revoke(Sent.AddHours(1));

        invitation.StateAt(Sent.AddHours(2)).Should().Be(InvitationState.Revoked);
    }

    /// <summary>
    /// Being taken up wins over being withdrawn. Somebody who joined and was then removed is on the
    /// list of people, and reading their invitation as merely withdrawn would say they never came.
    /// </summary>
    [Fact]
    public void An_Invitation_Taken_Up_And_Then_Withdrawn_Reads_As_Taken_Up()
    {
        var invitation = NewInvitation();

        invitation.Accept(Sent.AddHours(1));
        invitation.Revoke(Sent.AddHours(2));

        invitation.StateAt(Sent.AddHours(3)).Should().Be(InvitationState.Accepted);
    }

    [Fact]
    public void Sending_It_Again_Replaces_The_Secret_And_Moves_The_Day_It_Runs_Out()
    {
        var invitation = NewInvitation();
        var later = Sent.AddDays(10);

        invitation.Renew(OrganizationRole.Admin, new string('b', 64), later);

        invitation.Role.Should().Be(OrganizationRole.Admin);
        invitation.TokenHash.Should().Be(new string('b', 64));
        invitation.InvitedAt.Should().Be(later);
        invitation.ExpiresAt.Should().Be(later + OrganizationInvitation.Life);
        invitation.StateAt(later).Should().Be(InvitationState.Pending);
    }

    /// <summary>
    /// Somebody who left and is asked back arrives here. An invitation that stayed marked as taken
    /// up would hand them a link that had already stopped working.
    /// </summary>
    [Fact]
    public void Sending_It_Again_Undoes_Having_Been_Taken_Up_Or_Withdrawn()
    {
        var invitation = NewInvitation();

        invitation.Accept(Sent.AddHours(1));
        invitation.Revoke(Sent.AddHours(2));
        invitation.Renew(OrganizationRole.Member, new string('c', 64), Sent.AddDays(30));

        invitation.AcceptedAt.Should().BeNull();
        invitation.RevokedAt.Should().BeNull();
        invitation.StateAt(Sent.AddDays(30)).Should().Be(InvitationState.Pending);
    }

    private static OrganizationInvitation NewInvitation(
        string address = "ada@example.com",
        string tokenHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef") =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            address,
            OrganizationRole.Member,
            Guid.NewGuid(),
            tokenHash,
            Sent);
}
