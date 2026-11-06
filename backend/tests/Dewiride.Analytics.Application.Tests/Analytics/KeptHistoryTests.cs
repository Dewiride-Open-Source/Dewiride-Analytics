using System.Collections.Immutable;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Classification;

namespace Dewiride.Analytics.Application.Tests.Analytics;

/// <summary>
/// Covers reading a website's whole record from what was kept of its finished months and, for the
/// months nothing was kept of, from their reports.
/// </summary>
/// <remarks>
/// The record is read at the middle of March 2026, in UTC. A month that began before about the
/// middle of March 2025 can no longer be counted in full, so a record reaching back that far is
/// how a month comes to be missing.
/// </remarks>
public sealed class KeptHistoryTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Where judging has reached for the months answered from their reports.</summary>
    private static readonly DateTimeOffset LiveJudgedTo = AsOf.AddMinutes(-30);

    /// <summary>When the website the record is read for was added: in the summer of 2024.</summary>
    private static readonly DateTimeOffset AddedAt = new(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_Month_Running_Now_Is_Answered_From_Its_Reports_Even_Where_Something_Was_Kept_Of_It()
    {
        var record = Record(Kept(2026, 1), Kept(2026, 2), Kept(2026, 3));

        Sources(record).Should().Equal(MonthSource.Kept, MonthSource.Kept, MonthSource.Live);
    }

    /// <summary>
    /// Counting runs in the background and may be behind or switched off. A finished month whose
    /// reports are all still held is answered from them rather than left out.
    /// </summary>
    [Fact]
    public void A_Month_Nothing_Was_Kept_Of_Is_Answered_From_Its_Reports_While_They_Are_All_Held()
    {
        var record = Record(Unkept(2026, 1), Kept(2026, 2), Unkept(2026, 3));

        Sources(record).Should().Equal(MonthSource.Live, MonthSource.Kept, MonthSource.Live);
    }

    /// <summary>
    /// Once a month's oldest report could be gone, an answer from its reports would come back short
    /// without saying so. Nothing was kept of it either, so it is missing — never counted as nothing.
    /// </summary>
    [Fact]
    public void A_Month_Nothing_Was_Kept_Of_Whose_Reports_May_Have_Gone_Is_Missing()
    {
        var record = Record(Kept(2025, 1), Unkept(2025, 2), Unkept(2025, 4), Unkept(2026, 3));

        Sources(record).Should().Equal(MonthSource.Kept, MonthSource.Missing, MonthSource.Live, MonthSource.Live);
    }

    /// <summary>
    /// A month kept before judging had finished with it, and too old to be counted again, has its
    /// figures for everybody but never had its people settled. An answer resting on verdicts leaves
    /// it out and says so; an answer about everybody reads it as kept.
    /// </summary>
    [Fact]
    public void A_Month_Kept_Before_Judging_Finished_Is_Missing_From_Answers_Resting_On_Verdicts_Once_It_Froze()
    {
        var frozen = Kept(2025, 1, judgedTo: new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero));
        var record = Record(frozen, Unkept(2026, 3));

        KeptHistory.Resolve(record, AsOf, judged: true).Select(month => month.Source)
            .Should().Equal(MonthSource.Missing, MonthSource.Live);
        KeptHistory.Resolve(record, AsOf, judged: false).Select(month => month.Source)
            .Should().Equal(MonthSource.Kept, MonthSource.Live);
    }

    /// <summary>
    /// While its reports remain a month like that is counted again as judging moves on, so it is
    /// read as kept and said to be incomplete instead.
    /// </summary>
    [Fact]
    public void A_Month_Kept_Before_Judging_Finished_Is_Kept_While_It_Can_Still_Be_Counted_Again()
    {
        var waiting = Kept(2026, 1, judgedTo: new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero));
        var record = Record(waiting, Unkept(2026, 3));

        KeptHistory.Resolve(record, AsOf, judged: true).Select(month => month.Source)
            .Should().Equal(MonthSource.Kept, MonthSource.Live);
    }

    [Fact]
    public void The_Months_Answered_From_Their_Reports_Begin_At_The_First_Of_Them()
    {
        var months = KeptHistory.Resolve(Record(Kept(2026, 1), Unkept(2026, 2), Unkept(2026, 3)), AsOf, judged: false);

        KeptHistory.LiveFrom(months).Should().Be(Midnight(2026, 2));
        KeptHistory.LiveFrom([]).Should().BeNull();
    }

    [Fact]
    public void The_First_Day_Is_The_One_Kept_With_The_First_Month()
    {
        var record = Reporting(
            new DateOnly(2026, 1, 2),
            Kept(2025, 11, firstDay: new DateOnly(2025, 11, 19)),
            Kept(2025, 12),
            Unkept(2026, 3));

        FirstDayOf(record).Should().Be(new DateOnly(2025, 11, 19));
    }

    /// <summary>
    /// Where the first month was never kept, its oldest report is its first day — for a website first
    /// visited long after it was added, too, where months have been kept all along.
    /// </summary>
    [Fact]
    public void The_First_Day_Of_A_Record_Nothing_Was_Kept_Of_Is_The_Day_Of_Its_Oldest_Report()
    {
        var record = Reporting(new DateOnly(2026, 2, 19), Unkept(2026, 2), Unkept(2026, 3));

        FirstDayOf(record).Should().Be(new DateOnly(2026, 2, 19));
    }

    /// <summary>
    /// A website added long before this installation began keeping months may have sent reports that
    /// went uncounted, so its oldest report is not named as its first day — before its month is kept
    /// any more than after, where the engine keeps no first day for it either.
    /// </summary>
    [Fact]
    public void The_First_Day_Is_Not_Named_From_The_Oldest_Report_Where_Earlier_Ones_May_Have_Gone_Uncounted()
    {
        var record = Reporting(new DateOnly(2026, 2, 19), Unkept(2026, 2), Unkept(2026, 3)) with
        {
            KeepingSince = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero),
        };

        FirstDayOf(record).Should().Be(new DateOnly(2026, 2, 1));
    }

    /// <summary>
    /// The record reads a kept first day as it was kept, with the months it was kept with. A change of
    /// zone moves both when the months are next counted, so until then the record never names a day
    /// its own months do not show.
    /// </summary>
    [Fact]
    public void A_Kept_First_Day_Is_Read_As_It_Was_Kept_Until_Its_Months_Are_Counted_Again()
    {
        var record = Reporting(
            new DateOnly(2026, 2, 1),
            Kept(2026, 1, firstDay: new DateOnly(2026, 1, 31)),
            Unkept(2026, 2),
            Unkept(2026, 3));
        var months = KeptHistory.Resolve(record, AsOf, judged: false);

        KeptHistory.FirstDay(months, record, AddedAt, AsOf).Should().Be(new DateOnly(2026, 1, 31));
        months.Select(month => month.Month.Month)
            .Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1));
    }

    /// <summary>
    /// A first day kept with a later month says nothing of when an earlier month's figures began.
    /// Figures are never dropped for it: the record opens on that month, from the first of it.
    /// </summary>
    [Fact]
    public void A_Month_With_Figures_Before_A_Kept_First_Day_Begins_The_Record()
    {
        var record = Reporting(
            new DateOnly(2026, 2, 10),
            Kept(2025, 11),
            Kept(2026, 2, firstDay: new DateOnly(2026, 2, 10)),
            Unkept(2026, 3));
        var months = KeptHistory.Resolve(record, AsOf, judged: false);

        KeptHistory.FirstDay(months, record, AddedAt, AsOf).Should().Be(new DateOnly(2025, 11, 1));
        months.Should().HaveCount(3);
    }

    /// <summary>
    /// After a change to a calendar behind the old one, the first report can fall in the month before
    /// the one that kept the first day — a month still answered from its reports, which the next count
    /// will keep the first day with, read a day earlier. The record begins on that day, the one the
    /// month will be kept with, not on the first of it.
    /// </summary>
    [Fact]
    public void A_Month_Answered_From_Its_Reports_Before_A_Kept_First_Day_Begins_On_The_Day_Its_Next_Count_Keeps()
    {
        var record = Reporting(
            new DateOnly(2026, 1, 31),
            Unkept(2026, 1),
            Kept(2026, 2, firstDay: new DateOnly(2026, 2, 1)),
            Unkept(2026, 3));
        var months = KeptHistory.Resolve(record, AsOf, judged: false);

        KeptHistory.FirstDay(months, record, AddedAt, AsOf).Should().Be(new DateOnly(2026, 1, 31));
        months.Should().HaveCount(3);
    }

    /// <summary>
    /// A change to a calendar more than a day ahead of the old one can leave the first day's old month
    /// counted again as measured on none of its days while the month after, kept on the old calendar
    /// without a first day, waits for its own count. That month will decide the first day when it is
    /// counted, so the record names the day it will keep rather than the first of it.
    /// </summary>
    [Fact]
    public void A_First_Month_Kept_On_Another_Calendar_Is_Named_From_The_Day_Its_Next_Count_Keeps()
    {
        var february = Midnight(2026, 2);
        var record = Reporting(
            new DateOnly(2026, 2, 2),
            Reshaped(Kept(2026, 1), daysMeasured: 0),
            Reshaped(Kept(2026, 2), counted: new TimeRange(february.AddHours(12), february.AddMonths(1).AddHours(12))),
            Unkept(2026, 3));
        var months = KeptHistory.Resolve(record, AsOf, judged: false);

        KeptHistory.FirstDay(months, record, AddedAt, AsOf).Should().Be(new DateOnly(2026, 2, 2));
    }

    /// <summary>
    /// A month answered from its reports in front of a kept first day holds figures too. Where its
    /// oldest report is too far from the kept day to be the same visit on another calendar, the next
    /// count will not move the day into it, and it begins the record from the first of it.
    /// </summary>
    [Fact]
    public void A_Month_Answered_From_Its_Reports_Before_A_Kept_First_Day_Begins_The_Record()
    {
        var record = Reporting(
            new DateOnly(2026, 1, 28),
            Unkept(2026, 1),
            Kept(2026, 2, firstDay: new DateOnly(2026, 2, 1)),
            Unkept(2026, 3));
        var months = KeptHistory.Resolve(record, AsOf, judged: false);

        KeptHistory.FirstDay(months, record, AddedAt, AsOf).Should().Be(new DateOnly(2026, 1, 1));
        months.Should().HaveCount(3);
    }

    /// <summary>
    /// A change of zone to a calendar behind the old one can put the first visit's hours in a month
    /// before the one that keeps the first day — one never counted and no longer countable, so those
    /// hours are in no month. It holds nothing, so it is no part of the record, and the record
    /// opens on the month that keeps the first day.
    /// </summary>
    [Fact]
    public void A_Month_Never_Counted_Before_The_Kept_First_Day_Is_No_Part_Of_The_Record()
    {
        var record = Reporting(
            new DateOnly(2025, 1, 31),
            Unkept(2025, 1),
            Kept(2025, 2, firstDay: new DateOnly(2025, 2, 1)),
            Unkept(2026, 3));
        var months = KeptHistory.Resolve(record, AsOf, judged: false);

        months.Select(month => month.Month.Month).Should().Equal(new DateOnly(2025, 2, 1), new DateOnly(2026, 3, 1));
        KeptHistory.FirstDay(months, record, AddedAt, AsOf).Should().Be(new DateOnly(2025, 2, 1));
        KeptHistory.Missing(months).Should().BeEmpty();
    }

    /// <summary>
    /// A first month kept after its first reports went says nothing of the day measuring began, and
    /// the oldest report still held is later than the record. The record's first month is the
    /// earliest anything can still say.
    /// </summary>
    [Fact]
    public void The_First_Day_Is_The_First_Of_The_Record_Where_Nothing_Says_Otherwise()
    {
        var record = Reporting(new DateOnly(2026, 1, 3), Kept(2025, 11), Kept(2025, 12), Unkept(2026, 3));

        FirstDayOf(record).Should().Be(new DateOnly(2025, 11, 1));
    }

    [Fact]
    public void A_Website_Never_Measured_Has_No_First_Day()
    {
        FirstDayOf(new KeptMonths(null, [])).Should().BeNull();
    }

    /// <summary>
    /// A first month nothing was kept of and whose reports may already have begun to go says only
    /// how far back the store still reaches, which moves on every day. The first of that month is the
    /// earliest anything can still say, and it stays put.
    /// </summary>
    [Fact]
    public void The_First_Day_Is_The_First_Of_A_First_Month_That_Can_No_Longer_Be_Counted()
    {
        var record = Reporting(new DateOnly(2025, 2, 19), Unkept(2025, 2), Unkept(2026, 3));

        FirstDayOf(record).Should().Be(new DateOnly(2025, 2, 1));
    }

    /// <summary>
    /// A first month kept without a first day was counted after the website's first reports went, so
    /// the oldest report still held inside it is no first day either — it is wherever the store has
    /// deleted up to today.
    /// </summary>
    [Fact]
    public void The_First_Day_Of_A_First_Month_Kept_Without_One_Is_The_First_Of_It()
    {
        var record = Reporting(new DateOnly(2025, 12, 17), Kept(2025, 12), Unkept(2026, 3));

        FirstDayOf(record).Should().Be(new DateOnly(2025, 12, 1));
    }

    /// <summary>
    /// A change to a zone ahead of the old one can leave a month in front of the website's first day,
    /// kept as measured on none of its days. It is no part of the record: the record, and its first
    /// day, begin with the month the first report falls in.
    /// </summary>
    [Fact]
    public void A_Month_A_Change_Of_Zone_Left_Before_The_First_Day_Is_No_Part_Of_The_Record()
    {
        var record = Reporting(new DateOnly(2026, 2, 1), Reshaped(Kept(2026, 1), daysMeasured: 0), Unkept(2026, 2), Unkept(2026, 3));
        var months = KeptHistory.Resolve(record, AsOf, judged: false);

        months.Select(month => month.Month.Month).Should().Equal(new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1));
        KeptHistory.FirstDay(months, record, AddedAt, AsOf).Should().Be(new DateOnly(2026, 2, 1));
    }

    /// <summary>
    /// A month frozen before the website moved to a zone ahead of the old one keeps the old zone's
    /// end, and the next month, counted again in the new zone, begins before it. The hours between
    /// are in both months, so every total built from them counts those hours twice. That is what
    /// the record says it does; a change that stops doing it has to say so too.
    /// </summary>
    [Fact]
    public void A_Zone_Change_Counts_The_Overlap_At_The_Freeze_Line_In_Both_Months()
    {
        var frozen = Kept(2025, 8, pageViews: 13);
        var recounted = Reshaped(
            Kept(2025, 9, pageViews: 23),
            counted: new TimeRange(
                new DateTimeOffset(2025, 8, 31, 18, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2025, 9, 30, 18, 30, 0, TimeSpan.Zero)));
        var months = KeptHistory.Resolve(Record(frozen, recounted, Unkept(2026, 3)), AsOf, judged: false);

        KeptHistory.Series(months, PageViews, Live(), KeptGranularity.Month)
            .Select(point => point.Value)
            .Should().Equal(13, 23, 0);
        KeptHistory.Series(months, PageViews, Live(), KeptGranularity.Year)
            .Select(point => point.Value)
            .Should().Equal(36, 0);
    }

    [Fact]
    public void Missing_Months_Are_Named_By_Their_Year_And_Month()
    {
        var months = KeptHistory.Resolve(
            Record(Kept(2025, 1), Unkept(2025, 2), Unkept(2025, 3), Unkept(2026, 3)),
            AsOf,
            judged: false);

        KeptHistory.Missing(months).Should().Equal("2025-02", "2025-03");
        KeptHistory.Unjudged(months).Should().BeEmpty();
    }

    /// <summary>
    /// Each answer says for itself why it leaves a month out: never counted, or kept before judging had
    /// finished with it. A month kept that way is left out only of the answers resting on verdicts,
    /// and is never called uncounted, since its figures for everybody stand.
    /// </summary>
    [Fact]
    public void A_Month_Never_Counted_And_A_Month_Kept_Before_Judging_Finished_Are_Named_Apart()
    {
        var record = Record(
            Kept(2025, 1, judgedTo: new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero)),
            Unkept(2025, 2),
            Unkept(2026, 3));
        var judged = KeptHistory.Resolve(record, AsOf, judged: true);
        var everybody = KeptHistory.Resolve(record, AsOf, judged: false);

        KeptHistory.Missing(judged).Should().Equal("2025-02");
        KeptHistory.Unjudged(judged).Should().Equal("2025-01");
        KeptHistory.Missing(everybody).Should().Equal("2025-02");
        KeptHistory.Unjudged(everybody).Should().BeEmpty();
    }

    /// <summary>
    /// A month still held that was counted before judging had finished with it is where the record's
    /// people stop being complete, so everything after it is said to be still settling.
    /// </summary>
    [Fact]
    public void A_Month_Still_Held_That_Judging_Had_Not_Finished_Holds_The_Rest_Back()
    {
        var stopped = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        var months = KeptHistory.Resolve(Record(Kept(2026, 1, judgedTo: stopped), Kept(2026, 2), Unkept(2026, 3)), AsOf, judged: true);

        KeptHistory.JudgedTo(months, LiveJudgedTo, AsOf).Should().Be(stopped);
    }

    /// <summary>
    /// A month that can no longer be counted again will never be judged any further. Holding the
    /// record back at it would wash every later month as still settling for ever.
    /// </summary>
    [Fact]
    public void A_Month_That_Froze_Unjudged_Does_Not_Hold_Back_The_Months_After_It()
    {
        var frozen = Kept(2025, 1, judgedTo: new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero));
        var record = Record(frozen, Kept(2026, 2), Unkept(2026, 3));

        KeptHistory.JudgedTo(KeptHistory.Resolve(record, AsOf, judged: true), LiveJudgedTo, AsOf).Should().Be(LiveJudgedTo);
        KeptHistory.JudgedTo(KeptHistory.Resolve(record, AsOf, judged: false), LiveJudgedTo, AsOf).Should().Be(LiveJudgedTo);
    }

    [Fact]
    public void A_Record_Judged_Whole_Is_Complete_To_Where_Judging_Of_Its_Reports_Has_Reached()
    {
        var months = KeptHistory.Resolve(Record(Kept(2026, 1), Kept(2026, 2), Unkept(2026, 3)), AsOf, judged: true);

        KeptHistory.JudgedTo(months, LiveJudgedTo, AsOf).Should().Be(LiveJudgedTo);
    }

    [Fact]
    public void Each_Month_Gives_What_Was_Kept_Of_It_What_Its_Reports_Say_Or_Nothing()
    {
        var months = KeptHistory.Resolve(
            Record(Kept(2025, 1, pageViews: 50), Unkept(2025, 2), Kept(2026, 2, pageViews: 70), Unkept(2026, 3)),
            AsOf,
            judged: false);

        var points = KeptHistory.Series(months, PageViews, Live((Midnight(2026, 3), 9)), KeptGranularity.Month);

        points.Should().Equal(
            new KeptPoint(Midnight(2025, 1), 50),
            new KeptPoint(Midnight(2025, 2), null),
            new KeptPoint(Midnight(2026, 2), 70),
            new KeptPoint(Midnight(2026, 3), 9));
    }

    /// <summary>The reports of a month nothing was read in hold nothing, so it counts as nothing.</summary>
    [Fact]
    public void A_Month_Answered_From_Its_Reports_That_Held_None_Counts_As_Nothing()
    {
        var months = KeptHistory.Resolve(Record(Unkept(2026, 2), Unkept(2026, 3)), AsOf, judged: false);

        KeptHistory.Series(months, PageViews, Live((Midnight(2026, 3), 4)), KeptGranularity.Month)
            .Select(point => point.Value)
            .Should().Equal(0, 4);
    }

    /// <summary>
    /// A year adds up the months it has figures for, and is said to have none only where it has
    /// none of them. Each year is named by its first instant where the website is.
    /// </summary>
    [Fact]
    public void A_Year_Adds_Up_The_Months_It_Has_Figures_For()
    {
        var months = KeptHistory.Resolve(
            Record(Unkept(2024, 12), Kept(2025, 1, pageViews: 10), Unkept(2025, 2), Kept(2026, 1, pageViews: 5), Unkept(2026, 3)),
            AsOf,
            judged: false);

        var years = KeptHistory.Series(months, PageViews, Live((Midnight(2026, 3), 7)), KeptGranularity.Year);

        years.Should().Equal(
            new KeptPoint(Midnight(2024, 1), null),
            new KeptPoint(Midnight(2025, 1), 10),
            new KeptPoint(Midnight(2026, 1), 12));
    }

    /// <summary>
    /// Every kind of visit the record holds anywhere has a count in every bucket, so the groups line
    /// up on one list of buckets. A kept month that had none of a kind had none; a missing month has
    /// no count in any group.
    /// </summary>
    [Fact]
    public void Who_Came_Lines_Every_Kind_Of_Visit_Up_On_One_List_Of_Months()
    {
        var months = KeptHistory.Resolve(
            Record(
                Kept(2024, 12, verdicts: [new VerdictTally(TrafficCategory.LikelyHuman, 3, 9)]),
                Unkept(2025, 1),
                Unkept(2026, 3)),
            AsOf,
            judged: true);
        var live = new TrafficSeries(
            [Midnight(2026, 3)],
            [new TrafficSeriesGroup(TrafficCategory.KnownAiCrawler, [4], [8])]);

        var traffic = KeptHistory.Traffic(months, live, KeptGranularity.Month);

        traffic.Buckets.Should().Equal(Midnight(2024, 12), Midnight(2025, 1), Midnight(2026, 3));
        traffic.Groups.Select(group => group.Category)
            .Should().Equal(TrafficCategory.LikelyHuman, TrafficCategory.KnownAiCrawler);
        traffic.Groups[0].Visits.Should().Equal(3, null, 0);
        traffic.Groups[0].PageViews.Should().Equal(9, null, 0);
        traffic.Groups[1].Visits.Should().Equal(0, null, 4);
        traffic.Groups[1].PageViews.Should().Equal(0, null, 8);
    }

    [Fact]
    public void Who_Came_By_The_Year_Adds_Up_Each_Kind_Of_Visit()
    {
        var months = KeptHistory.Resolve(
            Record(
                Kept(2025, 11, verdicts: [new VerdictTally(TrafficCategory.LikelyHuman, 3, 9)]),
                Kept(2025, 12, verdicts: [new VerdictTally(TrafficCategory.LikelyHuman, 2, 4)]),
                Unkept(2026, 3)),
            AsOf,
            judged: true);
        var live = new TrafficSeries(
            [Midnight(2026, 3)],
            [new TrafficSeriesGroup(TrafficCategory.LikelyHuman, [1], [1])]);

        var traffic = KeptHistory.Traffic(months, live, KeptGranularity.Year);

        traffic.Buckets.Should().Equal(Midnight(2025, 1), Midnight(2026, 1));
        traffic.Groups.Should().ContainSingle();
        traffic.Groups[0].Visits.Should().Equal(5, 1);
        traffic.Groups[0].PageViews.Should().Equal(13, 1);
    }

    private static MonthSource[] Sources(KeptMonths record) =>
        [.. KeptHistory.Resolve(record, AsOf, judged: false).Select(month => month.Source)];

    /// <summary>The website's first day, as All time reads it.</summary>
    private static DateOnly? FirstDayOf(KeptMonths record) =>
        KeptHistory.FirstDay(KeptHistory.Resolve(record, AsOf, judged: false), record, AddedAt, AsOf);

    private static KeptMonths Record(params RecordedMonth[] months) => new(null, [.. months]);

    /// <summary>
    /// A record whose oldest report still held arrived on the given day, on an installation that has
    /// kept months since before the website was added (<see cref="AddedAt"/>).
    /// </summary>
    private static KeptMonths Reporting(DateOnly firstReportDay, params RecordedMonth[] months) =>
        new(firstReportDay, [.. months]) { KeepingSince = AddedAt.AddMonths(-1) };

    private static long PageViews(KeptFigures figures) => figures.Figures.PageViews;

    private static Dictionary<DateTimeOffset, long> Live(params (DateTimeOffset Begins, long Value)[] months) =>
        months.ToDictionary(month => month.Begins, month => month.Value);

    private static RecordedMonth Unkept(int year, int month) => Recorded(year, month, null);

    private static RecordedMonth Kept(
        int year,
        int month,
        long pageViews = 1,
        DateOnly? firstDay = null,
        DateTimeOffset? judgedTo = null,
        ImmutableArray<VerdictTally>? verdicts = null)
    {
        var begins = Midnight(year, month);
        var ends = begins.AddMonths(1);

        return Recorded(
            year,
            month,
            new KeptFigures(
                new TimeRange(begins, ends),
                firstDay,
                DateTime.DaysInMonth(year, month),
                new MonthFigures(pageViews, pageViews, null, 0, [.. new long[MonthFigures.Weekdays]], [.. new long[MonthFigures.Hours]]),
                verdicts ?? [],
                judgedTo ?? ends));
    }

    /// <summary>A kept month, counted over another stretch or measured on fewer days than its own.</summary>
    private static RecordedMonth Reshaped(RecordedMonth month, int? daysMeasured = null, TimeRange? counted = null)
    {
        var kept = month.Kept!;

        return month with
        {
            Kept = kept with { DaysMeasured = daysMeasured ?? kept.DaysMeasured, Counted = counted ?? kept.Counted },
        };
    }

    private static RecordedMonth Recorded(int year, int month, KeptFigures? kept)
    {
        var begins = Midnight(year, month);

        return new RecordedMonth(new DateOnly(year, month, 1), begins, Midnight(year, 1), begins.AddMonths(1), kept);
    }

    private static DateTimeOffset Midnight(int year, int month) => new(year, month, 1, 0, 0, 0, TimeSpan.Zero);
}
