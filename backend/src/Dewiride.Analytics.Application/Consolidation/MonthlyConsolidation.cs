using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Application.Consolidation;

/// <summary>
/// Keeps a website's finished months, counting each while its reports remain.
/// </summary>
/// <remarks>
/// <para>
/// It only ever adds and replaces. Nothing here deletes anything, kept months included: a website's
/// months go when the website does, by the same removal that takes its reports, and never because
/// something looked missing from here. A count that looked for what to delete by what the control
/// plane no longer lists would delete everything on an engine pointed at an empty or restored
/// database.
/// </para>
/// <para>
/// A month that fails to count is left as it was and tried again on the next run; the months after
/// it are counted regardless. A website removed while its months were being counted keeps none of
/// them: the control plane is asked again immediately before anything is written.
/// </para>
/// </remarks>
/// <param name="roster">Which websites still exist.</param>
/// <param name="months">Where months are counted and kept.</param>
/// <param name="progress">How far judging has reached.</param>
/// <param name="options">How many months a run counts.</param>
/// <param name="classification">How long a visit may be quiet before it counts as over.</param>
/// <param name="clock">The present moment.</param>
/// <param name="logger">Log sink.</param>
public sealed partial class MonthlyConsolidation(
    ISiteRoster roster,
    IMonthConsolidation months,
    IClassificationProgressStore progress,
    IOptions<ConsolidationOptions> options,
    IOptions<ClassificationOptions> classification,
    TimeProvider clock,
    ILogger<MonthlyConsolidation> logger)
{
    /// <summary>
    /// Counts and keeps whichever of a website's months need it.
    /// </summary>
    /// <param name="site">The website.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the run's months are kept.</returns>
    public async Task CatchUpAsync(SiteRegistration site, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        // Read before the months it qualifies, never after. Verdicts are stored before judging's
        // bookmark moves past them, so a bookmark read first can only understate what a count read
        // afterwards holds, which merely counts the month again later.
        var judging = await JudgingAsync(site.Id, now, cancellationToken).ConfigureAwait(false);
        var survey = await months
            .SurveyAsync(site.Id, site.TimeZoneId, now, cancellationToken)
            .ConfigureAwait(false);
        var chosen = MonthsToCount.Choose(survey, judging, site.TimeZoneId, now, options.Value.MonthsPerRun);

        if (chosen.IsEmpty)
        {
            return;
        }

        var firstDay = FirstDayOf(site, survey, now);
        var counted = new List<ConsolidatedMonth>(chosen.Length);

        foreach (var month in chosen)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                counted.Add(await CountAsync(site, month, firstDay, judging, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
            {
                // A statement the client gave up on is a failed month like any other: it is
                // cancelled by its own deadline, not by the run, and the months after it still count.
                Log.MonthFailed(logger, failure, site.Id, month.Month);
            }
        }

        if (counted.Count == 0)
        {
            return;
        }

        var kept = await roster
            .WhileItExistsAsync(site.Id, token => months.KeepAsync(counted, token), cancellationToken)
            .ConfigureAwait(false);

        if (!kept)
        {
            Log.Removed(logger, site.Id);
        }
    }

    private async Task<SiteJudging> JudgingAsync(Guid siteId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var caughtUp = await progress
            .CaughtUpToAsync(siteId, RulesetVersion.Current, cancellationToken)
            .ConfigureAwait(false);
        var running = await progress
            .JudgedThroughAsync(siteId, RulesetVersion.Current, cancellationToken)
            .ConfigureAwait(false);

        return new SiteJudging(
            caughtUp is { } reached
                ? WholeMilliseconds(JudgingReach.Settled(reached, now, classification.Value.IdleTimeout))
                : null,
            running is { } through ? WholeMilliseconds(through) : null,
            RulesetVersion.Current);
    }

    /// <summary>
    /// An instant as a kept month can hold it: to the millisecond, rounded down.
    /// </summary>
    /// <remarks>
    /// How far judging had reached is kept to the millisecond, as every instant in the telemetry store
    /// is, and compared on the next run with how far it has reached since. Held finer than that here,
    /// judging stalled part-way through a millisecond would always look a fraction further on than the
    /// month says it was, and the month would be counted again on every run. Down rather than up, so a
    /// month never says judging reached further than it did; no visit begins inside the fraction lost,
    /// because every report and verdict is stamped to the millisecond too.
    /// </remarks>
    private static DateTimeOffset WholeMilliseconds(DateTimeOffset instant) =>
        new(instant.UtcTicks - (instant.UtcTicks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);

    private async Task<ConsolidatedMonth> CountAsync(
        SiteRegistration site,
        SurveyedMonth month,
        DateOnly? firstDay,
        SiteJudging judging,
        CancellationToken cancellationToken)
    {
        // Taken before anything is read, so of two counts of one month the one that read later is
        // the one kept, whichever is written first.
        var countedAt = clock.GetUtcNow();
        var toCount = new MonthToCount(site.Id, site.TimeZoneId, month.Month, month.Window);

        var everybody = await months.CountAsync(toCount, Population.Everybody, cancellationToken).ConfigureAwait(false);
        var people = await months.CountAsync(toCount, Population.People, cancellationToken).ConfigureAwait(false);
        var verdicts = await months.CountVerdictsAsync(toCount, cancellationToken).ConfigureAwait(false);

        var ends = month.Window.To;

        return new ConsolidatedMonth(
            toCount,
            firstDay is { } first && first.Year == month.Month.Year && first.Month == month.Month.Month ? first : null,
            DaysMeasured.In(month.Month, firstDay, month.Month.AddMonths(1).AddDays(-1)),
            everybody,
            people,
            verdicts,
            judging.Running,
            Earlier(judging.RunningJudgedThrough ?? month.Window.From, ends),
            ends,
            Earlier(judging.Settled ?? month.Window.From, ends),
            countedAt);
    }

    /// <summary>
    /// The website's first recorded day, where it can be known, by the rule <see cref="FirstDays"/>
    /// gives; a month the day falls after is measured on none of its days.
    /// </summary>
    private static DateOnly? FirstDayOf(SiteRegistration site, MonthSurvey survey, DateTimeOffset now) =>
        FirstDays.Of(
            survey.Months.Select(month => month.Kept?.FirstDay).Where(day => day is not null).Min(),
            survey.FirstReportDay,
            survey.FirstReportDay is { } held && CanBeCounted(survey, held, now),
            site.AddedAt,
            survey.KeepingSince,
            now);

    /// <summary>
    /// Whether the month a day falls in, on the calendar the website keeps now, can still be counted.
    /// </summary>
    private static bool CanBeCounted(MonthSurvey survey, DateOnly day, DateTimeOffset now) =>
        survey.Months.FirstOrDefault(month => month.Month.Year == day.Year && month.Month.Month == day.Month) is { } holding
        && now < Retention.CountableUntil(holding.Window.From);

    private static DateTimeOffset Earlier(DateTimeOffset one, DateTimeOffset other) => one < other ? one : other;

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 5203,
            Level = LogLevel.Warning,
            Message = "Counting the month of {Month} on website {SiteId} failed. The month is left as it was "
                + "and tried again on the next run.")]
        public static partial void MonthFailed(ILogger logger, Exception failure, Guid siteId, DateOnly month);

        [LoggerMessage(
            EventId = 5206,
            Level = LogLevel.Information,
            Message = "Website {SiteId} was removed while its months were being counted, so they were not kept.")]
        public static partial void Removed(ILogger logger, Guid siteId);
    }
}
