using System.Collections.Immutable;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Classification.Sessions;

/// <summary>
/// Everything the engine is allowed to reason about when judging one session.
/// </summary>
/// <remarks>
/// <para>
/// A closed set of values rather than a handle on a store. The engine cannot fetch anything it
/// was not given, which is what makes a verdict reproducible from a fixture and what stops a
/// detector quietly acquiring a dependency on the network or the clock.
/// </para>
/// <para>
/// The three-state readings are three-state for the reason the whole product exists: a surface
/// that could not observe an interaction has not observed the absence of one.
/// <see langword="null"/> means nobody was watching and must never be weighed as evidence of
/// anything.
/// </para>
/// </remarks>
public sealed record SessionEvidence
{
    private readonly int? _pageCount;

    /// <summary>Identity of the session being judged.</summary>
    public required string SessionKey { get; init; }

    /// <summary>When the first request in the session was received.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the last was.</summary>
    public required DateTimeOffset EndedAt { get; init; }

    /// <summary>Every page the session asked for, oldest first.</summary>
    public required ImmutableArray<ObservedRequest> Requests { get; init; }

    /// <summary>Which capture surfaces contributed to this session.</summary>
    /// <remarks>
    /// Load-bearing rather than diagnostic. What a surface is able to see decides how the
    /// absence of a reading should be read, and a session seen only by a server is a session
    /// about which nothing behavioural can be concluded.
    /// </remarks>
    public required ImmutableArray<IngestSurface> Surfaces { get; init; }

    /// <summary>What the visitor said it was. Attacker-controlled; never treated as identity on its own.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Language the visitor declared, where any surface could observe one.</summary>
    public string? Language { get; init; }

    /// <summary>Viewport width the browser reported, where a browser surface ran.</summary>
    public int? ViewportWidth { get; init; }

    /// <summary>Milliseconds the pages were actually in front of somebody, summed across the session.</summary>
    public int? EngagedMs { get; init; }

    /// <summary>Furthest any page was scrolled, as a percentage of its height.</summary>
    public byte? MaxScrollDepthPercent { get; init; }

    /// <summary>Whether any pointer interaction was observed. Null means no surface could see.</summary>
    public bool? HadPointerInteraction { get; init; }

    /// <summary>Whether any keyboard interaction was observed. Null means no surface could see.</summary>
    public bool? HadKeyboardInteraction { get; init; }

    /// <summary>Whether the client declared itself under automation control.</summary>
    public bool? DeclaredWebDriver { get; init; }

    /// <summary>How long the session lasted.</summary>
    public TimeSpan Duration => EndedAt - StartedAt;

    /// <summary>
    /// How many pages the session asked for.
    /// </summary>
    /// <remarks>
    /// Counted rather than measured from <see cref="Requests"/>, because that array is capped when
    /// a session is rebuilt from stored activity: one sweep can ask for tens of thousands of pages
    /// in a single visit, and holding all of them in order to count them would let a visitor decide
    /// how much memory the engine uses. The count stays exact and the array is the earliest of
    /// them. Left unset — as a hand-written fixture leaves it — it is the length of the array.
    /// </remarks>
    public int PageCount
    {
        get => _pageCount ?? Requests.Length;
        init => _pageCount = value;
    }

    /// <summary>Whether a browser ran the tracker during this session.</summary>
    public bool ScriptRan => Surfaces.Contains(IngestSurface.BrowserTracker);

    /// <summary>Whether the no-script image was fetched, which means something rendered the page.</summary>
    public bool ImagesFetched => Surfaces.Contains(IngestSurface.NoScriptPixel);

    /// <summary>
    /// Whether anything in the request path reported this session.
    /// </summary>
    /// <remarks>
    /// Only these surfaces observe a visitor that never executes anything, so only they can
    /// establish that a session existed at all when no script ever ran.
    /// </remarks>
    public bool ServerObserved => Surfaces.Any(surface => surface is not (
        IngestSurface.Unknown or IngestSurface.BrowserTracker or IngestSurface.NoScriptPixel));
}

/// <summary>
/// One page a session asked for.
/// </summary>
/// <param name="At">When the request was received, by the collector's own clock.</param>
/// <param name="Path">
/// Path component of the requested URL. Written by whoever is visiting the site: never
/// interpolated into a statement, never rendered as markup, and never placed in a signal
/// parameter that reaches a screen.
/// </param>
/// <param name="StatusCode">What the site answered, where a surface could observe it.</param>
public readonly record struct ObservedRequest(DateTimeOffset At, string Path, short? StatusCode);
