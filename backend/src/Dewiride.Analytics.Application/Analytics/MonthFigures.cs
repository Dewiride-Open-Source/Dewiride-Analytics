using System.Collections.Immutable;

namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// One month's totals, for one population.
/// </summary>
/// <param name="PageViews">Pages delivered, counted as the dashboard counts them.</param>
/// <param name="Visitors">Daily visitors, counted as the overview counts them over the month.</param>
/// <param name="BestDay">The day the most pages were delivered, the earliest of a tie, or nothing where none were.</param>
/// <param name="BestDayPageViews">Pages delivered on that day.</param>
/// <param name="ByWeekday">Pages delivered on each day of the week, Monday first.</param>
/// <param name="ByHour">Pages delivered in each hour of the day, from midnight.</param>
public sealed record MonthFigures(
    long PageViews,
    long Visitors,
    DateOnly? BestDay,
    long BestDayPageViews,
    ImmutableArray<long> ByWeekday,
    ImmutableArray<long> ByHour)
{
    /// <summary>Days in a week, which is how many weekday figures a month carries.</summary>
    public const int Weekdays = 7;

    /// <summary>Hours in a day, which is how many hour figures a month carries.</summary>
    public const int Hours = 24;

    /// <summary>Pages delivered on each day of the week, Monday first.</summary>
    public ImmutableArray<long> ByWeekday { get; } = ByWeekday.Length == Weekdays
        ? ByWeekday
        : throw new ArgumentException("A month carries one figure for each day of the week.", nameof(ByWeekday));

    /// <summary>Pages delivered in each hour of the day, from midnight.</summary>
    public ImmutableArray<long> ByHour { get; } = ByHour.Length == Hours
        ? ByHour
        : throw new ArgumentException("A month carries one figure for each hour of the day.", nameof(ByHour));
}
