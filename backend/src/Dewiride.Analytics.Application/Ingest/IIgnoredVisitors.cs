namespace Dewiride.Analytics.Application.Ingest;

/// <summary>
/// The visitors whose visit is being turned away, for as long as the visit lasts.
/// </summary>
/// <remarks>
/// <para>
/// Only a page view says where a visitor came from. A page goes on to report how it is going, that
/// it was left and what was pressed on it, and none of those reports names a sender — so turning a
/// visit away by its sender means remembering the visitor whose arrival named one, and turning away
/// what they send next.
/// </para>
/// <para>
/// A visitor is remembered until they have been quiet for as long as a visit lasts, which is when
/// the engine would begin a new visit for them anyway. Every report that is turned away in the
/// meantime counts as being heard from, so a page left open goes on being turned away for as long
/// as it goes on reporting.
/// </para>
/// <para>
/// Asked about every report on the busiest path in the product, so an implementation answers from
/// memory and never from a store. Forgetting is the safe failure: a report from a visitor no longer
/// remembered is stored, and with no arrival before it, it never becomes a visit.
/// </para>
/// </remarks>
public interface IIgnoredVisitors
{
    /// <summary>
    /// Starts remembering a visitor whose visit began from a sender the installation ignores.
    /// </summary>
    /// <param name="siteId">The website the visit is on.</param>
    /// <param name="visitorKey">The visitor, as the collector keys them.</param>
    void Remember(Guid siteId, string visitorKey);

    /// <summary>
    /// Whether a visitor is still remembered, which counts as having heard from them again.
    /// </summary>
    /// <param name="siteId">The website the report is for.</param>
    /// <param name="visitorKey">The visitor, as the collector keys them.</param>
    /// <returns>Whether the visitor's visit is being turned away.</returns>
    bool Recalls(Guid siteId, string visitorKey);
}
