using System.Collections.Immutable;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Classification;

namespace Dewiride.Analytics.Application.Consolidation;

/// <summary>
/// Counts a website's finished months and keeps their totals.
/// </summary>
/// <remarks>
/// <para>
/// For the parts of the product that work on behalf of the system rather than on behalf of a
/// person, on the same terms as <see cref="ISiteVolume"/>: the website comes from the control
/// plane rather than from a request, so there is no caller whose membership could be checked and
/// no <see cref="Tenancy.TenantScope"/> to take. Nothing reachable from a request may use this.
/// What a person reads of the kept months is asked through <see cref="ITelemetryQueries"/>, with
/// proof of a role on the website.
/// </para>
/// <para>
/// A month is counted with exactly the arithmetic the dashboard counts the same month with, so a
/// kept month and the overview of that month are one figure rather than two derivations that
/// agree until they do not.
/// </para>
/// </remarks>
public interface IMonthConsolidation
{
    /// <summary>
    /// Reads a website's calendar: every month from the first it has a record of to the one
    /// running now, and what has been kept of each.
    /// </summary>
    /// <param name="siteId">The website.</param>
    /// <param name="timeZoneId">The zone its calendar is read in.</param>
    /// <param name="now">The moment to read the calendar at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The calendar, empty where the website has no record at all.</returns>
    Task<MonthSurvey> SurveyAsync(
        Guid siteId,
        string timeZoneId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts one month's totals for everybody, or for the visits judged to be people.
    /// </summary>
    /// <param name="month">The month.</param>
    /// <param name="population">Who to count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The totals.</returns>
    Task<MonthFigures> CountAsync(MonthToCount month, Population population, CancellationToken cancellationToken);

    /// <summary>
    /// Counts how many visits of each kind began in one month.
    /// </summary>
    /// <param name="month">The month.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One tally per kind of visit the month held, in a fixed order.</returns>
    Task<ImmutableArray<VerdictTally>> CountVerdictsAsync(MonthToCount month, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps counted months, each replacing whatever was kept of the same month before.
    /// </summary>
    /// <param name="months">The months, all of one website.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the store has them.</returns>
    Task KeepAsync(IReadOnlyCollection<ConsolidatedMonth> months, CancellationToken cancellationToken);
}

/// <summary>
/// A website's calendar, as the store holds it.
/// </summary>
/// <param name="FirstReportDay">
/// The day the oldest report still held arrived on, on the website's calendar, or nothing where
/// none is held.
/// </param>
/// <param name="Months">
/// Every month from the first the website has a record of — kept or still held — to the one
/// running now, oldest first.
/// </param>
public sealed record MonthSurvey(DateOnly? FirstReportDay, ImmutableArray<SurveyedMonth> Months)
{
    /// <summary>
    /// When this installation began keeping months, or nothing where that is not known.
    /// </summary>
    public DateTimeOffset? KeepingSince { get; init; }
}

/// <summary>
/// One month of a website's calendar.
/// </summary>
/// <param name="Month">The first of the month, on the website's calendar.</param>
/// <param name="Window">The month's first instant to its last, where the website is now.</param>
/// <param name="Kept">How it was counted when it was last kept, or nothing where it never was.</param>
public sealed record SurveyedMonth(DateOnly Month, TimeRange Window, KeptMonth? Kept);

/// <summary>
/// How a kept month was counted.
/// </summary>
/// <param name="TimeZoneId">The zone the month was read in.</param>
/// <param name="FirstDay">The website's first recorded day, where it fell in this month.</param>
/// <param name="Ruleset">The rules the engine was running.</param>
/// <param name="RulesetJudgedTo">How far those rules had judged the website.</param>
/// <param name="CountingRules">Which version of the arithmetic counted it.</param>
/// <param name="CountedTo">The last instant the count reached.</param>
/// <param name="JudgedTo">How far judging had reached when it was counted, no further than the month.</param>
/// <param name="CountedAt">When the count was taken.</param>
public sealed record KeptMonth(
    string TimeZoneId,
    DateOnly? FirstDay,
    RulesetVersion Ruleset,
    DateTimeOffset RulesetJudgedTo,
    int CountingRules,
    DateTimeOffset CountedTo,
    DateTimeOffset JudgedTo,
    DateTimeOffset CountedAt);

/// <summary>
/// One month to count.
/// </summary>
/// <param name="SiteId">The website.</param>
/// <param name="TimeZoneId">The zone its calendar is read in.</param>
/// <param name="Month">The first of the month, on that calendar.</param>
/// <param name="Window">The month's first instant to its last.</param>
public sealed record MonthToCount(Guid SiteId, string TimeZoneId, DateOnly Month, TimeRange Window);

/// <summary>
/// One month, counted and ready to keep.
/// </summary>
/// <param name="Month">Which month, and where.</param>
/// <param name="FirstDay">The website's first recorded day, where it falls in this month.</param>
/// <param name="DaysMeasured">How many of the month's days the website was measured on.</param>
/// <param name="Everybody">The month's totals for everybody.</param>
/// <param name="People">The month's totals for the visits judged to be people.</param>
/// <param name="Verdicts">How many visits of each kind began in the month.</param>
/// <param name="Ruleset">The rules the engine was running.</param>
/// <param name="RulesetJudgedTo">How far those rules had judged the website, no further than the month.</param>
/// <param name="CountedTo">The last instant the count reached.</param>
/// <param name="JudgedTo">How far judging had reached, no further than the month.</param>
/// <param name="CountedAt">When the count was taken.</param>
public sealed record ConsolidatedMonth(
    MonthToCount Month,
    DateOnly? FirstDay,
    int DaysMeasured,
    MonthFigures Everybody,
    MonthFigures People,
    ImmutableArray<VerdictTally> Verdicts,
    RulesetVersion Ruleset,
    DateTimeOffset RulesetJudgedTo,
    DateTimeOffset CountedTo,
    DateTimeOffset JudgedTo,
    DateTimeOffset CountedAt)
{
    /// <summary>Which version of the arithmetic counted it.</summary>
    public int CountingRules { get; init; } = Consolidation.CountingRules.Current;
}
