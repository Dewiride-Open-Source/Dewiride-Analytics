using System.Collections.Immutable;
using System.Text;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Posts;

/// <summary>
/// Reads what somebody searched a website's posts or pages for.
/// </summary>
/// <remarks>
/// <para>
/// A search is written by whoever asks, so it is only ever a value: compared with what each post is called
/// and with its address, or with the path of each of a website's pages, and never logged, repeated, or made
/// part of a statement.
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
    /// The text a path is compared with: where the text is a whole web address, the path it names, spelled
    /// as the collector stores a path; otherwise the text as written.
    /// </summary>
    /// <remarks>
    /// Every address a website lists begins with the website's own name, so comparing whole addresses would
    /// find every post for a search for that name; a path is what tells one page from another. An address
    /// pasted whole still finds its page: what precedes its path and what follows it are let go.
    /// </remarks>
    /// <param name="text">The text, which may hold anything.</param>
    /// <returns>The text to compare a path with.</returns>
    public static string PathIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Uri.TryCreate(text, UriKind.Absolute, out var address)
            && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
                ? PagePath.Of(address)
                : text;
    }

    /// <summary>
    /// The words a search holds, each as a path is compared with it, so an address pasted among other
    /// words is compared by the path it names.
    /// </summary>
    /// <param name="text">The search as <see cref="TryRead"/> read it, or nothing where nothing was searched for.</param>
    /// <returns>Each word once, in the order it was first written; none where nothing was searched for.</returns>
    public static ImmutableArray<string> WordsOf(string? text) =>
        text is null
            ? []
            : [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(PathIn).Distinct(StringComparer.Ordinal)];

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
