namespace Dewiride.Analytics.Application.Sites;

/// <summary>
/// Creating, listing and withdrawing the keys a site's server may report with.
/// </summary>
/// <remarks>
/// Uncached, unlike <see cref="IIngestKeyCatalog"/>. This is reached from the settings screen a
/// handful of times, and a cached list is how somebody keeps seeing a key they have just
/// withdrawn.
/// </remarks>
public interface IIngestKeyDirectory
{
    /// <summary>
    /// Lists the keys that still work for a site, newest first.
    /// </summary>
    /// <param name="siteId">The site.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The keys, without their secrets, which are not recoverable.</returns>
    Task<IReadOnlyList<IngestKeyDescription>> ListAsync(Guid siteId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a key for a site.
    /// </summary>
    /// <param name="siteId">The site the key will report for.</param>
    /// <param name="name">What to call it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The new key, including the secret. This is the only moment the secret exists outside the
    /// caller's own storage; nothing keeps a copy of it.
    /// </returns>
    Task<IssuedIngestKey> IssueAsync(Guid siteId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Withdraws a key.
    /// </summary>
    /// <param name="siteId">The site the key belongs to.</param>
    /// <param name="keyId">The key to withdraw.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if a key that still worked was withdrawn; <see langword="false"/>
    /// when there was no such key on that site, so that a caller cannot use this to discover
    /// which key identifiers on an install are real.
    /// </returns>
    Task<bool> RevokeAsync(Guid siteId, Guid keyId, CancellationToken cancellationToken);
}

/// <summary>
/// One key, as far as it can be described after the fact.
/// </summary>
/// <param name="Id">Identity of the key.</param>
/// <param name="Name">What it was called when it was created.</param>
/// <param name="Preview">
/// Last few characters of the secret, enough to recognise which stored copy this is.
/// </param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="LastUsedAt">
/// When it was last accepted, or <see langword="null"/> if nothing has ever reported with it.
/// </param>
public sealed record IngestKeyDescription(
    Guid Id,
    string Name,
    string Preview,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// A key at the one moment its secret is available.
/// </summary>
/// <param name="Description">Everything about it that survives.</param>
/// <param name="Secret">
/// The secret, in full. It is never stored, never logged and never returned again.
/// </param>
public sealed record IssuedIngestKey(IngestKeyDescription Description, string Secret);
