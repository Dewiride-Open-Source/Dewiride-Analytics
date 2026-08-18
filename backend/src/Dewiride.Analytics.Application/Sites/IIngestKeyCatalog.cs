namespace Dewiride.Analytics.Application.Sites;

/// <summary>
/// Turns a secret presented by a server-side reporter into the site it may report for.
/// </summary>
/// <remarks>
/// Separate from <see cref="IIngestKeyDirectory"/> for the same reason <see cref="ISiteCatalog"/>
/// is separate from <see cref="ISiteDirectory"/>: this runs on every report and needs a cached,
/// read-only answer, while managing keys needs tracked entities and writes. Implementations cache
/// both outcomes — a secret that resolves to nothing is the shape a guessing attack takes, and it
/// must not become a database query per attempt.
/// </remarks>
public interface IIngestKeyCatalog
{
    /// <summary>
    /// Resolves what a presented secret authorises.
    /// </summary>
    /// <param name="presentedSecret">The secret exactly as the caller sent it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// What the secret authorises, or <see langword="null"/> when it matches no key or matches
    /// one that has been withdrawn.
    /// </returns>
    Task<IngestAuthorization?> AuthorizeAsync(string presentedSecret, CancellationToken cancellationToken);
}

/// <summary>
/// What a presented secret turned out to authorise.
/// </summary>
/// <remarks>
/// Carries the site rather than letting the caller name one. A server-side reporter therefore
/// cannot file traffic under a site other than the one its key was issued for, and there is no
/// site identifier in the request body for anyone to change.
/// </remarks>
public sealed record IngestAuthorization
{
    /// <summary>The site whose traffic may be reported.</summary>
    public required Guid SiteId { get; init; }

    /// <summary>Which key was presented, so that use can be attributed to it.</summary>
    public required Guid KeyId { get; init; }
}
