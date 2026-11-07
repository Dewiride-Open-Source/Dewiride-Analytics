using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Decides which of the pages a sitemap lists are kept.
/// </summary>
/// <remarks>
/// <para>
/// Every location was written by whoever runs the website, and may be anything. An address has to pass
/// the same bounds as anything fetched before any more is asked of it, because that is what proves it
/// has a host that can be read.
/// </para>
/// <para>
/// The collector knows a page by its path and nothing else, so entries told apart only by a query string
/// would all be one page to it, and that page's views would be credited to every one of them. They are
/// skipped and counted, which is what lets a reading say why it found nothing. A website's front page is
/// never a post. An address too long to keep is skipped like any other that could not be kept.
/// </para>
/// </remarks>
/// <param name="bounds">Where the reading may go.</param>
internal sealed class PageEntries(WebsiteBounds bounds)
{
    private const string FrontPage = "/";

    /// <summary>How many entries led off the website.</summary>
    public int OffSite { get; private set; }

    /// <summary>How many entries were told apart only by a query string.</summary>
    public int WithQueries { get; private set; }

    /// <summary>
    /// Reads one entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The page it lists, or nothing where it is not kept.</returns>
    public PostListing? Read(SitemapEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!SitemapAddress.TryRead(entry.Location, bounds, out var address))
        {
            OffSite++;

            return null;
        }

        if (address.Query.Length > 1)
        {
            WithQueries++;

            return null;
        }

        var path = PagePath.Of(address);
        var link = SitemapAddress.Spell(address, path);

        return path == FrontPage || link.Length > Site.MaxSitemapAddressLength
            ? null
            : new PostListing(path, link, entry.LastModified);
    }
}
