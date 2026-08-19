using MaxMind.Db;

namespace Dewiride.Analytics.Infrastructure.Network;

/// <summary>
/// Holds whichever reference data is currently loaded, and lets it be replaced underneath readers.
/// </summary>
/// <remarks>
/// <para>
/// Empty until something loads, and it stays usable while empty: a first run has downloaded
/// nothing yet, and an install with no way out to the internet may never download anything. Both
/// answer that nothing is known rather than failing, which is a state the interface already knows
/// how to show.
/// </para>
/// <para>
/// Replacing the place data is the delicate part. The reader maps the file into memory rather
/// than copying it — which is what keeps a hundred-and-twenty-megabyte database off the heap and
/// out of a laptop's memory budget — so closing one while a lookup is still walking it is not an
/// exception, it is a read of memory that is no longer mapped. So a replaced reader is not closed
/// at the time it is replaced. It is held aside and closed at the <em>next</em> replacement, a
/// full refresh interval later, by which time no lookup begun before the swap can still be
/// running: lookups are synchronous and take microseconds. At most two readers are ever alive.
/// </para>
/// </remarks>
internal sealed class ReferenceDataStore : IDisposable
{
    private Reader? _places;
    private Reader? _retired;
    private AutonomousSystemTable? _networks;
    private bool _disposed;

    /// <summary>The place database, or <see langword="null"/> when none has loaded.</summary>
    public Reader? Places => Volatile.Read(ref _places);

    /// <summary>The network table, or <see langword="null"/> when none has loaded.</summary>
    public AutonomousSystemTable? Networks => Volatile.Read(ref _networks);

    /// <summary>
    /// Puts a newly loaded place database into service.
    /// </summary>
    /// <param name="reader">The database, already opened and proven readable.</param>
    public void PublishPlaces(Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var previous = Interlocked.Exchange(ref _places, reader);
        var stale = Interlocked.Exchange(ref _retired, previous);

        stale?.Dispose();
    }

    /// <summary>
    /// Puts a newly loaded network table into service.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <remarks>
    /// Nothing is held aside here. The table is an ordinary managed object holding no file, so the
    /// one it replaces is collected once the last lookup using it returns.
    /// </remarks>
    public void PublishNetworks(AutonomousSystemTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        Volatile.Write(ref _networks, table);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Interlocked.Exchange(ref _places, null)?.Dispose();
        Interlocked.Exchange(ref _retired, null)?.Dispose();
    }
}
