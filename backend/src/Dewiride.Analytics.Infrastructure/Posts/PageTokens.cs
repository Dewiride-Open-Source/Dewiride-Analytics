using System.Collections.Frozen;
using AngleSharp.Html;
using AngleSharp.Html.Parser;
using AngleSharp.Html.Parser.Tokens;
using AngleSharp.Html.Parser.Tokens.Struct;
using AngleSharp.Text;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Cuts a page's text into its tags, keeping only what is read of them.
/// </summary>
/// <remarks>
/// <para>
/// A tag keeps the attributes a page's description of itself is made of and no others, and the first of
/// each name, which is the one a browser keeps. So what one tag holds has a limit whatever is written on it,
/// and a page of nothing but attributes costs what it takes to read past them. Everything between the tags
/// is passed over unless whoever is walking the tokens has asked for it.
/// </para>
/// <para>
/// The tokenizer calls itself at two points, in tail position, and nothing says the machine code will not
/// use a frame for each: on an end tag with no name, <c>&lt;/&gt;</c>, straight after another, and on a
/// comment ending that turns out not to be one, <c>--!</c>, straight after another. A run of either the
/// length of a page overflows the stack, and that can not be caught: it ends the engine. Neither means
/// anything to anybody, so a long run is broken up before the tokenizer sees it, by one character in every
/// so many. An architecture test holds the tokenizer to calling itself at these points and no others.
/// </para>
/// </remarks>
internal static class PageTokens
{
    /// <summary>The tag a page describes itself with.</summary>
    public const string MetaTag = "meta";

    /// <summary>What a meta tag says.</summary>
    public const string ContentAttribute = "content";

    /// <summary>What a meta tag standing in for a header is called.</summary>
    public const string HttpEquivAttribute = "http-equiv";

    /// <summary>How many in a row of what the tokenizer calls itself on are left as they are written.</summary>
    private const int MostInARow = 16;

    private const string EmptyEndTag = "</>";
    private const string FalseCommentEnd = "--!";
    private const string EmptyEndTags = EmptyEndTag + EmptyEndTag;
    private const string FalseCommentEnds = FalseCommentEnd + "--";

    /// <summary>Marks the end of the text to the tokenizer, so it can not stand for a character of a page.</summary>
    private const char EndOfText = '\uFFFF';

    private const char Replacement = '\uFFFD';

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> AttributesRead =
        FrozenSet.ToFrozenSet(
            [
                "property",
                "name",
                ContentAttribute,
                "class",
                "itemprop",
                "itemtype",
                "datetime",
                "type",
                "title",
                "charset",
                HttpEquivAttribute,
            ],
            StringComparer.Ordinal)
        .GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>
    /// Begins cutting a page's text into tokens.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The tokenizer, passing over everything between tags, which the caller disposes.</returns>
    public static HtmlTokenizer Over(string text) =>
        new(new TextSource(new ReadOnlyMemoryTextSource(Readable(text))), HtmlEntityProvider.ResolverExtended)
        {
            ShouldEmitAttribute = Keeps,
            DisableElementPositionTracking = true,
            SkipComments = true,
            SkipCDATA = true,
            SkipDataText = true,
            SkipPlaintext = true,
            SkipProcessingInstructions = true,
            SkipRawText = true,
            SkipRCDataText = true,
            SkipScriptText = true,
        };

    /// <summary>
    /// Reads an attribute of a tag.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <param name="name">The attribute's name, in lower case.</param>
    /// <returns>Its value, or nothing where the tag does not have it.</returns>
    public static string? ValueOf(this HtmlTagToken tag, string name)
    {
        foreach (var attribute in tag.Attributes)
        {
            if (attribute.Name == name)
            {
                return attribute.Value;
            }
        }

        return null;
    }

    private static bool Keeps(ref StructHtmlToken tag, ReadOnlyMemory<char> name)
    {
        if (!AttributesRead.Contains(name.Span))
        {
            return false;
        }

        var held = tag.Attributes;

        for (var index = 0; index < held.Count; index++)
        {
            if (held[index].Name.Memory.Span.SequenceEqual(name.Span))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Makes a page's text safe to hand to the tokenizer, leaving alone every page that means anything.
    /// </summary>
    private static string Readable(string text)
    {
        var readable = text.Replace(EndOfText, Replacement);

        if (!readable.Contains(EmptyEndTags, StringComparison.Ordinal)
            && !readable.Contains(FalseCommentEnds, StringComparison.Ordinal))
        {
            return readable;
        }

        var characters = readable.ToCharArray();

        BreakRuns(characters, EmptyEndTag);
        BreakRuns(characters, FalseCommentEnd);

        return new string(characters);
    }

    /// <summary>
    /// Takes the last character out of one in every so many of a run. Dashes between them do not end a run,
    /// because the tokenizer goes through those without returning from anything.
    /// </summary>
    private static void BreakRuns(Span<char> text, string each)
    {
        var inARow = 0;
        var at = 0;

        while (true)
        {
            var next = text[at..].IndexOf(each);

            if (next < 0)
            {
                return;
            }

            inARow = text.Slice(at, next).ContainsAnyExcept('-') ? 1 : inARow + 1;
            at += next + each.Length;

            if (inARow > MostInARow)
            {
                text[at - 1] = ' ';
                inARow = 0;
            }
        }
    }
}
