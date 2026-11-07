using System.Globalization;
using System.Text;
using System.Xml;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers reading a file of a sitemap, which was written by whoever runs the website and which nothing
/// promises is a sitemap, or well formed, or finished.
/// </summary>
public sealed class SitemapDocumentTests
{
    private const string Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    [Fact]
    public async Task Reads_The_Pages_A_Sitemap_Lists()
    {
        using var document = await OpenAsync($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="{Namespace}">
              <url>
                <loc>https://example.com/first</loc>
                <lastmod>2024-05-06</lastmod>
                <changefreq>weekly</changefreq>
                <priority>0.5</priority>
              </url>
              <url><loc>https://example.com/second</loc></url>
            </urlset>
            """);

        document.Kind.Should().Be(SitemapRootKind.UrlSet);

        (await EntriesOfAsync(document)).Should().Equal(
            new SitemapEntry("https://example.com/first", new DateTimeOffset(2024, 5, 6, 0, 0, 0, TimeSpan.Zero)),
            new SitemapEntry("https://example.com/second", null));
    }

    [Fact]
    public async Task Reads_The_Parts_An_Index_Lists()
    {
        using var document = await OpenAsync($"""
            <sitemapindex xmlns="{Namespace}">
              <sitemap>
                <loc>https://example.com/post-sitemap.xml</loc>
                <lastmod>2024-05-06T10:30:00+00:00</lastmod>
              </sitemap>
              <sitemap><loc>https://example.com/page-sitemap.xml</loc></sitemap>
            </sitemapindex>
            """);

        document.Kind.Should().Be(SitemapRootKind.Index);

        (await EntriesOfAsync(document)).Select(entry => entry.Location).Should().Equal(
            "https://example.com/post-sitemap.xml",
            "https://example.com/page-sitemap.xml");
    }

    /// <summary>
    /// An entity that expands a thousand times over, a thousand times over, is what a document type is
    /// for as far as anybody writing one at a reader is concerned. It goes nowhere: the file is refused
    /// on its first line, before there is anything to expand.
    /// </summary>
    [Fact]
    public async Task A_Document_Type_Declaration_Is_Refused()
    {
        using var document = await OpenAsync("""
            <?xml version="1.0"?>
            <!DOCTYPE urlset [
              <!ENTITY a "aaaaaaaaaa">
              <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">
              <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">
            ]>
            <urlset><url><loc>&c;</loc></url></urlset>
            """);

        document.Kind.Should().BeNull();
        (await EntriesOfAsync(document)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_External_Entity_Is_Never_Resolved()
    {
        await using var website = await LoopbackWebsite.StartAsync();
        using var document = await OpenAsync($"""
            <?xml version="1.0"?>
            <!DOCTYPE urlset [ <!ENTITY secret SYSTEM "http://127.0.0.1:{website.Port}/secret"> ]>
            <urlset><url><loc>&secret;</loc></url></urlset>
            """);

        document.Kind.Should().BeNull();
        (await EntriesOfAsync(document)).Should().BeEmpty();
        website.Seen.Should().BeEmpty();
    }

    [Fact]
    public async Task An_Entity_Nobody_Declared_Fails_The_File()
    {
        using var document = await OpenAsync($"""
            <urlset xmlns="{Namespace}"><url><loc>https://example.com/a&undeclared;</loc></url></urlset>
            """);

        await EntriesFailingOfAsync<XmlException>(document);
    }

    /// <summary>
    /// What a website's answer arrives through stops at its limit, whatever is reading it, and partway
    /// through a document as readily as before one has begun.
    /// </summary>
    /// <param name="limit">The limit, past the first read of the file and short of it.</param>
    [Theory]
    [InlineData(16 * 1024)]
    [InlineData(1024)]
    public async Task A_Document_Longer_Than_The_Limit_Is_Refused(int limit)
    {
        var body = new LimitedReadStream(new MemoryStream(Encoding.UTF8.GetBytes(Pages(2000))), limit, Cancellation.Token);

        await FluentActions
            .Invoking(async () =>
            {
                using var document = await SitemapDocument.OpenAsync(body);

                await EntriesOfAsync(document);
            })
            .Should().ThrowAsync<ResponseTooLargeException>();
    }

    [Fact]
    public async Task No_More_Than_Fifty_Thousand_Entries_Are_Read()
    {
        using var document = await OpenAsync(Pages(SitemapDocument.MostEntries + 1));

        (await EntriesOfAsync(document)).Should().HaveCount(SitemapDocument.MostEntries);
        document.ListsMoreThanIsRead.Should().BeTrue();
    }

    /// <summary>
    /// What the XML reader keeps goes with what is in a file and not with how long it is. A file well
    /// inside its limit, built of nothing but elements left open, took three gibibytes when this was
    /// measured, and one of nothing but attributes more than one. A memory stream answers at once, so all
    /// of this runs on the one thread being measured.
    /// </summary>
    /// <param name="shape">What the file is made of.</param>
    [Theory]
    [InlineData(MemoryShape.ElementsLeftOpenInTheFile)]
    [InlineData(MemoryShape.ElementsLeftOpenInAnEntry)]
    [InlineData(MemoryShape.ElementsLeftOpenInAnAddress)]
    [InlineData(MemoryShape.AttributesOnOneElement)]
    [InlineData(MemoryShape.NamesNeverRepeated)]
    public async Task A_File_Built_To_Use_Up_Memory_Is_Refused_Before_It_Does(MemoryShape shape)
    {
        var xml = Encoding.UTF8.GetBytes(FileOf(shape, 10 * 1024 * 1024));
        var before = GC.GetAllocatedBytesForCurrentThread();

        using var document = await SitemapDocument.OpenAsync(new MemoryStream(xml));

        await EntriesFailingOfAsync<XmlException>(document);

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(4 * 1024 * 1024);
    }

    /// <summary>
    /// The same on the first element of the file, where it comes to a file that is not a sitemap.
    /// </summary>
    [Fact]
    public async Task A_First_Element_Built_To_Use_Up_Memory_Is_Not_A_Sitemap()
    {
        var attributes = string.Concat(Enumerable.Range(0, 2000).Select(attribute => $" a{attribute}=''"));

        using var document = await OpenAsync($"<urlset{attributes}/>");

        document.Kind.Should().BeNull();
    }

    /// <summary>
    /// What publishers add to an entry is well inside what is allowed, in any combination of the ones
    /// anybody uses.
    /// </summary>
    [Fact]
    public async Task What_Publishers_Add_To_An_Entry_Is_Passed_Over_Without_Complaint()
    {
        using var document = await OpenAsync($"""
            <urlset xmlns="{Namespace}"
                    xmlns:image="http://www.google.com/schemas/sitemap-image/1.1"
                    xmlns:video="http://www.google.com/schemas/sitemap-video/1.1"
                    xmlns:news="http://www.google.com/schemas/sitemap-news/0.9"
                    xmlns:xhtml="http://www.w3.org/1999/xhtml">
              <url>
                <loc>https://example.com/page</loc>
                <xhtml:link rel="alternate" hreflang="de" href="https://example.com/de/page"/>
                <image:image><image:loc>https://example.com/a.png</image:loc><image:title>A</image:title></image:image>
                <video:video>
                  <video:title>V</video:title>
                  <video:price currency="EUR">1.50</video:price>
                  <video:restriction relationship="allow">IE GB</video:restriction>
                </video:video>
                <news:news>
                  <news:publication><news:name>N</news:name><news:language>en</news:language></news:publication>
                  <news:publication_date>2024-05-06</news:publication_date>
                  <news:title>T</news:title>
                </news:news>
              </url>
            </urlset>
            """);

        (await EntriesOfAsync(document)).Should().Equal(new SitemapEntry("https://example.com/page", null));
    }

    [Fact]
    public async Task Exactly_Fifty_Thousand_Entries_Is_Not_More_Than_Is_Read()
    {
        using var document = await OpenAsync(Pages(SitemapDocument.MostEntries));

        (await EntriesOfAsync(document)).Should().HaveCount(SitemapDocument.MostEntries);
        document.ListsMoreThanIsRead.Should().BeFalse();
    }

    /// <summary>
    /// The limit is on entries, and it costs a file the same whether or not they have anything in
    /// them, so a file of empty entries is stopped as soon as one of pages is.
    /// </summary>
    [Fact]
    public async Task An_Entry_With_Nothing_In_It_Counts_Towards_The_Limit()
    {
        var empties = string.Concat(Enumerable.Repeat("<url/>", SitemapDocument.MostEntries));
        using var document = await OpenAsync(
            $"""<urlset xmlns="{Namespace}">{empties}<url><loc>https://example.com/late</loc></url></urlset>""");

        (await EntriesOfAsync(document)).Should().BeEmpty();
        document.ListsMoreThanIsRead.Should().BeTrue();
    }

    /// <summary>
    /// Forty million bytes of one value goes through a small buffer and is never gathered into a string.
    /// A memory stream answers at once, so all of this runs on the one thread being measured.
    /// </summary>
    [Fact]
    public async Task A_Value_Too_Long_To_Be_An_Address_Is_Skipped_Without_Being_Held()
    {
        var tooLong = new string('a', MarkupGuardStream.LongestStretch - 1024);
        var xml = Encoding.UTF8.GetBytes($"""
            <urlset xmlns="{Namespace}">
              <url><loc>https://example.com/{tooLong}</loc></url>
              <url><loc>https://example.com/next</loc></url>
            </urlset>
            """);

        var before = GC.GetAllocatedBytesForCurrentThread();
        using var document = await SitemapDocument.OpenAsync(new MemoryStream(xml));
        var entries = await EntriesOfAsync(document);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        entries.Select(entry => entry.Location).Should().Equal("https://example.com/next");
        allocated.Should().BeLessThan(4 * 1024 * 1024);
    }

    /// <summary>
    /// The XML reader holds the whole of each of these before it hands anything over, however it is asked, and
    /// reads so much more into memory than the file holds that a single 50 MiB tag came to 300 MiB, and a
    /// declaration to 629 MiB. All of them are a file no sitemap comes near being, and none gets that far.
    /// </summary>
    /// <param name="shape">What the file holds, with the stretch's place marked.</param>
    [Theory]
    [InlineData("<urlset><url{0}><loc>https://example.com/a</loc></url></urlset>")]
    [InlineData("<urlset><url a=\"{0}\"><loc>https://example.com/a</loc></url></urlset>")]
    [InlineData("<urlset><{0}/></urlset>")]
    [InlineData("<urlset><url><loc>https://example.com/a</loc></url{0}></urlset>")]
    [InlineData("<?xml version=\"1.0\"{0}?><urlset></urlset>")]
    [InlineData("<urlset{0}></urlset>")]
    [InlineData("<urlset><url><loc><![CDATA[{0}]]></loc></url></urlset>")]
    [InlineData("<urlset><url><a><![CDATA[{0}]]></a></url></urlset>")]
    public async Task What_The_Xml_Reader_Would_Hold_Whole_Is_Refused_Before_It_Has(string shape)
    {
        var stretch = new string(shape.Contains("CDATA", StringComparison.Ordinal) ? '<' : ' ', 20 * 1024 * 1024);
        var xml = Encoding.UTF8.GetBytes(string.Format(CultureInfo.InvariantCulture, shape, stretch));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var refused = await RefusedAsync(xml);

        refused.Should().BeTrue();
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(16 * 1024 * 1024);
    }

    /// <summary>The same in a charset that spreads each character out, which the reader can name for itself.</summary>
    [Fact]
    public async Task What_The_Xml_Reader_Would_Hold_Whole_Is_Refused_In_Any_Charset_It_Reads()
    {
        var xml = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes($"<urlset><url><loc><![CDATA[{new string('<', 10 * 1024 * 1024)}]]></loc></url></urlset>"))
            .ToArray();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var refused = await RefusedAsync(xml);

        refused.Should().BeTrue();
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(16 * 1024 * 1024);
    }

    /// <summary>
    /// Text is not held at all, as long as it is, and it gets the same answer all the same. A value that goes
    /// on this long is a file that is not a sitemap, and what it listed before that stands.
    /// </summary>
    [Fact]
    public async Task A_Value_Longer_Than_Anything_In_A_Sitemap_Stops_The_File_And_What_Came_Before_Stands()
    {
        var tooLong = new string('a', MarkupGuardStream.LongestStretch + 1);
        using var document = await OpenAsync($"""
            <urlset xmlns="{Namespace}">
              <url><loc>https://example.com/first</loc></url>
              <url><loc>https://example.com/{tooLong}</loc></url>
              <url><loc>https://example.com/never-reached</loc></url>
            </urlset>
            """);

        var entries = new List<SitemapEntry>();

        await FluentActions
            .Invoking(async () =>
            {
                await foreach (var entry in document.EntriesAsync(Cancellation.Token))
                {
                    entries.Add(entry);
                }
            })
            .Should().ThrowAsync<XmlException>();

        entries.Select(entry => entry.Location).Should().Equal("https://example.com/first");
    }

    /// <summary>
    /// Long, and with every line of it a tag that means something to someone. Publishers do write this kind of
    /// thing, and nothing in it comes near the limit however large the file gets.
    /// </summary>
    [Fact]
    public async Task A_File_With_Nothing_Unusual_About_It_But_Its_Length_Is_Read()
    {
        var caption = new string('a', 64 * 1024);
        var xml = new StringBuilder($"""<urlset xmlns="{Namespace}" xmlns:image="http://www.google.com/schemas/sitemap-image/1.1">""");

        for (var page = 0; page < 100; page++)
        {
            xml.Append("\n  <url><loc>https://example.com/").Append(page)
                .Append("</loc><image:image><image:caption><![CDATA[").Append(caption)
                .Append("]]></image:caption></image:image></url>");
        }

        using var document = await OpenAsync(xml.Append("</urlset>").ToString());

        (await EntriesOfAsync(document)).Should().HaveCount(100);
    }

    [Fact]
    public async Task The_Longest_Address_Kept_Is_Read_And_Anything_Longer_Is_Not()
    {
        var longest = "https://example.com/" + new string('a', Site.MaxSitemapAddressLength - 20);
        using var document = await OpenAsync($"""
            <urlset xmlns="{Namespace}">
              <url><loc>{longest}</loc></url>
              <url><loc>{longest}a</loc></url>
            </urlset>
            """);

        (await EntriesOfAsync(document)).Select(entry => entry.Location).Should().Equal(longest);
    }

    [Theory]
    [InlineData("2024", "2024-01-01T00:00:00+00:00")]
    [InlineData("2024-05", "2024-05-01T00:00:00+00:00")]
    [InlineData("2024-05-06", "2024-05-06T00:00:00+00:00")]
    [InlineData("2024-05-06T10:30+05:30", "2024-05-06T05:00:00+00:00")]
    [InlineData("2024-05-06T10:30Z", "2024-05-06T10:30:00+00:00")]
    [InlineData("2024-05-06T10:30:15+05:30", "2024-05-06T05:00:15+00:00")]
    [InlineData("2024-05-06T10:30:15Z", "2024-05-06T10:30:15+00:00")]
    [InlineData("2024-05-06T10:30:15.5-07:00", "2024-05-06T17:30:15.5+00:00")]
    [InlineData("2024-05-06T10:30:15.123456Z", "2024-05-06T10:30:15.123456+00:00")]
    public async Task Lastmod_In_Every_W3C_Form_Is_Read(string written, string expected)
    {
        using var document = await OpenAsync($"""
            <urlset xmlns="{Namespace}">
              <url><loc>https://example.com/a</loc><lastmod>{written}</lastmod></url>
            </urlset>
            """);

        var entry = (await EntriesOfAsync(document)).Should().ContainSingle().Which;

        entry.LastModified.Should().Be(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// When something changed is only ever a reason to look again. A file that says it nonsensically
    /// has still said where the page is.
    /// </summary>
    /// <param name="written">What it says.</param>
    [Theory]
    [InlineData("yesterday")]
    [InlineData("06/05/2024")]
    [InlineData("")]
    public async Task A_Lastmod_That_Can_Not_Be_Read_Is_The_Same_As_None(string written)
    {
        using var document = await OpenAsync($"""
            <urlset xmlns="{Namespace}">
              <url><loc>https://example.com/a</loc><lastmod>{written}</lastmod></url>
            </urlset>
            """);

        (await EntriesOfAsync(document)).Should().Equal(new SitemapEntry("https://example.com/a", null));
    }

    [Theory]
    // A news feed of two kinds.
    [InlineData("<rss version=\"2.0\"><channel><link>https://example.com/</link></channel></rss>")]
    [InlineData("<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry/></feed>")]
    // Not a sitemap's root, whatever it holds.
    [InlineData("<urlsets><url><loc>https://example.com/a</loc></url></urlsets>")]
    [InlineData("<URLSET><url><loc>https://example.com/a</loc></url></URLSET>")]
    // A web page, with and without a document type.
    [InlineData("<!DOCTYPE html><html><body>Hello</body></html>")]
    [InlineData("<html><body>Hello</body></html>")]
    // Not even markup.
    [InlineData("{\"urlset\": []}")]
    [InlineData("https://example.com/a\nhttps://example.com/b")]
    [InlineData("")]
    [InlineData("<")]
    public async Task Something_That_Is_Not_A_Sitemap_Reads_As_Not_A_Sitemap(string body)
    {
        using var document = await OpenAsync(body);

        document.Kind.Should().BeNull();
        (await EntriesOfAsync(document)).Should().BeEmpty();
    }

    [Fact]
    public async Task Entries_Escaped_As_The_Protocol_Requires_Are_Read_Unescaped()
    {
        using var document = await OpenAsync($"""
            <urlset xmlns="{Namespace}">
              <url><loc>https://example.com/a?b=1&amp;c=2&apos;&quot;&gt;&lt;</loc></url>
              <url><loc><![CDATA[https://example.com/d?e=1&f=2]]></loc></url>
              <url><loc>https://example.com/g<![CDATA[?h=1&i=2]]></loc></url>
              <url><loc>
                  https://example.com/indented
              </loc></url>
            </urlset>
            """);

        (await EntriesOfAsync(document)).Select(entry => entry.Location).Should().Equal(
            "https://example.com/a?b=1&c=2'\"><",
            "https://example.com/d?e=1&f=2",
            "https://example.com/g?h=1&i=2",
            "https://example.com/indented");
    }

    /// <summary>
    /// The protocol says which namespace to declare, and plenty of generators do something else. The
    /// shape of the file is what says it is a sitemap.
    /// </summary>
    /// <param name="opening">The root's opening tag.</param>
    /// <param name="closing">Its closing tag.</param>
    /// <param name="entry">An entry, in the form that root's namespace needs.</param>
    [Theory]
    [InlineData("<urlset>", "</urlset>", "<url><loc>https://example.com/a</loc></url>")]
    [InlineData(
        "<urlset xmlns=\"https://www.sitemaps.org/schemas/sitemap/0.9\">",
        "</urlset>",
        "<url><loc>https://example.com/a</loc></url>")]
    [InlineData(
        "<sm:urlset xmlns:sm=\"http://www.sitemaps.org/schemas/sitemap/0.9\">",
        "</sm:urlset>",
        "<sm:url><sm:loc>https://example.com/a</sm:loc></sm:url>")]
    public async Task A_Sitemap_Without_A_Namespace_Is_Still_Read(string opening, string closing, string entry)
    {
        using var document = await OpenAsync(opening + entry + closing);

        document.Kind.Should().Be(SitemapRootKind.UrlSet);
        (await EntriesOfAsync(document)).Select(read => read.Location).Should().Equal("https://example.com/a");
    }

    /// <summary>
    /// A picture's address is called <c>loc</c> as well, one level down and in another namespace. Read
    /// as the page's, a website's pictures would become its posts.
    /// </summary>
    [Fact]
    public async Task What_Else_An_Entry_Holds_Is_Not_Read_As_The_Entry()
    {
        using var document = await OpenAsync($"""
            <urlset xmlns="{Namespace}"
                    xmlns:image="http://www.google.com/schemas/sitemap-image/1.1"
                    xmlns:news="http://www.google.com/schemas/sitemap-news/0.9"
                    xmlns:other="https://example.com/other">
              <url>
                <image:image><image:loc>https://example.com/photo.png</image:loc></image:image>
                <loc>https://example.com/page</loc>
                <news:news><news:publication_date>2020-01-01</news:publication_date></news:news>
                <other:loc>https://example.com/not-the-page</other:loc>
                <other:lastmod>2020-01-01</other:lastmod>
              </url>
              <image:image><image:loc>https://example.com/stray.png</image:loc></image:image>
              <somethingelse><loc>https://example.com/stray</loc></somethingelse>
            </urlset>
            """);

        (await EntriesOfAsync(document)).Should().Equal(new SitemapEntry("https://example.com/page", null));
    }

    [Theory]
    [InlineData("<url/>")]
    [InlineData("<url></url>")]
    [InlineData("<url><loc/></url>")]
    [InlineData("<url><loc></loc></url>")]
    [InlineData("<url><loc>   </loc></url>")]
    [InlineData("<url><lastmod>2024-05-06</lastmod></url>")]
    public async Task An_Entry_With_No_Address_Is_Passed_Over(string entry)
    {
        using var document = await OpenAsync(
            $"""<urlset xmlns="{Namespace}">{entry}<url><loc>https://example.com/next</loc></url></urlset>""");

        (await EntriesOfAsync(document)).Select(read => read.Location).Should().Equal("https://example.com/next");
    }

    [Theory]
    [InlineData("<urlset/>")]
    [InlineData("<urlset></urlset>")]
    [InlineData("<sitemapindex/>")]
    public async Task A_File_Listing_Nothing_Is_Still_A_Sitemap(string body)
    {
        using var document = await OpenAsync(body);

        document.Kind.Should().NotBeNull();
        (await EntriesOfAsync(document)).Should().BeEmpty();
    }

    /// <summary>
    /// A file that stops partway is still a file that listed what came first, and that a reading can
    /// tell from a file that finished: the exception is what says so.
    /// </summary>
    /// <param name="ending">How the file goes on after its first entry.</param>
    [Theory]
    [InlineData("<url><loc>https://example.com/never")]
    [InlineData("<url><loc>a</wrong></url></urlset>")]
    [InlineData("&")]
    [InlineData("")]
    public async Task A_File_That_Breaks_Hands_Over_What_Came_Before_The_Break(string ending)
    {
        using var document = await OpenAsync(
            $"""<urlset xmlns="{Namespace}"><url><loc>https://example.com/first</loc></url>{ending}""");

        var read = new List<SitemapEntry>();

        await FluentActions
            .Invoking(async () =>
            {
                await foreach (var entry in document.EntriesAsync(Cancellation.Token))
                {
                    read.Add(entry);
                }
            })
            .Should().ThrowAsync<XmlException>();

        read.Select(entry => entry.Location).Should().Equal("https://example.com/first");
    }

    /// <summary>
    /// The protocol asks for UTF-8 and the world does not always oblige. A file that names its charset
    /// is read in it, whatever has or has not been read before it.
    /// </summary>
    [Fact]
    public async Task A_File_That_Names_Another_Charset_Is_Read_In_It()
    {
        var xml = Encoding.Latin1.GetBytes(
            $"""<?xml version="1.0" encoding="windows-1252"?><urlset xmlns="{Namespace}"><url><loc>https://example.com/café</loc></url></urlset>""");
        using var document = await SitemapDocument.OpenAsync(new MemoryStream(xml));

        (await EntriesOfAsync(document)).Select(entry => entry.Location).Should().Equal("https://example.com/café");
    }

    [Fact]
    public async Task Whoever_Stops_Waiting_Stops_It_Between_Entries()
    {
        using var stopping = new CancellationTokenSource();
        using var document = await OpenAsync(Pages(10));

        await FluentActions
            .Invoking(async () =>
            {
                await foreach (var _ in document.EntriesAsync(stopping.Token))
                {
                    await stopping.CancelAsync();
                }
            })
            .Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// What a file is made of, where it has been built to cost whatever reads it.
    /// </summary>
    public enum MemoryShape
    {
        /// <summary>Elements left open directly in the file.</summary>
        ElementsLeftOpenInTheFile,

        /// <summary>Elements left open in an entry.</summary>
        ElementsLeftOpenInAnEntry,

        /// <summary>Elements left open in an entry's address.</summary>
        ElementsLeftOpenInAnAddress,

        /// <summary>One element with attributes all the way to the end of the file.</summary>
        AttributesOnOneElement,

        /// <summary>Empty elements, each with a name of its own.</summary>
        NamesNeverRepeated,
    }

    private static string FileOf(MemoryShape shape, int length)
    {
        var xml = new StringBuilder(length + 64);

        xml.Append(shape switch
        {
            MemoryShape.AttributesOnOneElement => "<urlset><url",
            MemoryShape.ElementsLeftOpenInAnEntry => "<urlset><url>",
            MemoryShape.ElementsLeftOpenInAnAddress => "<urlset><url><loc>",
            _ => "<urlset>",
        });

        var count = 0;

        while (xml.Length < length)
        {
            xml.Append(shape switch
            {
                MemoryShape.AttributesOnOneElement => $" a{count}=''",
                MemoryShape.NamesNeverRepeated => $"<a{count}/>",
                _ => "<a>",
            });

            count++;
        }

        return xml.ToString();
    }

    private static string Pages(int count)
    {
        var xml = new StringBuilder($"""<urlset xmlns="{Namespace}">""");

        for (var page = 0; page < count; page++)
        {
            xml.Append("<url><loc>https://example.com/").Append(page).Append("</loc></url>");
        }

        return xml.Append("</urlset>").ToString();
    }

    private static Task<SitemapDocument> OpenAsync(string xml) =>
        SitemapDocument.OpenAsync(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static async Task<List<SitemapEntry>> EntriesOfAsync(SitemapDocument document)
    {
        var entries = new List<SitemapEntry>();

        await foreach (var entry in document.EntriesAsync(Cancellation.Token))
        {
            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Whether a file was refused, in whichever of the two ways it can be: at its first element, as not a
    /// sitemap, or partway through.
    /// </summary>
    private static async Task<bool> RefusedAsync(byte[] xml)
    {
        using var document = await SitemapDocument.OpenAsync(new MemoryStream(xml));

        if (document.Kind is null)
        {
            return true;
        }

        try
        {
            await EntriesOfAsync(document);

            return false;
        }
        catch (XmlException)
        {
            return true;
        }
    }

    private static async Task EntriesFailingOfAsync<TFailure>(SitemapDocument document)
        where TFailure : Exception
    {
        await FluentActions
            .Invoking(() => EntriesOfAsync(document))
            .Should().ThrowAsync<TFailure>();
    }
}
