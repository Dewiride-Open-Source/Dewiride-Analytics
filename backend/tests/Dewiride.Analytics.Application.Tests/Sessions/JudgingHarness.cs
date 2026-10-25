using System.Collections.Immutable;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Dewiride.Analytics.Application.Tests.Sessions;

/// <summary>
/// Builds a classifier with both stores stood in for and the clock held still, unless a test says
/// how long a read takes.
/// </summary>
/// <remarks>
/// What is being proven here is when a visit may be judged, which is arithmetic over a bookmark, a
/// window and the present moment. Standing the stores in lets every one of those be set exactly,
/// and the same rules are proven against real servers separately.
/// </remarks>
internal sealed class JudgingHarness
{
    /// <summary>The site every test in this suite works on.</summary>
    public static readonly Guid SiteId = Guid.Parse("0197c0de-0000-7000-8000-000000000001");

    /// <summary>When the site was added.</summary>
    public static readonly DateTimeOffset AddedAt = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The address the site is known by.</summary>
    public const string SiteDomain = "blog.example.com";

    /// <summary>The present moment when a run begins.</summary>
    public static readonly DateTimeOffset Now = new(2026, 5, 2, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly List<SessionWindow> _windows = [];
    private readonly List<SessionJudgement> _stored = [];
    private readonly List<DateTimeOffset> _stampedAt = [];
    private readonly List<NameConfirmation> _kept = [];
    private readonly List<DateTimeOffset> _keptAt = [];
    private readonly List<string> _writes = [];
    private readonly List<IReadOnlyCollection<string>> _asked = [];
    private ClassificationBookmark _bookmark = new(AddedAt, AddedAt);

    /// <summary>What <see cref="Writes"/> records for a batch of answers kept.</summary>
    public const string AnswersWritten = "answers";

    /// <summary>What <see cref="Writes"/> records for a batch of verdicts stored.</summary>
    public const string VerdictsWritten = "verdicts";

    /// <summary>Reconstructs visits.</summary>
    public ISessionSource Sessions { get; } = Substitute.For<ISessionSource>();

    /// <summary>Keeps verdicts.</summary>
    public IClassificationStore Verdicts { get; } = Substitute.For<IClassificationStore>();

    /// <summary>Keeps whose crawler a visit was, where that was settled by name.</summary>
    public INameConfirmationStore Confirmations { get; } = Substitute.For<INameConfirmationStore>();

    /// <summary>Remembers where to resume.</summary>
    public IClassificationProgressStore Progress { get; } = Substitute.For<IClassificationProgressStore>();

    /// <summary>Settles an address against the name it answers to. Settles nothing unless told to.</summary>
    public ICrawlerNameLookup Names { get; } = Substitute.For<ICrawlerNameLookup>();

    /// <summary>Every set of addresses the classifier asked about, in order.</summary>
    public IReadOnlyList<IReadOnlyCollection<string>> Asked => _asked;

    /// <summary>The windows the classifier asked for, in order.</summary>
    public IReadOnlyList<SessionWindow> Windows => _windows;

    /// <summary>The verdicts it stored, in order.</summary>
    public IReadOnlyList<SessionJudgement> Stored => _stored;

    /// <summary>When each batch of stored verdicts says it was reached, in order.</summary>
    public IReadOnlyList<DateTimeOffset> StampedAt => _stampedAt;

    /// <summary>The answers it kept, in order.</summary>
    public IReadOnlyList<NameConfirmation> Kept => _kept;

    /// <summary>When each batch of kept answers says it was settled, in order.</summary>
    public IReadOnlyList<DateTimeOffset> KeptAt => _keptAt;

    /// <summary>
    /// Every write the classifier made, in the order it made them: <see cref="AnswersWritten"/> or
    /// <see cref="VerdictsWritten"/>.
    /// </summary>
    public IReadOnlyList<string> Writes => _writes;

    /// <summary>Whether keeping an answer fails, as a store that has gone away does.</summary>
    public bool KeepingFails { get; init; }

    /// <summary>Where the bookmark ended up: every visit that began before it has been judged.</summary>
    public DateTimeOffset Bookmark => _bookmark.ClassifiedThrough;

    /// <summary>How far the bookmark has read: every visit that began before it has been read.</summary>
    public DateTimeOffset ReadThrough => _bookmark.ReadThrough;

    /// <summary>
    /// Whether another instance of the engine has always already been further than this one gets,
    /// so that no move this one asks for is ever made.
    /// </summary>
    public bool AnotherEngineIsAhead { get; init; }

    /// <summary>How the classifier is tuned.</summary>
    public ClassificationOptions Settings { get; init; } = new();

    /// <summary>
    /// How far the clock moves while the store answers one read. Nothing unless told otherwise, so
    /// the present stays where the run found it.
    /// </summary>
    /// <remarks>
    /// A real read takes time, and the present moves on while it runs. Whether a run is thrown by
    /// that is a question only a clock that moves can ask.
    /// </remarks>
    public TimeSpan EachReadTakes { get; init; } = TimeSpan.Zero;

    /// <summary>What the name check settles, keyed by address.</summary>
    private readonly Dictionary<string, string> _settles = new(StringComparer.Ordinal);

    /// <summary>Makes the name check settle one address as one company's.</summary>
    /// <param name="address">The address a visit arrived from.</param>
    /// <param name="operatorName">The company it turns out to belong to.</param>
    /// <returns>The harness, for chaining.</returns>
    public JudgingHarness Settling(string address, string operatorName)
    {
        _settles[address] = operatorName;

        return this;
    }

    /// <summary>Builds the harness and wires the stand-ins.</summary>
    public JudgingHarness()
    {
        Names.OperatorsOfAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var asked = call.Arg<IReadOnlyCollection<string>>();

                _asked.Add([.. asked]);

                return Task.FromResult<IReadOnlyDictionary<string, string>>(
                    asked.Where(_settles.ContainsKey)
                        .ToDictionary(address => address, address => _settles[address], StringComparer.Ordinal));
            });

        Progress.ResumeFromAsync(SiteId, Arg.Any<RulesetVersion>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => _bookmark);

        Progress.AdvanceAsync(
                SiteId,
                Arg.Any<RulesetVersion>(),
                Arg.Any<ClassificationBookmark>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Advance(call.Arg<ClassificationBookmark>()));

        Verdicts.SaveAsync(
                SiteId,
                Arg.Any<IReadOnlyCollection<SessionJudgement>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _stored.AddRange(call.Arg<IReadOnlyCollection<SessionJudgement>>());
                _stampedAt.Add(call.Arg<DateTimeOffset>());
                _writes.Add(VerdictsWritten);

                return Task.CompletedTask;
            });

        Confirmations.RecordAsync(
                SiteId,
                Arg.Any<IReadOnlyCollection<NameConfirmation>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (KeepingFails)
                {
                    return Task.FromException(new InvalidOperationException("The store has gone away."));
                }

                _kept.AddRange(call.Arg<IReadOnlyCollection<NameConfirmation>>());
                _keptAt.Add(call.Arg<DateTimeOffset>());
                _writes.Add(AnswersWritten);

                return Task.CompletedTask;
            });

        Answer();
    }

    /// <summary>Makes the source return these visits, whatever window it is asked for.</summary>
    /// <param name="found">The visits to return.</param>
    public void Answer(params ObservedSession[] found) =>
        Sessions.ReadAsync(Arg.Any<SessionWindow>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Read(call.Arg<SessionWindow>());

                return ImmutableArray.Create(found);
            });

    /// <summary>Makes the source return these visits once, then nothing.</summary>
    /// <param name="found">The visits to return on the first read.</param>
    public void AnswerOnce(params ObservedSession[] found)
    {
        var served = false;

        Sessions.ReadAsync(Arg.Any<SessionWindow>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Read(call.Arg<SessionWindow>());

                if (served)
                {
                    return ImmutableArray<ObservedSession>.Empty;
                }

                served = true;

                return ImmutableArray.Create(found);
            });
    }

    /// <summary>
    /// Makes the source answer the way the store does: with the visits that began inside the window
    /// asked for, less the ones already judged on everything they now hold.
    /// </summary>
    /// <param name="stored">Every visit the store holds.</param>
    public void Holding(params ObservedSession[] stored) =>
        Sessions.ReadAsync(Arg.Any<SessionWindow>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var window = call.Arg<SessionWindow>();

                Read(window);

                return ImmutableArray.CreateRange(stored.Where(visit =>
                    visit.Evidence.StartedAt >= window.From
                    && visit.Evidence.StartedAt < window.To
                    && !JudgedAsItIs(visit, window.UnjudgedUnder)));
            });

    /// <summary>Starts the bookmark somewhere other than when the site was added.</summary>
    /// <param name="instant">Where judging and reading should both resume from.</param>
    public void ResumeFrom(DateTimeOffset instant) => _bookmark = new ClassificationBookmark(instant, instant);

    /// <summary>
    /// Starts the bookmark with judging waiting behind how far it has read, as it does while a
    /// visit is still in progress.
    /// </summary>
    /// <param name="classifiedThrough">Where judging waits.</param>
    /// <param name="readThrough">How far everything has been read.</param>
    public void ResumeFrom(DateTimeOffset classifiedThrough, DateTimeOffset readThrough) =>
        _bookmark = new ClassificationBookmark(classifiedThrough, readThrough);

    /// <summary>Lets the present move on, as it does between two runs.</summary>
    /// <param name="interval">How far.</param>
    public void Wait(TimeSpan interval) => _clock.Advance(interval);

    /// <summary>Runs the classifier over the site.</summary>
    /// <returns>What the run got through.</returns>
    public Task<ClassificationOutcome> RunAsync() =>
        new SessionClassifier(
                Sessions,
                Verdicts,
                Confirmations,
                Progress,
                TrafficClassifier.Current(),
                Names,
                _clock,
                Options.Create(Settings),
                NullLogger<SessionClassifier>.Instance)
            .CatchUpAsync(new SiteRegistration(SiteId, SiteDomain, AddedAt), CancellationToken.None);

    /// <summary>Notes the window asked for, and lets the clock run on while it is read.</summary>
    /// <param name="window">The window the classifier asked for.</param>
    private void Read(SessionWindow window)
    {
        _windows.Add(window);
        _clock.Advance(EachReadTakes);
    }

    /// <summary>
    /// Moves the bookmark the way the store does: each instant forward only, and nothing at all
    /// where another engine is always ahead.
    /// </summary>
    /// <param name="reached">How far the classifier says it has got.</param>
    /// <returns>Whether anything moved.</returns>
    private bool Advance(ClassificationBookmark reached)
    {
        var judgedFurther = reached.ClassifiedThrough > _bookmark.ClassifiedThrough;
        var readFurther = reached.ReadThrough > _bookmark.ReadThrough;

        if (AnotherEngineIsAhead || (!judgedFurther && !readFurther))
        {
            return false;
        }

        _bookmark = new ClassificationBookmark(
            judgedFurther ? reached.ClassifiedThrough : _bookmark.ClassifiedThrough,
            readFurther ? reached.ReadThrough : _bookmark.ReadThrough);

        return true;
    }

    /// <summary>
    /// Whether a verdict is already stored for this visit under these rules, reached on everything
    /// the visit now holds.
    /// </summary>
    /// <param name="visit">The visit.</param>
    /// <param name="ruleset">The rules it is being judged under.</param>
    /// <returns><see langword="true"/> where the store would leave it out of a reading.</returns>
    private bool JudgedAsItIs(ObservedSession visit, RulesetVersion ruleset) =>
        _stored.Exists(judgement =>
            judgement.Session.SessionKey == visit.Evidence.SessionKey
            && judgement.Session.EndedAt == visit.Evidence.EndedAt
            && judgement.Verdict.RulesetVersion == ruleset);

    /// <summary>An ordinary browser string, which says nothing about who is behind it.</summary>
    public const string Anonymous =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/141.0.0.0 Safari/537.36";

    /// <summary>Builds a visit.</summary>
    /// <param name="startedAt">When it began.</param>
    /// <param name="pages">How many pages it asked for.</param>
    /// <param name="isClosed">Whether it is over.</param>
    /// <param name="address">The address it arrived from, where one is still kept.</param>
    /// <param name="userAgent">What it called itself.</param>
    /// <param name="confirmedOperator">Who the collector had already established it to be.</param>
    /// <returns>The visit.</returns>
    public static ObservedSession Visit(
        DateTimeOffset startedAt,
        int pages = 1,
        bool isClosed = true,
        string? address = null,
        string? userAgent = null,
        string? confirmedOperator = null) =>
        new(
            new SessionEvidence
            {
                SessionKey = $"visitor:{startedAt.ToUnixTimeMilliseconds()}",
                StartedAt = startedAt,
                EndedAt = startedAt.AddMinutes(pages),
                Requests =
                [
                    .. Enumerable.Range(0, pages).Select(page =>
                        new ObservedRequest(startedAt.AddMinutes(page), $"/posts/{page}", 200)),
                ],
                Surfaces = [IngestSurface.CloudflareWorker],
                UserAgent = userAgent ?? "Mozilla/5.0 (compatible; GPTBot/1.2; +https://openai.com/gptbot)",
                ConfirmedOperator = confirmedOperator,
            },
            isClosed,
            address);

    /// <summary>
    /// Builds a visit that reads like somebody reading.
    /// </summary>
    /// <remarks>
    /// A browser that ran the tracker, held a page for half a minute, scrolled most of the way
    /// down and used a pointer. Whether a visit looks like this is what decides whether its
    /// address is anybody's business, so the shape matters rather than the particular numbers.
    /// </remarks>
    /// <param name="startedAt">When it began.</param>
    /// <param name="address">The address it arrived from.</param>
    /// <returns>The visit.</returns>
    public static ObservedSession Reader(DateTimeOffset startedAt, string address) =>
        new(
            new SessionEvidence
            {
                SessionKey = $"reader:{startedAt.ToUnixTimeMilliseconds()}",
                StartedAt = startedAt,
                EndedAt = startedAt.AddMinutes(3),
                Requests = [new ObservedRequest(startedAt, "/posts/why-authenticity-matters", 200)],
                Surfaces = [IngestSurface.BrowserTracker],
                UserAgent = Anonymous,
                Language = "en-GB",
                ViewportWidth = 1440,
                EngagedMs = 42_000,
                MaxScrollDepthPercent = 78,
                HadPointerInteraction = true,
                HadKeyboardInteraction = false,
                DeclaredWebDriver = false,
            },
            true,
            address);

    /// <summary>
    /// Builds a visit that opened one page with the tracker running and did nothing anybody watched.
    /// </summary>
    /// <remarks>
    /// The engine can settle nothing about it, and that is the point: whether an unsettled visit
    /// with something pointing toward a person has its address asked about is what this shape
    /// decides.
    /// </remarks>
    /// <param name="startedAt">When it began.</param>
    /// <param name="address">The address it arrived from.</param>
    /// <returns>The visit.</returns>
    public static ObservedSession Glance(DateTimeOffset startedAt, string address)
    {
        var reader = Reader(startedAt, address);

        return reader with
        {
            Evidence = reader.Evidence with
            {
                SessionKey = $"glance:{startedAt.ToUnixTimeMilliseconds()}",
                EndedAt = startedAt,
                EngagedMs = 0,
                MaxScrollDepthPercent = 0,
                HadPointerInteraction = false,
            },
        };
    }
}
