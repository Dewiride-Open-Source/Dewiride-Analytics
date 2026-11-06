using Dewiride.Analytics.Application.Analytics;

namespace Dewiride.Analytics.Application.Tests.Analytics;

/// <summary>
/// Covers what a website's whole record says about when it is read: its best day, how its reading
/// falls across the week and the day, and how much of it each month holds.
/// </summary>
/// <remarks>
/// The record is read at the middle of March 2026. A month that began before about the middle of
/// March 2025 can no longer be counted in full, so a record reaching back that far is how a month
/// comes to have no figure.
/// </remarks>
public sealed class KeptInsightsTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan IndiaTime = TimeSpan.FromHours(5.5);
    private static readonly TimeZoneInfo Kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private static readonly LiveMonths NoReports = new(new Dictionary<DateTimeOffset, long>(), [], []);

    /// <summary>
    /// The busiest of the months' best days, and of two as busy the one that came first: a record
    /// nobody can be told two answers about.
    /// </summary>
    [Fact]
    public void The_Best_Day_Ever_Is_The_Busiest_Kept_Day_And_The_Earliest_Of_A_Tie()
    {
        var insights = Read(
            Kept(2026, 1, bestDay: new DateOnly(2026, 1, 20), bestDayPageViews: 50),
            Kept(2026, 2, bestDay: new DateOnly(2026, 2, 3), bestDayPageViews: 50),
            Kept(2025, 12, bestDay: new DateOnly(2025, 12, 9), bestDayPageViews: 49),
            Unkept(2026, 3));

        insights.BestDay.Should().Be(new DateOnly(2026, 1, 20));
        insights.BestDayPageViews.Should().Be(50);
    }

    /// <summary>
    /// Worked out afresh from the months rather than recorded, so a month still held that is counted
    /// again under newer rules can take the record with it. A figure that could only ever rise would
    /// go on naming a day the rules no longer support.
    /// </summary>
    [Fact]
    public void A_Record_Can_Fall_When_Its_Month_Is_Counted_Again()
    {
        var before = Read(
            Kept(2026, 1, bestDay: new DateOnly(2026, 1, 20), bestDayPageViews: 90),
            Kept(2026, 2, bestDay: new DateOnly(2026, 2, 3), bestDayPageViews: 40),
            Unkept(2026, 3));
        var after = Read(
            Kept(2026, 1, bestDay: new DateOnly(2026, 1, 20), bestDayPageViews: 30),
            Kept(2026, 2, bestDay: new DateOnly(2026, 2, 3), bestDayPageViews: 40),
            Unkept(2026, 3));

        before.BestDay.Should().Be(new DateOnly(2026, 1, 20));
        after.BestDay.Should().Be(new DateOnly(2026, 2, 3));
        after.BestDayPageViews.Should().Be(40);
    }

    [Fact]
    public void Every_Weekday_Sharing_The_Most_Is_Busiest()
    {
        var insights = Read(
            Kept(2026, 1, weekdays: [1, 7, 0, 2, 0, 0, 0]),
            Kept(2026, 2, weekdays: [0, 0, 0, 5, 0, 0, 7]),
            Unkept(2026, 3));

        insights.Weekdays.Should().Equal(1, 7, 0, 7, 0, 0, 7);
        insights.BusiestWeekdays.Should().Equal(1, 3, 6);
    }

    [Fact]
    public void Every_Hour_Sharing_The_Most_Is_Busiest()
    {
        var january = new long[24];
        january[0] = 4;
        january[14] = 1;

        var february = new long[24];
        february[14] = 3;
        february[23] = 4;

        var insights = Read(Kept(2026, 1, hours: january), Kept(2026, 2, hours: february), Unkept(2026, 3));

        insights.Hours[14].Should().Be(4);
        insights.BusiestHours.Should().Equal(0, 14, 23);
    }

    /// <summary>
    /// Nothing read has no busiest anything: a website with no pages is not busiest on Monday at
    /// midnight, which is where an index of the largest of nothing would land.
    /// </summary>
    [Fact]
    public void Nothing_Read_Has_No_Best_Day_And_No_Busiest_Day_Or_Hour()
    {
        var insights = Read(Kept(2026, 2), Unkept(2026, 3));

        insights.BestDay.Should().BeNull();
        insights.BestDayPageViews.Should().Be(0);
        insights.Weekdays.Should().HaveCount(7).And.OnlyContain(pages => pages == 0);
        insights.Hours.Should().HaveCount(24).And.OnlyContain(pages => pages == 0);
        insights.BusiestWeekdays.Should().BeEmpty();
        insights.BusiestHours.Should().BeEmpty();
    }

    /// <summary>
    /// A month answered from its reports is folded on the website's clock. In India the first hour of
    /// Monday 2 March begins at 18:30 on Sunday in UTC, so reading it in UTC would name another day of
    /// the week and another hour.
    /// </summary>
    [Fact]
    public void A_Month_Answered_From_Its_Reports_Is_Folded_On_The_Websites_Clock()
    {
        var monday = new DateTimeOffset(2026, 3, 2, 0, 0, 0, IndiaTime);
        var live = new LiveMonths(
            new Dictionary<DateTimeOffset, long> { [new DateTimeOffset(2026, 3, 1, 0, 0, 0, IndiaTime)] = 9 },
            [new TimeSeriesPoint(monday, 9)],
            [new TimeSeriesPoint(monday, 6), new TimeSeriesPoint(monday.AddHours(14), 3)]);

        var insights = KeptHistory.Insights(
            KeptHistory.Resolve(new KeptMonths(null, [RunningInIndia()]), AsOf, judged: false),
            new DateOnly(2026, 3, 1),
            live,
            Kolkata,
            AsOf);

        insights.BestDay.Should().Be(new DateOnly(2026, 3, 2));
        insights.BestDayPageViews.Should().Be(9);
        insights.Weekdays[0].Should().Be(9);
        insights.BusiestWeekdays.Should().Equal(0);
        insights.Hours[0].Should().Be(6);
        insights.Hours[14].Should().Be(3);
        insights.BusiestHours.Should().Equal(0);
    }

    /// <summary>
    /// The clocks go back through an hour once a year, and each of the two is that hour on the
    /// website's clock, so their pages fold into the one hour they were read in.
    /// </summary>
    [Fact]
    public void Two_Hours_The_Clocks_Show_Alike_Are_One_Hour_Of_The_Day()
    {
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var first = new DateTimeOffset(2025, 10, 26, 1, 0, 0, TimeSpan.FromHours(1));
        var second = new DateTimeOffset(2025, 10, 26, 1, 0, 0, TimeSpan.Zero);
        var begins = new DateTimeOffset(2025, 10, 1, 0, 0, 0, TimeSpan.FromHours(1));
        var month = new RecordedMonth(
            new DateOnly(2025, 10, 1),
            begins,
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 11, 1, 0, 0, 0, TimeSpan.Zero),
            null);

        var insights = KeptHistory.Insights(
            [new MonthOfRecord(month, MonthSource.Live)],
            new DateOnly(2025, 10, 1),
            new LiveMonths(
                new Dictionary<DateTimeOffset, long>(),
                [],
                [new TimeSeriesPoint(first, 2), new TimeSeriesPoint(second, 3)]),
            london,
            AsOf);

        insights.Hours[1].Should().Be(5);
    }

    /// <summary>
    /// The reports read for the months answered from them cover a stretch that can hold a month that
    /// was kept. That month is read from what was kept of it, and its days are not counted again.
    /// </summary>
    [Fact]
    public void A_Day_Of_A_Kept_Month_Is_Not_Counted_Again_From_The_Reports()
    {
        var keptDay = new DateOnly(2026, 2, 16);
        var live = new LiveMonths(
            new Dictionary<DateTimeOffset, long> { [Midnight(2026, 1)] = 1, [Midnight(2026, 2)] = 999, [Midnight(2026, 3)] = 1 },
            [
                new TimeSeriesPoint(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero), 1),
                new TimeSeriesPoint(new DateTimeOffset(2026, 2, 16, 0, 0, 0, TimeSpan.Zero), 999),
                new TimeSeriesPoint(new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero), 1),
            ],
            [
                new TimeSeriesPoint(new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero), 1),
                new TimeSeriesPoint(new DateTimeOffset(2026, 2, 16, 11, 0, 0, TimeSpan.Zero), 999),
                new TimeSeriesPoint(new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero), 1),
            ]);

        var insights = Read(
            live,
            Unkept(2026, 1),
            Kept(2026, 2, pageViews: 20, bestDay: keptDay, bestDayPageViews: 20, weekdays: [20, 0, 0, 0, 0, 0, 0]),
            Unkept(2026, 3));

        insights.BestDay.Should().Be(keptDay);
        insights.BestDayPageViews.Should().Be(20);
        insights.Weekdays[0].Should().Be(22);
        insights.Hours[10].Should().Be(1);
        insights.Hours[11].Should().Be(0);
        insights.Hours[12].Should().Be(1);
        insights.Months.Select(month => month.PageViews).Should().Equal(1, 20, 1);
    }

    /// <summary>A day from the reports can take the record from a kept one, and only by beating it.</summary>
    [Fact]
    public void A_Day_From_The_Reports_Takes_The_Record_Only_By_Beating_It()
    {
        var kept = Kept(2026, 2, bestDay: new DateOnly(2026, 2, 16), bestDayPageViews: 20);
        var today = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero);

        var asBusy = Read(new LiveMonths(new Dictionary<DateTimeOffset, long>(), [new TimeSeriesPoint(today, 20)], []), kept, Unkept(2026, 3));
        var busier = Read(new LiveMonths(new Dictionary<DateTimeOffset, long>(), [new TimeSeriesPoint(today, 21)], []), kept, Unkept(2026, 3));

        asBusy.BestDay.Should().Be(new DateOnly(2026, 2, 16));
        busier.BestDay.Should().Be(new DateOnly(2026, 3, 2));
        busier.BestDayPageViews.Should().Be(21);
    }

    /// <summary>
    /// An average per day divides by the days a month was measured on, as they were kept: a website
    /// first measured on the nineteenth is not averaged over days nobody was counting.
    /// </summary>
    [Fact]
    public void A_Kept_Month_Covers_The_Days_It_Was_Measured_On()
    {
        var insights = Read(Kept(2026, 1, pageViews: 130, daysMeasured: 13), Kept(2026, 2, pageViews: 280), Unkept(2026, 3));

        insights.Months[0].Should().Be(new MonthReading(new DateOnly(2026, 1, 1), 130, 13));
        insights.Months[1].Should().Be(new MonthReading(new DateOnly(2026, 2, 1), 280, 28));
    }

    /// <summary>The month running now counts today, and begins with the website's first day where that fell in it.</summary>
    [Fact]
    public void The_Month_Running_Now_Counts_Today_Among_Its_Days()
    {
        var running = KeptHistory.Resolve(new KeptMonths(null, [Unkept(2026, 3)]), AsOf, judged: false);

        var fromTheFirst = KeptHistory.Insights(running, new DateOnly(2026, 3, 1), NoReports, TimeZoneInfo.Utc, AsOf);
        var fromTheTenth = KeptHistory.Insights(running, new DateOnly(2026, 3, 10), NoReports, TimeZoneInfo.Utc, AsOf);

        fromTheFirst.Months[0].DaysMeasured.Should().Be(15);
        fromTheTenth.Months[0].DaysMeasured.Should().Be(6);
    }

    /// <summary>Today is the day it is on the website's clock, which in India turns over at 18:30 in UTC.</summary>
    [Fact]
    public void Today_Is_The_Day_It_Is_On_The_Websites_Clock()
    {
        var evening = new DateTimeOffset(2026, 3, 15, 20, 0, 0, TimeSpan.Zero);

        var insights = KeptHistory.Insights(
            KeptHistory.Resolve(new KeptMonths(null, [RunningInIndia()]), evening, judged: false),
            new DateOnly(2026, 3, 1),
            NoReports,
            Kolkata,
            evening);

        insights.Months[0].DaysMeasured.Should().Be(16);
    }

    /// <summary>A finished month not kept yet covers every day of it from the website's first day.</summary>
    [Fact]
    public void A_Finished_Month_Answered_From_Its_Reports_Covers_Its_Days_From_The_First_Day()
    {
        var months = KeptHistory.Resolve(new KeptMonths(null, [Unkept(2026, 2), Unkept(2026, 3)]), AsOf, judged: false);

        var insights = KeptHistory.Insights(months, new DateOnly(2026, 2, 19), NoReports, TimeZoneInfo.Utc, AsOf);

        insights.Months[0].DaysMeasured.Should().Be(10);
        insights.Months[0].PageViews.Should().Be(0);
    }

    /// <summary>
    /// A month that was never counted and no longer can be has no figure and covers no days, so a
    /// year's average over the months it has figures for does not count it as a quiet month.
    /// </summary>
    [Fact]
    public void A_Month_Never_Counted_Adds_Nothing_And_Covers_No_Days()
    {
        var insights = Read(
            Kept(2025, 1, pageViews: 10, bestDay: new DateOnly(2025, 1, 9), bestDayPageViews: 10),
            Unkept(2025, 2),
            Unkept(2026, 3));

        insights.Months[1].Should().Be(new MonthReading(new DateOnly(2025, 2, 1), null, 0));
        insights.Months[0].PageViews.Should().Be(10);
        insights.BestDay.Should().Be(new DateOnly(2025, 1, 9));
    }

    /// <summary>
    /// A month kept before judging had finished with it, and too old to be counted again, has figures
    /// for everybody. Asked about people, nothing of it counts: not its best day, not its weekdays or
    /// hours, and not its days.
    /// </summary>
    [Fact]
    public void A_Month_Whose_People_Were_Never_Settled_Adds_Nothing_To_An_Answer_About_People()
    {
        var unsettled = Kept(
            2025,
            1,
            pageViews: 40,
            bestDay: new DateOnly(2025, 1, 9),
            bestDayPageViews: 40,
            weekdays: [40, 0, 0, 0, 0, 0, 0],
            judgedTo: new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero));
        var record = new KeptMonths(null, [unsettled, Unkept(2026, 3)]);

        var aboutPeople = KeptHistory.Insights(KeptHistory.Resolve(record, AsOf, judged: true), null, NoReports, TimeZoneInfo.Utc, AsOf);
        var aboutEverybody = KeptHistory.Insights(KeptHistory.Resolve(record, AsOf, judged: false), null, NoReports, TimeZoneInfo.Utc, AsOf);

        aboutPeople.Months[0].Should().Be(new MonthReading(new DateOnly(2025, 1, 1), null, 0));
        aboutPeople.BestDay.Should().BeNull();
        aboutPeople.Weekdays.Should().OnlyContain(pages => pages == 0);
        aboutPeople.BusiestWeekdays.Should().BeEmpty();

        aboutEverybody.Months[0].PageViews.Should().Be(40);
        aboutEverybody.BestDay.Should().Be(new DateOnly(2025, 1, 9));
        aboutEverybody.BusiestWeekdays.Should().Equal(0);
    }

    [Fact]
    public void A_Record_With_Nothing_In_It_Says_Nothing()
    {
        var insights = KeptHistory.Insights([], null, NoReports, TimeZoneInfo.Utc, AsOf);

        insights.Months.Should().BeEmpty();
        insights.BestDay.Should().BeNull();
        insights.BusiestHours.Should().BeEmpty();
    }

    private static KeptInsights Read(params RecordedMonth[] months) => Read(NoReports, months);

    private static KeptInsights Read(LiveMonths live, params RecordedMonth[] months)
    {
        var ordered = months.OrderBy(month => month.Month).ToArray();

        return KeptHistory.Insights(
            KeptHistory.Resolve(new KeptMonths(null, [.. ordered]), AsOf, judged: false),
            ordered[0].Month,
            live,
            TimeZoneInfo.Utc,
            AsOf);
    }

    private static RecordedMonth RunningInIndia()
    {
        var begins = new DateTimeOffset(2026, 3, 1, 0, 0, 0, IndiaTime);

        return new RecordedMonth(
            new DateOnly(2026, 3, 1),
            begins,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, IndiaTime),
            begins.AddMonths(1),
            null);
    }

    private static RecordedMonth Unkept(int year, int month) => Recorded(year, month, null);

    private static RecordedMonth Kept(
        int year,
        int month,
        long pageViews = 0,
        int? daysMeasured = null,
        DateOnly? bestDay = null,
        long bestDayPageViews = 0,
        long[]? weekdays = null,
        long[]? hours = null,
        DateTimeOffset? judgedTo = null)
    {
        var begins = Midnight(year, month);
        var ends = begins.AddMonths(1);

        return Recorded(
            year,
            month,
            new KeptFigures(
                new TimeRange(begins, ends),
                null,
                daysMeasured ?? DateTime.DaysInMonth(year, month),
                new MonthFigures(
                    pageViews,
                    pageViews,
                    bestDay,
                    bestDayPageViews,
                    [.. weekdays ?? new long[MonthFigures.Weekdays]],
                    [.. hours ?? new long[MonthFigures.Hours]]),
                [],
                judgedTo ?? ends));
    }

    private static RecordedMonth Recorded(int year, int month, KeptFigures? kept)
    {
        var begins = Midnight(year, month);

        return new RecordedMonth(new DateOnly(year, month, 1), begins, Midnight(year, 1), begins.AddMonths(1), kept);
    }

    private static DateTimeOffset Midnight(int year, int month) => new(year, month, 1, 0, 0, 0, TimeSpan.Zero);
}
