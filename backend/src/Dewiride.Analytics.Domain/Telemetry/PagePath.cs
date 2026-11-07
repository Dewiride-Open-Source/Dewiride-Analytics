namespace Dewiride.Analytics.Domain.Telemetry;

/// <summary>
/// What a page is known by across everything measured about it.
/// </summary>
/// <remarks>
/// One definition, because everything that says anything about a page is joined on it. A path the
/// collector stored one way and a post read from a sitemap kept another way would be two pages, and
/// the post would read as never having been opened. Neither the host nor what follows the path is
/// part of it: a page is the same page at both of a website's names and whatever somebody appended
/// to the link they followed. A website that redirects one path to another has a page answering at
/// the second, which is the one a visit is recorded under, so a post read through such a redirect
/// keeps both.
/// </remarks>
public static class PagePath
{
    /// <summary>
    /// The path of a page's address, escaped as an address escapes it.
    /// </summary>
    /// <param name="address">The page's whole address.</param>
    /// <returns>The path, which is <c>/</c> at the least.</returns>
    public static string Of(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.AbsolutePath;
    }
}
