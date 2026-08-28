using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Application.Sessions;

/// <summary>
/// Reconstructs visits from the events that make them up.
/// </summary>
/// <remarks>
/// <para>
/// Sessions are derived rather than stored. Grouping happens on the way out, so changing what
/// counts as one visit is a change to a statement rather than a migration followed by a rebuild —
/// and there is never a table half-way between the old definition and the new one.
/// </para>
/// <para>
/// Only activity that could be attributed to a visitor takes part. A surface that could not
/// derive a visitor key has not observed an anonymous visitor; it has observed nothing about who
/// was there, and grouping all of those together would invent one impossibly busy phantom and
/// judge it.
/// </para>
/// </remarks>
public interface ISessionSource
{
    /// <summary>
    /// Reconstructs the visits that began inside a window.
    /// </summary>
    /// <param name="window">Which site, which stretch of time, and what counts as one visit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The visits, oldest first, finished and unfinished alike.</returns>
    Task<ImmutableArray<ObservedSession>> ReadAsync(SessionWindow window, CancellationToken cancellationToken);
}

/// <summary>
/// Which visits to reconstruct, and what counts as one.
/// </summary>
public sealed record SessionWindow
{
    /// <summary>The site whose activity is being grouped.</summary>
    public required Guid SiteId { get; init; }

    /// <summary>Only visits that began at or after this instant are returned.</summary>
    /// <remarks>
    /// A visit already under way at this instant is not one that began at it. Activity is read from
    /// a day earlier so that such a visit is recognised as having started before the window and
    /// left out, rather than being returned as a second, shorter visit beginning wherever the
    /// window happens to open. A caller may therefore move this forward into the middle of a visit
    /// it has already been given without being handed the remainder as a new one.
    /// </remarks>
    public required DateTimeOffset From { get; init; }

    /// <summary>Only visits that began before this instant are returned.</summary>
    /// <remarks>
    /// Which visits this window is answerable for, and nothing else. Where a visit ends decides
    /// whether it is over; where it began decides whose window it belongs to, so that a caller
    /// working forward covers a site once and only once however long any one visit ran.
    /// </remarks>
    public required DateTimeOffset To { get; init; }

    /// <summary>
    /// A visit whose last activity falls before this instant is over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The present moment less one idle timeout, and never further on than that: a visitor who is
    /// merely pausing has not left, so nothing after it can be known to have finished. It is a
    /// separate instant from <see cref="To"/> because the two answer separate questions, and
    /// reading a visit's fate off the edge of the window it happened to be attributed to is how a
    /// verdict comes to depend on where a boundary fell rather than on what the visitor did.
    /// </para>
    /// <para>
    /// A caller working through a backlog is asking about visits that finished long ago, and every
    /// one of them is over whatever window it lands in. A caller that has caught up is asking about
    /// the last few minutes, where the two instants meet.
    /// </para>
    /// </remarks>
    public required DateTimeOffset SettledBefore { get; init; }

    /// <summary>How long a visitor may be quiet before their next activity counts as a new visit.</summary>
    public required TimeSpan IdleTimeout { get; init; }

    /// <summary>
    /// Most pages carried back for any one visit.
    /// </summary>
    /// <remarks>
    /// A sweep can ask for tens of thousands of pages in a single visit, and carrying all of them
    /// back would let one visitor decide how much memory the engine uses. The pages returned are
    /// the earliest, and the page count on the visit stays exact.
    /// </remarks>
    public required int MaxRequestsPerSession { get; init; }
}

/// <summary>
/// One reconstructed visit.
/// </summary>
/// <param name="Evidence">Everything the engine is allowed to reason about.</param>
/// <param name="IsClosed">
/// Whether the visit is over. An unfinished visit is not judged: a verdict reached half-way
/// through would be replaced within the hour, and the honest thing to do with a visit still in
/// progress is wait for it.
/// </param>
/// <param name="Address">
/// One of the addresses the visit arrived from, or <see langword="null"/> once none is left.
/// </param>
/// <remarks>
/// The address rides alongside the evidence rather than inside it, and the distinction is the
/// whole reason this is a record of two parts. <see cref="SessionEvidence"/> is the closed set the
/// engine reasons about, and an address in it would let a detector reach conclusions from where
/// somebody lives. What the address is for is settling identity against what an operator publishes
/// about its own machines, which happens before the engine is asked and never inside it.
/// <para>
/// Absent for anything older than the retention window, because the address is erased 72 hours
/// after the activity was received. A visit re-judged a year later is judged without it, which is
/// correct: the evidence for that visit no longer includes an address, and inventing one would be
/// the only alternative.
/// </para>
/// </remarks>
public readonly record struct ObservedSession(SessionEvidence Evidence, bool IsClosed, string? Address = null);
