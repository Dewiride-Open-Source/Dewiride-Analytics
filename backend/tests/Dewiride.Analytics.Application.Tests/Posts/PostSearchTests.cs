using Dewiride.Analytics.Application.Posts;

namespace Dewiride.Analytics.Application.Tests.Posts;

/// <summary>
/// Covers how what somebody searched their posts for is read: as words, and no longer than a search may be.
/// </summary>
public sealed class PostSearchTests
{
    /// <summary>Nothing, or nothing but space and control characters, is no search, and every post is listed.</summary>
    /// <param name="asked">What was asked.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u0000")]
    [InlineData("\u00A0\u2003\u2028\u0085")]
    public void Nothing_Asked_Is_No_Search(string? asked)
    {
        PostSearch.TryRead(asked, out var text).Should().BeTrue();
        text.Should().BeNull();
    }

    /// <summary>
    /// Every run of space or of control characters is one space, with none left at either end; everything
    /// else is kept exactly as it was written, whatever a pattern or a statement would make of it.
    /// </summary>
    /// <param name="asked">What was asked.</param>
    /// <param name="expected">What is searched for.</param>
    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("  hello   world  ", "hello world")]
    [InlineData("hello\tworld", "hello world")]
    [InlineData("hello\r\nworld", "hello world")]
    [InlineData("hello\u0000world", "hello world")]
    [InlineData("hello\u0007\u001B world", "hello world")]
    [InlineData("hello\u00A0world", "hello world")]
    [InlineData("\u2003hello\u2028world\u0085", "hello world")]
    [InlineData("100% sure_thing C:\\temp It's \"quoted\"", "100% sure_thing C:\\temp It's \"quoted\"")]
    [InlineData("Ünïcödé 名前", "Ünïcödé 名前")]
    public void A_Search_Is_Read_As_Words_With_Every_Run_Of_Space_Or_Control_Characters_As_One_Space(
        string asked,
        string expected)
    {
        PostSearch.TryRead(asked, out var text).Should().BeTrue();
        text.Should().Be(expected);
    }

    /// <summary>
    /// A search is measured in the units text is written in, as the box it is typed into measures it, so a
    /// character written in two of them counts as two.
    /// </summary>
    /// <param name="character">What the search is made of.</param>
    [Theory]
    [InlineData("a")]
    [InlineData("\U0001F600")]
    public void A_Search_As_Long_As_Allowed_Is_Read_And_One_Character_Longer_Is_Refused(string character)
    {
        var longest = string.Concat(Enumerable.Repeat(character, PostSearch.MostCharacters / character.Length));

        PostSearch.TryRead(longest, out var allowed).Should().BeTrue();
        allowed.Should().Be(longest);
        PostSearch.TryRead(longest + character, out var refused).Should().BeFalse();
        refused.Should().BeNull();
    }

    /// <summary>
    /// Space that folds away counts for nothing, and what is left once it has is what is measured.
    /// </summary>
    [Fact]
    public void A_Search_Is_Measured_Once_Its_Space_Is_Folded()
    {
        var folded = new string('a', 50) + " " + new string('b', 49);
        var spread = "  " + new string('a', 50) + " \t\r\n " + new string('b', 49) + "  ";
        var tooLong = new string('a', 50) + "\t" + new string('b', 50);

        PostSearch.TryRead(spread, out var text).Should().BeTrue();
        text.Should().Be(folded);
        PostSearch.TryRead(tooLong, out _).Should().BeFalse();
    }

    /// <summary>
    /// Half of a character written in two units is not text, and is read as the character that stands in
    /// for what cannot be read, so whatever is compared is text.
    /// </summary>
    [Fact]
    public void Text_That_Is_Not_Well_Formed_Is_Read_With_The_Replacement_Character()
    {
        PostSearch.TryRead("caf\uD800e", out var text).Should().BeTrue();
        text.Should().Be("caf\uFFFDe");
    }
}
