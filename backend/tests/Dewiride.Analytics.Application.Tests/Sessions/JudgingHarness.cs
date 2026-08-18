using System.Collections.Immutable;
using Dewiride.Analytics.Application.Sessions;
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
    private DateTimeOffset _bookmark = AddedAt;

    /// <summary>Reconstructs visits.</summary>
    public ISessionSource Sessions { get; } = Substitute.For<ISessionSource>();

    /// <summary>Keeps verdicts.</summary>
    public IClassificationStore Verdicts { get; } = Substitute.For<IClassificationStore>();

    /// <summary>Remembers where to resume.</summary>
    public IClassificationProgressStore Progress { get; } = Substitute.For<IClassificationProgressStore>();

    /// <summary>The windows the classifier asked for, in order.</summary>
    public IReadOnlyList<SessionWindow> Windows => _windows;

    /// <summary>The verdicts it stored, in order.</summary>
    public IReadOnlyList<SessionJudgement> Stored => _stored;

    /// <summary>Where the bookmark ended up.</summary>
    public DateTimeOffset Bookmark => _bookmark;

    /// <summary>How the classifier is tuned.</summary>
    public ClassificationOptions Settings { get; init; } = new();

    /// <summary>Builds the harness and wires the stand-ins.</summary>
    public JudgingHarness()
    {
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
                new FakeTimeProvider(Now),
                Options.Create(Settings),
                NullLogger<SessionClassifier>.Instance)
            .CatchUpAsync(SiteId, AddedAt, CancellationToken.None);

    /// <summary>Builds a visit.</summary>
    /// <param name="startedAt">When it began.</param>
    /// <param name="pages">How many pages it asked for.</param>
    /// <param name="isClosed">Whether it is over.</param>
    /// <returns>The visit.</returns>
    public static ObservedSession Visit(DateTimeOffset startedAt, int pages = 1, bool isClosed = true) =>
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
                UserAgent = "Mozilla/5.0 (compatible; GPTBot/1.2; +https://openai.com/gptbot)",
            },
            isClosed);
}
