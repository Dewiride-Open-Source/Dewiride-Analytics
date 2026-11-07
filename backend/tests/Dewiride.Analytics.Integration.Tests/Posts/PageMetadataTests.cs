using System.Globalization;
using System.Text;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers what a website's page says about itself, and what it can do to whatever reads it.
/// </summary>
public sealed class PageMetadataTests
{
    private const string Earlier = "2024-05-06T10:30:00Z";
    private const string Later = "2024-06-07T11:45:00Z";

    private static readonly DateTimeOffset EarlierMoment = new(2024, 5, 6, 10, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// The present, handed to the reader rather than read off the machine, so that what counts as
    /// the future here is the same on every run.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_Open_Graph_Title_Is_Preferred()
    {
        var facts = Read($"""
            <html>
              <head>
                <title>The document</title>
                <meta name="twitter:title" content="The card">
                <meta property="og:title" content="The graph">
                <script type="application/ld+json">{Article(headline: "The headline")}</script>
              </head>
            </html>
            """);

        facts.Title.Should().Be("The graph");
        facts.TitleSource.Should().Be(PostTitleSource.OpenGraph);
    }

    [Fact]
    public void The_Structured_Data_Headline_Comes_Next()
    {
        var facts = Read($"""
            <html>
              <head>
                <title>The document</title>
                <meta name="twitter:title" content="The card">
                <script type="application/ld+json">{Article(headline: "The headline")}</script>
              </head>
            </html>
            """);

        facts.Title.Should().Be("The headline");
        facts.TitleSource.Should().Be(PostTitleSource.StructuredData);
    }

    [Fact]
    public void The_Twitter_Title_Comes_After_That()
    {
        var facts = Read("""
            <html>
              <head>
                <title>The document</title>
                <meta name="twitter:title" content="The card">
              </head>
            </html>
            """);

        facts.Title.Should().Be("The card");
        facts.TitleSource.Should().Be(PostTitleSource.Twitter);
    }

    [Fact]
    public void The_Document_Title_Is_The_Last_Resort()
    {
        var facts = Read("<html><head><title>The document</title></head></html>");

        facts.Title.Should().Be("The document");
        facts.TitleSource.Should().Be(PostTitleSource.Document);
    }

    /// <summary>
    /// A page has one title, and it is the first it gives, as a browser has it, even where that holds nothing.
    /// </summary>
    /// <param name="titles">The titles.</param>
    /// <param name="expected">What it is called.</param>
    [Theory]
    [InlineData("<title>First</title><title>Second</title>", "First")]
    [InlineData("<title></title><title>Second</title>", null)]
    public void The_First_Title_Is_The_Only_One_There_Is(string titles, string? expected)
    {
        Read(titles).Title.Should().Be(expected);
    }

    /// <summary>
    /// A title that comes to nothing has said nothing, so whatever is next gets to speak.
    /// </summary>
    [Fact]
    public void A_Title_With_Nothing_In_It_Gives_Way_To_The_Next()
    {
        var facts = Read("""
            <html>
              <head>
                <title>The document</title>
                <meta property="og:title" content="&#x200B;&#x202E;">
                <meta name="twitter:title" content="   ">
              </head>
            </html>
            """);

        facts.Title.Should().Be("The document");
        facts.TitleSource.Should().Be(PostTitleSource.Document);
    }

    /// <summary>
    /// A headline in structured data is a string in a script, so nothing has decoded its entities, and
    /// plenty of tools write them there. Once, because a title that really mentions an entity would
    /// otherwise lose it.
    /// </summary>
    [Fact]
    public void A_Headline_Is_Decoded_Once()
    {
        var facts = Read(Page(Article(headline: "Fish &amp; chips, &amp;amp; more")));

        facts.Title.Should().Be("Fish & chips, &amp; more");
    }

    [Fact]
    public void The_Article_Publish_Time_Is_Preferred()
    {
        var facts = Read($"""
            <html>
              <head>
                <meta property="article:published_time" content="{Earlier}">
                <meta itemprop="datePublished" content="{Later}">
                <script type="application/ld+json">{Article(published: Later)}</script>
              </head>
            </html>
            """);

        facts.PublishedAt.Should().Be(EarlierMoment);
        facts.PublishedSource.Should().Be(PostDateSource.OpenGraph);
    }

    [Fact]
    public void A_Date_That_Can_Not_Be_Believed_Gives_Way_To_The_Next()
    {
        var facts = Read($"""
            <html>
              <head>
                <meta property="article:published_time" content="whenever">
                <script type="application/ld+json">{Article(published: "2099-01-01")}</script>
                <meta itemprop="datePublished" content="{Earlier}">
              </head>
            </html>
            """);

        facts.PublishedAt.Should().Be(EarlierMoment);
        facts.PublishedSource.Should().Be(PostDateSource.Microdata);
    }

    /// <summary>
    /// A date alone can only mean a day on the website's own calendar, which is why it takes a zone
    /// at all.
    /// </summary>
    [Fact]
    public void A_Date_With_No_Offset_Is_Read_In_The_Websites_Zone()
    {
        var kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

        var facts = Read(
            """<html><head><meta property="article:published_time" content="2024-05-06"></head></html>""",
            zone: kolkata);

        facts.PublishedAt.Should().Be(new DateTimeOffset(2024, 5, 5, 18, 30, 0, TimeSpan.Zero));
    }

    [Theory]
    // The shapes search engines ask for.
    [InlineData("""{"@context":"https://schema.org","@type":"Article","headline":"H","datePublished":"2024-05-06T10:30:00Z"}""")]
    [InlineData("""{"@context":"https://schema.org","@type":"BlogPosting","headline":"H","datePublished":"2024-05-06T10:30:00Z"}""")]
    [InlineData("""{"@type":"NewsArticle","headline":"H","datePublished":"2024-05-06T10:30:00Z"}""")]
    [InlineData("""{"@type":"TechArticle","headline":"H","datePublished":"2024-05-06T10:30:00Z"}""")]
    // A type written out as an address, or as one of several.
    [InlineData("""{"@type":"https://schema.org/BlogPosting","headline":"H","datePublished":"2024-05-06T10:30:00Z"}""")]
    [InlineData("""{"@type":"schema:BlogPosting","headline":"H","datePublished":"2024-05-06T10:30:00Z"}""")]
    [InlineData("""{"@type":["WebPage","Article"],"headline":"H","datePublished":"2024-05-06T10:30:00Z"}""")]
    // In a list, and beside things that are not it.
    [InlineData("""[{"@type":"Article","headline":"H","datePublished":"2024-05-06T10:30:00Z"}]""")]
    [InlineData("""[{"@type":"WebSite","name":"W"},{"@type":"Article","headline":"H","datePublished":"2024-05-06T10:30:00Z"}]""")]
    // With what a parser that is not picky lets through.
    [InlineData("""{"@type":"Article","headline":"H","datePublished":"2024-05-06T10:30:00Z",}""")]
    [InlineData("""/* made by a plugin */ {"@type":"Article","headline":"H","datePublished":"2024-05-06T10:30:00Z"}""")]
    public void Structured_Data_In_A_Script_Block_Is_Read(string json)
    {
        var facts = Read(Page(json));

        facts.Title.Should().Be("H");
        facts.TitleSource.Should().Be(PostTitleSource.StructuredData);
        facts.PublishedAt.Should().Be(EarlierMoment);
        facts.PublishedSource.Should().Be(PostDateSource.StructuredData);
        facts.PageType.Should().Be(PostPageType.Article);
    }

    [Fact]
    public void A_Block_That_Names_Its_Charset_Is_Still_A_Block()
    {
        var facts = Read(
            $"""<html><head><script type="application/ld+json; charset=utf-8">{Article(headline: "H")}</script></head></html>""");

        facts.Title.Should().Be("H");
    }

    /// <summary>
    /// How Yoast and Rank Math write it: everything about the page in one graph, with the page and its
    /// website alongside the article and dated as well.
    /// </summary>
    [Fact]
    public void Structured_Data_In_A_Graph_Is_Read()
    {
        var facts = Read(Page($$"""
            {
              "@context": "https://schema.org",
              "@graph": [
                { "@type": "WebSite", "name": "The website" },
                { "@type": "WebPage", "name": "The page", "datePublished": "{{Later}}" },
                { "@type": "Article", "headline": "The article", "datePublished": "{{Earlier}}" }
              ]
            }
            """));

        facts.Title.Should().Be("The article");
        facts.PublishedAt.Should().Be(EarlierMoment);
    }

    /// <summary>
    /// A page describing its own website first is what the first block usually is, and only an article
    /// counts as the page's description of itself.
    /// </summary>
    [Fact]
    public void A_Later_Block_Describing_An_Article_Is_Found()
    {
        var facts = Read(
            Page("""{"@type":"WebSite","name":"W"}""", Article(headline: "The article", published: Earlier)));

        facts.Title.Should().Be("The article");
        facts.PublishedAt.Should().Be(EarlierMoment);
    }

    /// <summary>
    /// A post can describe others it links to, in a block or a graph of their own, and what it describes first
    /// is what it is talking about.
    /// </summary>
    [Fact]
    public void The_First_Article_Described_Is_The_One_That_Counts()
    {
        var inOneGraph = Read(Page($$"""
            {"@graph":[{"@type":"Article","headline":"First","datePublished":"{{Earlier}}"},
            {"@type":"Article","headline":"Second","datePublished":"{{Later}}"}]}
            """));
        var inTwoBlocks = Read(
            Page(Article(headline: "First", published: Earlier), Article(headline: "Second", published: Later)));

        inOneGraph.Title.Should().Be("First");
        inOneGraph.PublishedAt.Should().Be(EarlierMoment);
        inTwoBlocks.Title.Should().Be("First");
        inTwoBlocks.PublishedAt.Should().Be(EarlierMoment);
    }

    /// <summary>
    /// Limited on every side a page could push it. The first of these is an eleventh block, which
    /// is never looked at, and what is said in it is not believed.
    /// </summary>
    [Fact]
    public void No_More_Than_Ten_Blocks_Are_Looked_At()
    {
        var blocks = Enumerable.Repeat("""{"@type":"WebSite"}""", 10)
            .Append(Article(headline: "Too far down"))
            .ToArray();

        var facts = Read(Page(blocks));

        facts.Title.Should().BeNull();
        facts.PageType.Should().BeNull();
    }

    [Fact]
    public void A_Block_Larger_Than_Is_Read_Is_Not_Read()
    {
        var huge = $$"""{"@type":"Article","headline":"Too large","padding":"{{new string('x', 128 * 1024)}}"}""";

        var facts = Read(Page(huge));

        facts.Title.Should().BeNull();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("{\"@type\":\"Article\"")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"Article\"")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"@type\":42,\"headline\":\"H\"}")]
    [InlineData("{\"@type\":[42,null],\"headline\":\"H\"}")]
    [InlineData("{\"@graph\":\"nothing\"}")]
    [InlineData("{\"headline\":\"No type at all\"}")]
    public void Malformed_Structured_Data_Is_Skipped(string json)
    {
        var facts = Read(Page(json));

        facts.Should().Be(new PageFacts(null, null, null, null, null));
    }

    /// <summary>
    /// An article whose values are not text has still said it is an article, and has said nothing else.
    /// The escape is half a character, which is valid in the format and can never become a string.
    /// </summary>
    /// <param name="json">The block.</param>
    [Theory]
    [InlineData("""{"@type":"Article","headline":42,"datePublished":{"not":"text"}}""")]
    [InlineData("""{"@type":"Article","headline":"\ud800","datePublished":"\udc00"}""")]
    public void Values_That_Are_Not_Text_Say_Nothing(string json)
    {
        var facts = Read(Page(json));

        facts.Should().Be(new PageFacts(null, null, null, null, PostPageType.Article));
    }

    [Fact]
    public void Structured_Data_Nested_Too_Deeply_Is_Skipped()
    {
        var deep = string.Concat(Enumerable.Repeat("[", 100_000)) + Article(headline: "H")
            + string.Concat(Enumerable.Repeat("]", 100_000));

        var facts = Read(Page(deep));

        facts.Title.Should().BeNull();
    }

    [Theory]
    // Microdata, in each of the two attributes it can be in.
    [InlineData($"""<meta itemprop="datePublished" content="{Earlier}">""", PostDateSource.Microdata)]
    [InlineData($"""<time itemprop="datePublished" datetime="{Earlier}">May</time>""", PostDateSource.Microdata)]
    [InlineData($"""<span itemprop="headline datePublished" content="{Earlier}"></span>""", PostDateSource.Microdata)]
    // Microformats, new and old, in each of the three places they can be.
    [InlineData($"""<article class="h-entry"><time class="dt-published" datetime="{Earlier}">May</time></article>""", PostDateSource.Microformat)]
    [InlineData($"""<article class="hentry"><abbr class="published" title="{Earlier}">May</abbr></article>""", PostDateSource.Microformat)]
    [InlineData($"""<article class="hentry"><span class="published">{Earlier}</span></article>""", PostDateSource.Microformat)]
    [InlineData($"""<article class="hentry"><time class="entry-date published" datetime="{Earlier}">May</time><time class="updated" datetime="{Later}">June</time></article>""", PostDateSource.Microformat)]
    public void Microdata_And_Microformat_Dates_Are_Read(string markup, PostDateSource source)
    {
        var facts = Read($"<html><body>{markup}</body></html>");

        facts.PublishedAt.Should().Be(EarlierMoment);
        facts.PublishedSource.Should().Be(source);
        facts.PageType.Should().Be(PostPageType.Article);
    }

    /// <summary>
    /// A class of that name outside anything marked as an entry is just a class somebody chose.
    /// </summary>
    [Fact]
    public void A_Date_Marked_As_Published_Outside_An_Entry_Is_Not_Read()
    {
        var facts = Read($"""<html><body><time class="published" datetime="{Earlier}">May</time></body></html>""");

        facts.PublishedAt.Should().BeNull();
    }

    /// <summary>
    /// What WordPress's own Post Date block prints, on a theme with no SEO plugin behind it: the only
    /// date plain WordPress states anywhere on the page.
    /// </summary>
    [Fact]
    public void The_Date_WordPress_Prints_Is_Read_As_The_Last_Resort()
    {
        var facts = Read($"""
            <html><body>
              <div class="wp-block-post-date has-small-font-size"><time datetime="{Earlier}">May 6, 2024</time></div>
            </body></html>
            """);

        facts.PublishedAt.Should().Be(EarlierMoment);
        facts.PublishedSource.Should().Be(PostDateSource.WordPressBlock);
        facts.PageType.Should().Be(PostPageType.Article);
    }

    [Fact]
    public void Anything_A_Page_States_For_Machines_Is_Believed_Over_What_WordPress_Prints()
    {
        var facts = Read($"""
            <html>
              <head><meta property="article:published_time" content="{Earlier}"></head>
              <body><div class="wp-block-post-date"><time datetime="{Later}">June 7, 2024</time></div></body>
            </html>
            """);

        facts.PublishedAt.Should().Be(EarlierMoment);
        facts.PublishedSource.Should().Be(PostDateSource.OpenGraph);
    }

    /// <summary>
    /// When a post was last touched is not when it was published, and the block prints that too, marked
    /// as such. A date a calendar would show as a post's day has to be the day it went out.
    /// </summary>
    [Fact]
    public void The_Date_A_Post_Was_Modified_Is_Not_The_Date_It_Was_Published()
    {
        var facts = Read($"""
            <html><body>
              <div class="wp-block-post-date__modified-date wp-block-post-date"><time datetime="{Later}">June 7, 2024</time></div>
            </body></html>
            """);

        facts.PublishedAt.Should().BeNull();
        facts.PageType.Should().BeNull();
    }

    [Fact]
    public void A_Post_Printing_Both_Its_Dates_Has_The_Date_It_Was_Published_Read()
    {
        var facts = Read($"""
            <html><body>
              <div class="wp-block-post-date"><time datetime="{Earlier}">May 6, 2024</time></div>
              <div class="wp-block-post-date__modified-date wp-block-post-date"><time datetime="{Later}">June 7, 2024</time></div>
            </body></html>
            """);

        facts.PublishedAt.Should().Be(EarlierMoment);
        facts.PublishedSource.Should().Be(PostDateSource.WordPressBlock);
    }

    /// <summary>
    /// A list of related posts prints a date beside each, and there is no telling which is this post's,
    /// so none of them is.
    /// </summary>
    [Fact]
    public void A_Page_Printing_Several_Post_Dates_Has_None_Read()
    {
        var facts = Read($"""
            <html><body>
              <div class="wp-block-post-date"><time datetime="{Earlier}">May 6, 2024</time></div>
              <div class="wp-block-post-date"><time datetime="{Later}">June 7, 2024</time></div>
            </body></html>
            """);

        facts.PublishedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("website")]
    [InlineData("profile")]
    [InlineData("video.other")]
    public void A_Page_Whose_Open_Graph_Type_Is_Not_An_Article_Is_Not_A_Post(string type)
    {
        var facts = Read($"""
            <html>
              <head>
                <meta property="og:type" content="{type}">
                <meta property="article:published_time" content="{Earlier}">
              </head>
            </html>
            """);

        facts.PageType.Should().Be(PostPageType.Other);
    }

    /// <summary>
    /// Yoast and Hugo say this of every page they make, an About page included, so saying it and nothing else
    /// settles nothing. Where a website keeps a file of its posts, such a page still counts as one.
    /// </summary>
    /// <param name="type">How it is written.</param>
    [Theory]
    [InlineData("article")]
    [InlineData("Article")]
    [InlineData(" article ")]
    public void A_Page_Saying_Only_That_It_Is_An_Article_Has_Not_Said_Enough(string type)
    {
        var facts = Read($"""<html><head><meta property="og:type" content="{type}"></head></html>""");

        facts.PageType.Should().BeNull();
    }

    /// <param name="type">How it is written.</param>
    [Theory]
    [InlineData("article")]
    [InlineData("Article")]
    [InlineData(" article ")]
    public void A_Page_Saying_It_Is_An_Article_And_When_It_Was_Published_Is_One(string type)
    {
        var facts = Read($"""
            <html><head>
              <meta property="og:type" content="{type}">
              <meta property="article:published_time" content="{Earlier}">
            </head></html>
            """);

        facts.PageType.Should().Be(PostPageType.Article);
        facts.PublishedAt.Should().Be(EarlierMoment);
    }

    [Fact]
    public void A_Page_Whose_Structured_Data_Says_It_Is_An_Article_Is_One_With_No_Date()
    {
        var facts = Read($"""
            <html><head>
              <meta property="og:type" content="article">
              <script type="application/ld+json">{Article(headline: "H")}</script>
            </head></html>
            """);

        facts.PageType.Should().Be(PostPageType.Article);
        facts.PublishedAt.Should().BeNull();
    }

    /// <summary>
    /// Open Graph implies a page with no type is a website, and most pages with no Open Graph at all
    /// did not mean that.
    /// </summary>
    [Fact]
    public void A_Page_Saying_Nothing_About_Itself_Keeps_No_Title_And_No_Date()
    {
        var facts = Read("<html><body><h1>A heading is not a title</h1><p>Written today.</p></body></html>");

        facts.Should().Be(new PageFacts(null, null, null, null, null));
    }

    [Fact]
    public void The_First_Tag_Of_A_Name_Is_The_One_That_Counts()
    {
        var facts = Read("""
            <html>
              <head>
                <meta property="og:title" content="First">
                <meta property="og:title" content="Second">
              </head>
            </html>
            """);

        facts.Title.Should().Be("First");
    }

    /// <summary>
    /// Pages are not careful which attribute names a tag, or how they spell its name.
    /// </summary>
    /// <param name="tag">The tag.</param>
    [Theory]
    [InlineData("""<meta name="og:title" content="Found">""")]
    [InlineData("""<meta property="OG:TITLE" content="Found">""")]
    [InlineData("""<META PROPERTY="og:title" CONTENT="Found">""")]
    public void A_Tag_Is_Found_However_It_Is_Named(string tag)
    {
        var facts = Read($"<html><head>{tag}</head></html>");

        facts.Title.Should().Be("Found");
    }

    [Fact]
    public void The_Charset_The_Server_Names_Is_Honoured()
    {
        var page = Encoding.Latin1.GetBytes("<html><head><title>Café</title></head></html>");

        var facts = ReadBytes(page, "text/html; charset=windows-1252");

        facts.Title.Should().Be("Café");
    }

    [Fact]
    public void The_Charset_A_Page_Names_Is_Honoured_Where_The_Server_Names_None()
    {
        var page = Encoding.Latin1.GetBytes("""<html><head><meta charset="windows-1252"><title>Caf&#233; Caf""" + "é</title></head></html>");

        var facts = ReadBytes(page, "text/html");

        facts.Title.Should().Be("Café Café");
    }

    [Fact]
    public void A_Byte_Order_Mark_Is_Believed_Over_The_Server()
    {
        byte[] mark = [0xEF, 0xBB, 0xBF];
        var page = mark.Concat(Encoding.UTF8.GetBytes("<html><head><title>Café</title></head></html>")).ToArray();

        var facts = ReadBytes(page, "text/html; charset=windows-1252");

        facts.Title.Should().Be("Café");
    }

    [Theory]
    [InlineData("text/html; charset=klingon")]
    [InlineData("text/html")]
    [InlineData("application/xhtml+xml")]
    [InlineData(null)]
    public void A_Page_Is_Read_Whatever_Is_Said_About_Its_Charset(string? contentType)
    {
        var facts = ReadBytes(Encoding.UTF8.GetBytes("<html><head><title>Café</title></head></html>"), contentType);

        facts.Title.Should().Be("Café");
    }

    /// <summary>
    /// Whatever the page's markup does, what it says is still text. Nothing it contains is run, and a
    /// tag in its title is the tag and not a tag.
    /// </summary>
    [Fact]
    public void What_A_Page_Holds_Stays_What_It_Is()
    {
        var facts = Read("""
            <html>
              <head>
                <title>Why &lt;b&gt; is not <b>strong</b></title>
                <script>document.title = "Rewritten by a script";</script>
              </head>
            </html>
            """);

        facts.Title.Should().Be("Why <b> is not <b>strong</b>");
    }

    /// <summary>
    /// A page is read for what it says and nothing it points at is touched, because every one of those
    /// addresses was chosen by whoever wrote the page, and none of them has been anywhere near the
    /// check of where a connection may go. A frame, a stylesheet, a script, a picture and a refresh are
    /// what a browser would go after without being asked, and a frame's text is a page of its own to one.
    /// </summary>
    [Fact]
    public async Task Nothing_A_Page_Points_At_Is_Fetched()
    {
        await using var website = await LoopbackWebsite.StartAsync();
        var elsewhere = $"http://127.0.0.1:{website.Port}";

        var facts = Read($"""
            <html>
              <head>
                <title>The page</title>
                <base href="{elsewhere}/base/">
                <link rel="stylesheet" href="{elsewhere}/style.css">
                <link rel="preload" href="{elsewhere}/preload.css">
                <link rel="import" href="{elsewhere}/import.html">
                <meta http-equiv="refresh" content="0; url={elsewhere}/refresh">
                <script src="{elsewhere}/script.js"></script>
              </head>
              <body>
                <img src="{elsewhere}/picture.png">
                <iframe src="{elsewhere}/frame.html"></iframe>
                <iframe srcdoc="<img src={elsewhere}/inside-a-frame.png>"></iframe>
                <object data="{elsewhere}/object"></object>
                <embed src="{elsewhere}/embed">
                <video src="{elsewhere}/video" autoplay></video>
                <audio src="{elsewhere}/audio" autoplay></audio>
              </body>
            </html>
            """);

        await Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation.Token);

        facts.Title.Should().Be("The page");
        website.Seen.Should().BeEmpty();
    }

    /// <summary>
    /// A page is gone through in a loop, however deep it nests, so depth has nothing to run out of. A
    /// parser building a tree recurses for each of the later ones here, and overflows in a few kilobytes.
    /// </summary>
    /// <param name="level">The markup repeated for each level.</param>
    [Theory]
    [InlineData("<div>")]
    [InlineData("<b>")]
    [InlineData("<a href=x>")]
    [InlineData("<table><tr><td>")]
    [InlineData("<svg>")]
    [InlineData("<template>")]
    [InlineData("<select><option>")]
    [InlineData("<ul><li>")]
    [InlineData("<pre>")]
    [InlineData("<PRE>")]
    [InlineData("<pre/>")]
    [InlineData("<pre a=b>")]
    [InlineData("<listing>")]
    [InlineData("<listing a=b>")]
    [InlineData("<pre><listing>")]
    public void A_Page_Nested_However_Deep_Is_Read_On_A_Small_Stack(string level)
    {
        var html = $"""
            <html>
              <head><meta property="og:title" content="Survived"></head>
              <body>{string.Concat(Enumerable.Repeat(level, 100_000))}
            """;

        var facts = SmallStack.Run(() => Read(html));

        facts.Title.Should().Be("Survived");
    }

    /// <summary>
    /// The tokenizer calls itself on these, three frames to each, and a run the length of a page overflows
    /// any stack there is. What comes after the run is read as if it had not been there.
    /// </summary>
    /// <param name="start">What the run comes after.</param>
    /// <param name="each">What it is a run of.</param>
    /// <param name="end">What it ends with.</param>
    [Theory]
    [InlineData("", "</>", "")]
    [InlineData("<!-- ", "--!", "-->")]
    [InlineData("<!-- ", "---!", "-->")]
    [InlineData("<!-- ", "------!", "-->")]
    [InlineData("<!-", "--!", "-->")]
    [InlineData("<!--", "--!", "-->")]
    public void A_Run_The_Tokenizer_Would_Call_Itself_On_Is_Read_On_A_Small_Stack(string start, string each, string end)
    {
        var html = new StringBuilder(start)
            .Insert(start.Length, each, (512 * 1024) / each.Length)
            .Append(end)
            .Append("""<meta property="og:title" content="Read">""")
            .ToString();

        var facts = SmallStack.Run(() => Read(html));

        facts.Title.Should().Be("Read");
    }

    /// <summary>
    /// A run short enough to mean anything is left as it is written, in the one place it could matter.
    /// </summary>
    [Fact]
    public void A_Short_Run_Is_Left_As_It_Is_Written()
    {
        var run = string.Concat(Enumerable.Repeat("</>", 16));

        Read($"<title>Code {run} blog</title>").Title.Should().Be($"Code {run} blog");
        Read("<title>Code </> blog</title>").Title.Should().Be("Code </> blog");
    }

    /// <summary>
    /// A title holding the character the tokenizer takes for the end of its text still has the rest of
    /// the page after it.
    /// </summary>
    [Fact]
    public void A_Character_That_Means_The_End_To_The_Tokenizer_Ends_Nothing()
    {
        var facts = Read("<title>Ahead \uFFFFof it</title>" + """<meta property="og:title" content="After it">""");

        facts.Title.Should().Be("After it");
    }

    /// <summary>
    /// A whole page of a tag closing what is open in the wrong order, as much of it as is ever read.
    /// What this is about is how long it takes.
    /// </summary>
    /// <param name="repeated">The markup repeated.</param>
    [Theory]
    [InlineData("<b><i></b></i>")]
    [InlineData("<a><table><a>")]
    [InlineData("x</q>")]
    [InlineData("<html a=b><body c=d>")]
    [InlineData("<p><table>")]
    public void A_Page_Of_Hostile_Markup_Is_Read_In_Good_Time(string repeated)
    {
        var page = new StringBuilder("""<html><head><meta property="og:title" content="Survived"></head><body>""");

        while (page.Length < 512 * 1024)
        {
            page.Append(repeated);
        }

        var began = TimeProvider.System.GetTimestamp();
        var facts = SmallStack.Run(() => Read(page.ToString()));

        facts.Title.Should().Be("Survived");
        TimeProvider.System.GetElapsedTime(began).Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// What a page says is read wherever it says it, however much markup comes first. Going through it costs
    /// what it takes to read it, so nothing has to be left unread to keep that down.
    /// </summary>
    /// <param name="tagsBefore">How many tags come before the one that says something.</param>
    [Theory]
    [InlineData(100)]
    [InlineData(9_999)]
    [InlineData(10_000)]
    [InlineData(50_000)]
    public void A_Page_Is_Read_However_Many_Tags_Come_Before_What_It_Says(int tagsBefore)
    {
        var tags = string.Concat(Enumerable.Repeat("<i>", tagsBefore));

        var facts = Read($"""{tags}<meta property="og:title" content="Found">""");

        facts.Title.Should().Be("Found");
    }

    /// <summary>
    /// A charset where every character is two bytes wide, named by the server. The page itself could not
    /// have said so, because what it says could not have been read without knowing.
    /// </summary>
    /// <param name="charset">The charset it names.</param>
    /// <param name="bigEndian">Which way round its bytes are.</param>
    [Theory]
    [InlineData("utf-16", false)]
    [InlineData("utf-16le", false)]
    [InlineData("utf-16be", true)]
    public void A_Page_In_A_Wider_Charset_Is_Read_Where_The_Server_Names_It(string charset, bool bigEndian)
    {
        var encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        var page = encoding.GetBytes("""<i><meta property="og:title" content="Café">""");

        var facts = ReadBytes(page, $"text/html; charset={charset}");

        facts.Title.Should().Be("Café");
    }

    [Theory]
    // Stops in the middle of a tag, an attribute, a comment and a script.
    [InlineData("""<html><head><meta property="og:title" content="Kept"><title""")]
    [InlineData("""<html><head><meta property="og:title" content="Kept"><meta property="og:ty""")]
    [InlineData("""<html><head><meta property="og:title" content="Kept"><!-- never closed""")]
    [InlineData("""<html><head><meta property="og:title" content="Kept"><script>never closed""")]
    // Nothing a browser would not get through.
    [InlineData("""<meta property="og:title" content="Kept"></html></html></body><p></div></head>""")]
    [InlineData("""<<<<>>>><meta property="og:title" content="Kept">&&&&;;;&#xFFFFFFFF;&#0;&bogus;""")]
    public void A_Page_Cut_Short_Or_Badly_Made_Is_Read_For_What_It_Says(string html)
    {
        var facts = Read(html);

        facts.Title.Should().Be("Kept");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_Corrupt_Page_Says_Nothing_And_Does_No_Harm(int seed)
    {
        var noise = new byte[64 * 1024];

        new Random(seed).NextBytes(noise);

        ReadBytes(noise, "text/html; charset=utf-8");
    }

    [Fact]
    public void An_Empty_Page_Says_Nothing()
    {
        var facts = ReadBytes([], "text/html");

        facts.Should().Be(new PageFacts(null, null, null, null, null));
    }

    /// <summary>
    /// A page takes a moment to read, and the limit a website is on covers that moment too. Reading
    /// stops with it rather than running on with the answer already given up on.
    /// </summary>
    [Fact]
    public async Task A_Reading_Nobody_Is_Waiting_For_Stops()
    {
        using var stopped = new CancellationTokenSource();

        await stopped.CancelAsync();

        var reading = () => PageMetadata.Read(
            Encoding.UTF8.GetBytes("<html></html>"),
            "text/html",
            TimeZoneInfo.Utc,
            Now,
            stopped.Token);

        reading.Should().Throw<OperationCanceledException>();
    }

    /// <summary>
    /// Yoast's page for listing posts says article to Open Graph and collection to its structured data, and a
    /// list has no publish date of its own, whatever comes first on it. The last of these lists articles in
    /// the same graph, which the first thing it says about itself still makes a list.
    /// </summary>
    /// <param name="json">The block.</param>
    [Theory]
    [InlineData("""{"@type":"CollectionPage","name":"Blog"}""")]
    [InlineData("""{"@type":["WebPage","CollectionPage"],"name":"Blog"}""")]
    [InlineData("""{"@type":"https://schema.org/CollectionPage","name":"Blog"}""")]
    [InlineData("""{"@graph":[{"@type":"CollectionPage"},{"@type":"WebSite"}]}""")]
    [InlineData("""{"@graph":[{"@type":"CollectionPage"},{"@type":"BlogPosting","headline":"H","datePublished":"2024-05-06"}]}""")]
    public void A_Collection_Is_Not_An_Article_And_States_No_Date_Of_Its_Own(string json)
    {
        var facts = Read($"""
            <html><head>
              <meta property="og:type" content="article">
              <meta property="article:published_time" content="{Earlier}">
              <script type="application/ld+json">{json}</script>
            </head><body>{ThemePages.ClassicEntries(Earlier)}</body></html>
            """);

        facts.PageType.Should().Be(PostPageType.Other);
        facts.PublishedAt.Should().BeNull();
        facts.PublishedSource.Should().BeNull();
    }

    /// <summary>
    /// A post can describe a gallery it holds, and that does not make it a list.
    /// </summary>
    [Fact]
    public void An_Article_Holding_A_Gallery_Is_An_Article()
    {
        var facts = Read(Page(Article(headline: "A post", published: Earlier), """{"@type":"ImageGallery"}"""));

        facts.PageType.Should().Be(PostPageType.Article);
        facts.PublishedAt.Should().Be(EarlierMoment);
    }

    /// <summary>
    /// A shop's products say article to Open Graph under Yoast as well, and the structured data of whatever
    /// runs the shop says what they are. A review on one is dated, and that does not make it a post.
    /// </summary>
    /// <param name="type">What it says it is.</param>
    [Theory]
    [InlineData("Product")]
    [InlineData("https://schema.org/Product")]
    [InlineData("ProductGroup")]
    public void A_Product_Is_Not_An_Article(string type)
    {
        var facts = Read($$"""
            <html><head>
              <meta property="og:type" content="article">
              <script type="application/ld+json">{"@type":"{{type}}","name":"A shoe"}</script>
            </head><body><meta itemprop="datePublished" content="{{Earlier}}"></body></html>
            """);

        facts.PageType.Should().Be(PostPageType.Other);
    }

    /// <summary>
    /// A post reviewing a product can describe both, and it has said outright that it is an article.
    /// </summary>
    [Fact]
    public void An_Article_About_A_Product_Is_An_Article()
    {
        var facts = Read(Page(Article(headline: "A review", published: Earlier), """{"@type":"Product","name":"A shoe"}"""));

        facts.PageType.Should().Be(PostPageType.Article);
        facts.PublishedAt.Should().Be(EarlierMoment);
    }

    /// <summary>
    /// A list marks each of its entries and dates each one, in whichever of these it uses. None of those
    /// dates is its own, and the first of them makes it nothing.
    /// </summary>
    /// <param name="entry">Each entry, with the place of its date marked.</param>
    [Theory]
    [InlineData("""<article class="post hentry"><time class="entry-date published" datetime="{0}">May</time></article>""")]
    [InlineData("""<article class="h-entry"><a class="p-name" href="/p/">A post</a><time class="dt-published" datetime="{0}">May</time></article>""")]
    [InlineData("""<article itemscope itemtype="https://schema.org/BlogPosting"><time itemprop="datePublished" datetime="{0}">May</time></article>""")]
    public void A_Page_Of_Several_Entries_Is_A_List_And_States_No_Date_Of_Its_Own(string entry)
    {
        var first = string.Format(CultureInfo.InvariantCulture, entry, Earlier);
        var second = string.Format(CultureInfo.InvariantCulture, entry, Later);

        var facts = Read($"<html><body>{first}{second}</body></html>");

        facts.PageType.Should().Be(PostPageType.Other);
        facts.PublishedAt.Should().BeNull();
    }

    /// <summary>
    /// WordPress says so itself, in a body class it gives such pages whichever plugins and theme are running.
    /// That also covers one with a single entry, which has nothing to count.
    /// </summary>
    /// <param name="bodyClass">The body's class.</param>
    [Theory]
    [InlineData("blog")]
    [InlineData("home blog wp-theme-whichever hfeed")]
    [InlineData("archive category category-news")]
    [InlineData("archive tag tag-rare")]
    [InlineData("search search-results")]
    public void A_Page_WordPress_Says_Lists_Posts_Is_A_List_And_States_No_Date_Of_Its_Own(string bodyClass)
    {
        var facts = Read($"""<html><body class="{bodyClass}">{ThemePages.ClassicEntries(Earlier)}</body></html>""");

        facts.PageType.Should().Be(PostPageType.Other);
        facts.PublishedAt.Should().BeNull();
    }

    /// <summary>
    /// A theme can print other posts under a post, marked the way it marks its own, and WordPress says which
    /// sort of page this is. The first entry on it is the post's.
    /// </summary>
    [Fact]
    public void A_Post_Listing_Others_Keeps_Its_Own_Date()
    {
        var facts = Read($"""
            <html><body class="post-template-default single single-post">
              {ThemePages.ClassicEntries(Earlier, Later, Later)}
            </body></html>
            """);

        facts.PageType.Should().Be(PostPageType.Article);
        facts.PublishedAt.Should().Be(EarlierMoment);
        facts.PublishedSource.Should().Be(PostDateSource.Microformat);
    }

    /// <summary>
    /// A body class and a count of entries are inferences, and a page stating outright that it is an article
    /// outranks them: plenty of themes that are not WordPress give each section's pages the section's name.
    /// </summary>
    /// <param name="head">What it says of itself.</param>
    /// <param name="bodyClass">The body's class.</param>
    /// <param name="source">Where its date comes from.</param>
    [Theory]
    [InlineData($"""<meta property="article:published_time" content="{Earlier}">""", "blog", PostDateSource.OpenGraph)]
    [InlineData($"""<meta property="article:published_time" content="{Earlier}">""", "", PostDateSource.OpenGraph)]
    [InlineData($$"""<script type="application/ld+json">{"@type":"BlogPosting","datePublished":"{{Earlier}}"}</script>""", "blog", PostDateSource.StructuredData)]
    [InlineData($$"""<script type="application/ld+json">{"@type":"BlogPosting","datePublished":"{{Earlier}}"}</script>""", "", PostDateSource.StructuredData)]
    public void A_Page_Stating_That_It_Is_An_Article_Is_Not_Taken_For_A_List(string head, string bodyClass, PostDateSource source)
    {
        var facts = Read($"""
            <html><head>{head}</head>
            <body class="{bodyClass}">{ThemePages.ClassicEntries(Later, Later)}</body></html>
            """);

        facts.PageType.Should().Be(PostPageType.Article);
        facts.PublishedAt.Should().Be(EarlierMoment);
        facts.PublishedSource.Should().Be(source);
    }

    /// <summary>
    /// A comment marked up the way its post is, inside the post, is not a second entry on the page.
    /// </summary>
    [Fact]
    public void An_Entry_Inside_An_Entry_Is_Not_Another_Entry()
    {
        var facts = Read($"""
            <html><body>
              <article class="h-entry"><time class="dt-published" datetime="{Earlier}">May</time>
                <div class="h-entry"><time class="dt-published" datetime="{Later}">June</time></div>
                <div itemscope itemtype="https://schema.org/BlogPosting">A reply</div>
              </article>
            </body></html>
            """);

        facts.PageType.Should().Be(PostPageType.Article);
        facts.PublishedAt.Should().Be(EarlierMoment);
    }

    /// <summary>
    /// A date is either the page's or it is not, wherever a theme puts it. A list of posts printed under a post
    /// is other posts' dates, so what is left of them is what the page says for itself, one of them or none.
    /// </summary>
    /// <param name="others">How many other posts it lists.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void What_Ipsum_Prints_Is_Read(int others)
    {
        var facts = Read(ThemePages.Ipsum(Earlier, others));

        facts.PublishedAt.Should().Be(EarlierMoment);
        facts.PublishedSource.Should().Be(PostDateSource.WordPressBlock);
        facts.PageType.Should().Be(PostPageType.Article);
    }

    [Fact]
    public void What_Twenty_Twenty_Four_Prints_Is_Read()
    {
        var facts = Read(ThemePages.TwentyTwentyFour(Earlier));

        facts.PublishedAt.Should().Be(EarlierMoment);
        facts.PublishedSource.Should().Be(PostDateSource.WordPressBlock);
    }

    /// <summary>
    /// This theme prints no date for a post at all, only those of the list under it, which can include the
    /// post's own. So the honest answer is that it has none, however many there are.
    /// </summary>
    /// <param name="listed">How many posts it lists.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void What_Twenty_Twenty_Five_Prints_Is_No_Date_At_All(int listed)
    {
        var facts = Read(ThemePages.TwentyTwentyFive(listed));

        facts.PublishedAt.Should().BeNull();
        facts.PageType.Should().BeNull();
    }

    /// <summary>
    /// A tag page on a block theme with one post on it has one date, and that is the post's.
    /// </summary>
    [Fact]
    public void A_List_Of_One_Post_States_No_Date_Of_Its_Own()
    {
        var facts = Read($"""<html><body class="archive tag tag-rare">{ThemePages.ListOfPosts(1)}</body></html>""");

        facts.PublishedAt.Should().BeNull();
        facts.PageType.Should().Be(PostPageType.Other);
    }

    /// <param name="body">What a list of posts holds that a date is in.</param>
    [Theory]
    [InlineData("""<div class="wp-block-query"><div class="wp-block-post-date"><time datetime="2024-05-06">May</time></div></div>""")]
    [InlineData("""<ul class="wp-block-post-template"><li><div class="wp-block-post-date"><time datetime="2024-05-06">May</time></div></li></ul>""")]
    public void A_Date_In_A_List_Of_Posts_Is_Not_Read_Even_Where_It_Is_The_Only_One(string body)
    {
        var facts = Read($"<html><body>{body}</body></html>");

        facts.PublishedAt.Should().BeNull();
        facts.PageType.Should().BeNull();
    }

    /// <summary>
    /// Everything between these is plain text, and what looks like a tag in it is not one.
    /// </summary>
    /// <param name="element">The element.</param>
    [Theory]
    [InlineData("title")]
    [InlineData("textarea")]
    [InlineData("script")]
    [InlineData("style")]
    [InlineData("xmp")]
    [InlineData("iframe")]
    [InlineData("noembed")]
    [InlineData("noframes")]
    public void A_Tag_In_An_Element_Of_Plain_Text_Is_Not_A_Tag(string element)
    {
        var facts = Read($"""
            <{element}><meta property="og:title" content="Not this"></{element}>
            <meta property="og:title" content="This">
            """);

        facts.Title.Should().Be("This");
    }

    [Fact]
    public void Everything_After_Plaintext_Is_Plain_Text()
    {
        var facts = Read("""
            <meta property="og:title" content="This"><plaintext>
            <meta property="og:title" content="Not this"><meta property="article:published_time" content="2024-05-06">
            """);

        facts.Title.Should().Be("This");
        facts.PublishedAt.Should().BeNull();
    }

    [Fact]
    public void A_Tag_In_A_Comment_Is_Not_A_Tag()
    {
        var facts = Read("""
            <!-- <meta property="og:title" content="Not this"> -->
            <!doctype html>
            <![CDATA[ > <meta property="og:title" content="This"> ]]>
            """);

        facts.Title.Should().Be("This", "outside of drawings and formulas a section of that kind is a comment up to its first >");
    }

    /// <summary>
    /// A marked-out section means what it says in a drawing, where what is in it is text, and nowhere else.
    /// </summary>
    [Fact]
    public void A_Tag_In_A_Drawings_Marked_Out_Section_Is_Not_A_Tag()
    {
        var facts = Read("""
            <svg><![CDATA[ a > b <meta property="og:title" content="Not this"> ]]></svg>
            <meta property="og:title" content="This">
            """);

        facts.Title.Should().Be("This");
    }

    /// <summary>
    /// A drawing has a title and a stylesheet of its own, which are read as markup, where a page's own are
    /// text. So an end tag finds its way, and a page's title is the page's.
    /// </summary>
    /// <param name="foreign">The element.</param>
    [Theory]
    [InlineData("svg")]
    [InlineData("math")]
    public void A_Title_In_A_Drawing_Is_The_Drawings(string foreign)
    {
        var facts = Read($"""
            <{foreign}><title>A logo</title><style>.a {"{"} {"}"}</style><script>a</script></{foreign}>
            <title>The page</title>
            """);

        facts.Title.Should().Be("The page");
        facts.TitleSource.Should().Be(PostTitleSource.Document);
    }

    /// <summary>
    /// A drawing with nothing in it is closed by its own tag, and the page goes on as a page.
    /// </summary>
    [Fact]
    public void A_Drawing_Closing_Itself_Is_Closed()
    {
        var facts = Read("<svg/><math/><title>The page</title>");

        facts.Title.Should().Be("The page");
    }

    /// <summary>
    /// Closing itself is something a tag can do in a drawing. On one of the page's own the mark means nothing,
    /// so what it opens is still open, and a script that says so is still a script.
    /// </summary>
    [Fact]
    public void A_Tag_Of_The_Page_Marked_As_Closing_Itself_Stays_Open()
    {
        var facts = Read($"""
            <script src="a.js" /><meta property="og:title" content="Not this"></script>
            <div class="hentry" /><time class="published" datetime="{Earlier}">May</time>
            <svg class="hentry" /><svg><circle class="hentry" /><time class="published" datetime="{Later}">June</time></svg>
            """);

        facts.Title.Should().BeNull();
        facts.PublishedAt.Should().Be(EarlierMoment);
    }

    /// <summary>
    /// What a template holds is for a script to use, and it is no part of the page until one does.
    /// </summary>
    [Fact]
    public void What_Is_In_A_Template_Is_No_Part_Of_The_Page()
    {
        var facts = Read($$"""
            <template>
              <title>Not this</title>
              <meta property="og:title" content="Not this">
              <meta itemprop="datePublished" content="{{Later}}">
              <article class="hentry"><time class="published" datetime="{{Later}}">June</time></article>
              <div class="wp-block-post-date"><time datetime="{{Later}}">June</time></div>
              <script type="application/ld+json">{"@type":"Article","headline":"Not this"}</script>
            </template>
            <title>This</title>
            """);

        facts.Should().Be(new PageFacts("This", PostTitleSource.Document, null, null, null));
    }

    /// <summary>
    /// A date in a microformat can be the element's text, wherever in the element that is, and a page closing
    /// things in the wrong order closes them all the same.
    /// </summary>
    /// <param name="markup">The element.</param>
    [Theory]
    [InlineData("""<article class="hentry"><span class="published"> 2024-05-<b>06</b>T10:<!-- no -->30:00Z </span></article>""")]
    [InlineData("""<article class="hentry"><span class="published">2024-05-06T10:30:00Z</span> and words after it</article>""")]
    [InlineData("""<article class="hentry"><span class="published">2024-05-06T10:30:00Z</article>""")]
    [InlineData("""<article class="hentry"><span class="published"><i>2024-05-06T10:30:00Z""")]
    [InlineData("""<article class="hentry"><span class="published">2024-05-06T10:30:00&#90;</span></article>""")]
    public void A_Date_In_An_Elements_Text_Is_Read_However_It_Is_Written(string markup)
    {
        var facts = Read($"<html><body>{markup}</body></html>");

        facts.PublishedAt.Should().Be(EarlierMoment);
        facts.PublishedSource.Should().Be(PostDateSource.Microformat);
    }

    /// <summary>
    /// An element that can not hold anything has no text, so what follows it is not its date.
    /// </summary>
    [Fact]
    public void An_Element_That_Holds_Nothing_Has_No_Date_In_Its_Text()
    {
        var facts = Read("""<article class="hentry"><img class="published">2024-05-06T10:30:00Z</article>""");

        facts.PublishedAt.Should().BeNull();
    }

    /// <summary>
    /// Past the end of an entry, a date marked as published is just a class somebody chose.
    /// </summary>
    [Fact]
    public void A_Date_After_An_Entry_Has_Closed_Is_Not_The_Entrys()
    {
        var facts = Read($"""
            <html><body>
              <div class="hentry"><p>Words</p></div>
              <time class="published" datetime="{Earlier}">May</time>
            </body></html>
            """);

        facts.PublishedAt.Should().BeNull();
    }

    /// <summary>
    /// A page that goes on after the end of its body has only said where it meant to stop. What follows goes
    /// in the same body, in anything left open.
    /// </summary>
    [Fact]
    public void Whatever_Follows_The_End_Of_A_Body_Is_Still_In_It()
    {
        var facts = Read($"""
            <html><body class="hentry"></body></html>
            <time class="published" datetime="{Earlier}">May</time>
            """);

        facts.PublishedAt.Should().Be(EarlierMoment);
    }

    /// <summary>
    /// An end tag closes the nearest element of its name and everything opened since, and one with nothing to
    /// close closes nothing.
    /// </summary>
    [Fact]
    public void An_End_Tag_Closes_What_It_Names_And_Nothing_Else()
    {
        var kept = Read($"""
            <div class="hentry"></section></p></span><time class="published" datetime="{Earlier}">May</time></div>
            """);

        var closed = Read($"""
            <div class="hentry"><section><span></div><time class="published" datetime="{Earlier}">May</time>
            """);

        kept.PublishedAt.Should().Be(EarlierMoment);
        closed.PublishedAt.Should().BeNull();
    }

    /// <summary>
    /// The same attribute twice on a tag is the first of them, to a browser and here.
    /// </summary>
    [Fact]
    public void The_First_Of_An_Attribute_Named_Twice_Is_The_One_That_Counts()
    {
        var facts = Read("""<meta property="og:title" property="twitter:title" content="First" content="Second">""");

        facts.Title.Should().Be("First");
        facts.TitleSource.Should().Be(PostTitleSource.OpenGraph);
    }

    /// <summary>
    /// A page may state its charset in either of two tags in its first kibibyte, and no harm comes of the
    /// server having named one nobody can read.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <param name="contentType">What the server says.</param>
    [Theory]
    [InlineData("""<meta charset="windows-1251">""", "text/html")]
    [InlineData("""<meta charset=windows-1251>""", null)]
    [InlineData("""<META CHARSET=' WINDOWS-1251 '>""", "text/html; charset=klingon")]
    [InlineData("""<meta http-equiv="Content-Type" content="text/html; charset=windows-1251">""", "text/html")]
    [InlineData("""<meta http-equiv=content-type content='text/html;charset="windows-1251"'>""", "text/html")]
    public void The_Charset_A_Page_States_Is_Believed(string tag, string? contentType)
    {
        var page = Encoding.Latin1.GetBytes(tag + "<title>")
            .Concat<byte>([0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2])
            .ToArray();

        var facts = ReadBytes(page, contentType);

        facts.Title.Should().Be("Привет");
    }

    /// <summary>
    /// The longer form only counts on a tag standing in for the server's header, and one in a comment does not
    /// count at all. The bytes of the title are then read as what nothing else has named.
    /// </summary>
    /// <param name="tag">The tag.</param>
    [Theory]
    [InlineData("""<meta content="text/html; charset=windows-1251">""")]
    [InlineData("""<meta http-equiv="refresh" content="text/html; charset=windows-1251">""")]
    [InlineData("""<!-- <meta charset="windows-1251"> -->""")]
    [InlineData("""<meta charset="&#119;indows-1251">""")]
    public void A_Charset_That_Was_Not_Stated_Is_Not_Believed(string tag)
    {
        var page = Encoding.Latin1.GetBytes(tag + "<title>Café</title>");

        var facts = ReadBytes(page, "text/html");

        facts.Title.Should().Be("Café");
    }

    /// <summary>
    /// The server is believed over the page, and a mark on the bytes is believed over them both.
    /// </summary>
    [Fact]
    public void The_Server_Is_Believed_Over_The_Page()
    {
        var page = Encoding.Latin1.GetBytes("""<meta charset="windows-1251"><title>Caf""" + "é</title>");

        var facts = ReadBytes(page, "text/html; charset=windows-1252");

        facts.Title.Should().Be("Café");
    }

    /// <param name="mark">The mark.</param>
    /// <param name="bigEndian">Which way round the bytes after it are.</param>
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE }, false)]
    [InlineData(new byte[] { 0xFE, 0xFF }, true)]
    public void A_Mark_On_The_Bytes_Says_Which_Way_Round_They_Are(byte[] mark, bool bigEndian)
    {
        var encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        var page = mark.Concat(encoding.GetBytes("<title>Café</title>")).ToArray();

        var facts = ReadBytes(page, "text/html; charset=windows-1252");

        facts.Title.Should().Be("Café");
    }

    /// <summary>
    /// A page written in UTF-16 can not say so in markup that had to be read to find out, and one that does
    /// is really in something else.
    /// </summary>
    [Fact]
    public void A_Page_Saying_It_Is_In_UTF_16_Is_Not()
    {
        var page = Encoding.UTF8.GetBytes("""<meta charset="utf-16"><title>Caf""" + "é</title>");

        var facts = ReadBytes(page, null);

        facts.Title.Should().Be("Café");
    }

    /// <summary>
    /// A charset has to be stated in a page's first kibibyte, so a reader does not have to go looking for it
    /// through a whole page.
    /// </summary>
    [Fact]
    public void A_Charset_Stated_Too_Far_Down_Is_Not_Believed()
    {
        var page = Encoding.Latin1.GetBytes(new string(' ', 1024) + """<meta charset="windows-1251"><title>Caf""" + "é</title>");

        var facts = ReadBytes(page, "text/html");

        facts.Title.Should().Be("Café");
    }

    /// <summary>
    /// A page saying nothing about its charset is in UTF-8 if its bytes are, even where the most that is read
    /// has cut a character in half, and otherwise is what most pages like it are in.
    /// </summary>
    [Fact]
    public void A_Page_Saying_Nothing_Of_Its_Charset_Is_In_What_Its_Bytes_Can_Be()
    {
        var inUtf8 = Encoding.UTF8.GetBytes("<title>Café</title><p>€");
        var cutOff = inUtf8[..^1];
        var inWestern = Encoding.Latin1.GetBytes("<title>Café</title>");

        ReadBytes(inUtf8, "text/html").Title.Should().Be("Café");
        ReadBytes(cutOff, "text/html").Title.Should().Be("Café");
        ReadBytes(inWestern, "text/html").Title.Should().Be("Café");
    }

    /// <summary>
    /// A server can name its charset with any amount of anything around it, and none of it is a reason not
    /// to read the page.
    /// </summary>
    /// <param name="contentType">What it says.</param>
    [Theory]
    [InlineData("text/html;charset=windows-1251")]
    [InlineData("""text/html; CHARSET="WINDOWS-1251" """)]
    [InlineData("text/html; charset = 'windows-1251'; boundary=x")]
    [InlineData("text/html; charset charset=windows-1251")]
    public void The_Charset_The_Server_Names_Is_Found_However_It_Is_Written(string contentType)
    {
        byte[] page = [.. "<title>"u8, 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2];

        var facts = ReadBytes(page, contentType);

        facts.Title.Should().Be("Привет");
    }

    private static string Article(string? headline = null, string? published = null)
    {
        var parts = new List<string> { "\"@type\":\"Article\"" };

        if (headline is not null)
        {
            parts.Add($"\"headline\":\"{headline}\"");
        }

        if (published is not null)
        {
            parts.Add($"\"datePublished\":\"{published}\"");
        }

        return "{" + string.Join(',', parts) + "}";
    }

    private static string Page(params string[] blocks) =>
        "<html><head>"
        + string.Concat(blocks.Select(block => $"""<script type="application/ld+json">{block}</script>"""))
        + "</head></html>";

    private static PageFacts Read(string html, TimeZoneInfo? zone = null) =>
        PageMetadata.Read(
            Encoding.UTF8.GetBytes(html),
            "text/html; charset=utf-8",
            zone ?? TimeZoneInfo.Utc,
            Now,
            Cancellation.Token);

    private static PageFacts ReadBytes(byte[] page, string? contentType) =>
        PageMetadata.Read(page, contentType, TimeZoneInfo.Utc, Now, Cancellation.Token);
}
