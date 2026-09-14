using System.Collections.Immutable;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Application.Sessions;

/// <summary>
/// Answers what is happening on a site at this moment.
/// </summary>
/// <remarks>
/// <para>
/// The join between the store, which knows who has reported in the last half hour, and the engine,
/// which knows what a visitor looks like. It is the third thing in this project that puts those two
/// together, and the only one that keeps nothing: a verdict is reached once, when a visit is over,
/// and this reads a stretch of minutes that is still running. Nothing here is written, nothing here
/// supersedes anything, and nothing here is counted into any total the rest of the product reports.
/// </para>
/// <para>
/// What it may say about a visitor is decided by <see cref="SettledIdentity"/> and is deliberately
/// far less than the engine produced. The engine is asked in full, because asking it a narrower
/// question would be a second copy of its rules kept in step by nobody; what comes back is then
/// held to what cannot be withdrawn.
/// </para>
/// <para>
/// No name server is asked about anybody here, and that is a decision rather than an omission. The
/// classifier will ask about an address behind a visit that came out looking like machinery, and
/// refuses to ask about one that came out looking like a reader, because a reader's address is not
/// ours to ask anybody about. Half-way through a visit that distinction does not exist yet — a
/// person who has not scrolled reads as machinery — so asking here would send readers' addresses to
/// third parties on the strength of their not having scrolled yet. What a company publishes about
/// its own machines was settled by the collector and travels as an answer, which is enough.
/// </para>
/// <para>
/// The three readings are taken one after another rather than at once. They are small range scans
/// over the same few minutes of one site, the store serves them from the same marks, and asking for
/// three at a time would treble what one watched screen costs the store on every beat. They are
/// three reads a few milliseconds apart of a store that is still being written to, so a report that
/// becomes visible between two of them is in one and not the other; the pages can stand a visitor
/// out from the headline for one beat, and the next beat resolves it.
/// </para>
/// <para>
/// What one visitor did is read over the minutes of the reading it was opened from rather than over
/// a fresh present moment. The row was drawn from one stretch of minutes, and a trail read over a
/// stretch even slightly different — a beat later, across a minute boundary — shows a number of
/// pages that disagrees with the number printed beside it. So a trail is asked for under a
/// reading's own instant, that instant is held to being about now, and the reach back from it is
/// the same one the row had.
/// </para>
/// </remarks>
/// <param name="telemetry">Reads the telemetry store.</param>
/// <param name="engine">The detection engine.</param>
/// <param name="clock">Source of the present moment.</param>
/// <param name="settings">What counts as one visit, which is also what counts as being here.</param>
public sealed class LiveTrafficReader(
    ITelemetryQueries telemetry,
    TrafficClassifier engine,
    TimeProvider clock,
    IOptions<ClassificationOptions> settings)
{
    /// <summary>
    /// How far ahead of this clock a reading's instant may be and still be about now.
    /// </summary>
    /// <remarks>
    /// A reading is stamped by the engine's own clock, so an instant ahead of it is another
    /// instance of the engine a few seconds off, or a request somebody wrote by hand. One minute
    /// covers the first and refuses the second.
    /// </remarks>
    public static readonly TimeSpan Skew = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Reads what is happening on one site now.
    /// </summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="siteDomain">The site's own address, so it is never one of its own sources.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The reading, stamped with the moment it was taken.</returns>
    /// <exception cref="ArgumentNullException">The scope is missing.</exception>
    /// <exception cref="ArgumentException">The site's address is missing.</exception>
    public async Task<LiveTraffic> ReadAsync(
        TenantScope scope,
        string siteDomain,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteDomain);

        var now = clock.GetUtcNow();
        var window = TheseMinutes(now);

        var visitors = await telemetry
            .GetSiteLiveVisitorsAsync(
                scope,
                new SiteLiveVisitorsQuery(window, siteDomain, SiteLiveVisitorsQuery.MostVisitors),
                cancellationToken)
            .ConfigureAwait(false);

        var minutes = await telemetry
            .GetSiteLiveActivityAsync(scope, new SiteLiveActivityQuery(window), cancellationToken)
            .ConfigureAwait(false);

        var pages = await telemetry
            .GetSiteLivePagesAsync(
                scope,
                new SiteLivePagesQuery(window, SiteLivePagesQuery.MostPages),
                cancellationToken)
            .ConfigureAwait(false);

        return new LiveTraffic(
            now,
            window.From,
            visitors.VisitorsSeen,
            [.. visitors.Visitors.Select(Read)],
            [.. minutes],
            [.. pages]);
    }

    /// <summary>
    /// Whether an instant is one a reading could have been taken at.
    /// </summary>
    /// <remarks>
    /// No further ahead than <see cref="Skew"/>, and no further back than a visit can reach. A
    /// screen somebody has held still may be an hour old and its rows must still open; a day back
    /// is as far as any visit runs and is what the list of finished visits already answers about,
    /// so a trail under a reading older than that would be a question about the past wearing the
    /// clothes of one about now.
    /// </remarks>
    /// <param name="at">The instant a reading reported itself taken at.</param>
    /// <returns>Whether a trail may be read under it.</returns>
    public bool IsAboutNow(DateTimeOffset at) => IsAboutNow(at, clock.GetUtcNow(), TimeSpan.Zero);

    /// <summary>
    /// Whether an instant is about now against one reading of the clock, with some grace behind the
    /// far end.
    /// </summary>
    /// <param name="at">The instant a reading reported itself taken at.</param>
    /// <param name="now">The clock, read once by the caller.</param>
    /// <param name="grace">How far behind the far end the instant may still fall.</param>
    /// <returns>Whether the instant is within the bounds.</returns>
    private static bool IsAboutNow(DateTimeOffset at, DateTimeOffset now, TimeSpan grace) =>
        at <= now + Skew && at >= now - VisitorKeys.LongestVisit - grace;

    /// <summary>
    /// Reads what one visitor has been doing, over the minutes of the reading their row was drawn
    /// from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is asked of the engine here. A trail is what the reports say, and what may be said
    /// about the visitor was settled when their row was drawn — so this adds no conclusion, and a
    /// visitor nothing may yet be said about is opened and read without one appearing.
    /// </para>
    /// <para>
    /// The instant is held to <see cref="IsAboutNow(DateTimeOffset)"/>, with <see cref="Skew"/> of
    /// grace behind the far end. The caller asks that question first, against its own reading of
    /// the clock, and an instant it admitted must not be refused here because the clock moved on
    /// between the two.
    /// </para>
    /// </remarks>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="visitorKey">The visitor, as the reading of who is here named them.</param>
    /// <param name="at">
    /// The instant of the reading the visitor's row was drawn from, which is the stretch of minutes
    /// the trail is read over.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The steps, oldest first, and the instant they were read under. Empty where the key names
    /// nobody those minutes hold, which is what a visitor who has left looks like.
    /// </returns>
    /// <exception cref="ArgumentNullException">The scope is missing.</exception>
    /// <exception cref="ArgumentException">The visitor key is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The instant is not one a reading could have been taken at.
    /// </exception>
    public async Task<LiveTrail> ReadTrailAsync(
        TenantScope scope,
        string visitorKey,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(visitorKey);

        if (!IsAboutNow(at, clock.GetUtcNow(), Skew))
        {
            throw new ArgumentOutOfRangeException(nameof(at), at, "A trail is read under a reading taken about now.");
        }

        var steps = await telemetry
            .GetSiteLiveTrailAsync(
                scope,
                new SiteLiveTrailQuery(TheseMinutes(at), visitorKey, SiteLiveTrailQuery.MostSteps),
                cancellationToken)
            .ConfigureAwait(false);

        return new LiveTrail(at, [.. steps]);
    }

    /// <summary>
    /// The stretch of minutes a reading covers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// As long as a visitor may be quiet before their next activity would count as a new visit. That
    /// is the same silence the engine measures a visit's end by, and reusing it is the point: being
    /// here and not yet having finished are one idea, so a second number for the second of them
    /// would eventually disagree with the first.
    /// </para>
    /// <para>
    /// The near end is taken back to a whole minute so that the drawing of it has whole minutes to
    /// draw. Left where it fell, the first minute of the picture would be the part of one that
    /// happened to lie inside the window, and would be shown as a quiet minute on every reading.
    /// The window is a little wider than the silence as a result, never narrower, which is the safe
    /// direction: nobody who is here is left out of it.
    /// </para>
    /// </remarks>
    /// <param name="at">The instant the reading is taken at.</param>
    /// <returns>The window.</returns>
    private TimeRange TheseMinutes(DateTimeOffset at)
    {
        var opened = at - settings.Value.IdleTimeout;

        return new TimeRange(opened - TimeSpan.FromTicks(opened.Ticks % TimeSpan.TicksPerMinute), at);
    }

    /// <summary>
    /// Asks the engine about one visitor, and keeps the answer only where it may be shown.
    /// </summary>
    /// <param name="visitor">The visitor, and the evidence the window holds about them.</param>
    /// <returns>The visitor, named or not.</returns>
    private LiveReading Read(LiveVisitor visitor) =>
        new(visitor, SettledIdentity.Of(engine.Classify(visitor.Evidence)));
}

/// <summary>
/// What is happening on a site at one moment.
/// </summary>
/// <remarks>
/// Stamped with the instant it was taken so that everything a reader is shown about how long ago
/// something happened is measured against the same clock the answer was measured against. Rendered
/// against the reader's own clock instead, a machine an hour out would report visitors who arrived
/// in the future.
/// </remarks>
/// <param name="At">The moment the reading was taken.</param>
/// <param name="From">Where the stretch of minutes it covers begins.</param>
/// <param name="VisitorsSeen">
/// How many visitors reported in that stretch, counted over all of them rather than over the ones
/// this reading had room for.
/// </param>
/// <param name="Visitors">Those it had room for, the one most recently active first.</param>
/// <param name="Minutes">Every minute the stretch covers, oldest first, including the empty ones.</param>
/// <param name="Pages">
/// The pages visitors are on, the one holding most first; every visitor seen stands on exactly one
/// of them.
/// </param>
public sealed record LiveTraffic(
    DateTimeOffset At,
    DateTimeOffset From,
    int VisitorsSeen,
    ImmutableArray<LiveReading> Visitors,
    ImmutableArray<LiveMinute> Minutes,
    ImmutableArray<LivePage> Pages);

/// <summary>
/// One visitor who is here, and what may be said about them.
/// </summary>
/// <param name="Visitor">Everything the window holds about them.</param>
/// <param name="Named">
/// What the engine concluded, where the conclusion rests on evidence that cannot be withdrawn, and
/// <see langword="null"/> otherwise — which is the answer for most people and is not a gap. A
/// visitor nothing may yet be said about is judged in full once their visit has finished.
/// </param>
public readonly record struct LiveReading(LiveVisitor Visitor, ClassificationVerdict? Named);

/// <summary>
/// What one visitor has been doing in the last stretch of minutes.
/// </summary>
/// <remarks>
/// Carries no account of who they are. That was settled when their row was drawn, over these same
/// minutes, and a second answer to the same question taken moments later would be free to disagree
/// with the first. Read over the reading's own minutes, which is what makes its steps and the row's
/// page count one reading.
/// </remarks>
/// <param name="At">
/// The instant of the reading this trail sits under, which names the stretch of minutes the steps
/// were read over.
/// </param>
/// <param name="Steps">
/// The pages they were on and the controls they operated, oldest first. Empty where the key names
/// nobody the stretch holds.
/// </param>
public sealed record LiveTrail(DateTimeOffset At, ImmutableArray<VisitStep> Steps);
