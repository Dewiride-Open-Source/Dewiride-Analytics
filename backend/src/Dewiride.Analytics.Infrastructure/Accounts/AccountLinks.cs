using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// Builds the links this product sends by email, and reads back what comes with them.
/// </summary>
/// <remarks>
/// <para>
/// Every one of them has the same two parts — the address it was sent to, and a token the account
/// store issued — and every one of them fails in the same quiet way if either is mangled in
/// transit. Written once because both editions send such links, and because the thing most likely
/// to go wrong is not the cryptography but the encoding around it.
/// </para>
/// <para>
/// The address a link points at is supplied by the caller and is configured, never derived from a
/// request. A hostname taken from an incoming request is written by whoever sent it, so a link
/// built that way can be aimed at a server somebody else controls.
/// </para>
/// </remarks>
public static class AccountLinks
{
    /// <summary>
    /// Builds a link to one of the product's screens.
    /// </summary>
    /// <param name="publicAddress">
    /// The address the product is published on, or <see langword="null"/> when the installation has
    /// not declared one. Without it the link is relative, which is still the whole of what somebody
    /// needs to put after their own address.
    /// </param>
    /// <param name="screen">The screen's path, without a leading slash.</param>
    /// <param name="emailAddress">The address the link is being sent to.</param>
    /// <param name="token">The token the account store issued, unencoded.</param>
    /// <returns>The link.</returns>
    public static string To(Uri? publicAddress, string screen, string emailAddress, string token)
    {
        var path = $"{screen}?address={Uri.EscapeDataString(emailAddress)}&token={Carry(token)}";

        return publicAddress is null ? $"/{path}" : new Uri(EndingInSlash(publicAddress), path).AbsoluteUri;
    }

    /// <summary>
    /// Builds a link to one of the product's screens, for a message that carries no token.
    /// </summary>
    /// <param name="publicAddress">
    /// The address the product is published on, or <see langword="null"/> when the installation has
    /// not declared one.
    /// </param>
    /// <param name="screen">The screen's path, without a leading slash.</param>
    /// <returns>The link.</returns>
    public static string To(Uri? publicAddress, string screen) =>
        publicAddress is null ? $"/{screen}" : new Uri(EndingInSlash(publicAddress), screen).AbsoluteUri;

    /// <summary>
    /// Builds a link that carries a secret and nothing else.
    /// </summary>
    /// <remarks>
    /// For the links whose secret identifies what it is for on its own. An invitation is one:
    /// naming the address beside it would add nothing the secret does not already settle, and
    /// would put a mailbox into every log and browser history the link passes through.
    /// </remarks>
    /// <param name="publicAddress">
    /// The address the product is published on, or <see langword="null"/> when the installation has
    /// not declared one.
    /// </param>
    /// <param name="screen">The screen's path, without a leading slash.</param>
    /// <param name="token">The secret, which travels as it is.</param>
    /// <returns>The link.</returns>
    public static string Carrying(Uri? publicAddress, string screen, string token)
    {
        var path = $"{screen}?token={Uri.EscapeDataString(token)}";

        return publicAddress is null ? $"/{path}" : new Uri(EndingInSlash(publicAddress), path).AbsoluteUri;
    }

    /// <summary>
    /// Writes a token in the form that survives being part of an address.
    /// </summary>
    /// <remarks>
    /// What the account store hands over is ordinary base-64, whose plus signs arrive at the other
    /// end as spaces and produce a link that fails for a reason nobody could guess from what they
    /// are shown.
    /// </remarks>
    /// <param name="token">The token as the account store issued it.</param>
    /// <returns>The token as it travels.</returns>
    public static string Carry(string token) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));

    /// <summary>
    /// Reads a token back out of the address it travelled in.
    /// </summary>
    /// <remarks>
    /// Whatever is in the address was typed, pasted or forwarded by somebody, so anything at all
    /// may arrive here. Validity is asked before decoding rather than after, because the decoder
    /// answers something that is not a token by throwing.
    /// </remarks>
    /// <param name="carried">The value from the address.</param>
    /// <param name="token">The token, when it was one.</param>
    /// <returns><see langword="true"/> when a token was read.</returns>
    public static bool TryRead(string carried, [NotNullWhen(true)] out string? token)
    {
        token = null;

        if (!Base64Url.IsValid(carried, out var length))
        {
            return false;
        }

        var decoded = new byte[length];

        if (!Base64Url.TryDecodeFromChars(carried, decoded, out var written))
        {
            return false;
        }

        token = Encoding.UTF8.GetString(decoded, 0, written);

        return true;
    }

    private static Uri EndingInSlash(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri($"{address.AbsoluteUri}/");
}
