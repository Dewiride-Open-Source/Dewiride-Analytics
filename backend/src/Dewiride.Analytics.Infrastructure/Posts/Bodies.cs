using System.Collections.Frozen;
using System.IO.Compression;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Opens what a website answered with, unpacked, and no larger than is read.
/// </summary>
/// <remarks>
/// <para>
/// The handler is told not to unpack anything, because a body is held to two limits and it could only
/// ever be held to one of them behind a handler that did: what arrives is counted before it is
/// unpacked, and what comes of it is counted after. Neither count is any use without the other.
/// </para>
/// <para>
/// Anything that can not be unpacked is an <see cref="InvalidDataException"/> from the first read of it, the
/// same as a compressed body that turns out to be corrupt, so one answer covers the two. A website saying it
/// compressed something with what is not read has said nothing about what the thing is.
/// </para>
/// </remarks>
internal static class Bodies
{
    private const int GzipMarkLength = 2;
    private const byte GzipMarkFirst = 0x1F;
    private const byte GzipMarkSecond = 0x8B;

    /// <summary>What a website can say it compressed something with, and how to undo it.</summary>
    private static readonly FrozenDictionary<string, Func<Stream, Stream>> Unpackers =
        new Dictionary<string, Func<Stream, Stream>>
        {
            ["identity"] = static body => body,
            ["gzip"] = static body => new GZipStream(body, CompressionMode.Decompress),
            ["x-gzip"] = static body => new GZipStream(body, CompressionMode.Decompress),
            ["br"] = static body => new BrotliBody(body),
            ["deflate"] = static body => new ZLibStream(body, CompressionMode.Decompress),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Opens a body.
    /// </summary>
    /// <remarks>
    /// A sitemap may be looked inside as well. A compressed sitemap file is often served as what it
    /// is, an <c>.xml.gz</c> with no word about compression in its headers, and served compressed
    /// again on the way over by a server that compresses everything. Only that one extra layer is
    /// looked for, and it gets limits of its own.
    /// </remarks>
    /// <param name="raw">The body as it arrives.</param>
    /// <param name="encodings">What the website says it was compressed with.</param>
    /// <param name="limits">How much may arrive, and how much may come of it.</param>
    /// <param name="lookInside">Whether to look for a compressed file inside.</param>
    /// <param name="deadline">When to stop waiting for it.</param>
    /// <returns>The body, unpacked. Disposing it disposes what it was opened from.</returns>
    public static async Task<Stream> OpenAsync(
        Stream raw,
        ICollection<string> encodings,
        BodyLimits limits,
        bool lookInside,
        CancellationToken deadline)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(encodings);

        var arriving = new LimitedReadStream(raw, limits.Arriving, deadline);
        var unpacked = new LimitedReadStream(Unpacked(arriving, encodings), limits.Unpacked, deadline);

        return lookInside ? await UnpackedInsideAsync(unpacked, limits, deadline).ConfigureAwait(false) : unpacked;
    }

    private static Stream Unpacked(Stream arriving, ICollection<string> encodings)
    {
        if (encodings.Count == 0)
        {
            return arriving;
        }

        return encodings.Count == 1 && Unpackers.TryGetValue(encodings.First(), out var unpack)
            ? unpack(arriving)
            : new CompressedWithSomethingElse(arriving);
    }

    private static async Task<Stream> UnpackedInsideAsync(
        Stream unpacked,
        BodyLimits limits,
        CancellationToken deadline)
    {
        var head = new byte[GzipMarkLength];
        var read = await unpacked.ReadAtLeastAsync(head, GzipMarkLength, throwOnEndOfStream: false, deadline)
            .ConfigureAwait(false);

        var whole = new ReplayStream(head.AsMemory(0, read), unpacked);

        return read == GzipMarkLength && head[0] == GzipMarkFirst && head[1] == GzipMarkSecond
            ? new LimitedReadStream(new GZipStream(whole, CompressionMode.Decompress), limits.Unpacked, deadline)
            : whole;
    }

    /// <summary>
    /// A body compressed with something that is not read, of which nothing can be read.
    /// </summary>
    /// <param name="arriving">The body as it arrives.</param>
    private sealed class CompressedWithSomethingElse(Stream arriving) : ForwardOnlyStream
    {
        private const string Refusal = "A body compressed with anything else is not read.";

        /// <inheritdoc />
        public override int Read(Span<byte> buffer) => throw new InvalidDataException(Refusal);

        /// <inheritdoc />
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new InvalidDataException(Refusal));

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                arriving.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// A body compressed with Brotli, whose corrupt data comes to the same answer as anybody else's.
    /// </summary>
    /// <remarks>
    /// The framework's stream is documented as reporting corrupt data as invalid data, and reports it
    /// as an invalid operation. Nothing here reads a stream from two places at once, which is the only
    /// other thing that exception means, so here it means the body is corrupt.
    /// </remarks>
    /// <param name="arriving">The body as it arrives.</param>
    private sealed class BrotliBody(Stream arriving) : ForwardOnlyStream
    {
        private readonly BrotliStream _unpacking = new(arriving, CompressionMode.Decompress);

        /// <inheritdoc />
        public override int Read(Span<byte> buffer)
        {
            try
            {
                return _unpacking.Read(buffer);
            }
            catch (InvalidOperationException corrupt)
            {
                throw new InvalidDataException("The body is not what it says it was compressed with.", corrupt);
            }
        }

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await _unpacking.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException corrupt)
            {
                throw new InvalidDataException("The body is not what it says it was compressed with.", corrupt);
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _unpacking.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>
/// The most of a body that is read.
/// </summary>
/// <param name="Arriving">How much may arrive, compressed or not.</param>
/// <param name="Unpacked">How much may come of it once it is unpacked.</param>
internal readonly record struct BodyLimits(long Arriving, long Unpacked);
