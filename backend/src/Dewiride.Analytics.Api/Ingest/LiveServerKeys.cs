using System.Collections.Frozen;

namespace Dewiride.Analytics.Api.Ingest;

/// <summary>
/// The server keys that have been issued and not withdrawn, by hash, as of the last time they were
/// read — which decides whose allowance a batch is counted against.
/// </summary>
/// <remarks>
/// <para>
/// One host commonly serves many websites, each reporting with a key of its own, so a batch whose
/// key is live is counted against that key: a busy neighbour on the same machine spends only its own
/// allowance, and a quiet website or one reporting just after a restart is counted the same way as
/// one that never stops. Every other batch — no key, a value not shaped like one, a key that matches
/// nothing, a withdrawn one — is counted against the address it came from, before anything is looked
/// up. A key costs nothing to invent, and counted per key an invented one would arrive with an
/// allowance of its own, a limiter of its own and a lookup in the database each time.
/// </para>
/// <para>
/// Read again every minute (<see cref="Startup.LiveServerKeysRefresher"/>), so a key created a moment
/// ago is counted against its address until then, and a key withdrawn a moment ago against its own
/// allowance until then — the same minute for which the answer to a key is held anyway. Hashes,
/// never secrets: nothing replayable is kept here.
/// </para>
/// </remarks>
internal sealed class LiveServerKeys
{
    private volatile FrozenSet<string> _live = FrozenSet<string>.Empty;

    /// <summary>Whether a key is live, as of the last reading.</summary>
    /// <param name="hash">The key's hash.</param>
    /// <returns><see langword="true"/> when its batches are counted against it.</returns>
    public bool Includes(string hash) => _live.Contains(hash);

    /// <summary>Replaces what is held with a fresh reading.</summary>
    /// <param name="hashes">The hash of every key issued and not withdrawn.</param>
    public void Replace(IEnumerable<string> hashes) =>
        _live = hashes.ToFrozenSet(StringComparer.Ordinal);
}
