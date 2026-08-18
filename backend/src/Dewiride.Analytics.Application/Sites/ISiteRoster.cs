namespace Dewiride.Analytics.Application.Sites;

/// <summary>
/// Lists every site on this installation.
/// </summary>
/// <remarks>
/// For the parts of the product that work on behalf of the system rather than on behalf of a
/// person — judging traffic, retention, rebuilds. Kept apart from
/// <see cref="ISiteDirectory"/> deliberately: that one answers "which sites may this person see",
/// and a list that ignores membership must never be reachable from anything answering a request.
/// </remarks>
public interface ISiteRoster
{
    /// <summary>
    /// Lists every site, oldest first.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sites.</returns>
    Task<IReadOnlyList<SiteRegistration>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A site, as the background work needs to know it.
/// </summary>
/// <param name="Id">Identity of the site.</param>
/// <param name="AddedAt">
/// When it was added. Nothing can have been observed before then, so it is where work on a site
/// that has never been processed begins.
/// </param>
public readonly record struct SiteRegistration(Guid Id, DateTimeOffset AddedAt);
