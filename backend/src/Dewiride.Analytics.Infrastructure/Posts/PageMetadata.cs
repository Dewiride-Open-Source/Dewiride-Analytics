namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Reads what a website's page says about itself: what it is, what it is called, and when it was
/// published.
/// </summary>
/// <remarks>
/// <para>
/// A page is read by its tokens, and no document is ever built from it. A document is what a page can turn
/// against whatever reads it: an inline frame's text is opened as a document of its own, with no limit and
/// nothing to stop it, a run of certain tags recurses until the stack runs out, and open formatting tags are
/// copied for every run of text, by the gigabyte. Going through the tags costs what it takes to read them,
/// however they are written. Nothing a page says is ever acted on, and nothing it points at is ever fetched.
/// </para>
/// <para>
/// A date comes from what a page states for machines to read, in order: Open Graph, the structured data
/// in the page, microdata and microformats. Plain WordPress states none of that. Its own block for a
/// post's date is the one piece of a theme's markup read, where a page holds exactly one that is not part
/// of a list of posts. A theme can print no date for a post at all, as Twenty Twenty-Five does, and such a
/// post stays undated, which is what it is. The same block can print when a post was last modified, and that
/// is never when it was published.
/// </para>
/// <para>
/// A page listing posts is not one, whatever else it says, and a date on it is some post's and not its own.
/// It says so by naming a collection in its structured data, or by the body class WordPress gives it. A page
/// marking several entries is taken to be one as well, unless its body says it is one post, as a post with
/// others listed under it does. Those last two are inferences, so they give way to a page stating outright
/// that it is an article: one with a type of article in its structured data, or a publish time of its own.
/// </para>
/// <para>
/// A page saying only that it is an article, in Open Graph, has not said very much. Yoast and Hugo say so of
/// every page they make, down to an About page, so it takes a date or structured data to believe them. A page
/// saying nothing at all is still a post where a website's sitemap keeps a file of them.
/// </para>
/// <para>
/// A page that says nothing keeps nothing, and that is a correct answer. A title or a date is never
/// made up from anything less than the page saying so.
/// </para>
/// </remarks>
internal static class PageMetadata
{
    private const string OpenGraphArticleType = "article";

    /// <summary>
    /// Reads a page.
    /// </summary>
    /// <param name="page">The page, as far as it was read.</param>
    /// <param name="contentType">What the website says it is, which is where a charset may be named.</param>
    /// <param name="zone">The website's reporting zone, for a date that states no offset.</param>
    /// <param name="now">The present moment, for telling a date from a mistake.</param>
    /// <param name="cancellationToken">Stops the reading, between one token and the next.</param>
    /// <returns>What it says.</returns>
    public static PageFacts Read(
        ArraySegment<byte> page,
        string? contentType,
        TimeZoneInfo zone,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var stated = PageWalker.Walk(PageCharset.Decode(page, contentType), zone, now, cancellationToken);
        var structured = StructuredData.Read(stated.StructuredBlocks);
        var (title, titleSource) = TitleOf(stated, structured);
        var isAList = IsAList(stated, structured, zone, now);
        var (published, publishedSource) = isAList ? (null, null) : DateOf(stated, structured, zone, now);

        return new PageFacts(
            title,
            titleSource,
            published,
            publishedSource,
            TypeOf(stated, structured, isAList, published));
    }

    private static (string? Title, PostTitleSource? Source) TitleOf(PageStatements stated, StructuredFacts structured)
    {
        (string? Text, PostTitleSource Source)[] candidates =
        [
            (stated.Tags.GetValueOrDefault(PageTagNames.OpenGraphTitle), PostTitleSource.OpenGraph),
            (structured.Headline, PostTitleSource.StructuredData),
            (stated.Tags.GetValueOrDefault(PageTagNames.TwitterTitle), PostTitleSource.Twitter),
            (stated.Title, PostTitleSource.Document),
        ];

        foreach (var (text, source) in candidates)
        {
            var title = PostTitle.Clean(text);

            if (title is not null)
            {
                return (title, source);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Whether a page is a list of posts rather than one.
    /// </summary>
    private static bool IsAList(
        PageStatements stated,
        StructuredFacts structured,
        TimeZoneInfo zone,
        DateTimeOffset now)
    {
        if (structured.NamesACollection)
        {
            return true;
        }

        var statesItsOwnDate = PublishedDate.TryRead(
            stated.Tags.GetValueOrDefault(PageTagNames.OpenGraphPublished),
            zone,
            now,
            out _);

        if (structured.IsArticle || statesItsOwnDate || stated.SaysItIsOnePost)
        {
            return false;
        }

        return stated.SaysItListsPosts || stated.Entries > 1;
    }

    private static (DateTimeOffset? Published, PostDateSource? Source) DateOf(
        PageStatements stated,
        StructuredFacts structured,
        TimeZoneInfo zone,
        DateTimeOffset now)
    {
        (string? Value, PostDateSource Source)[] candidates =
        [
            (stated.Tags.GetValueOrDefault(PageTagNames.OpenGraphPublished), PostDateSource.OpenGraph),
            (structured.DatePublished, PostDateSource.StructuredData),
            (stated.MicrodataDate, PostDateSource.Microdata),
            (stated.MicroformatDate, PostDateSource.Microformat),
            (stated.PrintedDate, PostDateSource.WordPressBlock),
        ];

        foreach (var (value, source) in candidates)
        {
            if (PublishedDate.TryRead(value, zone, now, out var published))
            {
                return (published, source);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// What a page says it is. A page saying anything but an article is not one, whatever else it holds, and
    /// neither is a list. A product is not one either, unless the page says it is an article about one. Saying
    /// nothing of the kind is not the same as saying website, whatever Open Graph implies.
    /// </summary>
    private static PostPageType? TypeOf(
        PageStatements stated,
        StructuredFacts structured,
        bool isAList,
        DateTimeOffset? published)
    {
        var saysItIsSomethingElse = stated.Tags.TryGetValue(PageTagNames.OpenGraphType, out var declared)
            && !declared.Trim().Equals(OpenGraphArticleType, StringComparison.OrdinalIgnoreCase);

        if (saysItIsSomethingElse || isAList)
        {
            return PostPageType.Other;
        }

        if (structured.IsArticle)
        {
            return PostPageType.Article;
        }

        if (structured.NamesAProduct)
        {
            return PostPageType.Other;
        }

        return published is null ? null : PostPageType.Article;
    }
}

/// <summary>
/// What a page says about itself.
/// </summary>
/// <param name="Title">What it says it is called, cleaned.</param>
/// <param name="TitleSource">Where on the page that was.</param>
/// <param name="PublishedAt">When it says it was published, in UTC.</param>
/// <param name="PublishedSource">Where on the page that was.</param>
/// <param name="PageType">What it says it is.</param>
internal sealed record PageFacts(
    string? Title,
    PostTitleSource? TitleSource,
    DateTimeOffset? PublishedAt,
    PostDateSource? PublishedSource,
    PostPageType? PageType);
