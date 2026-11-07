using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Decides which of the files a sitemap's index names are read.
/// </summary>
/// <remarks>
/// <para>
/// A publisher that splits its sitemap by kind has a file for its posts, and reading only that spares its
/// website a request for every other page it has. An index with no such file is read whole, and what
/// each page says about itself decides.
/// </para>
/// <para>
/// A file's own limit is fifty thousand entries of up to two thousand characters each, which is a lot to
/// keep hold of for a list that ends up being a few hundred, so neither kind is kept past what could be
/// read. Whichever kind turns out to be the one read, having run past that is what says it was cut short.
/// </para>
/// </remarks>
/// <param name="bounds">Where the reading may go.</param>
/// <param name="mostParts">The most files read.</param>
internal sealed class IndexEntries(WebsiteBounds bounds, int mostParts)
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<string> _posts = [];
    private readonly List<string> _others = [];
    private bool _postsCutShort;
    private bool _othersCutShort;

    /// <summary>Whether any entry led off the website.</summary>
    public bool WentOffSite { get; private set; }

    /// <summary>Whether a file holding the website's posts was named.</summary>
    public bool FoundPosts => _posts.Count > 0;

    /// <summary>The files to read.</summary>
    public IReadOnlyList<string> Chosen => FoundPosts ? _posts : _others;

    /// <summary>Whether more were named than are read.</summary>
    public bool CutShort => FoundPosts ? _postsCutShort : _othersCutShort;

    /// <summary>
    /// Reads one entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public void Read(SitemapEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!SitemapAddress.TryRead(entry.Location, bounds, out var address))
        {
            WentOffSite = true;

            return;
        }

        var spelled = SitemapAddress.Spell(address, address.PathAndQuery);

        if (spelled.Length > Site.MaxSitemapAddressLength)
        {
            return;
        }

        if (PostsPart.Names(address))
        {
            _postsCutShort |= !TryKeep(_posts, spelled);
        }
        else
        {
            _othersCutShort |= !TryKeep(_others, spelled);
        }
    }

    private bool TryKeep(List<string> kept, string address)
    {
        if (_seen.Contains(address))
        {
            return true;
        }

        if (kept.Count == mostParts)
        {
            return false;
        }

        _seen.Add(address);
        kept.Add(address);

        return true;
    }
}
