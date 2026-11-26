using System.Text;

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
}
