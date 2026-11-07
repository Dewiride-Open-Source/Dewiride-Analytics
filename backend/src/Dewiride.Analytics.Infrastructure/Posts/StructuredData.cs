using System.Collections.Frozen;
using System.Net;
using System.Text.Json;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Reads the schema.org description a page gives of itself, where it gives one.
/// </summary>
/// <remarks>
/// <para>
/// A page can hold several descriptions in several blocks, of itself, its website, its author and
/// the list it is in. Only the first describing an article is what is wanted. The blocks are only
/// looked at where they say, at the top or in a graph, because a list of further articles nested
/// inside is usually a list of other pages.
/// </para>
/// <para>
/// Every block is looked at, because the first describing an article does not settle what the page is. A
/// page naming a collection is a list, whatever it holds, and one naming a product is about something on sale.
/// </para>
/// <para>
/// It is limited on every side a page could push it: how many blocks, how large and how deep each is.
/// A block that can not be read says nothing, and a malformed one is common enough that nobody needs
/// telling.
/// </para>
/// </remarks>
internal static class StructuredData
{
    /// <summary>How many of a page's blocks are looked at, whatever each turns out to hold.</summary>
    public const int MostBlocks = 10;

    private const int LargestBlock = 128 * 1024;
    private const string TypeProperty = "@type";
    private const string GraphProperty = "@graph";
    private const string HeadlineProperty = "headline";
    private const string PublishedProperty = "datePublished";

    private static readonly JsonDocumentOptions Reading = new()
    {
        MaxDepth = 32,
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Article and everything schema.org derives from it, as a page may name any of them.</summary>
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> ArticleFamily = Family(
    [
        "Article",
        "AdvertiserContentArticle",
        "NewsArticle",
        "AnalysisNewsArticle",
        "AskPublicNewsArticle",
        "BackgroundNewsArticle",
        "OpinionNewsArticle",
        "ReportageNewsArticle",
        "ReviewNewsArticle",
        "Report",
        "SatiricalArticle",
        "ScholarlyArticle",
        "MedicalScholarlyArticle",
        "SocialMediaPosting",
        "BlogPosting",
        "LiveBlogPosting",
        "DiscussionForumPosting",
        "TechArticle",
        "APIReference",
    ]);

    /// <summary>
    /// What a page calls itself for being a list of others. Its galleries are left out, because schema.org derives
    /// them from this and a post can describe a gallery it holds.
    /// </summary>
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> CollectionFamily = Family(
    [
        "CollectionPage",
    ]);

    /// <summary>Product and those schema.org derives from it that a shop lists its goods as.</summary>
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> ProductFamily = Family(
    [
        "Product",
        "IndividualProduct",
        "ProductCollection",
        "ProductGroup",
        "ProductModel",
        "SomeProducts",
    ]);

    /// <summary>
    /// Reads what a page's blocks of structured data say.
    /// </summary>
    /// <param name="blocks">The text of each, as far as they are looked at.</param>
    /// <returns>What they say, which is nothing at all where there is none.</returns>
    public static StructuredFacts Read(IEnumerable<string> blocks) =>
        blocks.Aggregate(default(StructuredFacts), ReadBlock);

    /// <summary>
    /// Whether a type names an article, written out as an address or not.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>Whether it does.</returns>
    public static bool NamesAnArticle(ReadOnlySpan<char> type) => ArticleFamily.Contains(WithoutItsNamespace(type));

    private static FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Family(string[] types) =>
        types.ToFrozenSet(StringComparer.OrdinalIgnoreCase).GetAlternateLookup<ReadOnlySpan<char>>();

    private static StructuredFacts ReadBlock(StructuredFacts sofar, string text)
    {
        if (text.Length > LargestBlock)
        {
            return sofar;
        }

        try
        {
            using var json = JsonDocument.Parse(text, Reading);

            return Add(sofar, json.RootElement);
        }
        catch (JsonException)
        {
            return sofar;
        }
        catch (ArgumentException)
        {
            // Text that can not be turned into what the parser reads is not what it says it is either.
            return sofar;
        }
    }

    private static StructuredFacts Add(StructuredFacts sofar, JsonElement node) =>
        node.ValueKind switch
        {
            JsonValueKind.Array => node.EnumerateArray().Aggregate(sofar, Add),
            JsonValueKind.Object => AddDescription(sofar, node),
            _ => sofar,
        };

    private static StructuredFacts AddDescription(StructuredFacts sofar, JsonElement node)
    {
        var said = sofar with
        {
            NamesACollection = sofar.NamesACollection || IsOneOf(node, CollectionFamily),
            NamesAProduct = sofar.NamesAProduct || IsOneOf(node, ProductFamily),
        };

        if (IsOneOf(node, ArticleFamily))
        {
            return said.IsArticle ? said : DescribingAnArticle(said, node);
        }

        return node.TryGetProperty(GraphProperty, out var graph) ? Add(said, graph) : said;
    }

    private static StructuredFacts DescribingAnArticle(StructuredFacts said, JsonElement node) =>
        said with
        {
            IsArticle = true,
            Headline = WebUtility.HtmlDecode(TextOf(node, HeadlineProperty)),
            DatePublished = TextOf(node, PublishedProperty),
        };

    private static bool IsOneOf(JsonElement node, FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> family)
    {
        if (!node.TryGetProperty(TypeProperty, out var type))
        {
            return false;
        }

        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Any(each => IsTypeIn(each, family))
            : IsTypeIn(type, family);
    }

    private static bool IsTypeIn(JsonElement type, FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> family)
    {
        var name = ReadText(type);

        return name is not null && family.Contains(WithoutItsNamespace(name));
    }

    private static ReadOnlySpan<char> WithoutItsNamespace(ReadOnlySpan<char> type) =>
        type[(type.LastIndexOfAny(['/', ':', '#']) + 1)..];

    private static string? TextOf(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) ? ReadText(value) : null;

    /// <summary>
    /// Reads text, where it is text. An escape can stand for half a character, which is valid in the
    /// format and can not become a string, and either way it is not something anybody meant.
    /// </summary>
    private static string? ReadText(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// What a page's structured data says.
/// </summary>
/// <param name="IsArticle">Whether it describes an article.</param>
/// <param name="Headline">What the first such description calls the article.</param>
/// <param name="DatePublished">The date it gives for it, as it was written.</param>
/// <param name="NamesACollection">Whether anything it describes is a page collecting others.</param>
/// <param name="NamesAProduct">Whether anything it describes is a product.</param>
internal readonly record struct StructuredFacts(
    bool IsArticle,
    string? Headline,
    string? DatePublished,
    bool NamesACollection,
    bool NamesAProduct);
