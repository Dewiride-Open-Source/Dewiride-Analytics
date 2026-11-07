using Dewiride.Analytics.Infrastructure.Posts;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers keeping track of what a page has opened and not yet closed.
/// </summary>
public sealed class OpenElementsTests
{
    [Fact]
    public void Nothing_Is_Open_To_Begin_With()
    {
        var open = new OpenElements();

        open.Depth.Should().Be(0);
        open.Marks.Should().Be(ElementMarks.None);
    }

    [Fact]
    public void What_Is_Known_Of_Being_Inside_Is_What_The_Deepest_Says()
    {
        var open = new OpenElements();

        open.Open("div", ElementMarks.OldEntry);
        open.Open("svg", ElementMarks.OldEntry | ElementMarks.Foreign);

        open.Marks.Should().Be(ElementMarks.OldEntry | ElementMarks.Foreign);

        open.Close("svg");

        open.Marks.Should().Be(ElementMarks.OldEntry);
    }

    [Fact]
    public void An_End_Tag_Closes_The_Nearest_Of_Its_Name_And_Everything_Opened_Since()
    {
        var open = new OpenElements();

        open.Open("div", ElementMarks.OldEntry);
        open.Open("div", ElementMarks.NewEntry);
        open.Open("span", ElementMarks.Inert);
        open.Open("b", ElementMarks.Foreign);

        open.Close("div");

        open.Depth.Should().Be(1);
        open.Marks.Should().Be(ElementMarks.OldEntry);
    }

    [Fact]
    public void An_End_Tag_With_Nothing_Of_Its_Name_Open_Closes_Nothing()
    {
        var open = new OpenElements();

        open.Open("div", ElementMarks.OldEntry);

        open.Close("section");

        open.Depth.Should().Be(1);
        open.Marks.Should().Be(ElementMarks.OldEntry);
    }

    /// <summary>
    /// A name is forgotten once nothing of it is open, or an end tag finding it would go looking for what is
    /// no longer there.
    /// </summary>
    [Fact]
    public void What_Has_Been_Closed_Can_Not_Be_Closed_Again()
    {
        var open = new OpenElements();

        open.Open("div", ElementMarks.None);
        open.Open("span", ElementMarks.None);
        open.Open("i", ElementMarks.None);
        open.Close("span");

        open.Close("i");
        open.Close("span");

        open.Depth.Should().Be(1);

        open.Close("div");
        open.Close("div");

        open.Depth.Should().Be(0);
    }

    /// <summary>
    /// A depth nobody's page needs is no reason to hold on to more. Whatever is deepest when that is reached
    /// stays open, and the end tags of what was never held have nothing to close.
    /// </summary>
    [Fact]
    public void Only_So_Many_Are_Held_And_Whatever_Is_Deepest_Then_Stays_Open()
    {
        var open = new OpenElements();

        for (var level = 0; level < 4096; level++)
        {
            open.Open("div", ElementMarks.None).Should().BeTrue();
        }

        open.Open("div", ElementMarks.Inert).Should().BeFalse();
        open.Open("span", ElementMarks.Inert).Should().BeFalse();

        open.Depth.Should().Be(4096);
        open.Marks.Should().Be(ElementMarks.None);

        open.Close("span");

        open.Depth.Should().Be(4096);
    }
}
