using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Persistence;

/// <summary>
/// Every PostgreSQL advisory lock this product takes, named in one place.
/// </summary>
/// <remarks>
/// <para>
/// Advisory locks share one namespace across the whole database, so a number chosen for one
/// purpose must not be chosen for another, and the only way to be sure of that is to keep them
/// together. There are two lock spaces, which PostgreSQL keeps separate: the single-key space,
/// addressed by one 64-bit number, and the pair-key space, addressed by two 32-bit numbers. A key
/// in one can never collide with a key in the other, so the first-run claim's single key and the
/// per-account keys below need know nothing about each other — but two uses of the pair-key space
/// must differ in their first number, which is why each has a namespace here.
/// </para>
/// <para>
/// Every lock is a transaction lock (<c>pg_advisory_xact_lock</c>): it is released when the
/// transaction that took it ends, however it ends, so a failure can never leave one held.
/// </para>
/// </remarks>
internal static class AdvisoryLocks
{
    /// <summary>
    /// The single-key lock the first-run claim is serialised under.
    /// </summary>
    /// <remarks>
    /// An installation is claimable for a few minutes of its life and claimed for the rest, so
    /// this is taken once and then never again.
    /// </remarks>
    public const long SetupKey = 0x4445_5749_5249_4445;

    /// <summary>
    /// Pair-key namespace for removing a website, keyed per person.
    /// </summary>
    /// <remarks>
    /// The rule it protects — nobody may remove the only website they own — is a count of what one
    /// person holds, so the lock is theirs rather than global and two customers' removals never
    /// queue behind each other.
    /// </remarks>
    public const int SiteRemovalNamespace = 0x44_57_53_52;

    /// <summary>
    /// Pair-key namespace for closing, restoring and purging an account, keyed per organisation.
    /// </summary>
    /// <remarks>
    /// Closing, bringing back and finally deleting one account are three acts on one row, from
    /// three places, and the lock is what makes them take turns: a restore that arrives while the
    /// sweep is deleting the account waits, and then finds nothing to restore.
    /// </remarks>
    public const int AccountClosureNamespace = 0x44_57_43_4C;

    /// <summary>
    /// Takes a single-key lock for the rest of the open transaction.
    /// </summary>
    /// <param name="database">Control-plane database, with a transaction open.</param>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task TakeAsync(ControlPlaneDbContext database, long key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        return database.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
    }

    /// <summary>
    /// Takes a pair-key lock, in a namespace and for one identifier, for the rest of the open
    /// transaction.
    /// </summary>
    /// <param name="database">Control-plane database, with a transaction open.</param>
    /// <param name="ns">Which use of the pair-key space this is.</param>
    /// <param name="id">What the lock is about.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task TakeAsync(ControlPlaneDbContext database, int ns, Guid id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        var key = KeyFor(id);

        return database.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ns}, {key})", cancellationToken);
    }

    /// <summary>
    /// Folds an identifier to the 32 bits a pair-key lock is addressed by.
    /// </summary>
    /// <remarks>
    /// Every byte contributes, because these identifiers are time-ordered and their leading bytes
    /// barely differ between two created in the same week. A collision between two identifiers
    /// costs one of them a wait behind the other and nothing else: the lock only serialises, and
    /// every rule taken under it re-reads its own row.
    /// </remarks>
    /// <param name="id">The identifier the lock is about.</param>
    /// <returns>The second number of the pair.</returns>
    private static int KeyFor(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);

        return BinaryPrimitives.ReadInt32LittleEndian(bytes)
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[4..])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[8..])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
    }
}
