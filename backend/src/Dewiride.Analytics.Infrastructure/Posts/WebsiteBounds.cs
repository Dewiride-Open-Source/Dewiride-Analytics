using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Which addresses one reading of a website's sitemap may go to.
/// </summary>
/// <remarks>
/// <para>
/// Two rules, and an address has to pass both. <see cref="Site.Publishes"/> is the one about the
/// website: a web address, on its default port, on a host the website is published at. The other is
/// the sitemaps.org location rule, which says a sitemap only speaks for its own host. What a website
/// is registered as is typed by whoever adds it, so it can be a whole hosting platform, and without
/// that second rule one sitemap on the platform would be a list of everybody's pages on it.
/// </para>
/// <para>
/// The order matters. An address has to be proved to have a usable host before the host is asked for.
/// </para>
/// </remarks>
internal sealed class WebsiteBounds
{
    private readonly Site _site;
    private readonly string _sitemapHost;

    /// <summary>The bounds of reading one sitemap of a website's.</summary>
    /// <param name="site">The website.</param>
    /// <param name="sitemap">The address of the sitemap, which <see cref="Site.Publishes"/> has accepted.</param>
    public WebsiteBounds(Site site, Uri sitemap)
    {
        ArgumentNullException.ThrowIfNull(sitemap);

        _site = site;
        _sitemapHost = sitemap.IdnHost;
    }

    /// <summary>
    /// Whether an address may be fetched in this reading, or stored as one of what it found.
    /// </summary>
    /// <param name="address">The address, which came from the website.</param>
    /// <returns>Whether it stays on the website.</returns>
    public bool Admits(Uri address) =>
        _site.Publishes(address) && PublishedHosts.SameWebsite(_sitemapHost, address.IdnHost);
}
