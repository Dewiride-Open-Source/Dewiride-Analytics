using System.Collections.Immutable;
using System.Security.Cryptography;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.Telemetry;

/// <summary>
/// Holds the previous, current and next day's visitor-key salts in memory, and rotates them.
/// </summary>
/// <remarks>
/// <para>
/// The ingest path derives a visitor key on every request and cannot afford a database round trip
/// to fetch a salt, so the salts are cached here and refreshed out of band. Nothing older than the
/// previous day is ever held: anything older is deleted from the database by
/// <see cref="RotateAsync"/>, which is what makes historical keys unrecoverable.
/// </para>
/// <para>
/// The next day's salt is made a day early. A day's first reports arrive the instant it begins,
/// and a refresh run out of band is due whenever its schedule says rather than at midnight — so a
/// salt made only once its day had started would leave every report before the next run without a
/// key, and a report without a key is nobody: no visit, no verdict, nobody on the screen of who is
/// here. Made ahead, it is cached many runs before it is needed. It says nothing about anybody
/// until its day begins, since no key is derived from it before then.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates the scope used to reach the control-plane database.</param>
/// <param name="timeProvider">Source of the current day.</param>
/// <param name="logger">Log sink.</param>
public sealed partial class VisitorKeySaltStore(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<VisitorKeySaltStore> logger)
{
    private const int SaltLengthBytes = 32;

    /// <summary>
    /// The days a salt is kept for, counting back from today: today's and the day before's.
    /// </summary>
    private const int DaysRetained = 2;

    /// <summary>The days a salt is made ahead of, counting on from today: tomorrow's.</summary>
    private const int DaysAhead = 1;

    private ImmutableDictionary<DateOnly, byte[]> _salts = ImmutableDictionary<DateOnly, byte[]>.Empty;

    /// <summary>
    /// Returns the salt for a day, or an empty span when it is not held.
    /// </summary>
    /// <param name="day">The UTC day.</param>
    /// <returns>
    /// The salt bytes, or empty when the day is outside the retained window. An empty result must
    /// produce a null visitor key rather than an unsalted hash — an unsalted hash of an address
    /// would be trivially reversible and would be exactly the identifier this design avoids.
    /// </returns>
    public ReadOnlySpan<byte> GetSalt(DateOnly day) =>
        _salts.TryGetValue(day, out var salt) ? salt : ReadOnlySpan<byte>.Empty;

    /// <summary>
    /// Ensures the current and next day's salts exist, prunes expired salts, and refreshes the
    /// cache.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RotateAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var oldest = today.AddDays(-(DaysRetained - 1));

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var expired = await database.Set<VisitorKeySalt>()
            .Where(salt => salt.Day < oldest)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (expired > 0)
        {
            Log.DeletedExpiredSalts(logger, expired);
        }

        var current = await database.Set<VisitorKeySalt>()
            .AsNoTracking()
            .Where(salt => salt.Day >= oldest)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var created = Enumerable.Range(0, DaysAhead + 1)
            .Select(offset => today.AddDays(offset))
            .Where(day => !current.Exists(salt => salt.Day == day))
            .Select(day => new VisitorKeySalt(day, RandomNumberGenerator.GetBytes(SaltLengthBytes)))
            .ToList();

        if (created.Count > 0)
        {
            database.AddRange(created);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            current.AddRange(created);

            foreach (var salt in created)
            {
                Log.GeneratedSalt(logger, salt.Day);
            }
        }

        _salts = current.ToImmutableDictionary(salt => salt.Day, salt => salt.Salt);
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Information,
            Message = "Deleted {Count} expired visitor-key salt(s).")]
        public static partial void DeletedExpiredSalts(ILogger logger, int count);

        [LoggerMessage(
            EventId = 1002,
            Level = LogLevel.Information,
            Message = "Generated a new visitor-key salt for {Day}.")]
        public static partial void GeneratedSalt(ILogger logger, DateOnly day);
    }
}
