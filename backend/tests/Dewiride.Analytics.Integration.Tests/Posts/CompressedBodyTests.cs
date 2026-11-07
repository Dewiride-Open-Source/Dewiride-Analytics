using System.IO.Compression;
using System.Text;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers opening what a website answered with: what it was compressed with, and how much of it may
/// arrive and come of it.
/// </summary>
public sealed class CompressedBodyTests
{
    private const string Text = "<urlset><url><loc>https://example.com/a</loc></url></urlset>";
    private const int OneMebibyte = 1024 * 1024;

    private static readonly BodyLimits Roomy = new(OneMebibyte, OneMebibyte);

    /// <summary>
    /// A compressed sitemap file served as what it is, an <c>.xml.gz</c> with nothing said about it in
    /// the headers, is the most ordinary thing in the world.
    /// </summary>
    [Fact]
    public async Task A_Gzipped_Sitemap_Is_Recognised_By_Its_First_Bytes()
    {
        var body = await OpenAsync(Gzip(Bytes(Text)), [], Roomy, lookInside: true);

        (await ReadAllAsync(body)).Should().Be(Text);
    }

    /// <summary>
    /// Only a sitemap's file is expected to be a compressed file. A page starting with the same two
    /// bytes is just a strange page.
    /// </summary>
    [Fact]
    public async Task A_Body_Is_Not_Looked_Inside_Unless_It_Is_Asked_To_Be()
    {
        var compressed = Gzip(Bytes(Text));
        var body = await OpenAsync(compressed, [], Roomy, lookInside: false);

        (await ReadBytesAsync(body)).Should().Equal(compressed);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("GZIP")]
    [InlineData("x-gzip")]
    [InlineData("br")]
    [InlineData("deflate")]
    [InlineData("identity")]
    public async Task A_Body_Compressed_In_Transit_Is_Expanded(string encoding)
    {
        var body = await OpenAsync(Compressed(Bytes(Text), encoding), [encoding], Roomy, lookInside: false);

        (await ReadAllAsync(body)).Should().Be(Text);
    }

    [Fact]
    public async Task A_Body_Compressed_Twice_Is_Expanded_Twice()
    {
        var body = await OpenAsync(Gzip(Gzip(Bytes(Text))), ["gzip"], Roomy, lookInside: true);

        (await ReadAllAsync(body)).Should().Be(Text);
    }

    /// <summary>
    /// A kilobyte or so that unpacks to ten mebibytes, in each of the places it could be hidden.
    /// </summary>
    /// <param name="encoding">What it says it was compressed with.</param>
    /// <param name="compressions">How many times it was gzipped before that.</param>
    [Theory]
    [InlineData("gzip", 0)]
    [InlineData("br", 0)]
    [InlineData("deflate", 0)]
    [InlineData(null, 1)]
    [InlineData("gzip", 1)]
    public async Task A_Body_That_Expands_Past_The_Limit_Is_Refused(string? encoding, int compressions)
    {
        var bomb = new byte[10 * OneMebibyte];

        for (var layer = 0; layer < compressions; layer++)
        {
            bomb = Gzip(bomb);
        }

        var arriving = encoding is null ? bomb : Compressed(bomb, encoding);

        arriving.Length.Should().BeLessThan(OneMebibyte);

        await using var body = await OpenAsync(
            arriving,
            encoding is null ? [] : [encoding],
            Roomy,
            lookInside: true);

        await body.Invoking(ReadBytesAsync).Should().ThrowAsync<ResponseTooLargeException>();
    }

    /// <summary>
    /// Sections of a compressed body with nothing in them unpack to nothing at all, so the limit on what
    /// comes out never notices, and they can go on for as long as a website likes.
    /// </summary>
    [Fact]
    public async Task Endless_Empty_Gzip_Members_Are_Refused_By_The_Compressed_Limit()
    {
        var endless = Gzipped.EmptySections(OneMebibyte);

        await using var body = await OpenAsync(endless, ["gzip"], new BodyLimits(OneMebibyte / 2, 1024), lookInside: false);

        await body.Invoking(ReadBytesAsync).Should().ThrowAsync<ResponseTooLargeException>();
    }

    [Fact]
    public async Task A_Plain_Body_Past_The_Limit_Is_Refused()
    {
        await using var body = await OpenAsync(new byte[101], [], new BodyLimits(100, 1000), lookInside: false);

        await body.Invoking(ReadBytesAsync).Should().ThrowAsync<ResponseTooLargeException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Body_Exactly_At_The_Limit_Is_Not_Past_It(bool lookInside)
    {
        var body = await OpenAsync(new byte[100], [], new BodyLimits(100, 100), lookInside);

        (await ReadBytesAsync(body)).Should().HaveCount(100);
    }

    /// <summary>
    /// What a website sends can be broken in any way at all, and has to come to one answer: corrupt
    /// data, which a reading can say something about, and not some other exception nobody foresaw.
    /// </summary>
    /// <param name="encoding">What it says it was compressed with.</param>
    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    [InlineData("deflate")]
    public async Task A_Corrupt_Body_Is_Refused_As_Corrupt(string encoding)
    {
        var corrupt = Compressed(Bytes(string.Concat(Enumerable.Repeat(Text, 100))), encoding);

        Array.Fill(corrupt, (byte)0xFF, 2, corrupt.Length - 2);

        await using var body = await OpenAsync(corrupt, [encoding], Roomy, lookInside: false);

        await body.Invoking(ReadBytesAsync).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task A_Corrupt_File_Inside_A_Body_Is_Refused_As_Corrupt()
    {
        var corrupt = Gzip(Bytes(string.Concat(Enumerable.Repeat(Text, 100))));

        Array.Fill(corrupt, (byte)0xFF, 2, corrupt.Length - 2);

        await using var body = await OpenAsync(corrupt, [], Roomy, lookInside: true);

        await body.Invoking(ReadBytesAsync).Should().ThrowAsync<InvalidDataException>();
    }

    /// <summary>
    /// Not being able to unpack something comes to the same answer as it being corrupt.
    /// </summary>
    /// <param name="encodings">What it says it was compressed with.</param>
    [Theory]
    [InlineData("zstd")]
    [InlineData("compress")]
    [InlineData("gzip", "br")]
    public async Task A_Body_Compressed_With_Anything_Else_Is_Refused(params string[] encodings)
    {
        var body = await OpenAsync(Bytes(Text), encodings, Roomy, lookInside: false);

        await body.Invoking(ReadBytesAsync).Should().ThrowAsync<InvalidDataException>();
    }

    /// <summary>
    /// Whether it is refused while opening or while reading comes to the same answer, and something that looks
    /// inside has read it by the time it has opened.
    /// </summary>
    [Fact]
    public async Task A_Body_Looked_Inside_That_Is_Compressed_With_Anything_Else_Is_Refused_As_It_Opens()
    {
        var opening = () => OpenAsync(Bytes(Text), ["zstd"], Roomy, lookInside: true);

        await opening.Should().ThrowAsync<InvalidDataException>();
    }

    /// <summary>Nothing can be read of it, and what it was opened from is let go of like any other.</summary>
    [Fact]
    public async Task Disposing_A_Body_Compressed_With_Anything_Else_Disposes_What_It_Was_Opened_From()
    {
        var raw = new MemoryStream(Bytes(Text));
        var body = await Bodies.OpenAsync(raw, ["zstd"], Roomy, lookInside: false, Cancellation.Token);

        await body.DisposeAsync();

        raw.CanRead.Should().BeFalse();
    }

    /// <summary>
    /// Looking inside reads the first two bytes, and a body shorter than that is still a body. Its bytes
    /// have to come back as they were, or a one-letter answer becomes an empty one.
    /// </summary>
    /// <param name="length">How long the body is.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task A_Very_Short_Body_Comes_Back_Whole(int length)
    {
        var bytes = Bytes(new string('x', length));
        var body = await OpenAsync(bytes, [], Roomy, lookInside: true);

        (await ReadBytesAsync(body)).Should().Equal(bytes);
    }

    /// <summary>
    /// A reader asks for as little as it pleases, and the bytes put back have to go out in order however
    /// few it takes at a time.
    /// </summary>
    [Fact]
    public async Task A_Body_Read_A_Byte_At_A_Time_Comes_Back_Whole()
    {
        var bytes = Bytes(Text);
        var body = await OpenAsync(bytes, [], Roomy, lookInside: true);
        var read = new List<byte>();
        var buffer = new byte[1];

        while (await body.ReadAsync(buffer, Cancellation.Token) > 0)
        {
            read.Add(buffer[0]);
        }

        read.Should().Equal(bytes);
    }

    /// <summary>
    /// What reads from these is often a parser with no way of passing a cancellation down, so every
    /// read has to go under the deadline it was given, whatever it was asked under.
    /// </summary>
    /// <param name="lookInside">Whether the body was looked inside on the way.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Read_Goes_Under_The_Deadline_Whatever_It_Was_Asked_Under(bool lookInside)
    {
        using var deadline = new CancellationTokenSource();
        await using var body = await Bodies.OpenAsync(
            new SilentAfterTwoBytes(),
            [],
            Roomy,
            lookInside,
            deadline.Token);

        var buffer = new byte[16];

        // The two bytes there are, whether or not they have been looked at already.
        await body.ReadAtLeastAsync(buffer, 2, cancellationToken: Cancellation.Token);

#pragma warning disable xUnit1051 // Asked under no deadline at all, as the XML reader asks, is what this is about.
        var waiting = body.ReadAsync(buffer, CancellationToken.None).AsTask();
#pragma warning restore xUnit1051

        waiting.IsCompleted.Should().BeFalse();

        await deadline.CancelAsync();

        await waiting.Invoking(read => read.WaitAsync(TimeSpan.FromSeconds(10), Cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_Blocking_Read_Past_The_Deadline_Does_Not_Begin()
    {
        using var deadline = new CancellationTokenSource();
        await using var limited = new LimitedReadStream(new MemoryStream(new byte[8]), 100, deadline.Token);

        await deadline.CancelAsync();

        limited.Invoking(stream => stream.ReadByte()).Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public async Task Disposing_A_Body_Disposes_What_It_Was_Opened_From()
    {
        var raw = new MemoryStream(Gzip(Bytes(Text)));
        var body = await Bodies.OpenAsync(raw, ["gzip"], Roomy, lookInside: true, Cancellation.Token);

        await body.DisposeAsync();

        raw.CanRead.Should().BeFalse();
    }

    private static Task<Stream> OpenAsync(byte[] body, string[] encodings, BodyLimits limits, bool lookInside) =>
        Bodies.OpenAsync(new MemoryStream(body), encodings, limits, lookInside, Cancellation.Token);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Gzip(byte[] bytes) => Gzipped.Bytes(bytes);

    private static byte[] Compressed(byte[] bytes, string encoding)
    {
        using var output = new MemoryStream();

        using (var compressor = Compressor(output, encoding))
        {
            compressor.Write(bytes);
        }

        return output.ToArray();
    }

    private static Stream Compressor(Stream output, string encoding) => encoding.ToUpperInvariant() switch
    {
        "GZIP" or "X-GZIP" => new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true),
        "BR" => new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true),
        "DEFLATE" => new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true),
        _ => new BufferedStream(output),
    };

    private static async Task<string> ReadAllAsync(Stream body) =>
        Encoding.UTF8.GetString(await ReadBytesAsync(body));

    private static async Task<byte[]> ReadBytesAsync(Stream body)
    {
        using var read = new MemoryStream();

        await body.CopyToAsync(read, Cancellation.Token);

        return read.ToArray();
    }

    /// <summary>
    /// A body that sends two bytes and then nothing, for as long as anybody waits, as a slow website
    /// does: it goes on not answering until whoever is waiting says to stop.
    /// </summary>
    private sealed class SilentAfterTwoBytes : ForwardOnlyStream
    {
        private int _sent;

        public override int Read(Span<byte> buffer) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_sent < 2)
            {
                var length = Math.Min(2 - _sent, buffer.Length);

                _sent += length;

                return length;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

            return 0;
        }
    }
}
