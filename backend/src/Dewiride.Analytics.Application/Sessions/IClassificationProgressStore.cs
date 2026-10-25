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
    /// <returns>How far judging and reading have got under these rules.</returns>
    Task<ClassificationBookmark> ResumeFromAsync(
        Guid siteId,
        RulesetVersion ruleset,
        DateTimeOffset ifUnrecorded,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves the bookmark forward: each instant to the later of where it is and where it is asked
    /// to go, in one step that no other instance of the engine can come between.
    /// </summary>
    /// <param name="siteId">The site.</param>
    /// <param name="ruleset">The rules the bookmark belongs to.</param>
    /// <param name="reached">How far judging and reading have now got.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when either instant moved; <see langword="false"/> when another
    /// instance of the engine had already been at least as far on both, or the bookmark is gone
    /// with the site it belonged to.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// When <paramref name="reached"/> says more was judged than was read.
    /// </exception>
    Task<bool> AdvanceAsync(
        Guid siteId,
        RulesetVersion ruleset,
        ClassificationBookmark reached,
        CancellationToken cancellationToken);

    /// <summary>
    /// How far every finished visit on a site has been judged, under whichever rules judged it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The furthest of how far the rules being run have read and how far any others judged. Every
    /// ruleset walks a site from the moment it was added, and what a reader is shown is the newest
    /// verdict each visit has, so a visit any of them reached has an answer on the screen — which
    /// is what a series of verdicts needs to know before it may call a stretch of time complete.
    /// </para>
    /// <para>
    /// Only the rules being run are taken as far as they read. A visit they found still in progress
    /// is judged when it finishes; one that other rules found in progress is judged by nothing until
    /// the rules being run reach it in their own walk, so for those only how far they judged counts.
    /// </para>
    /// </remarks>
    /// <param name="siteId">The site.</param>
    /// <param name="running">The rules the engine is running.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The instant, or <see langword="null"/> where judging has never started on the site.
    /// </returns>
    Task<DateTimeOffset?> CaughtUpToAsync(Guid siteId, RulesetVersion running, CancellationToken cancellationToken);
}

/// <summary>
/// How far judging and reading have got through one site's traffic under one set of rules.
/// </summary>
/// <param name="ClassifiedThrough">
/// Every visit that began before this instant has been judged. It waits at the earliest visit that
/// had not finished when it was last read, which is where the next reading starts.
/// </param>
/// <param name="ReadThrough">
/// Every visit that began before this instant has been read, and judged if it had finished. Never
/// earlier than <paramref name="ClassifiedThrough"/>.
/// </param>
public readonly record struct ClassificationBookmark(DateTimeOffset ClassifiedThrough, DateTimeOffset ReadThrough);
