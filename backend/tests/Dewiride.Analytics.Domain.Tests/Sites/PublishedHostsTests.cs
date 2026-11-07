using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Domain.Tests.Sites;

/// <summary>
/// Covers which hosts a website is published at, and which of those one of its sitemaps speaks for.
/// </summary>
/// <remarks>
/// The first half is what decides whether a report is stored against a website at all, so a change
/// to it is a change to what gets measured. The second half is what stops a sitemap written by a
/// stranger being read as a list of other people's pages.
/// </remarks>
public sealed class PublishedHostsTests
{
    [Theory]
    [InlineData("example.com")]
    [InlineData("blog.example.com")]
    [InlineData("www.blog.example.com")]
    public void A_Host_Is_Published_At_Its_Domain_And_Below_It(string host)
    {
        PublishedHosts.Covers("example.com", [], host).Should().BeTrue();
    }

    /// <summary>
    /// A website is its whole labels. Comparing the ends of two strings without the dot would hand
    /// <c>notexample.com</c> everything <c>example.com</c> is allowed.
    /// </summary>
    [Theory]
    [InlineData("notexample.com")]
    [InlineData("example.com.evil.test")]
    [InlineData("example.org")]
    [InlineData("com")]
    public void A_Look_Alike_Suffix_Is_Not_The_Website(string host)
    {
        PublishedHosts.Covers("example.com", [], host).Should().BeFalse();
    }

    /// <summary>
    /// Compared as given, because whoever asks has already settled the spelling and a second
    /// opinion here could only disagree with it.
    /// </summary>
    [Fact]
    public void Hosts_Are_Compared_Exactly_As_They_Are_Given()
    {
        PublishedHosts.Covers("example.com", [], "EXAMPLE.com").Should().BeFalse();
    }

    [Fact]
    public void Allowed_Origins_Replace_The_Domain()
    {
        string[] allowed = ["docs.example.net", "cdn.example.org"];

        PublishedHosts.Covers("example.com", allowed, "docs.example.net").Should().BeTrue();
        PublishedHosts.Covers("example.com", allowed, "eu.cdn.example.org").Should().BeTrue();
        PublishedHosts.Covers("example.com", allowed, "example.net").Should().BeFalse();
        PublishedHosts.Covers("example.com", allowed, "example.com").Should().BeFalse();
    }

    /// <summary>
    /// A website is routinely served at both names and writes either of them into its sitemap, so a
    /// reader insisting on the exact host would refuse whole sitemaps over nothing.
    /// </summary>
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("example.com", "www.example.com")]
    [InlineData("www.example.com", "example.com")]
    [InlineData("www.example.com", "www.example.com")]
    [InlineData("blog.example.com", "www.blog.example.com")]
    [InlineData("xn--bcher-kva.example", "www.xn--bcher-kva.example")]
    public void A_Sitemaps_Own_Host_And_Its_Twin_Are_One_Website(string sitemapHost, string otherHost)
    {
        PublishedHosts.SameWebsite(sitemapHost, otherHost).Should().BeTrue();
    }

    /// <summary>
    /// A host arrives from an address as the address spelled it, and a trailing dot or a capital
    /// letter does not make it another website.
    /// </summary>
    [Theory]
    [InlineData("example.com.", "example.com")]
    [InlineData("example.com", "www.example.com.")]
    [InlineData("Example.COM", "WWW.example.com")]
    public void How_A_Host_Is_Spelled_Does_Not_Make_It_Another_Website(string sitemapHost, string otherHost)
    {
        PublishedHosts.SameWebsite(sitemapHost, otherHost).Should().BeTrue();
    }

    /// <summary>
    /// The location rule of the sitemaps.org protocol: a sitemap speaks for its own host. A domain
    /// covering two siblings does not make either of them answerable for the other, and on a hosting
    /// platform those two are strangers.
    /// </summary>
    [Theory]
    [InlineData("blog.example.com", "shop.example.com")]
    [InlineData("www.example.com", "blog.example.com")]
    [InlineData("example.com", "blog.example.com")]
    [InlineData("blog.example.com", "example.com")]
    [InlineData("example.com", "www.www.example.com")]
    [InlineData("example.com", "www2.example.com")]
    [InlineData("example.com", "wwwexample.com")]
    [InlineData("example.com", "notexample.com")]
    [InlineData("example.com", "example.org")]
    [InlineData("alice.github.io", "bob.github.io")]
    public void Anything_Else_A_Sitemap_Leads_To_Is_Another_Website(string sitemapHost, string otherHost)
    {
        PublishedHosts.SameWebsite(sitemapHost, otherHost).Should().BeFalse();
    }

    /// <summary>
    /// Taking <c>www.</c> off <c>www.com</c> leaves a bare suffix, which is nobody's website and so
    /// nobody's twin.
    /// </summary>
    [Fact]
    public void A_Bare_Suffix_Is_Not_The_Twin_Of_Anything()
    {
        PublishedHosts.SameWebsite("www.com", "com").Should().BeFalse();
        PublishedHosts.SameWebsite("com", "www.com").Should().BeFalse();
    }

    /// <summary>
    /// How often a website is asked for anything is counted under this name, so two hosts go by one
    /// name exactly when they are one website, however they are spelled.
    /// </summary>
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("www.example.com", "example.com")]
    [InlineData("WWW.Example.COM", "example.com")]
    [InlineData("example.com.", "example.com")]
    [InlineData("www.example.com.", "example.com")]
    [InlineData("blog.example.com", "blog.example.com")]
    [InlineData("www.blog.example.com", "blog.example.com")]
    [InlineData("www.com", "www.com")]
    [InlineData("www2.example.com", "www2.example.com")]
    public void A_Host_And_Its_Twin_Go_By_One_Name(string host, string name)
    {
        PublishedHosts.WebsiteName(host).Should().Be(name);
    }
}
