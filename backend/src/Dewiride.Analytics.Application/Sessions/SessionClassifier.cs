using System.Collections.Immutable;
using Dewiride.Analytics.Classification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Application.Sessions;

/// <summary>
/// Brings one site's verdicts up to date.
/// </summary>
/// <remarks>
/// <para>
/// The join between the engine, which judges a visit and knows nothing else, and the two stores
/// that hold the activity and the conclusions. Everything about <em>when</em> a visit may be
/// judged lives here, and it comes down to one rule: a visit is judged only once it is certain to
/// be over, because a verdict on a visit still in progress would be replaced within the hour and
/// would have been wrong in the meantime.
/// </para>
/// <para>
/// A run is safe to interrupt, safe to repeat, and safe to have two of. Verdicts are stored under
/// the visit's own derived identity, so writing one twice leaves one row; the bookmark only moves
/// forward; and both instances reach the same conclusions because the engine is pure.
/// </para>
/// </remarks>
/// <param name="sessions">Reconstructs visits from stored activity.</param>
/// <param name="verdicts">Keeps what was concluded.</param>
/// <param name="progress">Remembers where to resume.</param>
/// <param name="engine">The detection engine.</param>
/// <param name="clock">Source of the present moment.</param>
/// <param name="options">How much to work through, and what counts as one visit.</param>
/// <param name="logger">Log sink.</param>
public sealed partial class SessionClassifier(
    ISessionSource sessions,
    IClassificationStore verdicts,
    IClassificationProgressStore progress,
    TrafficClassifier engine,
    TimeProvider clock,
    IOptions<ClassificationOptions> options,
    ILogger<SessionClassifier> logger)
{
    /// <summary>
    /// Judges everything on one site that has finished and has not been judged yet.
    /// </summary>
    /// <param name="siteId">The site to work through.</param>
    /// <param name="siteAddedAt">When the site was added, which is where judging starts from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the run got through.</returns>
    public async Task<ClassificationOutcome> CatchUpAsync(
        Guid siteId,
        DateTimeOffset siteAddedAt,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var ruleset = engine.RulesetVersion;

        var resumeFrom = await progress
            .ResumeFromAsync(siteId, ruleset, siteAddedAt, cancellationToken)
            .ConfigureAwait(false);

        var judged = 0;
        var passes = 0;

        while (passes < settings.PassesPerRun)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var now = clock.GetUtcNow();

            // Nothing that began after this can be known to have finished, because a visitor who
            // is merely pausing has not left.
            var horizon = now - settings.IdleTimeout;

            if (resumeFrom >= horizon)
            {
                break;
            }

            var pass = await RunPassAsync(siteId, resumeFrom, horizon, now, settings, cancellationToken)
                .ConfigureAwait(false);

            judged += pass.Judged;
            passes++;

            // The bookmark stops at the earliest visit still in progress. Everything after it was
            // judged all the same and will simply be judged again next time, which costs a little
            // work and means one visitor reading all afternoon does not hold up the whole site.
            if (pass.ResumeFrom <= resumeFrom
                || !await progress.AdvanceAsync(siteId, ruleset, pass.ResumeFrom, cancellationToken)
                    .ConfigureAwait(false))
            {
                break;
            }

            resumeFrom = pass.ResumeFrom;
        }

        if (judged > 0)
        {
            Log.Judged(logger, judged, siteId, ruleset);
        }

        return new ClassificationOutcome(judged, resumeFrom);
    }

    private async Task<PassResult> RunPassAsync(
        Guid siteId,
        DateTimeOffset resumeFrom,
        DateTimeOffset horizon,
        DateTimeOffset now,
        ClassificationOptions settings,
        CancellationToken cancellationToken)
    {
        var to = Earliest(horizon, resumeFrom + settings.LongestPass);

        var found = await sessions.ReadAsync(
                new SessionWindow
                {
                    SiteId = siteId,
                    From = resumeFrom,
                    To = to,
                    IdleTimeout = settings.IdleTimeout,
                    MaxRequestsPerSession = settings.MaxRequestsPerSession,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var judgements = Judge(found);

        if (judgements.Length > 0)
        {
            await verdicts.SaveAsync(siteId, judgements, now, cancellationToken).ConfigureAwait(false);
        }

        return new PassResult(judgements.Length, EarliestUnfinished(found, to));
    }

    private ImmutableArray<SessionJudgement> Judge(ImmutableArray<ObservedSession> found) =>
    [
        .. found.Where(session => session.IsClosed)
            .Select(session => new SessionJudgement(session.Evidence, engine.Classify(session.Evidence))),
    ];

    private static DateTimeOffset EarliestUnfinished(ImmutableArray<ObservedSession> found, DateTimeOffset otherwise) =>
        found.Where(session => !session.IsClosed)
            .Select(session => session.Evidence.StartedAt)
            .DefaultIfEmpty(otherwise)
            .Min();

    private static DateTimeOffset Earliest(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private readonly record struct PassResult(int Judged, DateTimeOffset ResumeFrom);

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 4001,
            Level = LogLevel.Information,
            Message = "Judged {Count} visit(s) on site {SiteId} under ruleset {Ruleset}.")]
        public static partial void Judged(ILogger logger, int count, Guid siteId, RulesetVersion ruleset);
    }
}

/// <summary>
/// What a run got through.
/// </summary>
/// <param name="Judged">How many visits were judged.</param>
/// <param name="ResumeFrom">Where the next run will pick up.</param>
public readonly record struct ClassificationOutcome(int Judged, DateTimeOffset ResumeFrom);
