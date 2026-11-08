using System.Collections.Immutable;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Application.Posts;

/// <summary>
/// What one post has done across a website's whole record: its views, every month it was read in, and
/// its recent weeks day by day.
/// </summary>
/// <remarks>
/// <para>
/// Read where Insights reads it, at the paths <see cref="PostsInsights.PathsOf"/> gives, and from the same
/// two sources: what was kept of each month before the first one answered from the reports, and the
/// reports from there to the present. So its views are the figure Insights gives it when both are read at
/// one moment.
/// </para>
/// <para>
/// A month that was never counted for the post has no figure, and neither has a month counted before the
/// post was listed that kept nothing at its paths: nothing says whether it was read then. A month counted
/// while it was listed that kept nothing at its paths is a month nobody read it in, and is nought. A
/// month's figure covers the days the website was measured on, and in the month the post was published
/// only the days from then on.
/// </para>
/// <para>
/// Where the reports answer more than one month, each month counts a visitor's page once on its own, so
/// between them they can come to a little more than the views counted across the whole stretch at once,
/// which is what the post's views carry.
/// </para>
/// </remarks>
public static class PostRecord
{
    /// <summary>How many weeks of days a post's record carries, the week running now among them.</summary>
    public const int RecentWeeks = 13;

    /// <summary>
    /// The first day of the recent weeks: the Monday <see cref="RecentWeeks"/> weeks back, counting the
    /// week running now as the last of them.
    /// </summary>
    /// <param name="today">Today, on the website's calendar.</param>
    /// <returns>The Monday.</returns>
    public static DateOnly WeeksFrom(DateOnly today) => today.AddDays(-MondayFirst(today) - (7 * (RecentWeeks - 1)));

    /// <summary>
    /// The stretch the recent weeks are read over: from the first instant of <see cref="WeeksFrom"/> on the
    /// website's calendar to the moment of reading.
    /// </summary>
    /// <param name="asOf">The moment the post is read at.</param>
    /// <param name="zone">The website's reporting zone.</param>
    /// <returns>The stretch.</returns>
    public static TimeRange Weeks(DateTimeOffset asOf, TimeZoneInfo zone) =>
        new(ZoneDays.StartOf(WeeksFrom(ZoneDays.Of(asOf, zone)), zone), asOf);

    /// <summary>
    /// Whether the reports answer more than one month, so each month's views have to be counted on their
    /// own. The views of a single month are the views counted since it began, by the same grouping.
    /// </summary>
    /// <param name="liveFrom">Where views begin to be counted from the reports.</param>
    /// <param name="asOf">The moment the post is read at.</param>
    /// <returns><see langword="true"/> where the reports reach past the month they take over at.</returns>
    public static bool SpansMonths(RecordedMonth liveFrom, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(liveFrom);

        return liveFrom.Ends < asOf;
    }

    /// <summary>
    /// Works out what one post has done.
    /// </summary>
    /// <param name="sources">What the post's record is read from.</param>
    /// <param name="addedAt">When the website was added.</param>
    /// <param name="zone">The website's reporting zone.</param>
    /// <param name="asOf">The moment the post is read at, which decides what today is.</param>
    /// <returns>What the post has done.</returns>
    public static PostRecordReading Read(
        PostRecordSources sources,
        DateTimeOffset addedAt,
        TimeZoneInfo zone,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(zone);

        var today = ZoneDays.Of(asOf, zone);
        var published = PostsInsights.PublishedOn(sources.Post.Listed, zone);

        if (sources.LiveFrom is not { } liveFrom)
        {
            return new PostRecordReading(0, null, null, null, published, [], DaysOf(sources.Days, null, zone, today));
        }

        var record = KeptHistory.Resolve(sources.Record, asOf, judged: false);
        var firstDay = KeptHistory.FirstDay(record, sources.Record, addedAt, asOf);
        var viewsFrom = PostsInsights.ViewsFrom([.. sources.Kept.Select(month => month.Month)], liveFrom)
            ?? liveFrom.Month;
        var figures = FiguresOf(sources, record, liveFrom, viewsFrom, asOf);
        var first = FirstMonth(figures, viewsFrom, published, today);
        ImmutableArray<MonthReading> months =
        [
            .. figures
                .Where(figure => figure.Month.Month >= first)
                .Select(figure => new MonthReading(
                    figure.Month.Month,
                    figure.PageViews,
                    DaysMeasuredIn(figure, firstDay, published, today))),
        ];

        return new PostRecordReading(
            KeptBefore(sources.Kept, liveFrom).Sum(month => month.PageViews) + sources.LiveViews,
            viewsFrom,
            firstDay,
            FromOf(months, firstDay, published, today),
            published,
            months,
            DaysOf(sources.Days, firstDay, zone, today));
    }

    /// <summary>
    /// Where the reports a post's record is read from begin: the earlier of the month they take over at
    /// and the first of the recent weeks.
    /// </summary>
    /// <param name="sources">What the post's record is read from.</param>
    /// <param name="weeks">The stretch the recent weeks are read over.</param>
    /// <returns>The instant, or nothing where nothing of the website is held and no report was read.</returns>
    public static DateTimeOffset? ReportsFrom(PostRecordSources sources, TimeRange weeks)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return sources.LiveFrom is { } liveFrom ? Earlier(liveFrom.Begins, weeks.From) : null;
    }

    /// <summary>
    /// How far the verdicts a post's record of people rests on are complete.
    /// </summary>
    /// <remarks>
    /// The earliest point any of it stops being complete at: where judging has reached for what the
    /// reports answer, or where it had reached when a month still held was counted, if it had not finished
    /// with it. A month that can no longer be counted again never holds the rest back.
    /// </remarks>
    /// <param name="sources">What the post's record is read from.</param>
    /// <param name="liveJudgedTo">Where judging has reached for what the reports answer.</param>
    /// <param name="asOf">The moment the post is read at.</param>
    /// <returns>The instant, which is <paramref name="asOf"/> where nothing is incomplete.</returns>
    public static DateTimeOffset JudgedTo(PostRecordSources sources, DateTimeOffset liveJudgedTo, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.LiveFrom is not { } liveFrom)
        {
            return asOf;
        }

        return KeptBefore(sources.Kept, liveFrom)
            .Where(month => KeptHistory.Unsettled(month.JudgedTo, month.Counted, asOf))
            .Select(month => month.JudgedTo)
            .Append(liveJudgedTo)
            .Append(asOf)
            .Min();
    }

    /// <summary>
    /// Each month of the record from the one the views are counted from: what was kept of the post for the
    /// months before the reports take over, and what the reports say from then on.
    /// </summary>
    private static ImmutableArray<MonthFigure> FiguresOf(
        PostRecordSources sources,
        ImmutableArray<MonthOfRecord> record,
        RecordedMonth liveFrom,
        DateOnly viewsFrom,
        DateTimeOffset asOf)
    {
        var kept = KeptBefore(sources.Kept, liveFrom).ToDictionary(month => month.Month);
        var live = SpansMonths(liveFrom, asOf)
            ? sources.LiveMonths.ToDictionary(point => point.BucketStart, point => point.Value)
            : new Dictionary<DateTimeOffset, long> { [liveFrom.Begins] = sources.LiveViews };

        return
        [
            .. record
                .Select(month => month.Month)
                .Where(month => month.Month >= viewsFrom)
                .Select(month => new MonthFigure(
                    month,
                    month.Month < liveFrom.Month
                        ? KeptFigure(kept.GetValueOrDefault(month.Month), sources.Post.FirstListedAt)
                        : live.GetValueOrDefault(month.Begins))),
        ];
    }

    /// <summary>
    /// What a month kept before the reports take over says of the post: nothing where the month's posts were
    /// never kept, or where it was counted before the post was listed and kept nothing at its paths.
    /// </summary>
    private static long? KeptFigure(KeptMonthOfPost? kept, DateTimeOffset firstListedAt) =>
        kept is null || (!kept.Named && kept.CountedAt < firstListedAt) ? null : kept.PageViews;

    /// <summary>
    /// The month a post's months begin with. For a dated post, the earlier of the month it was published in
    /// and the first month it was read in; for an undated one, the first month that may hold its views — one
    /// it was read in, or one that cannot say. Never before the month its views are counted from, and never
    /// after the month running now.
    /// </summary>
    private static DateOnly FirstMonth(
        ImmutableArray<MonthFigure> figures,
        DateOnly viewsFrom,
        DateOnly? published,
        DateOnly today)
    {
        var running = FirstOf(today);
        var first = published is { } day
            ? Earlier(FirstOf(day), FirstWhere(figures, figure => figure.PageViews > 0) ?? FirstOf(day))
            : FirstWhere(figures, figure => figure.PageViews != 0) ?? running;

        return Earlier(Later(first, viewsFrom), running);
    }

    /// <summary>The first month whose figure passes a test, or nothing where none does.</summary>
    private static DateOnly? FirstWhere(ImmutableArray<MonthFigure> figures, Func<MonthFigure, bool> passes) =>
        figures.Where(passes).Select(figure => (DateOnly?)figure.Month.Month).FirstOrDefault();

    /// <summary>
    /// How many days a month's figure covers: none where it has none; the days the website was measured on,
    /// and in the month the post was published only those from that day on — or from today, for a post
    /// dated later than today.
    /// </summary>
    private static int DaysMeasuredIn(MonthFigure figure, DateOnly? firstDay, DateOnly? published, DateOnly today)
    {
        if (figure.PageViews is null)
        {
            return 0;
        }

        var measured = KeptHistory.DaysOf(figure.Month, firstDay, today);

        return published is { } day && FirstOf(day) == figure.Month.Month
            ? Math.Min(measured, DaysMeasured.In(figure.Month.Month, Earlier(day, today), today))
            : measured;
    }

    /// <summary>
    /// The first day a post's first month counts: the first of it, or the website's first day or the day the
    /// post was published where either falls later in it — and never later than today.
    /// </summary>
    private static DateOnly? FromOf(
        ImmutableArray<MonthReading> months,
        DateOnly? firstDay,
        DateOnly? published,
        DateOnly today)
    {
        if (months.IsEmpty)
        {
            return null;
        }

        var first = months[0].Month;
        var from = firstDay is { } measured ? Later(first, measured) : first;

        if (published is { } day && FirstOf(day) == first)
        {
            from = Later(from, day);
        }

        return Earlier(from, today);
    }

    /// <summary>
    /// Every day of the recent weeks, to today: nothing before the website's first day, and otherwise the
    /// pages delivered at the post on it, nought where the reports hold none.
    /// </summary>
    private static ImmutableArray<PostDay> DaysOf(
        IReadOnlyList<TimeSeriesPoint> series,
        DateOnly? firstDay,
        TimeZoneInfo zone,
        DateOnly today)
    {
        var counted = series
            .GroupBy(point => ZoneDays.Of(point.BucketStart, zone))
            .ToDictionary(day => day.Key, day => day.Sum(point => point.Value));
        var from = WeeksFrom(today);

        return
        [
            .. Enumerable.Range(0, today.DayNumber - from.DayNumber + 1)
                .Select(from.AddDays)
                .Select(day => new PostDay(
                    day,
                    firstDay is { } first && day >= first ? counted.GetValueOrDefault(day) : null)),
        ];
    }

    private static IEnumerable<KeptMonthOfPost> KeptBefore(ImmutableArray<KeptMonthOfPost> kept, RecordedMonth liveFrom) =>
        kept.Where(month => month.Month < liveFrom.Month);

    /// <summary>Where a day falls in a week that begins on a Monday, from nought.</summary>
    private static int MondayFirst(DateOnly day) => ((int)day.DayOfWeek + 6) % 7;

    private static DateOnly FirstOf(DateOnly day) => new(day.Year, day.Month, 1);

    private static T Earlier<T>(T one, T other)
        where T : IComparable<T> =>
        one.CompareTo(other) <= 0 ? one : other;

    private static T Later<T>(T one, T other)
        where T : IComparable<T> =>
        one.CompareTo(other) >= 0 ? one : other;

    /// <summary>One month of a post's record, with its figure but not yet its days.</summary>
    /// <param name="Month">The month.</param>
    /// <param name="PageViews">Pages delivered at the post in it, or nothing where it cannot be said.</param>
    private readonly record struct MonthFigure(RecordedMonth Month, long? PageViews);
}

/// <summary>
/// What one post's record is read from.
/// </summary>
/// <param name="Post">The post, as the website lists it now.</param>
/// <param name="Record">
/// The website's whole record, month by month, for everybody: which months there are, where each begins,
/// and how many days of each the website was measured on.
/// </param>
/// <param name="Kept">Every month the website's posts were kept for, with what the post was read in each.</param>
/// <param name="LiveFrom">
/// Where the post's views begin to be counted from the reports, as <see cref="PostsInsights.LiveFrom"/>
/// decides; nothing where nothing of the website is held.
/// </param>
/// <param name="LiveViews">
/// Pages delivered at the post from the first instant of <paramref name="LiveFrom"/> to the moment of
/// reading, by the arithmetic of the list of pages.
/// </param>
/// <param name="LiveMonths">
/// Pages delivered at the post in each month from <paramref name="LiveFrom"/> on, by the instant it begins.
/// Read only where the reports answer more than one month (<see cref="PostRecord.SpansMonths"/>): a single
/// month's views are <paramref name="LiveViews"/>.
/// </param>
/// <param name="Days">Pages delivered at the post on each day of the recent weeks, by the instant it begins.</param>
public sealed record PostRecordSources(
    FoundPost Post,
    KeptMonths Record,
    ImmutableArray<KeptMonthOfPost> Kept,
    RecordedMonth? LiveFrom,
    long LiveViews,
    IReadOnlyList<TimeSeriesPoint> LiveMonths,
    IReadOnlyList<TimeSeriesPoint> Days);

/// <summary>
/// What one post has done across a website's whole record.
/// </summary>
/// <param name="PageViews">
/// Pages delivered at the post since <paramref name="ViewsFrom"/>, at its own path and where it landed: the
/// figure Insights gives it.
/// </param>
/// <param name="ViewsFrom">
/// The month its views are counted from, as <see cref="PostsInsights.ViewsFrom"/> decides; nothing where
/// nothing of the website is held.
/// </param>
/// <param name="FirstDay">The day the website's record begins on, or nothing where nothing of it is held.</param>
/// <param name="From">The first day the post's first month counts, or nothing where it has no months.</param>
/// <param name="Published">The day it was published, on the website's calendar, or nothing where it did not say.</param>
/// <param name="Months">
/// Every month from the post's first to the one running now, oldest first; none where nothing of the website
/// is held.
/// </param>
/// <param name="Days">Every day from <see cref="PostRecord.WeeksFrom"/> to today, on the website's calendar.</param>
public sealed record PostRecordReading(
    long PageViews,
    DateOnly? ViewsFrom,
    DateOnly? FirstDay,
    DateOnly? From,
    DateOnly? Published,
    ImmutableArray<MonthReading> Months,
    ImmutableArray<PostDay> Days);

/// <summary>
/// One day of a post's recent weeks.
/// </summary>
/// <param name="Day">The day, on the website's calendar.</param>
/// <param name="PageViews">
/// Pages delivered at the post on it, or nothing where it falls before the website was first measured.
/// </param>
public readonly record struct PostDay(DateOnly Day, long? PageViews);
