using System.Collections.Immutable;

namespace Dewiride.Analytics.Classification.Identity;

/// <summary>
/// The words a program uses to describe itself as something that fetches pages.
/// </summary>
/// <remarks>
/// <para>
/// Held to the same rule as everything else that reads a user agent: no regular expression. The
/// string is written by the visitor and arbitrarily long, and a pattern is something one carefully
/// written line of text can make run for a very long time. Each word is found by an ordinary
/// substring search and settled by looking at one character.
/// </para>
/// <para>
/// A word counts only where it ends: it must be followed by the end of the text or by something
/// other than a letter or a digit. It may begin anywhere, because that is how operators write these
/// names — <c>PetalBot</c>, <c>Bytespider</c>, <c>DataForSeoBot</c> — and demanding a boundary in
/// front would miss nearly every crawler on the web. Demanding one behind is what keeps
/// <c>robots.txt</c>, <c>Botswana</c> and <c>Abbott</c> out.
/// </para>
/// <para>
/// Matching a word is not identification and names nobody. It establishes only that the visitor
/// called itself a crawler, which is why the signal built from it carries nothing the visitor wrote.
/// </para>
/// </remarks>
public static class CrawlerWords
{
    /// <summary>Longest user agent examined, on the same reasoning as the client profiler.</summary>
    private const int MostCharactersExamined = 1024;

    /// <summary>The words, as a program writes them about itself.</summary>
    public static readonly ImmutableArray<string> Known = ["crawler", "spider", "scraper", "fetcher", "bot"];

    /// <summary>
    /// Names that end in one of the words without describing a crawler.
    /// </summary>
    /// <remarks>
    /// A phone maker whose model names sit inside an ordinary browser's user agent. Each is matched
    /// as the whole of the name ending where the word was found, so nothing else containing the
    /// letters is excused.
    /// </remarks>
    private static readonly ImmutableArray<string> NotCrawlers = ["cubot"];

    /// <summary>
    /// Whether a user agent describes itself as something that fetches pages.
    /// </summary>
    /// <param name="userAgent">The string the visitor sent. Attacker-controlled.</param>
    /// <returns><see langword="true"/> when one of the words appears and ends on a boundary.</returns>
    public static bool AppearIn(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return false;
        }

        var examined = userAgent.Length <= MostCharactersExamined
            ? userAgent
            : userAgent[..MostCharactersExamined];

        return Known.Any(word => EndsAWord(examined, word));
    }

    private static bool EndsAWord(string text, string word)
    {
        var from = 0;

        while (from <= text.Length - word.Length)
        {
            var at = text.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);

            if (at < 0)
            {
                return false;
            }

            if (IsBoundary(text, at + word.Length) && !IsExcused(text, at, word))
            {
                return true;
            }

            from = at + 1;
        }

        return false;
    }

    /// <summary>Whether the word ends here: at the end of the text, or before something that is not part of a name.</summary>
    private static bool IsBoundary(string text, int after) =>
        after == text.Length || !char.IsAsciiLetterOrDigit(text[after]);

    /// <summary>Whether the word found is the tail of a name that is not a crawler's.</summary>
    private static bool IsExcused(string text, int at, string word) =>
        NotCrawlers.Any(name => IsTailOf(text, at, word, name));

    private static bool IsTailOf(string text, int at, string word, string name)
    {
        var start = at - (name.Length - word.Length);

        return name.EndsWith(word, StringComparison.Ordinal)
            && start >= 0
            && text.AsSpan(start, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase);
    }
}
