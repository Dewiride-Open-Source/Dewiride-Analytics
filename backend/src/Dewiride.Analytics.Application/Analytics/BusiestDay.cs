namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// The busiest of some days, offered one at a time in any order.
/// </summary>
/// <remarks>
/// The day the most pages were delivered on and, of days with as many, the earliest — whatever
/// order they are offered in, because the store hands a month's days back in no order it promises,
/// and a busiest day kept for a month outlives the reports that could correct it. A day nothing was
/// delivered on is never the busiest, so a stretch with nothing in it has none.
/// </remarks>
public sealed class BusiestDay
{
    /// <summary>The busiest day offered so far, or nothing where no day had anything delivered.</summary>
    public DateOnly? Day { get; private set; }

    /// <summary>Pages delivered on that day.</summary>
    public long PageViews { get; private set; }

    /// <summary>
    /// Considers one more day.
    /// </summary>
    /// <param name="day">The day.</param>
    /// <param name="pageViews">Pages delivered on it.</param>
    public void Offer(DateOnly day, long pageViews)
    {
        var busier = pageViews > PageViews;
        var asBusyAndEarlier = pageViews == PageViews && pageViews > 0 && day < Day;

        if (busier || asBusyAndEarlier)
        {
            Day = day;
            PageViews = pageViews;
        }
    }
}
