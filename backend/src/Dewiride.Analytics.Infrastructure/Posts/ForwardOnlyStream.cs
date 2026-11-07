namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// A stream that can only be read, from the front, one read after another.
/// </summary>
/// <remarks>
/// What is common to the streams a website's answer passes through. A reader may call any of the four
/// ways of reading a stream, and what they do when left to the framework's defaults matters: the
/// array form of an asynchronous read, which is what the XML reader calls, would otherwise run the
/// blocking read on a thread of its own and take away everything a deadline can do about it.
/// </remarks>
internal abstract class ForwardOnlyStream : Stream
{
    /// <inheritdoc />
    public sealed override bool CanRead => true;

    /// <inheritdoc />
    public sealed override bool CanSeek => false;

    /// <inheritdoc />
    public sealed override bool CanWrite => false;

    /// <inheritdoc />
    public sealed override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public sealed override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public abstract override int Read(Span<byte> buffer);

    /// <inheritdoc />
    public abstract override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public sealed override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public sealed override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public sealed override void Flush()
    {
        // Nothing is ever written, so there is nothing to flush.
    }

    /// <inheritdoc />
    public sealed override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public sealed override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public sealed override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
