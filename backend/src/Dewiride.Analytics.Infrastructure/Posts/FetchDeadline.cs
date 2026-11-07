namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// How long one thing asked of a website has, from the first request to the last byte.
/// </summary>
/// <remarks>
/// It tells two endings apart, and the difference matters to whoever is waiting. A website running
/// out of time is something that happened to this one request. Whoever asked no longer wanting the
/// answer is the whole reading stopping.
/// </remarks>
internal sealed class FetchDeadline : IDisposable
{
    private readonly CancellationToken _owner;
    private readonly CancellationTokenSource _clock;
    private readonly CancellationTokenSource _either;

    /// <summary>Starts the time.</summary>
    /// <param name="allowed">How long there is.</param>
    /// <param name="clock">The clock it runs on.</param>
    /// <param name="owner">Cancels it from outside, when whoever asked stops waiting.</param>
    public FetchDeadline(TimeSpan allowed, TimeProvider clock, CancellationToken owner)
    {
        _owner = owner;
        _clock = new CancellationTokenSource(allowed, clock);
        _either = CancellationTokenSource.CreateLinkedTokenSource(owner, _clock.Token);
    }

    /// <summary>Cancelled once the time is up, or once whoever asked stops waiting.</summary>
    public CancellationToken Token => _either.Token;

    /// <summary>Whether whoever asked has stopped waiting.</summary>
    public bool OwnerGaveUp => _owner.IsCancellationRequested;

    /// <summary>Whether the time is up.</summary>
    public bool Passed => _clock.IsCancellationRequested;

    /// <inheritdoc />
    public void Dispose()
    {
        _either.Dispose();
        _clock.Dispose();
    }
}
