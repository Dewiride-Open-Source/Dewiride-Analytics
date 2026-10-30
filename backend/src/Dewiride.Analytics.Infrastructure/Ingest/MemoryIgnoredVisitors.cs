using System.Collections.Concurrent;
using Dewiride.Analytics.Application.Ingest;

namespace Dewiride.Analytics.Infrastructure.Ingest;

/// <summary>
/// Remembers, in this process, the visitors whose visit is being turned away.
/// </summary>
/// <remarks>
/// <para>
/// Held in memory because it is asked about every report, and because what it holds is worth
/// nothing once the visit is over. An installation running more than one engine behind a balancer
/// remembers a visitor only on the engine their arrival reached; a report that reaches another is
/// stored, and with no arrival before it, it never becomes a visit.
/// </para>
/// <para>
/// Bounded, because whoever sends the visits decides how many visitors there are. Visitors already
/// quiet for long enough are forgotten first; if that frees nothing, a new visitor is not
/// remembered. Their arrival is still turned away, and what they send next is stored and never
/// becomes a visit — so a flood costs storage rather than this process's memory.
/// </para>
/// </remarks>
/// <param name="quiet">How long a visitor may be quiet before they are forgotten.</param>
/// <param name="capacity">Most visitors remembered at once.</param>
/// <param name="clock">Source of when a visitor was last heard from.</param>
internal sealed class MemoryIgnoredVisitors(TimeSpan quiet, int capacity, TimeProvider clock) : IIgnoredVisitors
{
    /// <summary>Most visitors a running installation remembers at once.</summary>
    /// <remarks>
    /// Far past the number a campaign sends in the half hour a visitor is held for, and a few
    /// megabytes at most: a site, a key of a fixed length and an instant each.
    /// </remarks>
    internal const int MostRemembered = 50_000;

    private readonly ConcurrentDictionary<(Guid Site, string Visitor), DateTimeOffset> _lastHeard = new();

    /// <inheritdoc />
    public void Remember(Guid siteId, string visitorKey)
    {
        if (_lastHeard.Count >= capacity)
        {
            Forget();

            if (_lastHeard.Count >= capacity)
            {
                return;
            }
        }

        _lastHeard[(siteId, visitorKey)] = clock.GetUtcNow();
    }

    /// <inheritdoc />
    public bool Recalls(Guid siteId, string visitorKey)
    {
        var key = (siteId, visitorKey);

        if (!_lastHeard.TryGetValue(key, out var heard))
        {
            return false;
        }

        var now = clock.GetUtcNow();

        if (now - heard >= quiet)
        {
            _lastHeard.TryRemove(KeyValuePair.Create(key, heard));
            return false;
        }

        // Heard from again, so the visit is not over yet. Another report arriving at the same moment
        // may have said so first, which is the same answer.
        _lastHeard.TryUpdate(key, now, heard);
        return true;
    }

    private void Forget()
    {
        var stale = clock.GetUtcNow() - quiet;

        foreach (var entry in _lastHeard)
        {
            if (entry.Value <= stale)
            {
                _lastHeard.TryRemove(entry);
            }
        }
    }
}
