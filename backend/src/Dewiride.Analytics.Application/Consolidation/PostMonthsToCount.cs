using System.Collections.Immutable;

namespace Dewiride.Analytics.Application.Consolidation;

/// <summary>
/// Decides which of a website's months to count the posts of on this run.
/// </summary>
/// <remarks>
/// <para>
/// On the terms <see cref="MonthsToCount"/> decides a month's totals by: only finished months, only
/// while every report they are counted from is still held, the oldest first, a run's allowance at a
/// time, and again whenever what was kept no longer describes the month. A month's posts are kept on a
/// row of their own, so a month whose totals are settled can still need its posts counted, and the
/// other way round.
/// </para>
/// <para>
/// One more thing can leave a kept month of posts behind: the list of posts it was counted for. A post
/// found after the month was counted is missing from it, so a month is counted again once its list
/// differs from the website's — but only once what was kept is an hour old. A sitemap is read a page a
/// second, so the list moves on every run while a large one is being read, and counting a month again
/// on every run would read its reports again to add one post at a time.
/// </para>
/// </remarks>
public static class PostMonthsToCount
{
    /// <summary>
    /// How old a kept month of posts has to be before a change to the list of posts counts it again.
    /// </summary>
    public static readonly TimeSpan RecountAfter = TimeSpan.FromHours(1);

    /// <summary>
    /// Chooses the months, the oldest first, since they are the nearest to losing their reports.
    /// </summary>
    /// <param name="survey">The website's calendar.</param>
    /// <param name="keptPosts">How each month whose posts were kept was counted.</param>
    /// <param name="judging">How far judging has reached on the website.</param>
    /// <param name="timeZoneId">The zone the website is read in now.</param>
    /// <param name="fingerprint">
    /// The website's list of posts, as <see cref="Posts.PostFingerprint"/> gives it, or nothing where it
    /// lists none — which leaves nothing to count.
    /// </param>
    /// <param name="now">The present moment.</param>
    /// <param name="most">The most months to choose.</param>
    /// <returns>The months, oldest first.</returns>
    public static ImmutableArray<SurveyedMonth> Choose(
        MonthSurvey survey,
        ImmutableArray<KeptPostMonth> keptPosts,
        SiteJudging judging,
        string timeZoneId,
        string? fingerprint,
        DateTimeOffset now,
        int most)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(judging);

        if (fingerprint is null)
        {
            return [];
        }

        var kept = keptPosts.ToDictionary(post => post.Month);

        return
        [
            .. survey.Months
                .Where(month => MonthsToCount.Countable(month, now))
                .Where(month => NeedsCounting(month, kept.GetValueOrDefault(month.Month), judging, timeZoneId, fingerprint, now))
                .Take(most),
        ];
    }

    private static bool NeedsCounting(
        SurveyedMonth month,
        KeptPostMonth? kept,
        SiteJudging judging,
        string timeZoneId,
        string fingerprint,
        DateTimeOffset now) =>
        kept is null
        || MonthsToCount.NeedsCounting(month, kept.Described, judging, timeZoneId, CountingRules.Posts)
        || (!string.Equals(kept.Fingerprint, fingerprint, StringComparison.Ordinal)
            && now - kept.Described.CountedAt >= RecountAfter);
}
