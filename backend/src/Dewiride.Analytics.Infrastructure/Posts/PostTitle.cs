using System.Globalization;
using System.Text;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Makes what a page says it is called fit to keep and to show on one line.
/// </summary>
/// <remarks>
/// <para>
/// A title is whatever the page's author wrote, or wanted to look as if they had written. Anything
/// that is not text comes out here: control characters, and the invisible format characters that can
/// reverse the direction of what follows them or hide a word, which is how a title gets to read as
/// something other than what it holds. The two joiners stay, because several scripts spell words with
/// them.
/// </para>
/// <para>
/// Only so much of what a page says goes any further. Putting text in its normal form takes time in the square
/// of a run of combining marks, in native code nothing can cancel, so what a page could make of that has to
/// be kept to what a title could use. Four units is the most Unicode composes into one, as with a Greek vowel
/// carrying a breathing, an accent and a subscript.
/// </para>
/// <para>
/// What is left stays as text. Markup in a title is what the author typed and is not removed, because
/// a title about HTML is entitled to mention it, and nothing downstream ever reads a title as anything
/// but text.
/// </para>
/// </remarks>
internal static class PostTitle
{
    private const int ZeroWidthNonJoiner = 0x200C;
    private const int ZeroWidthJoiner = 0x200D;
    private const int FirstNoncharacterBlock = 0xFDD0;
    private const int LastNoncharacterBlock = 0xFDEF;
    private const int EndOfPlaneMask = 0xFFFE;

    /// <summary>How much text is put in its normal form, which is as much as it takes to fill a title.</summary>
    private const int MostUnitsNormalised = 4 * SitePost.MaxTitleLength;

    /// <summary>
    /// Cleans a title.
    /// </summary>
    /// <param name="text">What the page says, which may be anything.</param>
    /// <returns>
    /// The title, at most <see cref="SitePost.MaxTitleLength"/> characters, or nothing where none is left.
    /// </returns>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var title = Cut(WithTextOnly(text).Normalize(NormalizationForm.FormC)).TrimEnd();

        return title.Length == 0 ? null : title;
    }

    /// <summary>
    /// Keeps what is text, with every run of space in it brought down to one and none left at either
    /// end. Text that is not well formed comes out as the replacement character, which is what going
    /// through it a rune at a time does. It stops once it holds all that can be used.
    /// </summary>
    private static string WithTextOnly(string text)
    {
        var kept = new StringBuilder(Math.Min(text.Length, MostUnitsNormalised));
        var spaceDue = false;
        Span<char> encoded = stackalloc char[2];

        foreach (var rune in text.EnumerateRunes())
        {
            if (kept.Length >= MostUnitsNormalised)
            {
                break;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                spaceDue = kept.Length > 0;
                continue;
            }

            if (IsNotText(rune))
            {
                continue;
            }

            if (spaceDue)
            {
                kept.Append(' ');
                spaceDue = false;
            }

            kept.Append(encoded[..rune.EncodeToUtf16(encoded)]);
        }

        return kept.ToString();
    }

    private static bool IsNotText(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);

        return category == UnicodeCategory.Control
            || (category == UnicodeCategory.Format && rune.Value is not (ZeroWidthNonJoiner or ZeroWidthJoiner))
            || IsNoncharacter(rune.Value);
    }

    /// <summary>
    /// The code points Unicode promises never to assign: normalising text that holds one is refused
    /// outright, which would make a title of a single such character fail the whole page.
    /// </summary>
    private static bool IsNoncharacter(int value) =>
        value is >= FirstNoncharacterBlock and <= LastNoncharacterBlock
        || (value & EndOfPlaneMask) == EndOfPlaneMask;

    /// <summary>
    /// Cuts text at the last whole character that fits, as somebody reading it would count one, so that
    /// an accent or half an emoji is never left hanging on the end.
    /// </summary>
    private static string Cut(string text)
    {
        if (text.Length <= SitePost.MaxTitleLength)
        {
            return text;
        }

        var end = 0;

        while (end < text.Length)
        {
            var next = end + StringInfo.GetNextTextElementLength(text.AsSpan(end));

            if (next > SitePost.MaxTitleLength)
            {
                break;
            }

            end = next;
        }

        return text[..end];
    }
}
