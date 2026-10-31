namespace Dewiride.Analytics.Application.Consolidation;

/// <summary>
/// How many of a month's days a website was measured on.
/// </summary>
/// <remarks>
/// From its first recorded day where that falls inside the month, and up to and including today
/// where the month is still running; none where the month ends before that first day — which a
/// change of zone can leave in front of it — or has not yet begun; every day of it otherwise. What
/// an average per day divides by, so a website measured from the nineteenth is not averaged over
/// days nobody was counting. Where the first day cannot be known (<see cref="Telemetry.FirstDays"/>),
/// every day of the month is counted.
/// </remarks>
public static class DaysMeasured
{
    /// <summary>
    /// Counts the days.
    /// </summary>
    /// <param name="month">The first of the month.</param>
    /// <param name="firstDay">The website's first recorded day, or nothing where it is not known.</param>
    /// <param name="today">Today, on the website's calendar.</param>
    /// <returns>The number of days, never less than none.</returns>
    public static int In(DateOnly month, DateOnly? firstDay, DateOnly today)
    {
        var from = firstDay is { } first && first > month ? first : month;
        var next = month.AddMonths(1);
        var until = today < next ? today.AddDays(1) : next;

        return Math.Max(0, until.DayNumber - from.DayNumber);
    }
}
