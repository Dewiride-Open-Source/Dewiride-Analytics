namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// A body with the bytes already read from the front of it put back.
/// </summary>
/// <remarks>
/// A body from a website can only be read forwards, and finding out what it is takes reading the
/// start of it.
/// </remarks>
/// <param name="head">The bytes already read.</param>
/// <param name="rest">The body they were read from.</param>
internal sealed class ReplayStream(ReadOnlyMemory<byte> head, Stream rest) : ForwardOnlyStream
{
    private ReadOnlyMemory<byte> _head = head;

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) =>
        _head.IsEmpty || buffer.IsEmpty ? rest.Read(buffer) : Replay(buffer);

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _head.IsEmpty || buffer.IsEmpty
            ? rest.ReadAsync(buffer, cancellationToken)
            : ValueTask.FromResult(Replay(buffer.Span));

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            rest.Dispose();
        }

        base.Dispose(disposing);
    }

    private int Replay(Span<byte> buffer)
    {
        var replayed = Math.Min(_head.Length, buffer.Length);

        _head.Span[..replayed].CopyTo(buffer);
        _head = _head[replayed..];

        return replayed;
    }
}
