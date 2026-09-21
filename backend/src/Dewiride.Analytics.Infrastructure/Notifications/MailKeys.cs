using System.Security.Cryptography;
using System.Text;

namespace Dewiride.Analytics.Infrastructure.Notifications;

/// <summary>
/// What makes two attempts to send the same message one message.
/// </summary>
/// <remarks>
/// <para>
/// Every key here names the thing the message is about rather than the moment it was composed. A
/// key taken from the clock makes every attempt a new message, which is the same as having no key
/// at all while looking as though the question had been thought about. Where an instant is part of
/// a key it is the instant of the event being reported on — the closure of an account, say, as it
/// was recorded — and never the moment of sending: the same closure announced twice is one message,
/// and a second closure of the same account is another.
/// </para>
/// <para>
/// A key names who the message goes to as well as what it is about, which is added where the
/// message is composed rather than here: the service that delivers the hosted edition's mail
/// treats one key as one message and refuses the same key sent to a second address, so a message
/// to every owner of an account has to be a message per owner.
/// </para>
/// <para>
/// Where the thing a message is about is a secret — a reset token, an invitation's secret — the key
/// is a digest of it rather than the secret itself. Whatever delivers the message keeps the key,
/// often for longer than the secret is valid, and a credential sitting in somebody else's log
/// because it was convenient to identify a message with is the kind of leak nobody goes looking
/// for.
/// </para>
/// </remarks>
public static class MailKeys
{
    /// <summary>
    /// A key for a message that carries a secret.
    /// </summary>
    /// <param name="kind">Which message this is, so two kinds carrying one secret stay distinct.</param>
    /// <param name="secret">The secret the message carries.</param>
    /// <returns>The key.</returns>
    public static string ForSecret(string kind, string secret) =>
        $"{kind}:{Digest(secret)}";

    /// <summary>
    /// A key for a message about something the product has a name for.
    /// </summary>
    /// <param name="kind">Which message this is.</param>
    /// <param name="subject">What it is about.</param>
    /// <returns>The key.</returns>
    public static string For(string kind, Guid subject) =>
        $"{kind}:{subject:n}";

    /// <summary>
    /// A key for a message about an event that happened to something the product has a name for.
    /// </summary>
    /// <param name="kind">Which message this is.</param>
    /// <param name="subject">What it is about.</param>
    /// <param name="at">When the event it reports on happened, as recorded — never the moment of sending.</param>
    /// <returns>The key.</returns>
    public static string For(string kind, Guid subject, DateTimeOffset at) =>
        $"{kind}:{subject:n}:{at.UtcDateTime:yyyyMMddTHHmmssZ}";

    /// <summary>
    /// A message's key narrowed to one mailbox.
    /// </summary>
    /// <param name="key">The key for what the message is about.</param>
    /// <param name="address">The mailbox it goes to.</param>
    /// <returns>The key for that message to that mailbox.</returns>
    public static string ForRecipient(string key, string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return $"{key}:{Digest(address.Trim().ToUpperInvariant())}";
    }

    /// <summary>
    /// Shortened deliberately.
    /// </summary>
    /// <remarks>
    /// Half of a SHA-256 is 128 bits, which is past the point where two different secrets could
    /// collide on one key in any quantity of mail this product will ever send, and short enough to
    /// read in a log beside the rest of the key.
    /// </remarks>
    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32];
}
