using System.Text;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Makes a search into the patterns a post's title and path are compared with: anything holding the text,
/// with every character of it standing for itself.
/// </summary>
/// <remarks>
/// A pattern for <c>LIKE</c> and <c>ILIKE</c> gives the percent sign and the underscore a meaning of their
/// own, and so does the character that escapes them; nothing else in it means anything but itself. So each
/// of those three in the text is escaped, by a backslash, which the comparison is told is the escape
/// character — and a text ending in a backslash cannot leave the pattern ending in an escape.
/// </remarks>
internal static class PostMatch
{
    /// <summary>The character a pattern escapes a character with: one backslash.</summary>
    public const string Escape = "\\";

    /// <summary>
    /// The pattern matching every text that holds the one given.
    /// </summary>
    /// <param name="text">The text, which may hold anything.</param>
    /// <returns>The pattern, to be compared with <see cref="Escape"/> as its escape character.</returns>
    public static string Holding(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var pattern = new StringBuilder((2 * text.Length) + 2).Append('%');

        foreach (var character in text)
        {
            if (character is '\\' or '%' or '_')
            {
                pattern.Append('\\');
            }

            pattern.Append(character);
        }

        return pattern.Append('%').ToString();
    }

    /// <summary>
    /// The text a post's path is compared with: where the text is a whole web address, the path it names,
    /// spelled as a post's path is; otherwise the text as written.
    /// </summary>
    /// <remarks>
    /// Every address a website lists begins with the website's own name, so comparing whole addresses would
    /// find every post for a search for that name; a path is what tells one post from another. An address
    /// pasted whole still finds its post: what precedes its path and what follows it are let go.
    /// </remarks>
    /// <param name="text">The text, which may hold anything.</param>
    /// <returns>The text to compare a post's path with.</returns>
    public static string PathIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Uri.TryCreate(text, UriKind.Absolute, out var address)
            && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
                ? PagePath.Of(address)
                : text;
    }
}
