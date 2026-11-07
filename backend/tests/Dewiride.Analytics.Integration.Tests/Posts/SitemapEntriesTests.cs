using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Posts;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers which of the pages and files a sitemap names are kept, each of which was written by somebody
/// else and may be anything.
/// </summary>
public sealed class SitemapEntriesTests
{
    private const string Sitemap = "https://blog.example.com/sitemap.xml";

    private static readonly Site Website =
        new(Guid.NewGuid(), Guid.NewGuid(), "example.com", "Etc/UTC", DateTimeOffset.MinValue);

    private static WebsiteBounds Bounds => new(Website, new Uri(Sitemap));

    [Fact]
    public void A_Page_On_The_Website_Is_Kept_By_Its_Path()
    {
        var modified = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        new PageEntries(Bounds).Read(new SitemapEntry("https://blog.example.com/posts/hello/", modified))
            .Should().Be(new PostListing("/posts/hello/", "https://blog.example.com/posts/hello/", modified));
    }

    /// <summary>A sitemap and its website at the other name are one website, and either spelling of a host is one host.</summary>
    /// <param name="location">What the sitemap says.</param>
    /// <param name="address">What is kept.</param>
    [Theory]
    [InlineData("https://www.blog.example.com/a", "https://www.blog.example.com/a")]
    [InlineData("HTTPS://BLOG.EXAMPLE.COM/a", "https://blog.example.com/a")]
    [InlineData("https://blog.example.com./a", "https://blog.example.com/a")]
    [InlineData("http://blog.example.com/a", "http://blog.example.com/a")]
    [InlineData("https://blog.example.com/a#part-two", "https://blog.example.com/a")]
    [InlineData("https://blog.example.com:443/a", "https://blog.example.com/a")]
    [InlineData("  https://blog.example.com/a  ", "https://blog.example.com/a")]
    public void An_Address_Is_Kept_In_One_Spelling(string location, string address)
    {
        new PageEntries(Bounds).Read(new SitemapEntry(location, null))?.Address.Should().Be(address);
    }

    /// <summary>Everything off the sitemap's own host is counted and let go, including what the website is registered as covering.</summary>
    /// <param name="location">What the sitemap says.</param>
    [Theory]
    [InlineData("https://shop.example.com/a")]
    [InlineData("https://example.com/a")]
    [InlineData("https://www.example.com/a")]
    [InlineData("https://child.blog.example.com/a")]
    [InlineData("https://example.org/a")]
    [InlineData("https://blog.example.com.evil.test/a")]
    [InlineData("https://user:secret@blog.example.com/a")]
    [InlineData("https://blog.example.com:8443/a")]
    [InlineData("https://127.0.0.1/a")]
    [InlineData("https://[::1]/a")]
    [InlineData("ftp://blog.example.com/a")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/a")]
    [InlineData("a")]
    [InlineData("not an address at all")]
    [InlineData("https://")]
    public void What_Leads_Off_The_Website_Is_Counted_And_Not_Kept(string location)
    {
        var entries = new PageEntries(Bounds);

        entries.Read(new SitemapEntry(location, null)).Should().BeNull();

        entries.OffSite.Should().Be(1);
        entries.WithQueries.Should().Be(0);
    }

    [Fact]
    public void Addresses_Told_Apart_By_A_Query_Are_Counted_And_Not_Kept()
    {
        var entries = new PageEntries(Bounds);

        entries.Read(new SitemapEntry("https://blog.example.com/?p=123", null)).Should().BeNull();
        entries.Read(new SitemapEntry("https://blog.example.com/page?lang=fr", null)).Should().BeNull();

        entries.WithQueries.Should().Be(2);
        entries.OffSite.Should().Be(0);
    }

    /// <summary>A question mark with nothing after it tells nothing apart.</summary>
    [Fact]
    public void An_Empty_Query_Is_No_Query()
    {
        new PageEntries(Bounds).Read(new SitemapEntry("https://blog.example.com/a?", null))?.Path
            .Should().Be("/a");
    }

    /// <summary>A website's front page is not a post. An address too long to keep is simply not kept.</summary>
    /// <param name="location">What the sitemap says.</param>
    [Theory]
    [InlineData("https://blog.example.com/")]
    [InlineData("https://blog.example.com")]
    public void The_Front_Page_Is_Not_Kept_And_Is_Not_Counted_As_Anything(string location)
    {
        var entries = new PageEntries(Bounds);

        entries.Read(new SitemapEntry(location, null)).Should().BeNull();

        entries.OffSite.Should().Be(0);
        entries.WithQueries.Should().Be(0);
    }

    /// <summary>Escaping can make an address longer than it was written, and the column it goes in has a width.</summary>
    [Fact]
    public void An_Address_That_Would_Not_Fit_Is_Not_Kept()
    {
        var start = "https://blog.example.com/";
        var fits = start + new string('a', Site.MaxSitemapAddressLength - start.Length);
        var doesNot = start + new string('é', 1000);

        new PageEntries(Bounds).Read(new SitemapEntry(fits, null)).Should().NotBeNull();
        new PageEntries(Bounds).Read(new SitemapEntry(doesNot, null)).Should().BeNull();
    }

    /// <summary>
    /// A host the address parser accepts and then cannot turn into ASCII, a joiner in a label, has to come
    /// to not being on the website, and not to an exception partway through somebody's sitemap.
    /// </summary>
    [Fact]
    public void A_Host_That_Has_No_Ascii_Form_Leads_Off_The_Website()
    {
        var entries = new PageEntries(Bounds);

        entries.Read(new SitemapEntry("https://exa\u200Dmple.com/a", null)).Should().BeNull();

        entries.OffSite.Should().Be(1);
    }

    [Fact]
    public void An_Index_With_A_File_Of_Posts_Is_Read_For_That_Alone()
    {
        var entries = new IndexEntries(Bounds, 10);

        entries.Read(new SitemapEntry("https://blog.example.com/page-sitemap.xml", null));
        entries.Read(new SitemapEntry("https://blog.example.com/post-sitemap.xml", null));
        entries.Read(new SitemapEntry("https://blog.example.com/post-sitemap2.xml", null));

        entries.FoundPosts.Should().BeTrue();
        entries.Chosen.Should().Equal(
            "https://blog.example.com/post-sitemap.xml",
            "https://blog.example.com/post-sitemap2.xml");
        entries.CutShort.Should().BeFalse();
    }

    [Fact]
    public void An_Index_With_No_File_Of_Posts_Is_Read_Whole()
    {
        var entries = new IndexEntries(Bounds, 10);

        entries.Read(new SitemapEntry("https://blog.example.com/one.xml", null));
        entries.Read(new SitemapEntry("https://blog.example.com/two.xml", null));

        entries.FoundPosts.Should().BeFalse();
        entries.Chosen.Should().HaveCount(2);
    }

    /// <summary>
    /// The file of a website with plain links is told apart by its query, so that is kept, and the same
    /// file named twice would be one statement changing one row twice.
    /// </summary>
    [Fact]
    public void A_Part_Keeps_Its_Query_And_Is_Named_Once()
    {
        const string Part = "https://blog.example.com/?sitemap=posts&sitemap-subtype=post&paged=1";

        var entries = new IndexEntries(Bounds, 10);

        entries.Read(new SitemapEntry(Part, null));
        entries.Read(new SitemapEntry(Part + "#again", null));

        entries.FoundPosts.Should().BeTrue();
        entries.Chosen.Should().Equal(Part);
    }

    [Fact]
    public void A_Part_Off_The_Website_Is_Noted_And_Not_Kept()
    {
        var entries = new IndexEntries(Bounds, 10);

        entries.Read(new SitemapEntry("https://shop.example.com/post-sitemap.xml", null));

        entries.WentOffSite.Should().BeTrue();
        entries.Chosen.Should().BeEmpty();
        entries.FoundPosts.Should().BeFalse();
    }

    /// <summary>
    /// Only the limit on the kind that ends up being read says it was cut short: a thousand other files
    /// beside a website's one file of posts leave nothing out of what is read.
    /// </summary>
    [Fact]
    public void Being_Cut_Short_Is_About_The_Kind_That_Is_Read()
    {
        var entries = new IndexEntries(Bounds, 2);

        foreach (var number in Enumerable.Range(1, 5))
        {
            entries.Read(new SitemapEntry($"https://blog.example.com/other-{number}.xml", null));
        }

        entries.Chosen.Should().HaveCount(2);
        entries.CutShort.Should().BeTrue();

        entries.Read(new SitemapEntry("https://blog.example.com/post-sitemap.xml", null));

        entries.Chosen.Should().ContainSingle();
        entries.CutShort.Should().BeFalse();
    }

    [Fact]
    public void More_Files_Of_Posts_Than_Are_Read_Are_Cut_Short()
    {
        var entries = new IndexEntries(Bounds, 2);

        foreach (var number in Enumerable.Range(1, 3))
        {
            entries.Read(new SitemapEntry($"https://blog.example.com/post-sitemap{number}.xml", null));
        }

        entries.Chosen.Should().HaveCount(2);
        entries.CutShort.Should().BeTrue();
    }
}
