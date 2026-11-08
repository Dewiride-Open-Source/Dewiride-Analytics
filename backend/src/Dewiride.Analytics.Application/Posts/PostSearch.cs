using System.Text;

namespace Dewiride.Analytics.Application.Posts;

/// <summary>
/// Reads what somebody searched a website's posts for.
/// </summary>
/// <remarks>
/// <para>
/// A search is written by whoever asks, so it is only ever a value: compared with what each post is called
/// and with its address, and never logged, repeated, or made part of a statement.
/// </para>
/// <para>
/// It is read as words. Every run of space or of control characters is one space, and none is left at
/// either end, so words pasted with a line break between them find what the same words typed would; that
/// also keeps out the one character a PostgreSQL text value cannot hold. Text that is not well formed comes
/// out as the replacement character, which is what reading it a rune at a time does, so what is compared is
/// always text.
/// </para>
/// </remarks>
public static class PostSearch
{
    /// <summary>The most characters a search may hold, once its space is folded.</summary>
    public const int MostCharacters = 100;

    /// <summary>
    /// Reads a search.
    /// </summary>
    /// <param name="asked">What the caller supplied, or nothing.</param>
    /// <param name="text">The words searched for, or nothing where there are none.</param>
    /// <returns>
    /// <see langword="true"/> when the search can be made as asked, which includes there being nothing to
    /// search for; <see langword="false"/> when it holds more than <see cref="MostCharacters"/> characters.
    /// </returns>
    public static bool TryRead(string? asked, out string? text)
    {
        var words = Folded(asked ?? string.Empty);

        text = words.Length is > 0 and <= MostCharacters ? words : null;

        return words.Length <= MostCharacters;
    }

    /// <summary>
    /// Brings every run of space or of control characters down to one space, with none left at either end.
    /// It stops once it holds more than a search may, which is enough to refuse it.
    /// </summary>
    private static string Folded(string asked)
    {
        var words = new StringBuilder(Math.Min(asked.Length, MostCharacters + 1));
        var spaceDue = false;
        Span<char> encoded = stackalloc char[2];

        foreach (var rune in asked.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.IsControl(rune))
            {
                spaceDue = words.Length > 0;
                continue;
            }

            if (spaceDue)
            {
                words.Append(' ');
                spaceDue = false;
            }

            words.Append(encoded[..rune.EncodeToUtf16(encoded)]);

            if (words.Length > MostCharacters)
            {
                break;
            }
        }

        return words.ToString();
    }
}
