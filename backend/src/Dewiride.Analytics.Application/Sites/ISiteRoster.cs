namespace Dewiride.Analytics.Application.Sites;

/// <summary>
/// Lists every site on this installation whose account is open.
/// </summary>
/// <remarks>
/// For the parts of the product that work on behalf of the system rather than on behalf of a
/// person — judging traffic, keeping each month's totals, rebuilds. Kept apart from
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

    /// <summary>
    /// Does some work about a site while nothing can remove it, provided it still exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For background work that writes what is kept of a site: a site removed while the work was
    /// under way is not written about afterwards. Removing a site deletes its telemetry and then the
    /// site itself, and this waits for both, or holds the removal back until the work is done — so a
    /// write either lands before the removal deletes it, or finds no site and is never made.
    /// </para>
    /// <para>
    /// A site whose account has been closed still exists until the closure is carried out, and what
    /// is kept of it goes with it then.
    /// </para>
    /// </remarks>
    /// <param name="siteId">The site.</param>
    /// <param name="work">The work, given a cancellation token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> where the site existed and the work was done; <see langword="false"/>
    /// where it no longer exists and nothing was done.
    /// </returns>
    Task<bool> WhileItExistsAsync(Guid siteId, Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}

/// <summary>
/// A site, as the background work needs to know it.
/// </summary>
/// <param name="Id">Identity of the site.</param>
/// <param name="Domain">
/// The address it is known by, which is what tells a reader moving between its own pages from a
/// visitor another site sent.
/// </param>
/// <param name="AddedAt">
/// When it was added. Nothing can have been observed before then, so it is where work on a site
/// that has never been processed begins.
/// </param>
/// <param name="TimeZoneId">The zone its days, weeks and months are read in.</param>
public readonly record struct SiteRegistration(Guid Id, string Domain, DateTimeOffset AddedAt, string TimeZoneId);
