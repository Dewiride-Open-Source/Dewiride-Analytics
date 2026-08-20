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
    /// an idle timeout earlier so that such a visit is recognised as having started before the
    /// window and left out, rather than being returned as a second, shorter visit beginning
    /// wherever the window happens to open. A caller may therefore move this forward into the
    /// middle of a visit it has already been given without being handed the remainder as a new one.
    /// </remarks>
    public required DateTimeOffset From { get; init; }

    /// <summary>
    /// Only visits that began before this instant are returned, and a visit whose last activity
    /// falls before it can no longer grow.
    /// </summary>
    /// <remarks>
    /// Both readings come from the same instant on purpose. Activity is read up to
    /// <see cref="To"/> plus <see cref="IdleTimeout"/>, so a visit that ended before
    /// <see cref="To"/> has been observed falling silent for a full idle timeout and is therefore
    /// genuinely over — rather than merely appearing to be over because nothing later was read.
    /// The caller keeps this at or before the present moment less one idle timeout.
    /// </remarks>
    public required DateTimeOffset To { get; init; }

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
public readonly record struct ObservedSession(SessionEvidence Evidence, bool IsClosed);
