namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Recognises the file of a sitemap that holds a website's posts, for the publishers that split one
/// out.
/// </summary>
/// <remarks>
/// <para>
/// A sitemap index from a blogging platform lists posts, pages, categories and authors in files of
/// their own, and reading only the posts spares a website a request for every other page it has. The
/// names are each publisher's own, read from their code and documentation.
/// </para>
/// <list type="bullet">
/// <item><description>
/// WordPress itself: <c>wp-sitemap-posts-post-1.xml</c>, or a query naming the same thing on a website
/// without readable addresses.
/// </description></item>
/// <item><description>
/// Yoast, Rank Math, All in One SEO and SEOPress: <c>post-sitemap.xml</c>, and <c>post-sitemap2.xml</c>
/// as it grows.
/// </description></item>
/// <item><description>
/// Ghost: <c>sitemap-posts.xml</c>, and <c>sitemap-posts-2.xml</c> as it grows.
/// </description></item>
/// </list>
/// <para>
/// A file not recognised here is not a claim of any kind. A sitemap with no file recognised is read
/// whole, and what each page says about itself decides.
/// </para>
/// </remarks>
internal static class PostsPart
{
    private const string CompressedSuffix = ".gz";
    private const string FileSuffix = ".xml";
    private const string PublishersOwn = "post-sitemap";
    private const string WordPressOwn = "wp-sitemap-posts-post-";
    private const string GhostOwn = "sitemap-posts";
    private const string GhostOwnNumbered = "sitemap-posts-";
    private const string WordPressQueryProvider = "sitemap=posts";
    private const string WordPressQuerySubtype = "sitemap-subtype=post";

    /// <summary>
    /// Whether an address names the file holding a website's posts.
    /// </summary>
    /// <param name="address">The address of one file of a sitemap.</param>
    /// <returns>Whether its name says so.</returns>
    public static bool Names(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return NamedInItsFile(address.AbsolutePath) || NamedInItsQuery(address.Query);
    }

    private static bool NamedInItsFile(ReadOnlySpan<char> path)
    {
        var file = path[(path.LastIndexOf('/') + 1)..];

        if (file.EndsWith(CompressedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            file = file[..^CompressedSuffix.Length];
        }

        if (!file.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = file[..^FileSuffix.Length];

        return IsNumbered(name, PublishersOwn, numberRequired: false)
            || IsNumbered(name, WordPressOwn, numberRequired: true)
            || IsNumbered(name, GhostOwn, numberRequired: false)
            || IsNumbered(name, GhostOwnNumbered, numberRequired: true);
    }

    /// <summary>
    /// Whether a name is what a publisher begins with and, at most, the number of the file.
    /// </summary>
    private static bool IsNumbered(ReadOnlySpan<char> name, string beginning, bool numberRequired)
    {
        if (!name.StartsWith(beginning, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var number = name[beginning.Length..];

        return number.IsEmpty ? !numberRequired : !number.ContainsAnyExceptInRange('0', '9');
    }

    private static bool NamedInItsQuery(string query)
    {
        var pairs = query.TrimStart('?').Split('&');

        return Array.Exists(pairs, pair => pair.Equals(WordPressQueryProvider, StringComparison.OrdinalIgnoreCase))
            && Array.Exists(pairs, pair => pair.Equals(WordPressQuerySubtype, StringComparison.OrdinalIgnoreCase));
    }
}
