namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// The busiest of some days, or of some hours, offered one at a time in any order.
/// </summary>
/// <remarks>
/// <para>
/// The one the most pages were delivered in and, of those with as many, the earliest — whatever order
/// they are offered in, because the store hands a month's days and hours back in no order it promises,
/// and a peak kept for a month outlives the reports that could correct it. One in which nothing was
/// delivered is never the busiest, so a stretch with nothing in it has none.
/// </para>
/// <para>
/// A month's best day and its best hour are both decided here, so the two can never come to mean
/// different things by "busiest". Two offers of the same moment are two figures, never added together:
/// the hour the clocks go back through is offered twice, once on each side of the change, and the
/// busier of the two is that hour's.
/// </para>
/// </remarks>
/// <typeparam name="TWhen">
/// When the pages were delivered: a day, or an hour as the website's clock showed its first minute.
/// </typeparam>
public sealed class Peak<TWhen>
    where TWhen : struct, IComparable<TWhen>
{
    /// <summary>The busiest offered so far, or nothing where nothing offered had anything delivered.</summary>
    public TWhen? When { get; private set; }

    /// <summary>Pages delivered then.</summary>
    public long PageViews { get; private set; }

    /// <summary>
    /// Considers one more day or hour.
    /// </summary>
    /// <param name="when">The day or hour.</param>
    /// <param name="pageViews">Pages delivered in it.</param>
    public void Offer(TWhen when, long pageViews)
    {
        var busier = pageViews > PageViews;
        var asBusyAndEarlier = pageViews == PageViews
            && pageViews > 0
            && When is { } held
            && when.CompareTo(held) < 0;

        if (busier || asBusyAndEarlier)
        {
            When = when;
            PageViews = pageViews;
        }
    }
}
