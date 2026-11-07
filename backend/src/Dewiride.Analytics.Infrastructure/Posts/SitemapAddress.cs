using System.Diagnostics.CodeAnalysis;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Reads and writes the addresses a sitemap names.
/// </summary>
internal static class SitemapAddress
{
    /// <summary>
    /// Reads an address a sitemap names, if it is one the reading may go to.
    /// </summary>
    /// <param name="location">What the sitemap says, which may be anything.</param>
    /// <param name="bounds">Where the reading may go.</param>
    /// <param name="address">The address.</param>
    /// <returns>Whether it is.</returns>
    public static bool TryRead(string location, WebsiteBounds bounds, [NotNullWhen(true)] out Uri? address) =>
        Uri.TryCreate(location, UriKind.Absolute, out address) && bounds.Admits(address);

    /// <summary>
    /// Writes an address in the one spelling it is kept in: the host in lower-case ASCII, and neither a
    /// port nor a fragment. <paramref name="address"/> has to have passed the bounds, which is what
    /// proves its host can be asked for.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="pathAndQuery">As much of what comes after the host as is kept.</param>
    /// <returns>The address as it is kept.</returns>
    public static string Spell(Uri address, string pathAndQuery)
    {
        ArgumentNullException.ThrowIfNull(address);

        return $"{address.Scheme}://{address.IdnHost.TrimEnd('.')}{pathAndQuery}";
    }
}
