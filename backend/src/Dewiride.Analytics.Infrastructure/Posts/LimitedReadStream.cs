namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Reads a body written by a stranger, up to a limit and up to a deadline.
/// </summary>
/// <remarks>
/// <para>
/// One of these sits under each thing that unpacks a body and another above it, because the two can
/// be wildly apart in either direction: a few kilobytes that expand to gigabytes, and an endless run
/// of empty compressed sections that use up the connection while expanding to nothing at all.
/// </para>
/// <para>
/// Every read goes under the deadline it was given, whatever it was asked to go under. What reads
/// from this in the end is often a parser with no way to pass a cancellation down to it, and a body
/// dripped out a byte at a time would otherwise keep that parser waiting for as long as the website
/// cared to go on.
/// </para>
/// </remarks>
/// <param name="body">What is being read.</param>
/// <param name="limit">The most that may come out of it.</param>
/// <param name="deadline">When to stop waiting for it.</param>
internal sealed class LimitedReadStream(Stream body, long limit, CancellationToken deadline) : ForwardOnlyStream
{
    private long _left = limit;

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        deadline.ThrowIfCancellationRequested();

        return Counted(body.Read(buffer));
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        Counted(await body.ReadAsync(buffer, deadline).ConfigureAwait(false));

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            body.Dispose();
        }

        base.Dispose(disposing);
    }

    private int Counted(int read)
    {
        _left -= read;

        return _left >= 0 ? read : throw new ResponseTooLargeException();
    }
}

/// <summary>
/// A body that came to more than is read.
/// </summary>
internal sealed class ResponseTooLargeException() : IOException("The answer is larger than is read.");
