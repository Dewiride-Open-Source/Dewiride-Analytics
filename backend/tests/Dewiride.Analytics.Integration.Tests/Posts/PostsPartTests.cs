using Dewiride.Analytics.Infrastructure.Posts;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers recognising the file of a sitemap that holds a website's posts.
/// </summary>
public sealed class PostsPartTests
{
    [Theory]
    // WordPress itself, on a website with readable addresses and on one without.
    [InlineData("https://example.com/wp-sitemap-posts-post-1.xml")]
    [InlineData("https://example.com/wp-sitemap-posts-post-12.xml")]
    [InlineData("https://example.com/?sitemap=posts&sitemap-subtype=post&paged=1")]
    [InlineData("https://example.com/?sitemap-subtype=post&paged=2&sitemap=posts")]
    // Yoast, Rank Math, All in One SEO and SEOPress.
    [InlineData("https://example.com/post-sitemap.xml")]
    [InlineData("https://example.com/post-sitemap1.xml")]
    [InlineData("https://example.com/post-sitemap2.xml")]
    [InlineData("https://example.com/sitemaps/post-sitemap14.xml")]
    // Ghost.
    [InlineData("https://example.com/sitemap-posts.xml")]
    [InlineData("https://example.com/sitemap-posts-2.xml")]
    // Compressed, and in somebody else's capitals.
    [InlineData("https://example.com/post-sitemap.xml.gz")]
    [InlineData("https://example.com/sitemap-posts-2.xml.gz")]
    [InlineData("https://example.com/Post-Sitemap.XML")]
    [InlineData("https://example.com/WP-Sitemap-Posts-Post-1.xml")]
    public void The_Posts_Part_Of_Each_Publisher_Is_Recognised(string address)
    {
        PostsPart.Names(new Uri(address)).Should().BeTrue();
    }

    [Theory]
    // Other things WordPress lists.
    [InlineData("https://example.com/wp-sitemap.xml")]
    [InlineData("https://example.com/wp-sitemap-posts-page-1.xml")]
    [InlineData("https://example.com/wp-sitemap-posts-product-1.xml")]
    [InlineData("https://example.com/wp-sitemap-taxonomies-category-1.xml")]
    [InlineData("https://example.com/wp-sitemap-users-1.xml")]
    [InlineData("https://example.com/wp-sitemap-posts-post.xml")]
    [InlineData("https://example.com/?sitemap=posts&sitemap-subtype=page&paged=1")]
    [InlineData("https://example.com/?sitemap=taxonomies&sitemap-subtype=post")]
    [InlineData("https://example.com/?sitemap=index")]
    // Other things the plugins list.
    [InlineData("https://example.com/sitemap_index.xml")]
    [InlineData("https://example.com/page-sitemap.xml")]
    [InlineData("https://example.com/category-sitemap.xml")]
    [InlineData("https://example.com/product-sitemap1.xml")]
    [InlineData("https://example.com/author-sitemap.xml")]
    // Other things Ghost lists.
    [InlineData("https://example.com/sitemap.xml")]
    [InlineData("https://example.com/sitemap-pages.xml")]
    [InlineData("https://example.com/sitemap-tags.xml")]
    [InlineData("https://example.com/sitemap-authors.xml")]
    // What only looks like the right name.
    [InlineData("https://example.com/post-sitemap-extra.xml")]
    [InlineData("https://example.com/notpost-sitemap.xml")]
    [InlineData("https://example.com/post-sitemap")]
    [InlineData("https://example.com/post-sitemap.html")]
    [InlineData("https://example.com/post-sitemap.xml/other.xml")]
    [InlineData("https://example.com/sitemap-posts-a.xml")]
    public void Pages_Categories_And_Other_Post_Types_Are_Not_Posts(string address)
    {
        PostsPart.Names(new Uri(address)).Should().BeFalse();
    }
}
