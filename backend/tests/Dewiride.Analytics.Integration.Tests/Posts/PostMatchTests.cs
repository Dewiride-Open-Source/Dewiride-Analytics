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
}
