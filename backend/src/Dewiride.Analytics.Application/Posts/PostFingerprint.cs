using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Dewiride.Analytics.Application.Posts;

/// <summary>
/// Names a list of paths, so a kept month of posts can say which posts it was counted for.
/// </summary>
/// <remarks>
/// A kept month is counted again when the list it was counted for is no longer the website's, and
/// keeping the list itself on every month would repeat fifty thousand paths a month to answer one
/// question. The name is the same however the list is ordered or repeated, and differs for any other
/// list, so it answers that question in thirty-two characters.
/// </remarks>
public static class PostFingerprint
{
    /// <summary>How many bytes of the hash are kept, which is far more than any website's lists could collide in.</summary>
    private const int Kept = 16;

    /// <summary>
    /// Names a list of paths.
    /// </summary>
    /// <param name="paths">The paths.</param>
    /// <returns>
    /// The first sixteen bytes of the SHA-256 of the distinct paths in ordinal order, joined by line
    /// feeds, as lower-case hexadecimal.
    /// </returns>
    public static string Of(ImmutableArray<string> paths)
    {
        var listed = string.Join('\n', paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(listed)).AsSpan(0, Kept));
    }
}
