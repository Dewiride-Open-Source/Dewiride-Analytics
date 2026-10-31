namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// How long the telemetry store keeps what it is sent, and the instants that follow from it.
/// </summary>
/// <remarks>
/// <para>
/// Every report, every verdict and every answer about a name is deleted by the store itself twelve
/// months after it arrived, or after the visit it is about began. That is a rule written into each
/// table, so it holds whether or not the engine is running. The engine deletes none of them sooner,
/// except when a website is removed or a closed account's records are deleted, which empties every
/// table of that website at once. What is worked out here is only what can still be counted in full.
/// </para>
/// <para>
/// A month's totals outlive the reports they were counted from: they are counted while every
/// report of the month is still held, and kept until the website is removed.
/// </para>
/// </remarks>
public static class Retention
{
    /// <summary>
    /// How many months a report is kept after it arrives. The schema's rule, which a test holds
    /// this to.
    /// </summary>
    public const int ReportMonths = 12;

    /// <summary>
    /// How long before the store could begin deleting a stretch's reports the engine stops counting
    /// it.
    /// </summary>
    /// <remarks>
    /// A day, which is far more than a count takes and far more than two machines' clocks drift
    /// apart. A count that began a moment before the first report went would be missing that report
    /// and would not say so.
    /// </remarks>
    private static readonly TimeSpan Margin = TimeSpan.FromDays(1);

    /// <summary>
    /// The instant after which a stretch beginning at <paramref name="begins"/> can no longer be
    /// counted in full.
    /// </summary>
    /// <remarks>
    /// A stretch is counted from its own reports and, for the people in it, from the verdicts on
    /// visits that began up to a whole visit before it. The oldest of those is the first to be
    /// deleted, twelve calendar months after it began, counted as the store counts them: a day the
    /// later month does not have becomes the last day it does.
    /// </remarks>
    /// <param name="begins">The first instant of the stretch.</param>
    /// <returns>The instant.</returns>
    public static DateTimeOffset CountableUntil(DateTimeOffset begins) =>
        MonthsAfter(begins - VisitorKeys.LongestVisit, ReportMonths) - Margin;

    /// <summary>
    /// The instant from which the store may delete something that arrived at
    /// <paramref name="arrived"/>.
    /// </summary>
    /// <param name="arrived">When it arrived.</param>
    /// <returns>The instant.</returns>
    public static DateTimeOffset ExpiresAfter(DateTimeOffset arrived) => MonthsAfter(arrived, ReportMonths);

    /// <summary>
    /// The first day a website still holds in full, as the instant it begins where the website is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first midnight on the website's calendar that falls a whole visit or more after the oldest
    /// report the store still keeps. A day from there on has every report of what was read on it,
    /// and every verdict that decides who did the reading; a day before it may already have lost
    /// some of either.
    /// </para>
    /// <para>
    /// The dashboard works out the same day for itself, by the same rule, so a stretch it lets
    /// somebody choose is a stretch the engine can answer in detail.
    /// </para>
    /// </remarks>
    /// <param name="now">The moment to measure from.</param>
    /// <param name="zone">The website's reporting zone.</param>
    /// <returns>The instant the day begins.</returns>
    public static DateTimeOffset DetailFrom(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var oldest = MonthsAfter(now, -ReportMonths) + VisitorKeys.LongestVisit;
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(oldest, zone).DateTime);
        var begins = ZoneDays.StartOf(day, zone);

        return begins < oldest ? ZoneDays.StartOf(day.AddDays(1), zone) : begins;
    }

    /// <summary>
    /// The same moment a number of calendar months later, or earlier, on the clock reports are
    /// stamped and deleted by.
    /// </summary>
    private static DateTimeOffset MonthsAfter(DateTimeOffset moment, int months) =>
        new(moment.UtcDateTime.AddMonths(months), TimeSpan.Zero);
}
