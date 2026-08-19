using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Domain.Tests;

/// <summary>
/// Guards the enumerations whose members are written to a database and read back later.
/// </summary>
/// <remarks>
/// These are not tests of the language. A stored vocabulary outlives the code that wrote it: a
/// renamed member reinterprets rows nobody will look at again, and there is no deployment window
/// in which to notice, because self-hosted installations upgrade whenever their owner decides to.
/// Changing any of these is a migration, and the test is what makes that a conversation rather
/// than a surprise.
/// </remarks>
public sealed class StoredVocabularyTests
{
    /// <summary>
    /// Longest role name the control-plane column accepts. Roles are stored as text rather than
    /// as numbers so that the table can be read without a lookup elsewhere.
    /// </summary>
    private const int RoleColumnLength = 20;

    [Fact]
    public void Site_Roles_Are_Exactly_The_Three_The_Product_Grants()
    {
        Enum.GetNames<SiteRole>().Should().BeEquivalentTo("Viewer", "Editor", "Owner");
    }

    [Fact]
    public void Site_Role_Names_Fit_The_Column_They_Are_Stored_In()
    {
        Enum.GetNames<SiteRole>().Should().OnlyContain(name => name.Length <= RoleColumnLength);
    }

    /// <summary>
    /// Both enumerations reserve nought for the unattributed case, so a value that was never set
    /// reads as "not known" rather than as whichever member happened to be declared first.
    /// </summary>
    [Fact]
    public void Unattributed_Is_The_Default_Value_Of_Both_Telemetry_Enumerations()
    {
        default(EventKind).Should().Be(EventKind.Unknown);
        default(IngestSurface).Should().Be(IngestSurface.Unknown);
    }

    [Fact]
    public void Event_Kinds_Are_The_Four_A_Surface_May_Report()
    {
        Enum.GetNames<EventKind>()
            .Should()
            .BeEquivalentTo("Unknown", "PageView", "Engagement", "Exit", "Action");
    }

    /// <summary>
    /// The stored value of an enumeration is its number, so a member that changed number would
    /// reinterpret every row already written under the old one.
    /// </summary>
    [Fact]
    public void Every_Event_Kind_Has_The_Number_Its_Rows_Were_Written_Under()
    {
        ((int)EventKind.Unknown).Should().Be(0);
        ((int)EventKind.PageView).Should().Be(1);
        ((int)EventKind.Engagement).Should().Be(2);
        ((int)EventKind.Exit).Should().Be(3);
        ((int)EventKind.Action).Should().Be(4);
    }

    [Fact]
    public void Every_Capture_Surface_Has_A_Distinct_Number()
    {
        var surfaces = Enum.GetValues<IngestSurface>();

        surfaces.Should().OnlyHaveUniqueItems();
        surfaces.Cast<int>().Should().OnlyHaveUniqueItems();
    }
}
