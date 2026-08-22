using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// Generates and recognises the secret carried in an invitation link.
/// </summary>
/// <remarks>
/// <para>
/// Not one of the account store's tokens, deliberately. Those are sealed against an account that
/// already exists, and an invitation is addressed to somebody who has none — there is nothing for
/// the store to bind a token to until they take it up.
/// </para>
/// <para>
/// Two hundred and fifty-six bits from the operating system's generator, which is what makes
/// storing only a digest sound: there is nothing to guess and nothing to look up. The alphabet is
/// URL-safe so the link survives being pasted, forwarded and re-typed.
/// </para>
/// </remarks>
internal static class InvitationSecret
{
    /// <summary>Marks a string as one of these secrets.</summary>
    public const string Prefix = "dwi_";

    /// <summary>Bytes of randomness behind each secret.</summary>
    private const int EntropyBytes = 32;

    /// <summary>
    /// Length of the encoded random part. Thirty-two bytes reach forty-three characters without
    /// padding, and stating it lets a wrongly-sized string be turned away before anything is
    /// hashed or looked up.
    /// </summary>
    private const int EncodedLength = 43;

    /// <summary>Total length of a well-formed secret.</summary>
    public static int Length => Prefix.Length + EncodedLength;

    /// <summary>
    /// Creates a secret and the digest that is stored in its place.
    /// </summary>
    /// <returns>The secret and its hash.</returns>
    public static (string Secret, string Hash) Create()
    {
        var secret = Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(EntropyBytes));

        return (secret, Hash(secret));
    }

    /// <summary>
    /// Hashes a secret into the form that is stored and looked up.
    /// </summary>
    /// <param name="secret">The secret.</param>
    /// <returns>Its hash, lower-case hexadecimal.</returns>
    public static string Hash(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    /// <summary>
    /// Decides whether a presented string is even shaped like one of these secrets.
    /// </summary>
    /// <remarks>
    /// Cheap rejection before anything reaches the database. Whatever arrives here was typed,
    /// pasted or forwarded by somebody, so anything at all may turn up.
    /// </remarks>
    /// <param name="candidate">Whatever the caller sent.</param>
    /// <returns><see langword="true"/> if it is worth hashing and looking up.</returns>
    public static bool LooksWellFormed(string? candidate) =>
        candidate is not null
        && candidate.Length == Length
        && candidate.StartsWith(Prefix, StringComparison.Ordinal);
}
