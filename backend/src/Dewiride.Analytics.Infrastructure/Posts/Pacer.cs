using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Keeps requests to one website at least a set time apart, however many readers want to ask it.
/// </summary>
/// <remarks>
/// <para>
/// Kept per website rather than per reading, and for the whole process. Domains are not unique across
/// an installation, so two accounts can name one website, and a website answering at both of a host's
/// names is one server being asked twice. <see cref="PublishedHosts.WebsiteName"/> is what makes those
/// one queue here.
/// </para>
/// <para>
/// A turn is booked before it is waited for, so whoever asks second is told when the second turn
/// falls and does not have to find out by being woken with the first. A turn nobody came for is
/// simply a gap that was longer than it had to be.
/// </para>
/// <para>
/// Once the table has grown it is tidied of every website with nothing booked ahead. That is nothing
/// anybody could be made to wait behind, so forgetting it changes nothing, and an engine that has read
/// a great many websites holds on to none of them.
/// </para>
/// </remarks>
/// <param name="clock">The clock turns are measured on.</param>
/// <param name="gap">The least time between two requests to one website.</param>
internal sealed class Pacer(TimeProvider clock, TimeSpan gap)
{
    /// <summary>How many websites may be remembered before the table is tidied.</summary>
    private const int MostRemembered = 1024;

    private readonly long _began = clock.GetTimestamp();
    private readonly Dictionary<string, TimeSpan> _nextTurns = new(StringComparer.Ordinal);

    /// <summary>
    /// Waits until it is this request's turn to go to a host.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="cancellationToken">Gives up waiting.</param>
    /// <returns>A task that completes once the request may go.</returns>
    public async Task WaitTurnAsync(string host, CancellationToken cancellationToken)
    {
        var turn = Book(PublishedHosts.WebsiteName(host));

        // A timer can wake a moment early, and early is the one thing this is here to prevent.
        for (var left = turn - Now(); left > TimeSpan.Zero; left = turn - Now())
        {
            await Task.Delay(left, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan Now() => clock.GetElapsedTime(_began);

    private TimeSpan Book(string website)
    {
        var now = Now();

        lock (_nextTurns)
        {
            ForgetThosePast(now);

            var turn = _nextTurns.TryGetValue(website, out var next) && next > now ? next : now;

            _nextTurns[website] = turn + gap;

            return turn;
        }
    }

    private void ForgetThosePast(TimeSpan now)
    {
        if (_nextTurns.Count < MostRemembered)
        {
            return;
        }

        foreach (var (website, next) in _nextTurns)
        {
            if (next <= now)
            {
                _nextTurns.Remove(website);
            }
        }
    }
}
