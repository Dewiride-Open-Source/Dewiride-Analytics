using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Application.Sessions;

/// <summary>
/// Keeps what the engine concluded about each visit.
/// </summary>
/// <remarks>
/// The verdict is stored because it cannot be re-derived: it is the output of a particular
/// ruleset, and the ruleset changes. Storing it beside the ruleset that produced it is what lets
/// the dashboard say which rules a number came from, and what lets a stretch of history be judged
/// again and compared rather than silently overwritten.
/// </remarks>
public interface IClassificationStore
{
    /// <summary>
    /// Records verdicts.
    /// </summary>
    /// <remarks>
    /// Writing the same verdict twice leaves one row. That is what makes a run safe to interrupt
    /// and safe to duplicate, so neither a restart nor a second instance of the engine needs to
    /// coordinate with anything.
    /// </remarks>
    /// <param name="siteId">The site the visits belong to.</param>
    /// <param name="judgements">What was concluded, and about which visit.</param>
    /// <param name="classifiedAt">When the run reached these conclusions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the verdicts are stored.</returns>
    Task SaveAsync(
        Guid siteId,
        IReadOnlyCollection<SessionJudgement> judgements,
        DateTimeOffset classifiedAt,
        CancellationToken cancellationToken);
}

/// <summary>
/// One visit and what the engine made of it.
/// </summary>
/// <param name="Session">The visit that was judged.</param>
/// <param name="Verdict">The conclusion, with the evidence for and against it.</param>
public readonly record struct SessionJudgement(SessionEvidence Session, ClassificationVerdict Verdict);
