using Dewiride.Analytics.Classification;

namespace Dewiride.Analytics.Application.Sessions;

/// <summary>
/// Remembers where judging should resume for each site.
/// </summary>
/// <remarks>
/// Control-plane state rather than telemetry: it is small, it changes constantly, and it is the
/// sort of thing a relational database is good at. It also has to survive being written by two
/// instances of the engine at once, which is why the bookmark only ever moves forward.
/// </remarks>
public interface IClassificationProgressStore
{
    /// <summary>
    /// Reads where judging should resume, starting a bookmark if the site has none.
    /// </summary>
    /// <param name="siteId">The site.</param>
    /// <param name="ruleset">The rules currently compiled into this build.</param>
    /// <param name="ifUnrecorded">
    /// Where to begin when the site has never been judged under these rules. Normally the moment
    /// the site was added, since nothing can have been observed before then.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The instant every earlier visit has already been judged through.</returns>
    Task<DateTimeOffset> ResumeFromAsync(
        Guid siteId,
        RulesetVersion ruleset,
        DateTimeOffset ifUnrecorded,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves the bookmark forward.
    /// </summary>
    /// <param name="siteId">The site.</param>
    /// <param name="ruleset">The rules the bookmark belongs to.</param>
    /// <param name="classifiedThrough">The instant everything before has now been judged through.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the bookmark moved.</returns>
    Task<bool> AdvanceAsync(
        Guid siteId,
        RulesetVersion ruleset,
        DateTimeOffset classifiedThrough,
        CancellationToken cancellationToken);
}
