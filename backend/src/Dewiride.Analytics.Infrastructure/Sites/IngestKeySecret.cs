using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Dewiride.Analytics.Infrastructure.Sites;

/// <summary>
/// Generates and recognises the secrets a server-side reporter presents.
/// </summary>
/// <remarks>
/// <para>
/// The shape is deliberate. A fixed prefix makes a leaked secret identifiable on sight — by the
/// person who leaked it, by a secret scanner run over a public repository, and by whoever has to
/// decide in a hurry what a string found in a log actually is. The alphabet is URL-safe so that
/// pasting one into a shell, a configuration file or an environment variable cannot mangle it.
/// </para>
/// <para>
/// Two hundred and fifty-six bits of randomness from the operating system's generator, which is
/// what makes storing only a digest sound: there is nothing to guess and nothing to look up.
/// </para>
/// </remarks>
internal static class IngestKeySecret
{
    /// <summary>Marks a string as one of these secrets.</summary>
    public const string Prefix = "dwk_";

    /// <summary>How many characters of the secret are kept so a key can be recognised.</summary>
    public const int PreviewLength = 4;

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
    /// Creates a secret and the two derived values that are stored in its place.
    /// </summary>
    /// <returns>The secret, its hash and its preview.</returns>
    public static (string Secret, string Hash, string Preview) Create()
    {
        var entropy = RandomNumberGenerator.GetBytes(EntropyBytes);
        var secret = Prefix + Base64Url.EncodeToString(entropy);

        return (secret, Hash(secret), secret[^PreviewLength..]);
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
    /// <param name="candidate">Whatever the caller sent.</param>
    /// <returns><see langword="true"/> if it is worth hashing and looking up.</returns>
    /// <remarks>
    /// Cheap rejection before anything reaches the cache or the database. Without it, a stream of
    /// arbitrary headers would fill a cache with entries that can never match anything.
    /// </remarks>
    public static bool LooksWellFormed(string? candidate) =>
        candidate is not null
        && candidate.Length == Length
        && candidate.StartsWith(Prefix, StringComparison.Ordinal);
}
