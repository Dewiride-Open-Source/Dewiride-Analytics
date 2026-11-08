using System.Collections.Immutable;
using System.Globalization;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;

namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// Reads a website's whole record, month by month, from what was kept of each finished month and,
/// for the months nothing was kept of, from their reports.
/// </summary>
/// <remarks>
/// <para>
/// A finished month is read from what was kept of it. The month running now is never kept, and a
/// finished month may not have been kept yet — counting runs in the background, and may be behind
/// or switched off — so every month nothing was kept of is answered from its reports, by the same
/// arithmetic, wherever they are all still held. That is what keeps the record level with the
/// periods read from reports alone: all of it for a website measured only this year is this year.
/// </para>
/// <para>
/// A month of the record that was never kept and whose reports may already have begun to go is
/// missing, and is said to be: never invented, never counted as nothing. So is a month whose people,
/// or whose verdicts, were kept before judging had finished with it and can no longer be counted
/// again — its figures for everybody stand, but how many of those were people was never settled. A
/// month before the first one kept is part of the record only while a report of it is still held;
/// once the last has gone, nothing is left to say it was measured, and the record begins after it.
/// </para>
/// </remarks>
public static class KeptHistory
{
    /// <summary>
    /// Decides where each month of a record is read from.
    /// </summary>
    /// <remarks>
    /// The record begins with the first month kept, or with the month of the oldest report still held
    /// where that is earlier — but never with a month in front of the website's first day that holds
    /// nothing. A change of zone can leave one there: a month kept as measured on none of its days, or a
    /// month the new calendar puts the first report's hours in that was never counted and no longer can
    /// be, whose hours are in no month. Neither is any part of the record. A month in front of the first
    /// day that holds figures is never dropped for it.
    /// </remarks>
    /// <param name="record">The record, with what was kept of each month.</param>
    /// <param name="asOf">The moment the record is read at.</param>
    /// <param name="judged">
    /// Whether the answer rests on verdicts — figures about people, or how many visits of each kind
    /// there were — so that a month kept before its verdicts were settled, and no longer countable
    /// again, is missing from it.
    /// </param>
    /// <returns>Each month, in order, with where it is read from.</returns>
    public static ImmutableArray<MonthOfRecord> Resolve(KeptMonths record, DateTimeOffset asOf, bool judged)
    {
        ArgumentNullException.ThrowIfNull(record);

        var running = record.Months.Length - 1;
        var firstKept = KeptFirstDay(record.Months.Select(month => month.Kept));

        return
        [
            .. record.Months
                .Select((month, index) =>
                    new MonthOfRecord(month, index == running ? MonthSource.Live : SourceOf(month, asOf, judged)))
                .SkipWhile(month => month.Month.Kept is { DaysMeasured: 0 } || NothingBefore(month, firstKept)),
        ];
    }

    /// <summary>
    /// Whether a month was never counted, can no longer be, and ends before the website's first day.
    /// </summary>
    private static bool NothingBefore(MonthOfRecord month, DateOnly? firstDay) =>
        firstDay is { } first
        && month.Source == MonthSource.Missing
        && month.Month.Kept is null
        && month.Month.Month.AddMonths(1) <= first;

    private static DateOnly? KeptFirstDay(IEnumerable<KeptFigures?> months) =>
        months.Select(kept => kept?.FirstDay).Where(day => day is not null).Min();

    /// <summary>
    /// Where the months that are answered from their reports begin: the first instant of the first of
    /// them, or nothing where the record is empty.
    /// </summary>
    /// <param name="months">The record, resolved.</param>
    /// <returns>The instant.</returns>
    public static DateTimeOffset? LiveFrom(ImmutableArray<MonthOfRecord> months) =>
        months.FirstOrDefault(month => month.Source == MonthSource.Live)?.Month.Begins;

    /// <summary>
    /// The day the website's record begins on.
    /// </summary>
    /// <remarks>
    /// Where a first day was kept, that day as it was kept: a change of zone moves it when the months
    /// are next counted, as it moves their figures, and until then the record reads both as they were
    /// kept. A kept first day that falls after a first month holding figures and no first day of its
    /// own does not begin the record. Where that month is answered from its reports and the next count
    /// will move the kept day into it — a change of zone to a calendar behind the old one, read a day
    /// or two earlier — the record begins on the day that count will keep; otherwise on the first of
    /// the month. Where nothing kept says when the website was first measured, the day the engine
    /// would keep (<see cref="FirstDays"/>), so the day named before the month is next counted is the
    /// day it is counted with: it stands only while the record's first month will decide it when it
    /// is next counted — never kept, or kept on a calendar the website no longer keeps — can still be
    /// counted in full, and holds that day. Otherwise the first of the record's first month, which is
    /// the earliest anything can still say: the oldest report still held then only says how far back
    /// the store reaches, and that moves on every day.
    /// </remarks>
    /// <param name="record">The record, resolved.</param>
    /// <param name="source">The record as it was read, with its oldest report and when keeping began.</param>
    /// <param name="addedAt">When the website was added.</param>
    /// <param name="asOf">The moment the record is read at.</param>
    /// <returns>The day, or nothing where nothing of the website is held.</returns>
    public static DateOnly? FirstDay(
        ImmutableArray<MonthOfRecord> record,
        KeptMonths source,
        DateTimeOffset addedAt,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (record.IsEmpty)
        {
            return null;
        }

        var opening = record[0];

        if (KeptFirstDay(record.Select(month => month.Month.Kept)) is { } kept)
        {
            // A month answered from its reports can be counted, so this is what its next count keeps.
            var next = FirstDays.Of(kept, source.FirstReportDay, oldestHeldCanBeCounted: true, addedAt, source.KeepingSince, asOf);

            return AsKept(kept, opening, next);
        }

        var first = FirstDays.NothingWentUncounted(addedAt, source.KeepingSince, asOf) ? source.FirstReportDay : null;

        return FirstDayOfUnkept(first, opening.Month, asOf);
    }

    /// <summary>
    /// A kept first day, unless it falls after a first month that holds figures and no first day of
    /// its own, which then begins the record: on the day the next count will keep where the month is
    /// answered from its reports and that day falls in it, otherwise on the first of it.
    /// </summary>
    private static DateOnly AsKept(DateOnly kept, MonthOfRecord opening, DateOnly? next)
    {
        if (opening.Source == MonthSource.Missing
            || opening.Month.Kept?.FirstDay is not null
            || kept < opening.Month.Month.AddMonths(1))
        {
            return kept;
        }

        return opening.Source == MonthSource.Live
            && next is { } moved
            && moved.Year == opening.Month.Month.Year
            && moved.Month == opening.Month.Month.Month
                ? moved
                : opening.Month.Month;
    }

    /// <summary>
    /// Where nothing kept says when the website was first measured: the website's first day where the
    /// record's first month will decide it when it is next counted, and holds that day; otherwise the
    /// first of that month.
    /// </summary>
    private static DateOnly FirstDayOfUnkept(DateOnly? first, RecordedMonth opening, DateTimeOffset asOf) =>
        WillBeCountedAgain(opening, asOf)
        && first is { } day
        && day.Year == opening.Month.Year
        && day.Month == opening.Month.Month
            ? day
            : opening.Month;

    /// <summary>
    /// Whether a month's next count will decide what is kept of it: one never kept, or kept on a
    /// calendar the website no longer keeps, that can still be counted in full.
    /// </summary>
    private static bool WillBeCountedAgain(RecordedMonth month, DateTimeOffset asOf) =>
        (month.Kept is null || month.Kept.Counted.From != month.Begins)
        && asOf < Retention.CountableUntil(month.Begins);

    /// <summary>
    /// The months of the record an answer cannot give a figure for because they were never counted:
    /// nothing was kept of them and their reports may already have begun to go, as <c>yyyy-MM</c>.
    /// </summary>
    /// <param name="months">The record, resolved.</param>
    /// <returns>The months, oldest first.</returns>
    public static ImmutableArray<string> Missing(ImmutableArray<MonthOfRecord> months) =>
        Named(months.Where(month => month.Source == MonthSource.Missing && month.Month.Kept is null));

    /// <summary>
    /// The months of the record an answer resting on verdicts cannot give a figure for because they
    /// were kept before judging had finished with them and can no longer be counted again, as
    /// <c>yyyy-MM</c>. Their figures for everybody stand; how many of their visits were people was
    /// never settled.
    /// </summary>
    /// <param name="months">The record, resolved.</param>
    /// <returns>The months, oldest first; none for an answer about everybody.</returns>
    public static ImmutableArray<string> Unjudged(ImmutableArray<MonthOfRecord> months) =>
        Named(months.Where(month => month.Source == MonthSource.Missing && month.Month.Kept is not null));

    private static ImmutableArray<string> Named(IEnumerable<MonthOfRecord> months) =>
        [.. months.Select(month => month.Month.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture))];

    /// <summary>
    /// How far the verdicts the record's figures rest on are complete.
    /// </summary>
    /// <remarks>
    /// The earliest point any month stops being complete at: where judging had reached when a month
    /// still held was counted, if it had not finished with it, or where judging has reached now for the
    /// months answered from their reports. A month that can no longer be counted again is either whole
    /// or missing, so it never holds the rest back.
    /// </remarks>
    /// <param name="months">The record, resolved.</param>
    /// <param name="liveJudgedTo">Where judging has reached for the months answered from their reports.</param>
    /// <param name="asOf">The moment the record is read at.</param>
    /// <returns>The instant, which is <paramref name="asOf"/> where nothing is incomplete.</returns>
    public static DateTimeOffset JudgedTo(
        ImmutableArray<MonthOfRecord> months,
        DateTimeOffset liveJudgedTo,
        DateTimeOffset asOf)
    {
        var reached = asOf;

        foreach (var month in months)
        {
            var stops = month.Source switch
            {
                MonthSource.Live => liveJudgedTo,
                MonthSource.Kept when month.Month.Kept is { } kept
                    && Unsettled(kept.JudgedTo, kept.Counted, asOf) => kept.JudgedTo,
                _ => asOf,
            };

            reached = stops < reached ? stops : reached;
        }

        return reached;
    }

    /// <summary>
    /// Whether a month was kept before judging had finished with it and can still be counted again, so
    /// that what it says of people is not yet settled.
    /// </summary>
    /// <remarks>
    /// A month that can no longer be counted again is either whole or missing, and is never unsettled.
    /// </remarks>
    /// <param name="judgedTo">How far judging had reached when the month was counted.</param>
    /// <param name="counted">The stretch it was counted over.</param>
    /// <param name="asOf">The moment the record is read at.</param>
    /// <returns><see langword="true"/> where its people are still to be settled.</returns>
    public static bool Unsettled(DateTimeOffset judgedTo, TimeRange counted, DateTimeOffset asOf) =>
        judgedTo < counted.To && asOf < Retention.CountableUntil(counted.From);

    /// <summary>
    /// How many of a month's days the website was measured on: the days kept with it, where it is
    /// finished and was kept; otherwise from the website's first day, and up to and including today in
    /// the month running now.
    /// </summary>
    /// <param name="month">The month.</param>
    /// <param name="firstDay">The day the record begins on.</param>
    /// <param name="today">Today, on the website's calendar.</param>
    /// <returns>The number of days.</returns>
    public static int DaysOf(RecordedMonth month, DateOnly? firstDay, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(month);

        return month.Kept is { } kept && month.Month.AddMonths(1) <= today
            ? kept.DaysMeasured
            : DaysMeasured.In(month.Month, firstDay, today);
    }

    /// <summary>
    /// One figure per month, or per year, from what was kept and what the reports say.
    /// </summary>
    /// <param name="months">The record, resolved.</param>
    /// <param name="kept">The figure a kept month gives.</param>
    /// <param name="live">The figure the reports give for a month, by the instant it begins.</param>
    /// <param name="granularity">Whether to give a figure per month or per year.</param>
    /// <returns>
    /// One bucket per month, or per year, in order. A month missing from the record has no figure;
    /// a year adds up the months it has figures for, and has none only where it has none of them.
    /// </returns>
    public static ImmutableArray<KeptPoint> Series(
        ImmutableArray<MonthOfRecord> months,
        Func<KeptFigures, long> kept,
        IReadOnlyDictionary<DateTimeOffset, long> live,
        KeptGranularity granularity)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(live);

        var monthly = months.Select(month => new KeptPoint(month.Month.Begins, ValueOf(month, kept, live)));

        return granularity switch
        {
            KeptGranularity.Month => [.. monthly],
            KeptGranularity.Year =>
            [
                .. months
                    .Zip(monthly)
                    .GroupBy(pair => pair.First.Month.YearBegins)
                    .Select(year => new KeptPoint(year.Key, Sum(year.Select(pair => pair.Second.Value)))),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Ask for months or years."),
        };
    }

    /// <summary>
    /// How many visits of each kind there were, month by month or year by year.
    /// </summary>
    /// <param name="months">The record, resolved for verdicts.</param>
    /// <param name="live">What the reports' verdicts say of the months answered from them.</param>
    /// <param name="granularity">Whether to count per month or per year.</param>
    /// <returns>
    /// The bucket instants, and one group per kind of visit present anywhere in the record, each as
    /// long as the bucket list. A missing month has no count in any group.
    /// </returns>
    public static KeptTraffic Traffic(
        ImmutableArray<MonthOfRecord> months,
        TrafficSeries live,
        KeptGranularity granularity)
    {
        ArgumentNullException.ThrowIfNull(live);

        var categories = months
            .SelectMany(month => month.Source == MonthSource.Kept && month.Month.Kept is { } held
                ? held.Verdicts.Select(tally => tally.Category)
                : [])
            .Concat(live.Groups.Select(group => group.Category))
            .Distinct()
            .Order()
            .ToImmutableArray();

        var groups = categories.Select(category =>
        {
            var counted = live.Groups.FirstOrDefault(group => group.Category == category);

            return new KeptTrafficGroup(
                category,
                Values(Series(months, figures => Tally(figures, category).Visits, LiveCounts(live, counted?.Sessions), granularity)),
                Values(Series(months, figures => Tally(figures, category).PageViews, LiveCounts(live, counted?.PageViews), granularity)));
        });

        var buckets = granularity == KeptGranularity.Year
            ? months.Select(month => month.Month.YearBegins).Distinct()
            : months.Select(month => month.Month.Begins);

        return new KeptTraffic([.. buckets], [.. groups]);
    }

    /// <summary>
    /// What a website's whole record says about when it is read: its best day, how its reading falls
    /// across the week and the day, and how much of it each month holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A kept month gives the best day and the weekday and hour figures kept with it. A month answered
    /// from its reports gives them from its figures day by day and hour by hour, on the website's
    /// clock, which is how a kept month's were counted. Those figures cover a stretch that can hold
    /// months that were kept, and only the months answered from their reports are read from them, so
    /// no day is counted twice. A month missing from the record adds nothing and covers no days.
    /// </para>
    /// <para>
    /// The best day is the busiest of the months' best days and the earliest of a tie. Being worked
    /// out afresh rather than recorded, it falls with its month where a month still held is counted
    /// again under newer rules. Every weekday or hour sharing the most is busiest, and none is where
    /// nothing was read. A figure a day or an hour counted a visitor's page in may count it again in
    /// another, so a weekday's or an hour's share is of its own figures, never of the month's total.
    /// </para>
    /// </remarks>
    /// <param name="months">The record, resolved.</param>
    /// <param name="firstDay">The day the record begins on.</param>
    /// <param name="live">What the reports say of the months answered from them.</param>
    /// <param name="zone">The website's reporting zone.</param>
    /// <param name="asOf">The moment the record is read at.</param>
    /// <returns>What the record says.</returns>
    public static KeptInsights Insights(
        ImmutableArray<MonthOfRecord> months,
        DateOnly? firstDay,
        LiveMonths live,
        TimeZoneInfo zone,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(zone);

        var best = new BusiestDay();
        var weekdays = new long[MonthFigures.Weekdays];
        var hours = new long[MonthFigures.Hours];
        var fromReports = months
            .Where(month => month.Source == MonthSource.Live)
            .Select(month => month.Month.Month)
            .ToHashSet();

        foreach (var month in months.Where(month => month.Source == MonthSource.Kept))
        {
            AddKept(month.Month.Kept!.Figures, best, weekdays, hours);
        }

        foreach (var point in live.Days)
        {
            var day = ZoneDays.Of(point.BucketStart, zone);

            if (fromReports.Contains(new DateOnly(day.Year, day.Month, 1)))
            {
                best.Offer(day, point.Value);
                weekdays[MondayFirst(day)] += point.Value;
            }
        }

        foreach (var point in live.Hours)
        {
            var shown = TimeZoneInfo.ConvertTime(point.BucketStart, zone);

            if (fromReports.Contains(new DateOnly(shown.Year, shown.Month, 1)))
            {
                hours[shown.Hour] += point.Value;
            }
        }

        var today = ZoneDays.Of(asOf, zone);

        return new KeptInsights(
            best.Day,
            best.PageViews,
            [.. weekdays],
            Busiest(weekdays),
            [.. hours],
            Busiest(hours),
            [.. months.Select(month => ReadingOf(month, firstDay, live.PageViews, today))]);
    }

    private static void AddKept(MonthFigures figures, BusiestDay best, long[] weekdays, long[] hours)
    {
        if (figures.BestDay is { } day)
        {
            best.Offer(day, figures.BestDayPageViews);
        }

        AddTo(weekdays, figures.ByWeekday);
        AddTo(hours, figures.ByHour);
    }

    private static void AddTo(long[] sums, ImmutableArray<long> figures)
    {
        for (var index = 0; index < sums.Length; index++)
        {
            sums[index] += figures[index];
        }
    }

    /// <summary>Where a day falls in a week that begins on a Monday, from nought.</summary>
    private static int MondayFirst(DateOnly day) => ((int)day.DayOfWeek + 6) % MonthFigures.Weekdays;

    /// <summary>Every place sharing the most, or none where nothing is counted anywhere.</summary>
    private static ImmutableArray<int> Busiest(long[] figures)
    {
        var most = figures.Max();

        return most == 0 ? [] : [.. Enumerable.Range(0, figures.Length).Where(index => figures[index] == most)];
    }

    /// <summary>
    /// A month's page views and the days they cover: as they were kept, or from its reports, where the
    /// running month counts today; none of either where the month cannot be given a figure.
    /// </summary>
    private static MonthReading ReadingOf(
        MonthOfRecord month,
        DateOnly? firstDay,
        IReadOnlyDictionary<DateTimeOffset, long> live,
        DateOnly today) =>
        month.Source switch
        {
            MonthSource.Kept when month.Month.Kept is { } kept =>
                new MonthReading(month.Month.Month, kept.Figures.PageViews, DaysOf(month.Month, firstDay, today)),
            MonthSource.Live => new MonthReading(
                month.Month.Month,
                live.GetValueOrDefault(month.Month.Begins),
                DaysOf(month.Month, firstDay, today)),
            _ => new MonthReading(month.Month.Month, null, 0),
        };

    private static MonthSource SourceOf(RecordedMonth month, DateTimeOffset asOf, bool judged)
    {
        var countable = asOf < Retention.CountableUntil(month.Begins);

        if (month.Kept is not { } kept)
        {
            return countable ? MonthSource.Live : MonthSource.Missing;
        }

        var settled = kept.JudgedTo >= kept.Counted.To;

        return judged && !settled && asOf >= Retention.CountableUntil(kept.Counted.From)
            ? MonthSource.Missing
            : MonthSource.Kept;
    }

    private static long? ValueOf(MonthOfRecord month, Func<KeptFigures, long> kept, IReadOnlyDictionary<DateTimeOffset, long> live) =>
        month.Source switch
        {
            MonthSource.Kept when month.Month.Kept is { } figures => kept(figures),
            MonthSource.Live => live.TryGetValue(month.Month.Begins, out var counted) ? counted : 0,
            _ => null,
        };

    private static long? Sum(IEnumerable<long?> values)
    {
        long? total = null;

        foreach (var value in values)
        {
            if (value is { } known)
            {
                total = (total ?? 0) + known;
            }
        }

        return total;
    }

    private static VerdictTally Tally(KeptFigures figures, TrafficCategory category) =>
        figures.Verdicts.FirstOrDefault(tally => tally.Category == category);

    private static ImmutableArray<long?> Values(ImmutableArray<KeptPoint> points) =>
        [.. points.Select(point => point.Value)];

    /// <summary>
    /// One kind of visit's counts from the reports, by the instant each bucket begins; nothing in
    /// any bucket where the reports held none of that kind.
    /// </summary>
    private static Dictionary<DateTimeOffset, long> LiveCounts(TrafficSeries live, ImmutableArray<long>? counts)
    {
        var byBucket = new Dictionary<DateTimeOffset, long>();

        for (var index = 0; index < live.Buckets.Length; index++)
        {
            byBucket[live.Buckets[index]] = counts is { } held ? held[index] : 0;
        }

        return byBucket;
    }
}

/// <summary>
/// Where one month of a record is read from.
/// </summary>
public enum MonthSource
{
    /// <summary>From what was kept of it when it was counted.</summary>
    Kept = 1,

    /// <summary>From its reports, which are all still held.</summary>
    Live = 2,

    /// <summary>
    /// From nowhere: never kept, with reports that may already have begun to go — or, for an answer
    /// resting on verdicts, kept before judging had finished with it and no longer countable again.
    /// </summary>
    Missing = 3,
}

/// <summary>
/// How wide each bucket of a website's whole record is.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="TimeGranularity"/> on purpose: a record is read a month or a year at a
/// time, and a question about reports is never asked in years.
/// </remarks>
public enum KeptGranularity
{
    /// <summary>A calendar month on the website's calendar.</summary>
    Month = 1,

    /// <summary>A calendar year on the website's calendar.</summary>
    Year = 2,
}

/// <summary>
/// One month of a record, and where it is read from.
/// </summary>
/// <param name="Month">The month.</param>
/// <param name="Source">Where it is read from.</param>
public sealed record MonthOfRecord(RecordedMonth Month, MonthSource Source);

/// <summary>
/// One bucket of a record.
/// </summary>
/// <param name="BucketStart">The bucket's first instant, where the website is.</param>
/// <param name="Value">Its figure, or nothing where none of it can be given.</param>
public readonly record struct KeptPoint(DateTimeOffset BucketStart, long? Value);

/// <summary>
/// What the reports say of the months a record answers from them.
/// </summary>
/// <param name="PageViews">Pages delivered in each month, by the instant it begins.</param>
/// <param name="Days">Pages delivered on each day of the stretch those months begin at, to the present.</param>
/// <param name="Hours">Pages delivered in each hour of that stretch.</param>
public sealed record LiveMonths(
    IReadOnlyDictionary<DateTimeOffset, long> PageViews,
    IReadOnlyList<TimeSeriesPoint> Days,
    IReadOnlyList<TimeSeriesPoint> Hours);

/// <summary>
/// What a website's whole record says about when it is read.
/// </summary>
/// <param name="BestDay">The day the most pages were delivered, the earliest of a tie, or nothing where none were.</param>
/// <param name="BestDayPageViews">Pages delivered on that day.</param>
/// <param name="Weekdays">Pages delivered on each day of the week, Monday first.</param>
/// <param name="BusiestWeekdays">Every place in <paramref name="Weekdays"/> sharing the most, or none where nothing was read.</param>
/// <param name="Hours">Pages delivered in each hour of the day, from midnight on the website's clock.</param>
/// <param name="BusiestHours">Every place in <paramref name="Hours"/> sharing the most, or none where nothing was read.</param>
/// <param name="Months">Each month of the record, oldest first.</param>
public sealed record KeptInsights(
    DateOnly? BestDay,
    long BestDayPageViews,
    ImmutableArray<long> Weekdays,
    ImmutableArray<int> BusiestWeekdays,
    ImmutableArray<long> Hours,
    ImmutableArray<int> BusiestHours,
    ImmutableArray<MonthReading> Months);

/// <summary>
/// How much of a website's record one month holds.
/// </summary>
/// <param name="Month">The first of the month, on the website's calendar.</param>
/// <param name="PageViews">Pages delivered in it, or nothing where it cannot be said.</param>
/// <param name="DaysMeasured">How many of its days those pages cover, which is none where it cannot be said.</param>
public readonly record struct MonthReading(DateOnly Month, long? PageViews, int DaysMeasured);

/// <summary>
/// How many visits of each kind a record holds, bucket by bucket.
/// </summary>
/// <param name="Buckets">Each bucket's first instant, in order.</param>
/// <param name="Groups">One group per kind of visit present anywhere in the record.</param>
public sealed record KeptTraffic(ImmutableArray<DateTimeOffset> Buckets, ImmutableArray<KeptTrafficGroup> Groups);

/// <summary>
/// One kind of visit across a record.
/// </summary>
/// <param name="Category">What the visits were concluded to be.</param>
/// <param name="Visits">How many there were in each bucket, or nothing where it cannot be said.</param>
/// <param name="PageViews">How many pages they read in each bucket, or nothing where it cannot be said.</param>
public sealed record KeptTrafficGroup(
    TrafficCategory Category,
    ImmutableArray<long?> Visits,
    ImmutableArray<long?> PageViews);
