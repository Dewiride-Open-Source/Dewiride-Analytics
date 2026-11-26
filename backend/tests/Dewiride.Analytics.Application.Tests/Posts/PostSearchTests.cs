using Dewiride.Analytics.Application.Posts;

namespace Dewiride.Analytics.Application.Tests.Posts;

/// <summary>
/// Covers how what somebody searched their posts or pages for is read: as words, no longer than a search
/// may be, and with a whole web address compared by the path it names.
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

    /// <summary>
    /// A whole web address is compared by the path it names, spelled as the collector stores a path: its
    /// scheme and the website's name before it and whatever follows it let go, and letters an address
    /// escapes escaped.
    /// </summary>
    /// <param name="text">The address searched for.</param>
    /// <param name="path">The path it is compared as.</param>
    [Theory]
    [InlineData("https://example.com/posts/older/", "/posts/older/")]
    [InlineData("HTTP://EXAMPLE.COM/posts/older/?utm_source=newsletter#top", "/posts/older/")]
    [InlineData("https://example.com/caf\u00E9/", "/caf%C3%A9/")]
    [InlineData("https://example.com", "/")]
    public void A_Whole_Web_Address_Is_Compared_By_The_Path_It_Names(string text, string path)
    {
        PostSearch.PathIn(text).Should().Be(path);
    }

    /// <summary>
    /// Anything that is not a whole web address — part of a path, an address without its scheme, another
    /// kind of address, words — is compared exactly as it was written.
    /// </summary>
    /// <param name="text">The text searched for.</param>
    [Theory]
    [InlineData("/posts/older/")]
    [InlineData("posts/older")]
    [InlineData("example.com/posts/older/")]
    [InlineData("ftp://example.com/posts/older/")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("file:///posts/older/")]
    [InlineData("https://")]
    [InlineData("Moved House")]
    public void Text_That_Is_No_Whole_Web_Address_Is_Compared_As_Written(string text)
    {
        PostSearch.PathIn(text).Should().Be(text);
    }

    /// <summary>
    /// A search of a website's pages is read as the words it holds, each once, in the order first written —
    /// a word repeated in other letters' case is another word, since case is set aside where the words are
    /// compared — and nothing searched for is no words at all.
    /// </summary>
    [Fact]
    public void A_Search_Is_Read_As_Every_Word_It_Holds()
    {
        PostSearch.TryRead("  guide \t 2024  guide Caf\u00E9\r\nGUIDE ", out var text).Should().BeTrue();

        PostSearch.WordsOf(text).Should().Equal("guide", "2024", "Caf\u00E9", "GUIDE");
        PostSearch.WordsOf(null).Should().BeEmpty();
    }

    /// <summary>
    /// An address pasted among other words is compared by the path it names, as an address pasted alone is,
    /// and the words around it are left as they were written.
    /// </summary>
    [Fact]
    public void A_Pasted_Address_Is_Searched_For_By_Its_Path()
    {
        PostSearch.TryRead("older https://example.com/posts/older/?utm_source=feed caf\u00E9", out var text)
            .Should().BeTrue();

        PostSearch.WordsOf(text).Should().Equal("older", "/posts/older/", "caf\u00E9");
    }
}
