using System.Collections.Immutable;
using Dewiride.Analytics.Application.Sessions;
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
/// Builds a classifier with both stores stood in for and the clock held still.
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

    /// <summary>The present moment, held still.</summary>
    public static readonly DateTimeOffset Now = new(2026, 5, 2, 0, 0, 0, TimeSpan.Zero);

    private readonly List<SessionWindow> _windows = [];
    private readonly List<SessionJudgement> _stored = [];
    private readonly List<IReadOnlyCollection<string>> _asked = [];
    private DateTimeOffset _bookmark = AddedAt;

    /// <summary>Reconstructs visits.</summary>
    public ISessionSource Sessions { get; } = Substitute.For<ISessionSource>();

    /// <summary>Keeps verdicts.</summary>
    public IClassificationStore Verdicts { get; } = Substitute.For<IClassificationStore>();

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

    /// <summary>Where the bookmark ended up.</summary>
    public DateTimeOffset Bookmark => _bookmark;

    /// <summary>How the classifier is tuned.</summary>
    public ClassificationOptions Settings { get; init; } = new();

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

        Progress.AdvanceAsync(SiteId, Arg.Any<RulesetVersion>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var to = call.Arg<DateTimeOffset>();

                if (to <= _bookmark)
                {
                    return false;
                }

                _bookmark = to;

                return true;
            });

        Verdicts.SaveAsync(
                SiteId,
                Arg.Any<IReadOnlyCollection<SessionJudgement>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _stored.AddRange(call.Arg<IReadOnlyCollection<SessionJudgement>>());

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
                _windows.Add(call.Arg<SessionWindow>());

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
                _windows.Add(call.Arg<SessionWindow>());

                if (served)
                {
                    return ImmutableArray<ObservedSession>.Empty;
                }

                served = true;

                return ImmutableArray.Create(found);
            });
    }

    /// <summary>Starts the bookmark somewhere other than when the site was added.</summary>
    /// <param name="instant">Where judging should resume from.</param>
    public void ResumeFrom(DateTimeOffset instant) => _bookmark = instant;

    /// <summary>Runs the classifier over the site.</summary>
    /// <returns>What the run got through.</returns>
    public Task<ClassificationOutcome> RunAsync() =>
        new SessionClassifier(
                Sessions,
                Verdicts,
                Progress,
                TrafficClassifier.Current(),
                Names,
                new FakeTimeProvider(Now),
                Options.Create(Settings),
                NullLogger<SessionClassifier>.Instance)
            .CatchUpAsync(SiteId, AddedAt, CancellationToken.None);

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
