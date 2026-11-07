using System.Text;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// The path of one rule in a robots.txt, ready to be matched in time that does not depend on how it
/// was written.
/// </summary>
/// <remarks>
/// <para>
/// A rule is written by whoever runs the website, and a wildcard matcher that backtracks can be made
/// to take as long as somebody likes with a dozen stars and one long address. This one never goes
/// back. Between two stars, the earliest place a run of text fits is always as good as any later one,
/// so each run is searched for from where the last one ended, and each search only moves forwards.
/// The whole match takes time in proportion to the address and the rule together.
/// </para>
/// <para>
/// RFC 9309 asks for both sides of a comparison to be escaped the same way first, and
/// <see cref="Canonical"/> is that way. It is one function with no idea of which part of an address it
/// is in, because a rule often does not say: <c>/*sort=</c> is meant to match inside a query string,
/// and has no question mark to say so. The characters that give an address its shape stay as they
/// are, however they were spelled, and everything else reserved is escaped, however it was spelled.
/// </para>
/// </remarks>
internal sealed class RobotsPattern
{
    private const string HexDigits = "0123456789ABCDEF";
    private const int MostBytesInACharacter = 4;
    private const int EscapeLength = 3;

    private readonly string _start;
    private readonly TextRun[] _runs;
    private readonly string? _end;
    private readonly bool _hasWildcard;

    private RobotsPattern(string start, TextRun[] runs, string? end, bool hasWildcard, int octets)
    {
        _start = start;
        _runs = runs;
        _end = end;
        _hasWildcard = hasWildcard;
        Octets = octets;
    }

    /// <summary>
    /// How specific the rule is, which is what decides between two rules that both match: the length
    /// of the path as it is compared, with a wildcard and an end marker counted as written.
    /// </summary>
    public int Octets { get; }

    /// <summary>
    /// Reads a rule's path.
    /// </summary>
    /// <param name="written">The path as it appears in the file, which is not empty.</param>
    /// <returns>The pattern.</returns>
    public static RobotsPattern Of(ReadOnlySpan<char> written)
    {
        var anchored = written[^1] == '$';
        var body = anchored ? written[..^1] : written;
        var texts = new List<string>();

        foreach (var text in body.Split('*'))
        {
            texts.Add(Canonical(body[text]));
        }

        var hasWildcard = texts.Count > 1;

        // The first text has to be where the path starts, and where the rule says it stops there the
        // last has to be where the path ends. Only what is between is searched for.
        var searched = texts.Skip(1).Take(anchored && hasWildcard ? texts.Count - 2 : texts.Count - 1);
        var runs = searched.Where(text => text.Length > 0).Select(text => new TextRun(text)).ToArray();
        var octets = texts.Sum(text => text.Length) + texts.Count - 1 + (anchored ? 1 : 0);

        return new RobotsPattern(texts[0], runs, anchored ? texts[^1] : null, hasWildcard, octets);
    }

    /// <summary>
    /// Writes a path or a piece of one in the one spelling it is compared in.
    /// </summary>
    /// <remarks>
    /// Escaped unreserved characters are written plainly, and hex digits come out in capitals, as
    /// RFC 3986 says the two spellings are equal. Anything outside ASCII is escaped as its UTF-8. A
    /// literal star or dollar sign in an address comes out escaped, which is how a rule refers to one.
    /// A percent sign that is not the start of an escape is one, and comes out escaped as well.
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <returns>It in that spelling, which is all ASCII.</returns>
    public static string Canonical(ReadOnlySpan<char> text)
    {
        var written = new StringBuilder(text.Length);

        while (!text.IsEmpty)
        {
            text = text[Append(text, written)..];
        }

        return written.ToString();
    }

    /// <summary>
    /// Whether this rule matches a path, starting at its first character.
    /// </summary>
    /// <param name="path">The path, in the spelling <see cref="Canonical"/> writes.</param>
    /// <returns>Whether the rule reaches its end before the two differ.</returns>
    public bool Matches(ReadOnlySpan<char> path)
    {
        if (!path.StartsWith(_start, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = path[_start.Length..];

        foreach (var run in _runs)
        {
            var found = run.FindIn(rest);

            if (found < 0)
            {
                return false;
            }

            rest = rest[(found + run.Length)..];
        }

        return EndsAsItShould(rest);
    }

    private bool EndsAsItShould(ReadOnlySpan<char> rest)
    {
        if (_end is null)
        {
            return true;
        }

        return _hasWildcard ? rest.EndsWith(_end, StringComparison.Ordinal) : rest.IsEmpty;
    }

    /// <summary>
    /// Writes the next character and says how much of the text it took up.
    /// </summary>
    private static int Append(ReadOnlySpan<char> text, StringBuilder written)
    {
        if (text[0] == '%' && TryReadEscape(text, out var escaped))
        {
            AppendByte(escaped, written);

            return EscapeLength;
        }

        if (char.IsAscii(text[0]))
        {
            AppendByte((byte)text[0], written);

            return 1;
        }

        return AppendBeyondAscii(text, written);
    }

    private static int AppendBeyondAscii(ReadOnlySpan<char> text, StringBuilder written)
    {
        Rune.DecodeFromUtf16(text, out var rune, out var consumed);

        Span<byte> bytes = stackalloc byte[MostBytesInACharacter];
        var length = rune.EncodeToUtf8(bytes);

        foreach (var value in bytes[..length])
        {
            AppendEscaped(value, written);
        }

        return consumed;
    }

    private static bool TryReadEscape(ReadOnlySpan<char> text, out byte value)
    {
        value = 0;

        if (text.Length < EscapeLength || !char.IsAsciiHexDigit(text[1]) || !char.IsAsciiHexDigit(text[2]))
        {
            return false;
        }

        value = (byte)((Uri.FromHex(text[1]) << 4) | Uri.FromHex(text[2]));

        return true;
    }

    private static void AppendByte(byte value, StringBuilder written)
    {
        if (StaysAsItIs((char)value))
        {
            written.Append((char)value);
        }
        else
        {
            AppendEscaped(value, written);
        }
    }

    private static void AppendEscaped(byte value, StringBuilder written) =>
        written.Append('%').Append(HexDigits[value >> 4]).Append(HexDigits[value & 0xF]);

    /// <summary>
    /// The unreserved characters, which never need escaping, and the four that give an address its
    /// shape, which a rule has to be able to write as they are.
    /// </summary>
    private static bool StaysAsItIs(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '-' or '.' or '_' or '~' or '/' or '?' or '=' or '&';

    /// <summary>
    /// One stretch of plain text between two wildcards, with what it takes to search for it without
    /// ever going back over what has been read.
    /// </summary>
    private sealed class TextRun
    {
        private readonly string _text;

        /// <summary>
        /// For each length of match so far, how much of the run is still matched if the next
        /// character breaks it: the longest start of the run that is also an end of that match.
        /// </summary>
        private readonly int[] _fallback;

        public TextRun(string text)
        {
            _text = text;
            _fallback = new int[text.Length];

            var matched = 0;

            for (var at = 1; at < text.Length; at++)
            {
                matched = Advance(matched, text[at]);
                _fallback[at] = matched;
            }
        }

        public int Length => _text.Length;

        /// <summary>The first place the run appears in some text, or a negative number where it does not.</summary>
        public int FindIn(ReadOnlySpan<char> text)
        {
            var matched = 0;

            for (var at = 0; at < text.Length; at++)
            {
                matched = Advance(matched, text[at]);

                if (matched == _text.Length)
                {
                    return at - matched + 1;
                }
            }

            return -1;
        }

        private int Advance(int matched, char next)
        {
            while (matched > 0 && _text[matched] != next)
            {
                matched = _fallback[matched - 1];
            }

            return _text[matched] == next ? matched + 1 : 0;
        }
    }
}
