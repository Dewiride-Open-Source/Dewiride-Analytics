using System.Text;
using System.Xml;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers what is let through to the XML reader: where a stretch begins, where it ends and how long it may be.
/// </summary>
public sealed class MarkupGuardStreamTests
{
    private const int Longest = MarkupGuardStream.LongestStretch;
    private const string Section = "<![CDATA[";
    private const string SectionEnd = "]]>";

    /// <summary>How many bytes each read asks for, which has to make no difference to what is watched for.</summary>
    public static TheoryData<int> ReadSizes => [1, 3, 7, 4096, 2 * Longest];

    /// <summary>Everything a read can come back with comes back as it was.</summary>
    /// <param name="size">How many bytes each read asks for.</param>
    [Theory]
    [MemberData(nameof(ReadSizes))]
    public async Task What_Goes_Through_Comes_Out_As_It_Was(int size)
    {
        var text = Bytes("<urlset><url><loc><![CDATA[https://example.com/]]></loc></url></urlset>");

        (await ThroughAsync(text, size)).Should().Equal(text);
    }

    /// <summary>
    /// The longest stretch that is let through is exactly that long. Past it is a file nothing in a sitemap could
    /// account for, and the reader says of it what it says of any other file that is not one.
    /// </summary>
    /// <param name="size">How many bytes each read asks for.</param>
    [Theory]
    [MemberData(nameof(ReadSizes))]
    public async Task A_Stretch_May_Be_As_Long_As_The_Limit_And_No_Longer(int size)
    {
        await ThroughAsync(Bytes("<" + new string(' ', Longest)), size);

        await RefusedAsync(Bytes("<" + new string(' ', Longest + 1)), size);
    }

    /// <summary>A file that has yet to say anything is held to the same.</summary>
    [Fact]
    public async Task A_Stretch_At_The_Very_Beginning_Counts()
    {
        await ThroughAsync(Bytes(new string(' ', Longest)), 4096);

        await RefusedAsync(Bytes(new string(' ', Longest + 1)), 4096);
    }

    /// <summary>
    /// A file is as long as it likes. What has a limit is how far it goes between tags, and nothing that is
    /// inside a tag may be a <c>&lt;</c>.
    /// </summary>
    [Fact]
    public async Task Each_Opening_Starts_The_Count_Again()
    {
        var stretch = "<" + new string(' ', Longest);

        await ThroughAsync(Bytes(stretch + stretch + stretch), 4096);
    }

    /// <summary>
    /// A marked-out section can hold any number of what would otherwise start the count again, and the XML reader
    /// holds the whole of it, so it is counted from where it opens.
    /// </summary>
    /// <param name="size">How many bytes each read asks for.</param>
    [Theory]
    [MemberData(nameof(ReadSizes))]
    public async Task A_Section_Full_Of_Openings_Is_Held_To_The_Limit_All_The_Same(int size)
    {
        await RefusedAsync(Bytes(Section + new string('<', Longest + 1)), size);
    }

    /// <summary>
    /// Once a section has closed it is behind the reader, and however long the next stretch is has nothing to do
    /// with it. The closing of one takes three bytes, which can arrive however they like.
    /// </summary>
    /// <param name="size">How many bytes each read asks for.</param>
    [Theory]
    [MemberData(nameof(ReadSizes))]
    public async Task A_Section_That_Has_Closed_Is_Not_Counted_Any_Longer(int size)
    {
        var section = Section + new string('<', Longest - SectionEnd.Length) + SectionEnd;

        await ThroughAsync(Bytes(section + section + new string(' ', Longest)), size);
    }

    /// <summary>A section closes where its closing is written, however many brackets come before.</summary>
    /// <param name="closing">How it closes.</param>
    [Theory]
    [InlineData("]]>")]
    [InlineData("]]]>")]
    [InlineData("]]]]]>")]
    public async Task A_Section_Closes_However_Many_Brackets_Come_Before_It(string closing)
    {
        var section = Section + "text" + closing;

        await ThroughAsync(Bytes(section + "<" + new string(' ', Longest)), 4096);
    }

    /// <summary>What looks like the closing of one, and is not, leaves it open.</summary>
    /// <param name="notClosing">What is written.</param>
    [Theory]
    [InlineData("]>")]
    [InlineData("] ]>")]
    [InlineData("]]x>")]
    public async Task A_Section_Does_Not_Close_On_Something_That_Only_Looks_Like_It(string notClosing)
    {
        await RefusedAsync(Bytes(Section + notClosing + new string('<', Longest)), 4096);
    }

    /// <summary>What only looks like the opening of one is not one, and starts the count again as any other tag.</summary>
    /// <param name="notOpening">What is written.</param>
    [Theory]
    [InlineData("<![CDAT")]
    [InlineData("<![cdata[")]
    [InlineData("<! [CDATA[")]
    [InlineData("<<![CDAT[")]
    public async Task Something_That_Only_Looks_Like_A_Section_Opening_Is_Not_One(string notOpening)
    {
        var file = notOpening + new string('x', 16) + "<" + new string(' ', Longest) + "<" + new string(' ', Longest);

        await ThroughAsync(Bytes(file), 4096);
    }

    /// <summary>An opening of one that begins straight after a false start still opens one.</summary>
    [Fact]
    public async Task A_Section_Opening_Straight_After_A_False_Start_Is_One()
    {
        await RefusedAsync(Bytes("<![CDAT" + Section + new string('<', Longest + 1)), 4096);
    }

    /// <summary>
    /// A file can name a charset that spreads each character out, and the XML reader will read it. What is being
    /// looked for is the same bytes with zero bytes between them.
    /// </summary>
    /// <param name="charset">The charset.</param>
    [Theory]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public async Task Charsets_That_Spread_Their_Characters_Out_Are_Looked_Past(string charset)
    {
        var encoding = Encoding.GetEncoding(charset);

        // The section's own zero bytes count as well, which only makes it refused the sooner.
        await RefusedAsync(encoding.GetBytes(Section + new string('<', Longest)), 4096);

        var closes = encoding.GetBytes(Section + new string('<', 8) + SectionEnd);

        await ThroughAsync([.. closes, .. encoding.GetBytes("<" + new string(' ', (Longest / encoding.GetByteCount(" ")) - 2))], 4096);
    }

    /// <summary>
    /// The XML reader asks one way and anything else reading a stream can ask another, and a stream asked the
    /// other way must not forget to look.
    /// </summary>
    [Fact]
    public void A_Blocking_Read_Is_Watched_As_Well()
    {
        using var guard = new MarkupGuardStream(new MemoryStream(Bytes(new string(' ', Longest + 1))));

        guard.Invoking(stream => stream.ReadExactly(new byte[Longest + 1])).Should().Throw<XmlException>();
    }

    /// <summary>It only ever looks. Whoever handed it the file is the one to close that.</summary>
    [Fact]
    public async Task What_It_Reads_From_Stays_Open()
    {
        var file = new MemoryStream(Bytes("<urlset></urlset>"));

        await new MarkupGuardStream(file).DisposeAsync();

        file.CanRead.Should().BeTrue();
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static async Task<byte[]> ThroughAsync(byte[] file, int size)
    {
        await using var guard = new MarkupGuardStream(new MemoryStream(file));
        using var output = new MemoryStream();
        var buffer = new byte[size];

        for (
            var read = await guard.ReadAsync(buffer, Cancellation.Token);
            read > 0;
            read = await guard.ReadAsync(buffer, Cancellation.Token))
        {
            await output.WriteAsync(buffer.AsMemory(0, read), Cancellation.Token);
        }

        return output.ToArray();
    }

    private static async Task RefusedAsync(byte[] file, int size)
    {
        var reading = () => ThroughAsync(file, size);

        await reading.Should().ThrowAsync<XmlException>();
    }
}
