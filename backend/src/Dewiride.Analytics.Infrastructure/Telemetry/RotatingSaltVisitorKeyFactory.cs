using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Infrastructure.Telemetry;

/// <summary>
/// Derives visitor keys by keyed hashing against the day's rotating salt.
/// </summary>
/// <param name="saltStore">Holds the current day's salt.</param>
public sealed class RotatingSaltVisitorKeyFactory(VisitorKeySaltStore saltStore) : IVisitorKeyFactory
{
    /// <summary>
    /// Bytes of the hash kept. Sixteen is far beyond what is needed to avoid collisions within a
    /// single site-day and keeps the stored column small.
    /// </summary>
    private const int KeyLengthBytes = 16;

    /// <summary>Size of a SHA-256 digest.</summary>
    private const int DigestLengthBytes = 32;

    /// <summary>Bytes contributed by the site identifier.</summary>
    private const int SiteIdLengthBytes = 16;

    /// <summary>Bytes contributed by the day number.</summary>
    private const int DayLengthBytes = 4;

    /// <summary>
    /// Separates the address from the user agent. A byte that cannot appear in either input, so an
    /// address ending one way and a user agent beginning another cannot produce the same message
    /// as a different pair and be grouped as one visitor.
    /// </summary>
    private const byte FieldSeparator = 0x1F;

    /// <inheritdoc />
    public string? Derive(Guid siteId, string? ipAddress, string? userAgent, DateTimeOffset observedAt)
    {
        // With neither an address nor a user agent there is nothing distinguishing to hash, and a
        // key derived from the site alone would group every such visitor into one fictitious
        // person. Returning null says "this activity cannot be grouped", which is true.
        if (string.IsNullOrEmpty(ipAddress) && string.IsNullOrEmpty(userAgent))
        {
            return null;
        }

        var day = DateOnly.FromDateTime(observedAt.UtcDateTime);
        var salt = saltStore.GetSalt(day);

        // No salt means the day is outside the retained window. Hashing without one would produce
        // a stable, reversible identifier — precisely what this design exists to prevent.
        if (salt.IsEmpty)
        {
            return null;
        }

        var addressLength = Encoding.UTF8.GetByteCount(ipAddress ?? string.Empty);
        var agentLength = Encoding.UTF8.GetByteCount(userAgent ?? string.Empty);
        var messageLength = SiteIdLengthBytes + DayLengthBytes + addressLength + 1 + agentLength;

        var buffer = ArrayPool<byte>.Shared.Rent(messageLength);

        try
        {
            var message = buffer.AsSpan(0, messageLength);
            WriteMessage(message, siteId, day, ipAddress, userAgent, addressLength);

            Span<byte> digest = stackalloc byte[DigestLengthBytes];
            HMACSHA256.HashData(salt, message, digest);

            return Convert.ToHexStringLower(digest[..KeyLengthBytes]);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void WriteMessage(
        Span<byte> message,
        Guid siteId,
        DateOnly day,
        string? ipAddress,
        string? userAgent,
        int addressLength)
    {
        siteId.TryWriteBytes(message);
        BinaryPrimitives.WriteInt32LittleEndian(message[SiteIdLengthBytes..], day.DayNumber);

        var offset = SiteIdLengthBytes + DayLengthBytes;
        Encoding.UTF8.GetBytes(ipAddress ?? string.Empty, message[offset..]);

        offset += addressLength;
        message[offset] = FieldSeparator;

        Encoding.UTF8.GetBytes(userAgent ?? string.Empty, message[(offset + 1)..]);
    }
}
