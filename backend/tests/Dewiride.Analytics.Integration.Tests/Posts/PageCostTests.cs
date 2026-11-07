using System.Globalization;
using System.Text;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Xunit.v3;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Groups what measures how much of a processor something took, which only the process as a whole can be
/// asked, so nothing else may be running.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class AloneOnTheProcessorDefinition
{
    /// <summary>Name the collection is referenced by.</summary>
    public const string Name = "alone-on-the-processor";
}

/// <summary>
/// Covers what a page can make the reader spend. Any account can name a sitemap on a server it runs. The
/// first dozen shapes here are what can hold a parser building a document for ever, take gigabytes of memory
/// from it, or overflow its stack, in a page far smaller than the most that is read.
/// </summary>
/// <remarks>
/// A page costs what it takes to go through it, however it is written. These bounds are many times what any
/// of them takes, even with the compiler still warming up on a loaded machine, and many times less than what
/// a return to a cost that grows faster than the page would come to.
/// </remarks>
[Collection(AloneOnTheProcessorDefinition.Name)]
public sealed class PageCostTests
{
    private const int LargestPage = 512 * 1024;
    private const string Html = "text/html; charset=utf-8";
    private const string Statement = """<html><head><meta property="og:title" content="Survived"></head><body>""";

    /// <summary>A combining mark drawn above a letter, which Unicode sorts after the one below.</summary>
    private const char MarkAbove = (char)0x0301;

    /// <summary>A combining mark drawn below a letter.</summary>
    private const char MarkBelow = (char)0x0323;

    private const int MarksOfEachKind = 262_000;

    private static readonly TimeSpan MostProcessor = TimeSpan.FromSeconds(3);
    private static readonly long MostAllocated = 128L * 1024 * 1024;
    private static readonly long MostAllocatedForOneTag = 8L * 1024 * 1024;
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, Func<string>> Shapes = new()
    {
        ["pre left open"] = () => Fill("<pre>"),
        ["pre left open, ten thousand"] = () => Repeated("<pre>", 10_000),
        ["listing left open"] = () => Fill("<listing>"),
        ["listing left open, ten thousand"] = () => Repeated("<listing>", 10_000),
        ["pre with an attribute"] = () => Fill("<pre a=b>"),
        ["frames nested forty levels"] = () => FramesWithin(40),
        ["frames nested a hundred levels"] = () => FramesWithin(100),
        ["open lists in a frame's text"] = () => "<iframe srcdoc=\"" + Fill("&lt;ul&gt;&lt;li&gt;") + "\">",
        ["formatting in a frame's text"] = () =>
            "<iframe srcdoc=\"" + Repeated("&lt;b&gt;", 1000) + Repeated("&lt;p&gt;x", 40_000) + "\">",
        ["formatting copied for every paragraph"] = () => FormattingThenParagraphs(500, 0, 9000),
        ["formatting with many attributes"] = () => FormattingThenParagraphs(500, 200, 9000),
        ["a few formatting tags with very many attributes"] = () => FormattingThenParagraphs(100, 950, 9700),
        ["lists left open"] = () => Fill("<ul><li>"),
        ["sections left open"] = () => Fill("<section>"),
        ["divisions left open"] = () => Fill("<div>"),
        ["one tag of different attributes"] = () => OneTagOf(number => $"a{number}=\"b\""),
        ["one tag of one attribute repeated"] = () => OneTagOf(_ => "a=b"),
        ["one tag of an attribute that is read, repeated"] = () => OneTagOf(_ => "class=b"),
        ["one tag of attributes that are read, in turn"] = () =>
            OneTagOf(number => number % 2 == 0 ? "class=a" : "content=b"),
        ["one tag of repeats, and then others"] = () =>
            "<a " + Repeated("class ", 80_000) + Repeated("a ", 40_000) + ">",
        ["one attribute the size of the page"] = () => "<a b=\"" + Fill("x") + "\">",
        ["one attribute's name the size of the page"] = () => "<a " + Fill("x") + ">",
        ["one tag's name the size of the page"] = () => "<" + Fill("a") + ">",
        ["one class the size of the page"] = () => "<a class=\"" + Fill("x ") + "\">",
        ["end tags with nothing to close"] = () => Fill("</q>"),
        ["end tags closing what is open"] = () => Fill("<q></q>"),
        ["end tags closing all that is open"] = () => Repeated("<q>", 4000) + Fill("<r></q><q>"),
        ["empty end tags"] = () => Fill("</>"),
        ["comment endings that are not"] = () => "<!-- " + Fill("--!"),
        ["comment endings with more dashes"] = () => "<!-- " + Fill("---!"),
        ["dashes in a comment"] = () => "<!-- " + Fill("-"),
        ["comments"] = () => Fill("<!---->"),
        ["bogus comments"] = () => Fill("<!x>"),
        ["character references with long names"] = () => Fill("&aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
        ["character references with long numbers"] = () => Fill("&#1111111111111111111111111111111111"),
        ["one character reference the size of the page"] = () => "&#" + Fill("1"),
        ["character references in an attribute"] = () => "<a class=\"" + Fill("&notin") + "\">",
        ["script text full of end tags"] = () => "<script>" + Fill("</a"),
        ["script text full of comment openings"] = () => "<script>" + Fill("<!--"),
        ["script text escaped twice"] = () => "<script><!--" + Fill("<script>"),
        ["script text nearly ending"] = () => "<script>" + Fill("</scrip"),
        ["scripts, one after another"] = () => Fill("<script></script>"),
        ["title text full of end tags"] = () => "<title>" + Fill("</a"),
        ["style text full of end tags"] = () => "<style>" + Fill("</a"),
        ["less-than signs"] = () => Fill("<"),
        ["dates that nearly read"] = () =>
            Fill("""<i itemprop="datePublished" content="2024-05-06T10:30:00.1234567+05:30xxxxxxxxxx">"""),
        ["dates that read and are not believed"] = () => Fill("""<i itemprop="datePublished" content="1980-01-01">"""),
        ["printed dates, over and over"] = () =>
            Fill("""<div class="wp-block-post-date"><time datetime="2024-05-06"></time></div>"""),
        ["entries, over and over"] = () => Fill("""<i class="hentry" itemtype="BlogPosting">"""),
        ["structured data of many descriptions"] = () =>
            """<script type="application/ld+json">[""" + Fill("""{"@type":"a"},""") + "{}]</script>",
        ["many blocks of structured data"] = () => Fill("""<script type="application/ld+json">{}</script>"""),
        ["svg left open with tags closing themselves"] = () => "<svg>" + Fill("<a/>"),
        ["templates left open"] = () => Fill("<template>"),
    };

    /// <summary>The name of each shape.</summary>
    public static TheoryData<string> ShapeNames => [.. Shapes.Keys];

    /// <summary>
    /// How much of a processor something took means nothing with other tests running beside it.
    /// </summary>
    [Fact]
    public void Nothing_Else_Runs_While_These_Measure()
    {
        var collection = TestContext.Current.TestCollection.Should().BeAssignableTo<ICoreTestCollection>().Subject;

        collection.DisableParallelization.Should().BeTrue();
    }

    /// <summary>
    /// The whole of reading a page, on a small stack, in a moment of a processor and a modest amount of
    /// memory that is all short-lived. What the page says above all that is still read.
    /// </summary>
    /// <param name="shape">The name of the shape.</param>
    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void A_Page_Made_To_Be_Expensive_Costs_What_It_Takes_To_Read_It(string shape)
    {
        var (facts, processor, allocated) = Measure(Encoding.UTF8.GetBytes(Statement + Shapes[shape]()), Html);

        facts.Title.Should().Be("Survived");
        processor.Should().BeLessThan(MostProcessor);
        allocated.Should().BeLessThan(MostAllocated);
    }

    /// <summary>
    /// What one tag holds has a limit, whatever is written on it, because only the attributes that are read
    /// are kept, and only the first of each name. So a page that is one tag costs the same as a page that is
    /// one long word. The first reading of each pays for whatever is made once.
    /// </summary>
    /// <param name="shape">The name of the shape.</param>
    [Theory]
    [InlineData("one tag of different attributes")]
    [InlineData("one tag of one attribute repeated")]
    [InlineData("one tag of an attribute that is read, repeated")]
    [InlineData("one tag of attributes that are read, in turn")]
    [InlineData("one tag of repeats, and then others")]
    public void What_One_Tag_Holds_Has_A_Limit_Whatever_Is_Written_On_It(string shape)
    {
        var page = Encoding.UTF8.GetBytes(Statement + Shapes[shape]());

        Measure(page, Html);

        var (_, _, allocated) = Measure(page, Html);

        allocated.Should().BeLessThan(MostAllocatedForOneTag);
    }

    /// <summary>
    /// A title of marks in descending order takes normalising the square of its length, in code nothing can
    /// cancel, so a page's time limit passes unnoticed. Not much of one is ever worked through, wherever on
    /// the page it is. A run of marks with no letter to go on is one character, too long to keep.
    /// </summary>
    /// <param name="markup">The page, with the title's place marked.</param>
    [Theory]
    [InlineData("<title>{0}</title>")]
    [InlineData("""<meta property="og:title" content="{0}">""")]
    [InlineData("""<meta name="twitter:title" content="{0}">""")]
    public void A_Title_Made_To_Be_Expensive_To_Normalise_Costs_Next_To_Nothing(string markup)
    {
        var page = string.Format(CultureInfo.InvariantCulture, markup, DescendingMarks());

        var (facts, processor, _) = Measure(Encoding.UTF8.GetBytes(page), Html);

        facts.Title.Should().BeNull();
        processor.Should().BeLessThan(MostProcessor);
    }

    [Fact]
    public void A_Headline_Made_To_Be_Expensive_To_Normalise_Costs_Next_To_Nothing()
    {
        var headline = new string(MarkAbove, 30_000) + new string(MarkBelow, 30_000);
        var page = $$"""<script type="application/ld+json">{"@type":"Article","headline":"{{headline}}"}</script>""";

        var (facts, processor, _) = Measure(Encoding.UTF8.GetBytes(page), Html);

        facts.Title.Should().BeNull();
        processor.Should().BeLessThan(MostProcessor);
    }

    /// <summary>
    /// The same title where each mark is a single byte, so that a page of the most that is read holds as
    /// many of them as it can.
    /// </summary>
    [Fact]
    public void A_Title_In_A_Charset_Of_One_Byte_Marks_Costs_Next_To_Nothing()
    {
        var page = "<title>"u8.ToArray()
            .Concat(Enumerable.Repeat((byte)0xEC, MarksOfEachKind))
            .Concat(Enumerable.Repeat((byte)0xF2, MarksOfEachKind))
            .ToArray();

        var (facts, processor, _) = Measure(page, "text/html; charset=windows-1258");

        facts.Title.Should().BeNull();
        processor.Should().BeLessThan(MostProcessor);
    }

    private static (PageFacts Facts, TimeSpan Processor, long Allocated) Measure(byte[] page, string contentType)
    {
        var processorBefore = Environment.CpuUsage.TotalTime;

        var (facts, allocated) = SmallStack.Run(() =>
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var read = PageMetadata.Read(page, contentType, TimeZoneInfo.Utc, Now, Cancellation.Token);

            return (read, GC.GetAllocatedBytesForCurrentThread() - before);
        });

        return (facts, Environment.CpuUsage.TotalTime - processorBefore, allocated);
    }

    private static string DescendingMarks() =>
        new string(MarkAbove, MarksOfEachKind) + new string(MarkBelow, MarksOfEachKind);

    private static string Fill(string each) => Repeated(each, (LargestPage / each.Length) + 1)[..LargestPage];

    private static string Repeated(string each, int times) =>
        new StringBuilder(each.Length * times).Insert(0, each, times).ToString();

    /// <summary>
    /// An inline frame whose text is an inline frame, with each level's text escaped once more, and no space for
    /// its unquoted value to end at.
    /// </summary>
    private static string FramesWithin(int levels)
    {
        var inner = "x";

        for (var level = 0; level < levels; level++)
        {
            var escaped = inner
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);

            inner = $"<iframe/srcdoc={escaped}>";
        }

        return inner;
    }

    /// <summary>
    /// Formatting tags told apart by an attribute, so that a parser keeping them keeps them all, and paragraphs
    /// of text, each of which has one that keeps them copy all of them again.
    /// </summary>
    private static string FormattingThenParagraphs(int tags, int attributes, int paragraphs)
    {
        var page = new StringBuilder("<p>");

        for (var tag = 0; tag < tags; tag++)
        {
            page.Append("<b id=").Append(tag);

            for (var attribute = 0; attribute < attributes; attribute++)
            {
                page.Append(" a").Append(attribute);
            }

            page.Append('>');
        }

        page.Append("</p>");

        return page.Insert(page.Length, "<p>x", paragraphs).ToString();
    }

    private static string OneTagOf(Func<int, string> attribute)
    {
        var page = new StringBuilder("<a", LargestPage + 64);
        var number = 0;

        while (page.Length < LargestPage)
        {
            page.Append(' ').Append(attribute(number++));
        }

        return page.Append('>').ToString();
    }
}
