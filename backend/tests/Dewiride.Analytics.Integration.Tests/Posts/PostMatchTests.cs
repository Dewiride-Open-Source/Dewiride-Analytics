using Dewiride.Analytics.Infrastructure.Posts;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers the pattern a search is compared with: anything holding the text, with every character of it
/// standing for itself.
/// </summary>
public sealed class PostMatchTests
{
    /// <summary>
    /// The percent sign, the underscore and the backslash mean something in a pattern, so each is escaped,
    /// by the one backslash the comparison is told escapes them — a text ending in a backslash among them.
    /// </summary>
    /// <param name="text">The text searched for.</param>
    /// <param name="pattern">The pattern it is compared as.</param>
    [Theory]
    [InlineData("100%", @"%100\%%")]
    [InlineData("snake_case", @"%snake\_case%")]
    [InlineData(@"C:\temp", @"%C:\\temp%")]
    [InlineData(@"\", @"%\\%")]
    [InlineData(@"%_\", @"%\%\_\\%")]
    public void Wildcards_And_The_Escape_Character_Stand_For_Themselves(string text, string pattern)
    {
        PostMatch.Escape.Should().Be(@"\");
        PostMatch.Holding(text).Should().Be(pattern);
    }

    /// <summary>
    /// Nothing else in a pattern means anything but itself — not a quote, nor a bracket or a caret, which
    /// other databases give a meaning — so everything else is left exactly as it was written.
    /// </summary>
    /// <param name="text">The text searched for.</param>
    [Theory]
    [InlineData("hello")]
    [InlineData("It's \"quoted\"")]
    [InlineData("[draft] ^1 $2")]
    [InlineData("café 名前")]
    public void Every_Other_Character_Is_Left_As_Written(string text)
    {
        PostMatch.Holding(text).Should().Be($"%{text}%");
    }

    /// <summary>
    /// A whole web address is compared by the path it names, spelled as a post's path is: its scheme and the
    /// website's name before it and whatever follows it let go, and letters an address escapes escaped.
    /// </summary>
    /// <param name="text">The address searched for.</param>
    /// <param name="path">The path it is compared as.</param>
    [Theory]
    [InlineData("https://example.com/posts/older/", "/posts/older/")]
    [InlineData("HTTP://EXAMPLE.COM/posts/older/?utm_source=newsletter#top", "/posts/older/")]
    [InlineData("https://example.com/café/", "/caf%C3%A9/")]
    [InlineData("https://example.com", "/")]
    public void A_Whole_Web_Address_Is_Compared_By_The_Path_It_Names(string text, string path)
    {
        PostMatch.PathIn(text).Should().Be(path);
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
        PostMatch.PathIn(text).Should().Be(text);
    }
}
