using System.Collections.Frozen;
using System.Text;
using AngleSharp.Html.Parser;
using AngleSharp.Html.Parser.Tokens;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Goes through a page's tags in order and notes what it states about itself.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is built from them. What the tags mean is worked out from the few things that decide it: which
/// elements switch the tokenizer to reading plain text, which never hold anything, and what is inside what.
/// Vector graphics and mathematics follow their own rules on the first two, so a title in a drawing is part
/// of the drawing, and what is inside a template is no part of the page.
/// </para>
/// <para>
/// A page that nests badly gets the plain reading of it: an element is open until an end tag closes it or
/// something around it. A browser closes some elements for a page, and moves others about, in ways that
/// only ever change what a badly made page says by being inside something it left open.
/// </para>
/// </remarks>
internal sealed class PageWalker
{
    private const string BodyTag = "body";
    private const string HtmlTag = "html";
    private const string ScriptTag = "script";
    private const string TimeTag = "time";
    private const string TitleTag = "title";
    private const string ClassAttribute = "class";
    private const string TitleAttribute = "title";
    private const string DateTimeAttribute = "datetime";
    private const string DatePublishedProperty = "datePublished";
    private const string StructuredBlockType = "application/ld+json";
    private const string SpaceBetweenWords = " \t\n\f\r";

    /// <summary>The elements whose content is plain text, and the way the tokenizer reads each.</summary>
    private static readonly FrozenDictionary<string, HtmlParseMode> PlainTextElements =
        new Dictionary<string, HtmlParseMode>
        {
            [TitleTag] = HtmlParseMode.RCData,
            ["textarea"] = HtmlParseMode.RCData,
            [ScriptTag] = HtmlParseMode.Script,
            ["style"] = HtmlParseMode.Rawtext,
            ["xmp"] = HtmlParseMode.Rawtext,
            ["iframe"] = HtmlParseMode.Rawtext,
            ["noembed"] = HtmlParseMode.Rawtext,
            ["noframes"] = HtmlParseMode.Rawtext,
            ["plaintext"] = HtmlParseMode.Plaintext,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The elements that never hold anything, and so are never left open.</summary>
    private static readonly FrozenSet<string> EmptyElements = FrozenSet.ToFrozenSet(
    [
        "area",
        "base",
        "basefont",
        "bgsound",
        "br",
        "col",
        "embed",
        "frame",
        "hr",
        "image",
        "img",
        "input",
        "keygen",
        "link",
        PageTokens.MetaTag,
        "param",
        "source",
        "track",
        "wbr",
    ], StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ElementMarks> MarksOfElements =
        new Dictionary<string, ElementMarks>
        {
            ["svg"] = ElementMarks.Foreign,
            ["math"] = ElementMarks.Foreign,
            ["template"] = ElementMarks.Inert,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ElementMarks>.AlternateLookup<ReadOnlySpan<char>> MarksOfClasses =
        new Dictionary<string, ElementMarks>
        {
            ["h-entry"] = ElementMarks.NewEntry | ElementMarks.Entry,
            ["hentry"] = ElementMarks.OldEntry | ElementMarks.Entry,
            ["dt-published"] = ElementMarks.NewPublished,
            ["published"] = ElementMarks.OldPublished,
            ["wp-block-post-date"] = ElementMarks.PostDate,
            ["wp-block-post-date__modified-date"] = ElementMarks.ModifiedDate,
            ["wp-block-post-template"] = ElementMarks.PostList,
            ["wp-block-query"] = ElementMarks.PostList,
            ["single"] = ElementMarks.OnePost,
            ["blog"] = ElementMarks.ListOfPosts,
            ["archive"] = ElementMarks.ListOfPosts,
            ["search"] = ElementMarks.ListOfPosts,
        }.ToFrozenDictionary(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>The tags a page describes itself with, of those that are read. Any others are not kept.</summary>
    private static readonly FrozenSet<string> TagsRead = FrozenSet.ToFrozenSet(
    [
        PageTagNames.OpenGraphType,
        PageTagNames.OpenGraphTitle,
        PageTagNames.OpenGraphPublished,
        PageTagNames.TwitterTitle,
    ], StringComparer.OrdinalIgnoreCase);

    private readonly HtmlTokenizer _tokenizer;
    private readonly TimeZoneInfo _zone;
    private readonly DateTimeOffset _now;
    private readonly OpenElements _open = new();
    private readonly StringBuilder _text = new();
    private readonly Dictionary<string, string> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _blocks = [];
    private Keeping _keeping;
    private int _keepingUntilDepth;
    private string? _title;
    private string? _microdataDate;
    private string? _microformatDate;
    private string? _printedDate;
    private int _printedDates;
    private int _entries;
    private ElementMarks _body;

    private PageWalker(HtmlTokenizer tokenizer, TimeZoneInfo zone, DateTimeOffset now)
    {
        _tokenizer = tokenizer;
        _zone = zone;
        _now = now;
    }

    private enum Keeping
    {
        Nothing,
        Title,
        StructuredBlock,
        DateText,
    }

    /// <summary>
    /// Goes through a page.
    /// </summary>
    /// <param name="text">The page's text.</param>
    /// <param name="zone">The website's reporting zone, for telling a date that can be read.</param>
    /// <param name="now">The present moment, for telling a date from a mistake.</param>
    /// <param name="cancellationToken">Stops the reading, between one token and the next.</param>
    /// <returns>What it states.</returns>
    public static PageStatements Walk(
        string text,
        TimeZoneInfo zone,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var tokenizer = PageTokens.Over(text);
        var walker = new PageWalker(tokenizer, zone, now);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var token = tokenizer.Get();

            if (token.Type == HtmlTokenType.EndOfFile)
            {
                return walker.WhatWasStated();
            }

            walker.Take(token);
        }
    }

    private void Take(HtmlToken token)
    {
        switch (token.Type)
        {
            case HtmlTokenType.StartTag:
                Open(token.AsTag());
                break;

            case HtmlTokenType.EndTag:
                Close(token.Name);
                break;

            case HtmlTokenType.Character when _keeping != Keeping.Nothing:
                _text.Append(token.Data);
                break;
        }
    }

    private void Open(HtmlTagToken tag)
    {
        var around = _open.Marks;
        var own = MarksOf(tag);
        var marks = around | (own & ElementMarks.PassedDown);
        var held = !EmptyElements.Contains(tag.Name) && !ClosesItself(tag, marks) && _open.Open(tag.Name, marks);

        if (!around.HasFlag(ElementMarks.Inert))
        {
            NoteWhatItStates(tag, own, around, held);
        }

        EnterWhatItHolds(tag, marks);
        _tokenizer.IsAcceptingCharacterData = _open.Marks.HasFlag(ElementMarks.Foreign);
    }

    /// <summary>
    /// Whether a tag marked as closing itself means it. Only in vector graphics and mathematics does it: on
    /// a tag of the page's own the mark is ignored, and what it opened stays open.
    /// </summary>
    private static bool ClosesItself(HtmlTagToken tag, ElementMarks marks) =>
        tag.IsSelfClosing && marks.HasFlag(ElementMarks.Foreign);

    /// <summary>
    /// Closes what an end tag closes. The page's outermost two stay open, because whatever follows the end
    /// of them is taken to be inside them all the same.
    /// </summary>
    private void Close(string name)
    {
        if (name is HtmlTag or BodyTag)
        {
            return;
        }

        _open.Close(name);

        if (_keeping is Keeping.Title or Keeping.StructuredBlock
            || (_keeping == Keeping.DateText && _open.Depth < _keepingUntilDepth))
        {
            FinishKeeping();
        }

        _tokenizer.IsAcceptingCharacterData = _open.Marks.HasFlag(ElementMarks.Foreign);
    }

    private void NoteWhatItStates(HtmlTagToken tag, ElementMarks own, ElementMarks around, bool held)
    {
        switch (tag.Name)
        {
            case PageTokens.MetaTag:
                NoteMetaTag(tag);
                break;

            case BodyTag:
                _body |= own;
                break;

            case TimeTag:
                NotePrintedDate(tag, around);
                break;
        }

        if (own.HasFlag(ElementMarks.Entry) && !around.HasFlag(ElementMarks.Entry))
        {
            _entries++;
        }

        if (HoldsTheWord(tag.ValueOf("itemprop"), DatePublishedProperty))
        {
            Offer(ref _microdataDate, tag.ValueOf(PageTokens.ContentAttribute) ?? tag.ValueOf(DateTimeAttribute));
        }

        NoteMicroformatDate(tag, own, around, held);
    }

    /// <summary>
    /// Reads a tag describing the page, the first of each name winning. Open Graph's are named in one
    /// attribute and everybody else's in another, and pages are not careful which.
    /// </summary>
    private void NoteMetaTag(HtmlTagToken tag)
    {
        var name = tag.ValueOf("property") ?? tag.ValueOf("name");
        var content = tag.ValueOf(PageTokens.ContentAttribute);

        if (name is not null && TagsRead.Contains(name) && !string.IsNullOrWhiteSpace(content))
        {
            _tags.TryAdd(name, content);
        }
    }

    /// <summary>
    /// Reads the date in WordPress's block for it, unless the block is part of a list of posts, where it is
    /// some other post's date.
    /// </summary>
    private void NotePrintedDate(HtmlTagToken tag, ElementMarks around)
    {
        if (around.HasFlag(ElementMarks.PostDate)
            && !around.HasFlag(ElementMarks.PostList)
            && tag.ValueOf(DateTimeAttribute) is { } date)
        {
            _printedDates++;
            _printedDate ??= date;
        }
    }

    /// <summary>
    /// Reads a date marked as an entry's. Where it is not in an attribute it is the text of the element, which
    /// is kept until that closes.
    /// </summary>
    private void NoteMicroformatDate(HtmlTagToken tag, ElementMarks own, ElementMarks around, bool held)
    {
        var marked = (own.HasFlag(ElementMarks.OldPublished) && around.HasFlag(ElementMarks.OldEntry))
            || (own.HasFlag(ElementMarks.NewPublished) && around.HasFlag(ElementMarks.NewEntry));

        if (!marked)
        {
            return;
        }

        if ((tag.ValueOf(DateTimeAttribute) ?? tag.ValueOf(TitleAttribute)) is { } stated)
        {
            Offer(ref _microformatDate, stated);
        }
        else if (held && _microformatDate is null)
        {
            BeginKeeping(Keeping.DateText);
        }
    }

    /// <summary>
    /// Keeps a date, if none of its kind has been yet and this one can be read.
    /// </summary>
    private void Offer(ref string? kept, string? date)
    {
        if (kept is null && PublishedDate.TryRead(date, _zone, _now, out _))
        {
            kept = date;
        }
    }

    /// <summary>
    /// Switches the tokenizer to plain text for an element whose content is that, and keeps the text of the
    /// page's first title and of its first few blocks of structured data.
    /// </summary>
    private void EnterWhatItHolds(HtmlTagToken tag, ElementMarks marks)
    {
        if (marks.HasFlag(ElementMarks.Foreign) || !PlainTextElements.TryGetValue(tag.Name, out var mode))
        {
            return;
        }

        _tokenizer.State = mode;

        if (marks.HasFlag(ElementMarks.Inert))
        {
            return;
        }

        if (tag.Name == TitleTag && _title is null)
        {
            BeginKeeping(Keeping.Title);
        }
        else if (IsAStructuredBlock(tag) && _blocks.Count < StructuredData.MostBlocks)
        {
            BeginKeeping(Keeping.StructuredBlock);
        }
    }

    private static bool IsAStructuredBlock(HtmlTagToken tag) =>
        tag.Name == ScriptTag
        && tag.ValueOf("type") is { } type
        && type.StartsWith(StructuredBlockType, StringComparison.OrdinalIgnoreCase);

    private void BeginKeeping(Keeping what)
    {
        if (_keeping != Keeping.Nothing)
        {
            FinishKeeping();
        }

        _keeping = what;
        _keepingUntilDepth = _open.Depth;
        AskForTextKept();
    }

    private void FinishKeeping()
    {
        var text = _text.ToString();

        switch (_keeping)
        {
            case Keeping.Title:
                _title = text;
                break;

            case Keeping.StructuredBlock:
                _blocks.Add(text);
                break;

            case Keeping.DateText:
                Offer(ref _microformatDate, text);
                break;
        }

        _text.Clear();
        _keeping = Keeping.Nothing;
        AskForTextKept();
    }

    /// <summary>
    /// Has the tokenizer hand over the text being kept and pass over all the rest, which it then does without
    /// holding on to any of it.
    /// </summary>
    private void AskForTextKept()
    {
        _tokenizer.SkipRCDataText = _keeping != Keeping.Title;
        _tokenizer.SkipScriptText = _keeping != Keeping.StructuredBlock;
        _tokenizer.SkipDataText = _keeping != Keeping.DateText;
    }

    private PageStatements WhatWasStated()
    {
        if (_keeping != Keeping.Nothing)
        {
            FinishKeeping();
        }

        return new PageStatements
        {
            Tags = _tags,
            Title = _title,
            StructuredBlocks = _blocks,
            MicrodataDate = _microdataDate,
            MicroformatDate = _microformatDate,
            PrintedDate = _printedDates == 1 ? _printedDate : null,
            Entries = _entries,
            SaysItIsOnePost = _body.HasFlag(ElementMarks.OnePost),
            SaysItListsPosts = _body.HasFlag(ElementMarks.ListOfPosts),
        };
    }

    private static ElementMarks MarksOf(HtmlTagToken tag)
    {
        var marks = MarksOfElements.GetValueOrDefault(tag.Name) | MarksIn(tag.ValueOf(ClassAttribute));

        if (StatesAnArticle(tag.ValueOf("itemtype")))
        {
            marks |= ElementMarks.Entry;
        }

        // A date printed as when a post was last changed is that, whichever other blocks it is marked as too.
        return marks.HasFlag(ElementMarks.ModifiedDate) ? marks & ~ElementMarks.PostDate : marks;
    }

    private static ElementMarks MarksIn(string? classes)
    {
        if (classes is null)
        {
            return ElementMarks.None;
        }

        var marks = ElementMarks.None;
        var text = classes.AsSpan();

        foreach (var word in text.SplitAny(SpaceBetweenWords))
        {
            if (MarksOfClasses.TryGetValue(text[word], out var mark))
            {
                marks |= mark;
            }
        }

        return marks;
    }

    private static bool StatesAnArticle(string? types)
    {
        if (types is null)
        {
            return false;
        }

        var text = types.AsSpan();

        foreach (var word in text.SplitAny(SpaceBetweenWords))
        {
            if (StructuredData.NamesAnArticle(text[word]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HoldsTheWord(string? words, string wanted)
    {
        if (words is null)
        {
            return false;
        }

        var text = words.AsSpan();

        foreach (var word in text.SplitAny(SpaceBetweenWords))
        {
            if (text[word].SequenceEqual(wanted))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// What a page states about itself, before anything has been decided from it.
/// </summary>
internal sealed class PageStatements
{
    /// <summary>The tags that are read among those it describes itself with, by name, in any case.</summary>
    public required IReadOnlyDictionary<string, string> Tags { get; init; }

    /// <summary>What its first title element holds, where it has one.</summary>
    public required string? Title { get; init; }

    /// <summary>What each of its first few blocks of structured data holds.</summary>
    public required IReadOnlyList<string> StructuredBlocks { get; init; }

    /// <summary>The first date it marks as published in microdata that can be read.</summary>
    public required string? MicrodataDate { get; init; }

    /// <summary>The first date it marks as an entry's in a microformat that can be read.</summary>
    public required string? MicroformatDate { get; init; }

    /// <summary>The date in WordPress's block for it, where that is the only one outside a list of posts.</summary>
    public required string? PrintedDate { get; init; }

    /// <summary>How many entries it marks, not counting those inside another.</summary>
    public required int Entries { get; init; }

    /// <summary>Whether its body carries the class WordPress gives one post on a page of its own.</summary>
    public required bool SaysItIsOnePost { get; init; }

    /// <summary>Whether its body carries a class WordPress gives a page listing posts.</summary>
    public required bool SaysItListsPosts { get; init; }
}

/// <summary>
/// The names of the tags a page describes itself with.
/// </summary>
internal static class PageTagNames
{
    /// <summary>What it says it is.</summary>
    public const string OpenGraphType = "og:type";

    /// <summary>What it says it is called, to those sharing it.</summary>
    public const string OpenGraphTitle = "og:title";

    /// <summary>When it says it was published.</summary>
    public const string OpenGraphPublished = "article:published_time";

    /// <summary>What it says it is called, to one service in particular.</summary>
    public const string TwitterTitle = "twitter:title";
}
