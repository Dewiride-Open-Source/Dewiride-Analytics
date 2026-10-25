using System.Collections.Immutable;
using Dewiride.Analytics.Application.Telemetry;
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
/// A visit still in progress holds back nothing but itself. The bookmark keeps two instants: how
/// far every visit has been judged, which waits where the earliest unfinished visit began so that
/// it is found again, and how far every visit has been read, which does not wait for anybody. Each
/// pass reads from the first to new ground past the second, and is handed only what still needs
/// judging — so a page somebody leaves open all afternoon is picked up whole when it is closed,
/// and everything that began while it was open is judged as it finishes rather than after it.
/// </para>
/// <para>
/// A run is safe to interrupt, safe to repeat, and safe to have two of. Verdicts are stored under
/// the visit's own derived identity, so writing one twice leaves one row; the bookmark only moves
/// forward; and both instances reach the same conclusions because the engine is pure.
/// </para>
/// <para>
/// The one question that cannot be answered from stored activity alone is asked here rather than
/// inside the engine. Several companies publish no list of their crawlers' addresses and instead
/// document a domain their machines answer to, and settling that means asking a name server —
/// which the engine may not do and the collector must not. So a pass judges everything once, asks
/// about the addresses behind the visits that came out looking like machinery, and judges those
/// again with the answer. Only the second verdict is kept; the first is a question about which
/// addresses are worth asking after, and its cost is that the engine is pure enough to run twice.
/// </para>
/// <para>
/// A visit that came out looking like somebody reading is not asked about at all. Its address
/// belongs to a person, and sending it to a name server to satisfy a curiosity is not a thing this
/// product does. The price is that a crawler which produced enough engagement to be taken for a
/// reader stays unrecognised, and the answer to that is the address files rather than a wider net.
/// </para>
/// </remarks>
/// <param name="sessions">Reconstructs visits from stored activity.</param>
/// <param name="verdicts">Keeps what was concluded.</param>
/// <param name="progress">Remembers where to resume.</param>
/// <param name="engine">The detection engine.</param>
/// <param name="names">Settles whose crawlers an address belongs to from the name it answers to.</param>
/// <param name="clock">Source of the present moment.</param>
/// <param name="options">How much to work through, and what counts as one visit.</param>
/// <param name="logger">Log sink.</param>
public sealed partial class SessionClassifier(
    ISessionSource sessions,
    IClassificationStore verdicts,
    IClassificationProgressStore progress,
    TrafficClassifier engine,
    ICrawlerNameLookup names,
    TimeProvider clock,
    IOptions<ClassificationOptions> options,
    ILogger<SessionClassifier> logger)
{
    /// <summary>What a pass with nothing to ask about gets back, without asking.</summary>
    private static readonly IReadOnlyDictionary<string, string> NothingSettled =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Judges everything on one site that had finished when the run began and has not been judged
    /// yet.
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

        // Nothing that began after the horizon can be known to have finished, because a visitor who
        // is merely pausing has not left. It is fixed when the run begins. The present moves on while
        // a pass is reading, so a horizon taken again after every pass would always lie a little past
        // the one just reached, and a site already up to date would be read over and over in slivers
        // as wide as the last read took, until the run had spent every pass it was given. Whatever
        // finishes while a run is working is the next run's to judge.
        var horizon = clock.GetUtcNow() - settings.IdleTimeout;

        var bookmark = await progress
            .ResumeFromAsync(siteId, ruleset, siteAddedAt, cancellationToken)
            .ConfigureAwait(false);

        var judged = 0;
        var passes = 0;

        // A run is over once everything that began before the horizon has been read. What was still
        // in progress then stays in progress for the rest of the run, so reading again would find
        // nothing new; the next run, with a later horizon, starts from it.
        while (passes < settings.PassesPerRun && bookmark.ReadThrough < horizon)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Each pass stamps its verdicts with the moment it began, so where two passes judge the
            // same visit, the later pass's conclusion is the one that stands.
            var began = clock.GetUtcNow();

            // The reading starts where judging waits, at the earliest visit that was still in
            // progress, so that visit is found again. How far it reaches is counted from where the
            // last reading stopped instead: the stretch between the two holds nothing but visits
            // already judged, which the reading leaves out, and the visits still waiting.
            var window = new SessionWindow
            {
                SiteId = siteId,
                From = bookmark.ClassifiedThrough,
                To = Earliest(horizon, bookmark.ReadThrough + settings.LongestPass),
                SettledBefore = horizon,
                UnjudgedUnder = ruleset,
                IdleTimeout = settings.IdleTimeout,
                MaxRequestsPerSession = settings.MaxRequestsPerSession,
            };

            var pass = await RunPassAsync(window, began, settings, cancellationToken).ConfigureAwait(false);

            judged += pass.Judged;
            passes++;

            var reached = new ClassificationBookmark(pass.ClassifiedThrough, window.To);

            if (!await progress.AdvanceAsync(siteId, ruleset, reached, cancellationToken).ConfigureAwait(false))
            {
                // Another instance of the engine has already been at least this far, and is the one
                // to carry on from here — or the site has been removed while this run was reading it.
                break;
            }

            bookmark = reached;
        }

        if (judged > 0)
        {
            Log.Judged(logger, judged, siteId, ruleset);
        }

        return new ClassificationOutcome(judged, bookmark.ClassifiedThrough);
    }

    private async Task<PassResult> RunPassAsync(
        SessionWindow window,
        DateTimeOffset began,
        ClassificationOptions settings,
        CancellationToken cancellationToken)
    {
        var found = await sessions.ReadAsync(window, cancellationToken).ConfigureAwait(false);

        var judgements = await JudgeAsync(found, settings, cancellationToken).ConfigureAwait(false);

        if (judgements.Length > 0)
        {
            await verdicts.SaveAsync(window.SiteId, judgements, began, cancellationToken).ConfigureAwait(false);
        }

        return new PassResult(judgements.Length, EarliestUnfinished(found, window.To));
    }

    /// <summary>
    /// Judges every finished visit in a pass, settling what can be settled about who was visiting.
    /// </summary>
    private async Task<ImmutableArray<SessionJudgement>> JudgeAsync(
        ImmutableArray<ObservedSession> found,
        ClassificationOptions settings,
        CancellationToken cancellationToken)
    {
        var judged = found
            .Where(session => session.IsClosed)
            .Select(session => new Reading(session, engine.Classify(session.Evidence)))
            .ToArray();

        var asking = judged
            .Where(WorthAsking)
            .Select(reading => reading.Session.Address)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(settings.MostNameChecksPerPass)
            .ToArray();

        var settled = asking.Length == 0
            ? NothingSettled
            : await names.OperatorsOfAsync(asking, cancellationToken).ConfigureAwait(false);

        if (settled.Count > 0)
        {
            Log.Named(logger, settled.Count, asking.Length);
        }

        return [.. judged.Select(reading => Reconsidered(reading, settled).Judgement)];
    }

    /// <summary>
    /// Whether it is worth asking a name server about the address behind a visit.
    /// </summary>
    /// <remarks>
    /// Three conditions, and each rules out a different waste. There is no address left on a visit
    /// older than the retention window; there is nothing to settle where the collector settled it
    /// already; and a visit that may be somebody reading is treated as somebody reading, whose
    /// address is not ours to ask anybody about.
    /// </remarks>
    private static bool WorthAsking(Reading reading) =>
        !string.IsNullOrEmpty(reading.Session.Address)
        && string.IsNullOrEmpty(reading.Session.Evidence.ConfirmedOperator)
        && !MayBeSomebodyReading(reading.Verdict);

    /// <summary>
    /// Whether a person may be behind a verdict: called one outright, or left open with something
    /// pointing toward one.
    /// </summary>
    /// <remarks>
    /// A visit nothing could be concluded about is not thereby machinery. Where anything observed
    /// points toward a person the address is treated as a person's, which is the only safe way to
    /// treat an address the product is not sure about. For an open verdict every observation is
    /// carried as supporting, so the ones pointing toward a person are found there.
    /// </remarks>
    private static bool MayBeSomebodyReading(ClassificationVerdict verdict) =>
        verdict.Category is TrafficCategory.LikelyHuman
        || (verdict.Category is TrafficCategory.Unknown
            && verdict.Supporting.Any(signal => signal.Direction == SignalDirection.TowardHuman));

    /// <summary>
    /// Judges a visit again where the address turned out to belong to a company's own crawlers.
    /// </summary>
    /// <remarks>
    /// Applied to every visit from a settled address rather than only to the ones that prompted the
    /// question. An address that answers to a company's own name belongs to that company whatever
    /// the visit from it looked like, and two visits from one address cannot have been two
    /// different companies. A visit the collector had already settled keeps that answer, because a
    /// list a company publishes about all of its machines is the stronger of the two statements.
    /// </remarks>
    private Reading Reconsidered(Reading reading, IReadOnlyDictionary<string, string> settled)
    {
        if (!string.IsNullOrEmpty(reading.Session.Evidence.ConfirmedOperator)
            || reading.Session.Address is not { } address
            || !settled.TryGetValue(address, out var operatorName))
        {
            return reading;
        }

        var evidence = reading.Session.Evidence with { ConfirmedOperator = operatorName };

        return new Reading(reading.Session with { Evidence = evidence }, engine.Classify(evidence));
    }

    private static DateTimeOffset EarliestUnfinished(ImmutableArray<ObservedSession> found, DateTimeOffset otherwise) =>
        found.Where(session => !session.IsClosed)
            .Select(session => session.Evidence.StartedAt)
            .DefaultIfEmpty(otherwise)
            .Min();

    private static DateTimeOffset Earliest(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    /// <summary>What one pass got through.</summary>
    /// <param name="Judged">How many visits it judged.</param>
    /// <param name="ClassifiedThrough">
    /// Every visit that began before this has now been judged: where the earliest visit the pass
    /// found still in progress began, or the end of what it read where every visit it found had
    /// finished.
    /// </param>
    private readonly record struct PassResult(int Judged, DateTimeOffset ClassifiedThrough);

    /// <summary>One finished visit and what the engine made of it.</summary>
    /// <param name="Session">The visit, with the address the identity check needs.</param>
    /// <param name="Verdict">What the engine concluded.</param>
    private readonly record struct Reading(ObservedSession Session, ClassificationVerdict Verdict)
    {
        /// <summary>The pair that gets stored, which carries no address.</summary>
        public SessionJudgement Judgement => new(Session.Evidence, Verdict);
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 4001,
            Level = LogLevel.Information,
            Message = "Judged {Count} visit(s) on site {SiteId} under ruleset {Ruleset}.")]
        public static partial void Judged(ILogger logger, int count, Guid siteId, RulesetVersion ruleset);

        [LoggerMessage(
            EventId = 4002,
            Level = LogLevel.Information,
            Message = "Settled {Named} of {Asked} address(es) against the names their operators publish.")]
        public static partial void Named(ILogger logger, int named, int asked);
    }
}

/// <summary>
/// What a run got through.
/// </summary>
/// <param name="Judged">How many visits were judged.</param>
/// <param name="ResumeFrom">
/// Where this run left judging waiting: every visit that began before it has been judged.
/// </param>
public readonly record struct ClassificationOutcome(int Judged, DateTimeOffset ResumeFrom);
