using System.Buffers;
using System.Text;
using System.Text.Unicode;
using AngleSharp.Html.Parser;
using AngleSharp.Html.Parser.Tokens;
using AngleSharp.Text;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Turns a page's bytes into its text.
/// </summary>
/// <remarks>
/// <para>
/// The order is the one the HTML standard's encoding sniffing sets out, and the first to answer settles it:
/// a byte order mark, then what the server says, then what the page says of itself in its first kibibyte. A
/// charset nobody can read is the same as none having been named.
/// </para>
/// <para>
/// A page saying nothing anywhere is UTF-8 where its bytes are, and windows-1252 where they are not. The
/// standard leaves this last step to the reader. Nothing written in windows-1252 looks like valid UTF-8 for
/// long, and text in plain ASCII reads the same either way.
/// </para>
/// </remarks>
internal static class PageCharset
{
    private const string Utf8Name = "utf-8";
    private const string Utf16BigEndianName = "utf-16be";
    private const string Utf16LittleEndianName = "utf-16le";
    private const string WesternName = "windows-1252";
    private const string CharsetWord = "charset";
    private const string ContentTypePragma = "content-type";
    private const string AsciiSpace = " \t\n\f\r";

    /// <summary>How much of a page is looked through for where it says what it is written in.</summary>
    private const int FirstBytesLooked = 1024;

    /// <summary>The most bytes of one character a page can end with, cut off, in UTF-8.</summary>
    private const int LongestCutCharacter = 3;

    private static readonly SearchValues<char> EndOfACharset = SearchValues.Create(AsciiSpace + ";");

    /// <summary>
    /// Makes the charsets a page can name available to the table of them, which works out what each name
    /// means once, the first time it is looked at.
    /// </summary>
    static PageCharset() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Turns a page's bytes into its text.
    /// </summary>
    /// <param name="page">The page, as far as it was read.</param>
    /// <param name="contentType">What the server says it is, which is where a charset may be named.</param>
    /// <returns>The text, with anything that could not be read in it as the replacement character.</returns>
    public static string Decode(ReadOnlySpan<byte> page, string? contentType)
    {
        var (marked, markLength) = MarkOn(page);

        if (marked is not null)
        {
            return marked.GetString(page[markLength..]);
        }

        var encoding = contentType is null ? null : Named(NameIn(contentType));

        return (encoding ?? NamedByThePage(page) ?? Unnamed(page)).GetString(page);
    }

    /// <summary>
    /// A page's byte order mark, which is all a page has to say to be believed, and its length.
    /// </summary>
    private static (Encoding? Encoding, int Length) MarkOn(ReadOnlySpan<byte> page)
    {
        if (page.StartsWith<byte>([0xEF, 0xBB, 0xBF]))
        {
            return (TextEncoding.Resolve(Utf8Name), 3);
        }

        if (page.StartsWith<byte>([0xFE, 0xFF]))
        {
            return (TextEncoding.Resolve(Utf16BigEndianName), 2);
        }

        return page.StartsWith<byte>([0xFF, 0xFE]) ? (TextEncoding.Resolve(Utf16LittleEndianName), 2) : (null, 0);
    }

    /// <summary>
    /// What a page says of itself in the tags at its very top. Its first kibibyte is read a byte at a time,
    /// with nothing decoded, because a charset's name is in ASCII whatever charset it names. A page saying
    /// it is in UTF-16 can not be, or these tags could not have been read, so that means UTF-8.
    /// </summary>
    private static Encoding? NamedByThePage(ReadOnlySpan<byte> page)
    {
        var firstBytes = page[..Math.Min(page.Length, FirstBytesLooked)];

        using var tokenizer = PageTokens.Over(Encoding.Latin1.GetString(firstBytes));

        tokenizer.IsNotConsumingCharacterReferences = true;

        for (var token = tokenizer.Get(); token.Type != HtmlTokenType.EndOfFile; token = tokenizer.Get())
        {
            if (token.IsStartTag(PageTokens.MetaTag) && NamedBy(token.AsTag()) is { } named)
            {
                return named.IsUnicode() ? TextEncoding.Resolve(Utf8Name) : named;
            }
        }

        return null;
    }

    /// <summary>
    /// What one tag says, in either of the two ways it can say it. The longer one only counts on a tag that
    /// says it stands in for the server's header.
    /// </summary>
    private static Encoding? NamedBy(HtmlTagToken meta)
    {
        var said = Named(meta.ValueOf(CharsetWord));

        if (said is not null || meta.ValueOf(PageTokens.ContentAttribute) is not { } content)
        {
            return said;
        }

        var standsInForTheHeader = string.Equals(
            meta.ValueOf(PageTokens.HttpEquivAttribute),
            ContentTypePragma,
            StringComparison.OrdinalIgnoreCase);

        return standsInForTheHeader ? Named(NameIn(content)) : null;
    }

    /// <summary>
    /// Finds a charset's name in a header's value, or in a tag standing in for one, by the steps the HTML
    /// standard gives for it.
    /// </summary>
    private static string? NameIn(string value)
    {
        var at = 0;

        while (true)
        {
            var found = value.IndexOf(CharsetWord, at, StringComparison.OrdinalIgnoreCase);

            if (found < 0)
            {
                return null;
            }

            at = PastSpace(value, found + CharsetWord.Length);

            if (at < value.Length && value[at] == '=')
            {
                return ValueAt(value, PastSpace(value, at + 1));
            }
        }
    }

    private static string? ValueAt(string text, int at)
    {
        if (at == text.Length)
        {
            return null;
        }

        if (text[at] is '"' or '\'')
        {
            var closing = text.IndexOf(text[at], at + 1);

            return closing < 0 ? null : text[(at + 1)..closing];
        }

        var length = text.AsSpan(at).IndexOfAny(EndOfACharset);

        return length < 0 ? text[at..] : text.Substring(at, length);
    }

    private static int PastSpace(string text, int at)
    {
        var length = text.AsSpan(at).IndexOfAnyExcept(AsciiSpace);

        return length < 0 ? text.Length : at + length;
    }

    private static Encoding? Named(string? charset)
    {
        var name = charset?.Trim(AsciiSpace.ToCharArray());

        return name is not null && TextEncoding.IsSupported(name) ? TextEncoding.Resolve(name) : null;
    }

    private static Encoding Unnamed(ReadOnlySpan<byte> page) =>
        TextEncoding.Resolve(Utf8.IsValid(WithoutACutCharacter(page)) ? Utf8Name : WesternName);

    /// <summary>
    /// A page cut off at the most that is read can end halfway through a character, and that says nothing
    /// about what the rest of it is written in.
    /// </summary>
    private static ReadOnlySpan<byte> WithoutACutCharacter(ReadOnlySpan<byte> page)
    {
        for (var at = page.Length - 1; at >= 0 && at >= page.Length - LongestCutCharacter; at--)
        {
            if (!IsTheRestOfACharacter(page[at]))
            {
                return Rune.DecodeFromUtf8(page[at..], out _, out _) == OperationStatus.NeedMoreData
                    ? page[..at]
                    : page;
            }
        }

        return page;
    }

    private static bool IsTheRestOfACharacter(byte value) => (value & 0xC0) == 0x80;
}
