using System.Collections.Immutable;

namespace Dewiride.Analytics.Application.Consolidation;

/// <summary>
/// Decides which of a website's months to count the busiest pages of on this run.
/// </summary>
/// <remarks>
/// On the terms <see cref="MonthsToCount"/> decides a month's totals by: only finished months, only
/// while every report they are counted from is still held, the oldest first, a run's allowance at a
/// time, and again whenever what was kept no longer describes the month — read in another zone, counted
/// by older arithmetic or only part of the way through, counted before judging had finished with it, or
/// counted by older rules the rules now running have since judged the whole of. A month's pages are kept
/// on a row of their own, so a month whose totals or posts are settled can still need its pages counted,
/// and the other way round. Nothing else leaves a kept month of pages behind: every path is counted, so
/// there is no list of them to fall out of date.
/// </remarks>
public static class PageMonthsToCount
{
    /// <summary>
    /// Chooses the months, the oldest first, since they are the nearest to losing their reports.
    /// </summary>
    /// <param name="survey">The website's calendar.</param>
    /// <param name="keptPages">How each month whose pages were kept was counted.</param>
    /// <param name="judging">How far judging has reached on the website.</param>
    /// <param name="timeZoneId">The zone the website is read in now.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="most">The most months to choose.</param>
    /// <returns>The months, oldest first.</returns>
    public static ImmutableArray<SurveyedMonth> Choose(
        MonthSurvey survey,
        ImmutableArray<KeptPageMonth> keptPages,
        SiteJudging judging,
        string timeZoneId,
        DateTimeOffset now,
        int most)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(judging);

        var kept = keptPages.ToDictionary(page => page.Month, page => page.Described);

        return
        [
            .. survey.Months
                .Where(month => MonthsToCount.Countable(month, now))
                .Where(month => MonthsToCount.NeedsCounting(
                    month,
                    kept.GetValueOrDefault(month.Month),
                    judging,
                    timeZoneId,
                    CountingRules.Pages))
                .Take(most),
        ];
    }
}
