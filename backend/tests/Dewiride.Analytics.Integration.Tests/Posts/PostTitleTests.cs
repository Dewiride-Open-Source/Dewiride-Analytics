using Dewiride.Analytics.Infrastructure.Posts;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers making what a page says it is called fit to keep and to show.
/// </summary>
public sealed class PostTitleTests
{
    [Theory]
    // Controls.
    [InlineData("A\u0000B", "AB")]
    [InlineData("A\u0007B", "AB")]
    [InlineData("A\u007FB", "AB")]
    [InlineData("A\u0090B", "AB")]
    // Direction overrides and isolates, which make a title read as something it does not hold.
    [InlineData("safe\u202Egnp.exe", "safegnp.exe")]
    [InlineData("A\u202AB\u202CC", "ABC")]
    [InlineData("A\u2066B\u2069C", "ABC")]
    [InlineData("A\u200EB\u200FC", "ABC")]
    [InlineData("A\u061CB", "AB")]
    // Other invisible format characters.
    [InlineData("A\u200BB", "AB")]
    [InlineData("\uFEFFA", "A")]
    [InlineData("A\u2060B", "AB")]
    [InlineData("A\U000E0041B", "AB")]
    // Code points nothing will ever be, which normalising text refuses outright.
    [InlineData("A\uFFFEB", "AB")]
    [InlineData("A\uFFFFB", "AB")]
    [InlineData("A\uFDD0B", "AB")]
    [InlineData("A\U0001FFFEB", "AB")]
    public void Control_And_Direction_Characters_Are_Removed(string text, string expected)
    {
        PostTitle.Clean(text).Should().Be(expected);
    }

    /// <summary>
    /// Several scripts spell words with the two joiners, and some emoji are built with one. They are
    /// format characters too, and they stay.
    /// </summary>
    /// <param name="text">The title.</param>
    [Theory]
    [InlineData("\u0915\u094D\u200D\u0937")]
    [InlineData("\u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645")]
    [InlineData("\U0001F469\u200D\U0001F4BB at work")]
    public void The_Joiners_Words_Are_Spelled_With_Are_Kept(string text)
    {
        PostTitle.Clean(text).Should().Be(text);
    }

    [Theory]
    [InlineData("  A  ", "A")]
    [InlineData("A   B", "A B")]
    [InlineData("A\tB", "A B")]
    [InlineData("A\r\nB", "A B")]
    [InlineData("A\u00A0B", "A B")]
    [InlineData("A\u2028B", "A B")]
    [InlineData("A\u2029B", "A B")]
    [InlineData("A \t\n\u2003 B", "A B")]
    [InlineData("\n  A\n  ", "A")]
    public void Runs_Of_Space_Become_One(string text, string expected)
    {
        PostTitle.Clean(text).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0000\u0007")]
    [InlineData("\u200B\u202E")]
    [InlineData("\uFFFF")]
    public void A_Title_With_Nothing_Left_In_It_Is_No_Title(string? text)
    {
        PostTitle.Clean(text).Should().BeNull();
    }

    /// <summary>
    /// A title with half a character in it is still a title, and a stored string has to be well formed
    /// for anything to do anything with it later.
    /// </summary>
    [Fact]
    public void Half_A_Character_Comes_Out_As_The_Replacement_Character()
    {
        PostTitle.Clean("A\uD83DB").Should().Be("A\uFFFDB");
        PostTitle.Clean("A\uDC00B").Should().Be("A\uFFFDB");
    }

    /// <summary>
    /// One spelling for one word, or the same title read twice can look changed, and search and sort
    /// disagree about it.
    /// </summary>
    [Fact]
    public void An_Accent_Written_Separately_Is_Written_As_One_Character()
    {
        PostTitle.Clean("Cafe\u0301").Should().Be("Caf\u00E9");
    }

    [Fact]
    public void A_Title_Is_Cut_On_A_Character_Boundary()
    {
        // Each of these is one character to a reader and several to a computer.
        var family = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";
        var title = PostTitle.Clean(string.Concat(Enumerable.Repeat(family, 100)));

        title.Should().NotBeNull();
        title.Length.Should().BeLessThanOrEqualTo(SitePost.MaxTitleLength);
        (title.Length % family.Length).Should().Be(0);
        title.Should().EndWith(family);
    }

    [Fact]
    public void A_Title_Just_Long_Enough_Is_Left_Alone()
    {
        var title = new string('a', SitePost.MaxTitleLength);

        PostTitle.Clean(title).Should().Be(title);
    }

    [Fact]
    public void A_Title_Too_Long_Is_Cut_To_What_Is_Kept_With_Nothing_Added()
    {
        var title = PostTitle.Clean(new string('a', SitePost.MaxTitleLength + 50));

        title.Should().Be(new string('a', SitePost.MaxTitleLength));
    }

    [Fact]
    public void A_Cut_Leaves_No_Space_On_The_End()
    {
        var text = new string('a', SitePost.MaxTitleLength - 1) + " " + new string('b', 50);

        PostTitle.Clean(text).Should().Be(new string('a', SitePost.MaxTitleLength - 1));
    }

    /// <summary>
    /// One base character with a thousand marks on it is one character that does not fit.
    /// </summary>
    [Fact]
    public void One_Character_Too_Long_To_Keep_Leaves_No_Title()
    {
        PostTitle.Clean("a" + new string('\u0301', 1000)).Should().BeNull();
    }

    /// <summary>
    /// Only so much text is put in its normal form, and that is enough to fill a title from the most that
    /// Unicode composes into one character: a Greek vowel with a breathing, an accent and a subscript.
    /// </summary>
    [Fact]
    public void Text_Is_Cut_Before_It_Is_Normalised_Only_Past_What_A_Title_Can_Hold()
    {
        var composed = "\u1F82";
        var decomposed = "\u03B1\u0313\u0300\u0345";
        var title = PostTitle.Clean(string.Concat(Enumerable.Repeat(decomposed, SitePost.MaxTitleLength + 50)));

        title.Should().Be(string.Concat(Enumerable.Repeat(composed, SitePost.MaxTitleLength)));
    }

    /// <summary>
    /// Space and what is not text are not what the limit is counted in, so however much of them a page
    /// puts first, the title after them is read.
    /// </summary>
    [Fact]
    public void What_Comes_Out_Of_A_Title_Does_Not_Count_Towards_What_Is_Kept()
    {
        var title = PostTitle.Clean(new string(' ', 5000) + new string('\u200B', 5000) + "Found");

        title.Should().Be("Found");
    }

    /// <summary>
    /// The title goes on being cut where a reader would count a character, even where it was cut once
    /// before being normalised.
    /// </summary>
    [Fact]
    public void A_Title_Cut_Short_Leaves_No_Half_Of_A_Character()
    {
        var emoji = "\uD83D\uDE00";
        var title = PostTitle.Clean(string.Concat(Enumerable.Repeat(emoji, 2 * SitePost.MaxTitleLength)));

        title.Should().Be(string.Concat(Enumerable.Repeat(emoji, SitePost.MaxTitleLength / 2)));
    }

    /// <summary>
    /// Nothing downstream reads a title as anything but text, so a title about markup, and one trying
    /// to be markup, are both left as the words they are.
    /// </summary>
    /// <param name="text">The title.</param>
    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Why <b>bold</b> is not strong")]
    [InlineData("&lt;b&gt; is an escape")]
    public void Markup_In_A_Title_Stays_Text(string text)
    {
        PostTitle.Clean(text).Should().Be(text);
    }
}
