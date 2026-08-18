namespace Dewiride.Analytics.Domain.Sites;

/// <summary>
/// A secret that lets a server report traffic for one site.
/// </summary>
/// <remarks>
/// <para>
/// The browser tracker needs no credential, because everything it reports is observed from the
/// connection it arrives on and a site identifier is printed in the page source of every page it
/// measures. A server-side reporter is the opposite case: it asserts the visitor's address, their
/// user agent and the status the site returned, none of which the collector can verify. Accepting
/// those assertions from anybody would let a stranger write whatever traffic they liked into
/// somebody else's account, so the assertion is what this key authorises.
/// </para>
/// <para>
/// Only a hash is held. The key is shown to the person who created it once, at the moment it is
/// created, and cannot be recovered afterwards — a stolen backup of the control plane must not
/// hand over the ability to write into every customer's telemetry.
/// </para>
/// </remarks>
public sealed class SiteIngestKey
{
    /// <summary>Longest name a key may be given.</summary>
    public const int MaxNameLength = 60;

    /// <summary>Identity of the key. Safe to show; it is not the secret.</summary>
    public Guid Id { get; private set; }

    /// <summary>Site this key may report for. A key can never report for any other.</summary>
    public Guid SiteId { get; private set; }

    /// <summary>What the person who created it called it, so a list of keys can be told apart.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// Hash of the secret, which is the only form of it that is stored.
    /// </summary>
    /// <remarks>
    /// A plain digest rather than a password hash, deliberately. The secret is not a password: it
    /// is generated here with full entropy and never chosen by a person, so there is no dictionary
    /// to run against it, and it is verified on the busiest write path in the product where a
    /// deliberately slow hash would be a denial of service rather than a defence.
    /// </remarks>
    public string TokenHash { get; private set; }

    /// <summary>
    /// Last few characters of the secret, so the owner can recognise which key is which without
    /// the whole of it being recoverable.
    /// </summary>
    public string Preview { get; private set; }

    /// <summary>When the key was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// When the key was last accepted, or <see langword="null"/> if it never has been.
    /// </summary>
    /// <remarks>
    /// Recorded coarsely rather than on every report: the collector holds a resolved key briefly
    /// in memory, and this is refreshed when that lapses. It answers "is this key still in use",
    /// which is the question somebody deciding whether to remove one is actually asking.
    /// </remarks>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>When the key was withdrawn, or <see langword="null"/> while it still works.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Whether the key still authorises anything.</summary>
    public bool IsRevoked => RevokedAt is not null;

    private SiteIngestKey()
    {
        Name = string.Empty;
        TokenHash = string.Empty;
        Preview = string.Empty;
    }

    /// <summary>Issues a key.</summary>
    /// <param name="id">Identity to assign.</param>
    /// <param name="siteId">Site the key reports for.</param>
    /// <param name="name">What to call it.</param>
    /// <param name="tokenHash">Hash of the generated secret.</param>
    /// <param name="preview">Last few characters of the generated secret.</param>
    /// <param name="createdAt">Creation time, from the injected clock.</param>
    /// <exception cref="ArgumentException">
    /// The name, hash or preview is empty or whitespace.
    /// </exception>
    public SiteIngestKey(
        Guid id,
        Guid siteId,
        string name,
        string tokenHash,
        string preview,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(preview);

        Id = id;
        SiteId = siteId;
        Name = Shorten(name);
        TokenHash = tokenHash;
        Preview = preview;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Withdraws the key.
    /// </summary>
    /// <param name="at">When it was withdrawn, from the injected clock.</param>
    /// <remarks>
    /// The row stays. A withdrawn key is part of the account's history — it is how somebody
    /// answers "what was reporting for this site in March" — and deleting the row would take that
    /// with it while gaining nothing, since the secret was never stored in the first place.
    /// </remarks>
    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;

    /// <summary>Records that the key was accepted.</summary>
    /// <param name="at">When it was accepted, from the injected clock.</param>
    public void RecordUse(DateTimeOffset at) => LastUsedAt = at;

    private static string Shorten(string value)
    {
        var trimmed = value.Trim();

        return trimmed.Length <= MaxNameLength ? trimmed : trimmed[..MaxNameLength];
    }
}
