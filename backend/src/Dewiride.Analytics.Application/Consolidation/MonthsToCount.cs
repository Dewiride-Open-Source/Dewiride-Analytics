using System.Collections.Immutable;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;

namespace Dewiride.Analytics.Application.Consolidation;

/// <summary>
/// Decides which of a website's months to count on this run.
/// </summary>
/// <remarks>
/// <para>
/// Only finished months are ever kept. The month running now is answered from its reports
/// whenever somebody looks, so keeping it would only add a second, staler copy of the same figure.
/// </para>
/// <para>
/// A month is counted while every report it was counted from is still held, and never afterwards:
/// once the oldest of them could be gone, a count would come back short without saying so, so the
/// month keeps whatever was last kept of it — or, never having been counted, stays uncounted.
/// </para>
/// <para>
/// Within that, a month is counted when nothing has been kept of it, and again whenever what was
/// kept no longer describes it: it was read in another zone, counted by older arithmetic or only
/// part of the way through, counted before judging had finished with it and judging has since moved
/// on, or counted by older rules that the rules now running have since judged the whole of.
/// </para>
/// </remarks>
public static class MonthsToCount
{
    /// <summary>
    /// Chooses the months, the oldest first, since they are the nearest to losing their reports.
    /// </summary>
    /// <param name="survey">The website's calendar and what was kept of it.</param>
    /// <param name="judging">How far judging has reached on the website.</param>
    /// <param name="timeZoneId">The zone the website is read in now.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="most">The most months to choose.</param>
    /// <returns>The months, oldest first.</returns>
    public static ImmutableArray<SurveyedMonth> Choose(
        MonthSurvey survey,
        SiteJudging judging,
        string timeZoneId,
        DateTimeOffset now,
        int most)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(judging);

        return
        [
            .. survey.Months
                .Where(month => month.Window.To <= now && now < Retention.CountableUntil(month.Window.From))
                .Where(month => NeedsCounting(month, judging, timeZoneId))
                .Take(most),
        ];
    }

    private static bool NeedsCounting(SurveyedMonth month, SiteJudging judging, string timeZoneId) =>
        month.Kept is not { } kept
        || !string.Equals(kept.TimeZoneId, timeZoneId, StringComparison.Ordinal)
        || kept.CountingRules < CountingRules.Current
        || kept.CountedTo < month.Window.To
        || JudgingMovedOn(month, kept, judging)
        || NewerRulesFinished(month, kept, judging);

    /// <summary>
    /// Whether judging has reached further into the stretch a month's people are decided by than it
    /// had when the month was counted.
    /// </summary>
    /// <remarks>
    /// That stretch begins a whole visit before the month, since a visit that began the evening
    /// before counts the people it held on the first. Movement before it changes nothing in the
    /// month, so a month judging has not yet reached is left alone however far judging moves
    /// elsewhere — which is what keeps a backlog of judging from recounting the same months on every
    /// run.
    /// </remarks>
    private static bool JudgingMovedOn(SurveyedMonth month, KeptMonth kept, SiteJudging judging)
    {
        if (judging.Settled is not { } settled)
        {
            return false;
        }

        var reached = settled < month.Window.To ? settled : month.Window.To;
        var decidedFrom = month.Window.From - VisitorKeys.LongestVisit;
        var counted = kept.JudgedTo > decidedFrom ? kept.JudgedTo : decidedFrom;

        return reached > counted;
    }

    /// <summary>
    /// Whether the rules now running have judged the whole of a month that was counted by other
    /// rules, or counted before they had finished with it.
    /// </summary>
    /// <remarks>
    /// Improving the rules judges a website's whole history again, and a month's verdicts change as
    /// the walk passes through it. Counting it again only once the walk has passed its end counts it
    /// once rather than on every run of the walk. Rules older than the ones that counted it never
    /// count it again, so an engine rolled back to an earlier release leaves a newer one's months as
    /// they are.
    /// </remarks>
    private static bool NewerRulesFinished(SurveyedMonth month, KeptMonth kept, SiteJudging judging) =>
        judging.RunningJudgedThrough is { } through
        && (kept.Ruleset < judging.Running || kept.RulesetJudgedTo < month.Window.To)
        && through >= month.Window.To
        && through > kept.RulesetJudgedTo;
}

/// <summary>
/// How far judging has reached on one website.
/// </summary>
/// <param name="Settled">
/// Where its verdicts stop being complete, as the dashboard reckons it, or nothing where judging
/// has never started on it.
/// </param>
/// <param name="RunningJudgedThrough">
/// How far the rules now running have judged it, or nothing where they have not started.
/// </param>
/// <param name="Running">The rules now running.</param>
public sealed record SiteJudging(DateTimeOffset? Settled, DateTimeOffset? RunningJudgedThrough, RulesetVersion Running);
