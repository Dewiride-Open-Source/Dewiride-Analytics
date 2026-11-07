using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Domain.Tests.Telemetry;

/// <summary>
/// Covers what a page is known by.
/// </summary>
/// <remarks>
/// Everything said about a page is joined on this one value, across a store that keeps reports and
/// one that keeps what a website's own pages say about themselves. Changing what it returns splits
/// every page's history in two, so what it returns is pinned.
/// </remarks>
public sealed class PagePathTests
{
    [Theory]
    [InlineData("https://example.com/", "/")]
    [InlineData("https://example.com", "/")]
    [InlineData("https://example.com/posts/hello", "/posts/hello")]
    [InlineData("https://example.com/posts/hello/", "/posts/hello/")]
    [InlineData("https://www.example.com/posts/hello", "/posts/hello")]
    [InlineData("http://example.com/posts/hello", "/posts/hello")]
    public void A_Page_Is_Known_By_Its_Path_Whichever_Name_It_Was_Reached_At(string address, string path)
    {
        PagePath.Of(new Uri(address)).Should().Be(path);
    }

    /// <summary>
    /// What follows a path is what somebody appended to the link they followed, and what is in a
    /// fragment never reaches a server at all. Neither makes it another page.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/posts/hello?utm_source=mail")]
    [InlineData("https://example.com/posts/hello#comments")]
    [InlineData("https://example.com/posts/hello?utm_source=mail#comments")]
    public void What_Follows_The_Path_Is_No_Part_Of_It(string address)
    {
        PagePath.Of(new Uri(address)).Should().Be("/posts/hello");
    }

    /// <summary>
    /// Kept escaped, as an address carries it, because a path is compared as text and this is the
    /// form a browser and a sitemap both send.
    /// </summary>
    [Fact]
    public void A_Path_Is_Kept_As_An_Address_Escapes_It()
    {
        PagePath.Of(new Uri("https://example.com/café/a b")).Should().Be("/caf%C3%A9/a%20b");
    }

    /// <summary>
    /// WordPress writes a slug in another alphabet into its sitemap with lower-case escapes, and a
    /// browser sends the same page with upper-case ones. They come out as one path, which is the only
    /// reason such a post is credited with its views at all.
    /// </summary>
    [Theory]
    [InlineData("/caf%c3%a9")]
    [InlineData("/caf%C3%A9")]
    [InlineData("/café")]
    public void Text_Escaped_In_Either_Case_Is_One_Path(string path)
    {
        PagePath.Of(new Uri($"https://example.com{path}")).Should().Be("/caf%C3%A9");
    }

    /// <summary>
    /// A known limit, pinned so that changing it is a decision. An escape of anything that is not
    /// text keeps the case it was written in, so a link spelling one differently from the sitemap is
    /// not credited to the post. Folding the case here would change what the collector stores and
    /// split the history of every page it touched at the release that did it.
    /// </summary>
    [Theory]
    [InlineData("/a%2fb")]
    [InlineData("/a%2Fb")]
    [InlineData("/a%e4b")]
    public void An_Escape_Of_Anything_Else_Keeps_The_Case_It_Was_Written_In(string path)
    {
        PagePath.Of(new Uri($"https://example.com{path}")).Should().Be(path);
    }
}
