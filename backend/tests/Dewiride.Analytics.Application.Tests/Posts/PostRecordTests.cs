using System.Collections.Immutable;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Posts;

/// <summary>
/// Covers what one post is said to have done on its own page: its views, the months it was read in and the
/// days each covers, and its recent weeks day by day.
/// </summary>
/// <remarks>
/// Read on a Saturday morning, the seventh of November 2026, in UTC, for a website first measured on the
/// eleventh of October 2025 whose months were kept to September 2026. Unless a test says otherwise the
/// post's months were kept to September too, so the reports take over in October, and the post was listed
/// before any of them was counted.
/// </remarks>
public sealed class PostRecordTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Today, where the website is.</summary>
    private static readonly DateOnly Today = new(2026, 11, 7);

    /// <summary>When the website was added.</summary>
    private static readonly DateTimeOffset AddedAt = new(2025, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The website's first measured day.</summary>
    private static readonly DateOnly WebsiteFirstDay = new(2025, 10, 11);

    /// <summary>The first month of the website's record.</summary>
    private static readonly DateOnly FirstMonth = new(2025, 10, 1);

    /// <summary>The last month the website's months, and by default its posts, were kept for.</summary>
    private static readonly DateOnly LastKept = new(2026, 9, 1);

    /// <summary>The month the reports take over at by default.</summary>
    private static readonly DateOnly October = new(2026, 10, 1);

    /// <summary>The month running now.</summary>
    private static readonly DateOnly Running = new(2026, 11, 1);

    /// <summary>When the post was first listed, unless a test says otherwise: before anything was counted.</summary>
    private static readonly DateTimeOffset ListedLongAgo = new(2025, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Where judging has reached for what the reports answer.</summary>
    private static readonly DateTimeOffset LiveJudgedTo = AsOf.AddMinutes(-30);

    /// <summary>
    /// A post's views are what was kept of it in each month before the reports take over, and the views the
    /// reports count since — not the reports' months added up, which can come to a little more.
    /// </summary>
    [Fact]
    public void A_Posts_Views_Are_Its_Months_Kept_Before_The_Reports_Take_Over_And_The_Reports_Since()
    {
        var sources = Sources(Post(), KeptEachMonth(month => month.Month)) with
        {
            LiveViews = 9,
            LiveMonths = [Point(October, 4), Point(Running, 6)],
        };

        var reading = Read(sources);

        reading.PageViews.Should().Be(78 + 9);
        reading.ViewsFrom.Should().Be(FirstMonth);
        reading.Months.Select(month => month.PageViews).TakeLast(2).Should().Equal(4, 6);
    }

    /// <summary>
    /// A month kept after the reports took over is answered from the reports, as every month after it is,
    /// and what was kept of it adds nothing.
    /// </summary>
    [Fact]
    public void A_Month_Kept_After_The_Reports_Took_Over_Is_Read_From_The_Reports_Alone()
    {
        var august = new DateOnly(2026, 8, 1);
        var september = new DateOnly(2026, 9, 1);
        var kept = KeptEachMonth(_ => 1).Where(month => month.Month != august).ToImmutableArray();
        var sources = Sources(Post(), [.. kept.Select(month => month.Month == september ? month with { PageViews = 50 } : month)]) with
        {
            LiveViews = 6,
            LiveMonths = [Point(august, 3), Point(september, 2), Point(October, 1), Point(Running, 0)],
        };

        var reading = Read(sources);

        sources.LiveFrom!.Month.Should().Be(august);
        Month(reading, september).PageViews.Should().Be(2);
        reading.PageViews.Should().Be(10 + 6);
    }

    /// <summary>
    /// A month counted while the post was listed that kept nothing at its paths is a month nobody read it in.
    /// </summary>
    [Fact]
    public void A_Month_Counted_After_The_Post_Was_Listed_Without_It_Is_Nought()
    {
        var january = new DateOnly(2026, 1, 1);
        var sources = Sources(
            Post(published: new DateOnly(2025, 10, 15)),
            KeptEachMonth(month => month == january ? 0 : 1));

        Month(Read(sources), january).Should().Be(new MonthReading(january, 0, 31));
    }

    /// <summary>
    /// A month counted before the post was listed kept nothing at its paths whoever read it, so nothing says
    /// how much it was read then: the month has no figure and covers no days. One counted afterwards is read.
    /// </summary>
    [Fact]
    public void A_Month_Counted_Before_The_Post_Was_Listed_Has_No_Figure()
    {
        var january = new DateOnly(2026, 1, 1);
        var listed = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var sources = Sources(
            Post(published: new DateOnly(2025, 10, 15), listedAt: listed),
            KeptEachMonth(month => month < new DateOnly(2026, 3, 1) ? 0 : 2));

        var reading = Read(sources);

        Month(reading, january).Should().Be(new MonthReading(january, null, 0));
        Month(reading, new DateOnly(2026, 3, 1)).Should().Be(new MonthReading(new DateOnly(2026, 3, 1), 2, 31));
        reading.PageViews.Should().Be(7 * 2);
    }

    [Fact]
    public void A_Month_Whose_Posts_Were_Never_Kept_And_Can_No_Longer_Be_Counted_Has_No_Figure_And_No_Days()
    {
        var november = new DateOnly(2025, 11, 1);
        var sources = Sources(Post(), [.. KeptEachMonth(_ => 1).Where(month => month.Month != november)]);

        var reading = Read(sources);

        sources.LiveFrom!.Month.Should().Be(October);
        Month(reading, november).Should().Be(new MonthReading(november, null, 0));
    }

    [Fact]
    public void A_Posts_Months_Begin_With_The_Month_It_Was_Published()
    {
        var march = new DateOnly(2026, 3, 1);
        var reading = Read(Sources(Post(published: new DateOnly(2026, 3, 14)), KeptEachMonth(month => month < march ? 0 : 5)));

        reading.Months.Select(month => month.Month).Should().Equal(MonthsFrom(march));
    }

    /// <summary>A post read before the day it says it was published begins with the month it was first read in.</summary>
    [Fact]
    public void A_Posts_Months_Begin_With_The_First_Month_It_Was_Read_Where_That_Came_First()
    {
        var january = new DateOnly(2026, 1, 1);
        var reading = Read(Sources(Post(published: new DateOnly(2026, 3, 14)), KeptEachMonth(month => month < january ? 0 : 2)));

        reading.Months[0].Month.Should().Be(january);
    }

    /// <summary>
    /// A post published before the first month its views are counted from begins with that month, which is
    /// as far back as anything says how much it was read.
    /// </summary>
    [Fact]
    public void A_Posts_Months_Never_Begin_Before_Its_Views_Are_Counted_From()
    {
        var reading = Read(Sources(Post(published: new DateOnly(2024, 5, 1)), KeptEachMonth(_ => 1)));

        reading.ViewsFrom.Should().Be(FirstMonth);
        reading.Months[0].Month.Should().Be(FirstMonth);
    }

    [Fact]
    public void A_Post_Never_Read_And_Never_Dated_Has_The_Month_Running_Now_Alone()
    {
        var reading = Read(Sources(Post(), KeptEachMonth(_ => 0)));

        reading.Months.Should().Equal(new MonthReading(Running, 0, 7));
        reading.From.Should().Be(Running);
    }

    [Fact]
    public void A_Post_Dated_After_Today_And_Never_Read_Has_The_Month_Running_Now_Alone()
    {
        var reading = Read(Sources(Post(published: new DateOnly(2027, 1, 10)), KeptEachMonth(_ => 0)));

        reading.Months.Should().Equal(new MonthReading(Running, 0, 7));
    }

    /// <summary>
    /// An undated post begins with the first month that may hold its views: one it was read in, or one that
    /// cannot say — counted before it was listed — rather than one known to hold none.
    /// </summary>
    [Fact]
    public void An_Undated_Posts_Months_Begin_With_The_First_Month_That_May_Hold_Its_Views()
    {
        var december = new DateOnly(2025, 12, 1);
        var listed = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var recounted = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        ImmutableArray<KeptMonthOfPost> kept =
        [
            PostKept(FirstMonth, 0, countedAt: recounted),
            PostKept(new DateOnly(2025, 11, 1), 0, countedAt: recounted),
            PostKept(december, 0),
            .. MonthsBetween(new DateOnly(2026, 1, 1), LastKept).Select(month => PostKept(month, 3)),
        ];

        var reading = Read(Sources(Post(listedAt: listed), kept));

        reading.Months[0].Should().Be(new MonthReading(december, null, 0));
        reading.Months[1].Should().Be(new MonthReading(new DateOnly(2026, 1, 1), 3, 31));
    }

    [Fact]
    public void A_Posts_Record_Begins_On_The_Day_Its_First_Month_Is_Counted_From()
    {
        var dated = Read(Sources(Post(published: new DateOnly(2026, 3, 14)), KeptEachMonth(_ => 1)));
        var undated = Read(Sources(Post(), KeptEachMonth(_ => 1)));

        dated.Months[0].Month.Should().Be(new DateOnly(2025, 10, 1));
        dated.From.Should().Be(WebsiteFirstDay);
        undated.From.Should().Be(WebsiteFirstDay);
        Read(Sources(Post(published: new DateOnly(2026, 3, 14)), KeptEachMonth(month => month.Month >= 3 && month.Year == 2026 ? 1 : 0)))
            .From.Should().Be(new DateOnly(2026, 3, 14));
    }

    /// <summary>
    /// A post that says it will be published later this month, and is not read yet, has a record that begins
    /// today and counts today alone.
    /// </summary>
    [Fact]
    public void A_Post_Dated_Later_This_Month_Has_A_Record_That_Begins_Today()
    {
        var reading = Read(Sources(Post(published: new DateOnly(2026, 11, 20)), KeptEachMonth(_ => 0)));

        reading.From.Should().Be(Today);
        reading.Months.Should().Equal(new MonthReading(Running, 0, 1));
    }

    [Fact]
    public void The_Month_A_Post_Was_Published_In_Counts_Its_Days_From_The_Day_It_Was_Published()
    {
        var march = new DateOnly(2026, 3, 1);
        var reading = Read(Sources(Post(published: new DateOnly(2026, 3, 14)), KeptEachMonth(month => month < march ? 0 : 5)));

        Month(reading, march).Should().Be(new MonthReading(march, 5, 18));
    }

    [Fact]
    public void The_Month_A_Post_Was_Published_In_Counts_Its_Days_From_The_Websites_First_Day_Where_That_Came_Later()
    {
        var reading = Read(Sources(Post(published: new DateOnly(2025, 10, 5)), KeptEachMonth(_ => 1)));

        reading.Months[0].Should().Be(new MonthReading(FirstMonth, 1, 21));
    }

    /// <summary>
    /// A month before the post says it was published that it was read in covers every day the website was
    /// measured on: nothing says which of them the post could be read on.
    /// </summary>
    [Fact]
    public void A_Month_Before_A_Post_Was_Published_That_It_Was_Read_In_Counts_Every_Day_The_Website_Was_Measured()
    {
        var january = new DateOnly(2026, 1, 1);
        var reading = Read(Sources(Post(published: new DateOnly(2026, 3, 14)), KeptEachMonth(month => month < january ? 0 : 2)));

        reading.Months.Take(3).Should().Equal(
            new MonthReading(january, 2, 31),
            new MonthReading(new DateOnly(2026, 2, 1), 2, 28),
            new MonthReading(new DateOnly(2026, 3, 1), 2, 18));
    }

    [Fact]
    public void An_Undated_Post_Counts_The_Days_The_Website_Was_Measured()
    {
        var reading = Read(Sources(Post(), KeptEachMonth(_ => 1)));

        reading.Months[0].Should().Be(new MonthReading(FirstMonth, 1, 21));
        reading.Months[1].Should().Be(new MonthReading(new DateOnly(2025, 11, 1), 1, 30));
    }

    [Fact]
    public void A_Kept_Month_Counts_The_Days_Kept_With_It()
    {
        var january = new DateOnly(2026, 1, 1);
        var record = Record(
            new DateOnly(2025, 11, 8),
            WebsiteMonths().Select(month => month.Month == january ? month with { Kept = month.Kept! with { DaysMeasured = 20 } } : month));

        var reading = Read(Sources(Post(), KeptEachMonth(_ => 1), record));

        Month(reading, january).DaysMeasured.Should().Be(20);
    }

    [Fact]
    public void The_Month_Running_Now_Counts_Today_Among_Its_Days()
    {
        var reading = Read(Sources(Post(), KeptEachMonth(_ => 1)) with { LiveMonths = [Point(October, 0), Point(Running, 3)] });

        reading.Months[^2].Should().Be(new MonthReading(October, 0, 31));
        reading.Months[^1].Should().Be(new MonthReading(Running, 3, 7));
    }

    /// <summary>A post published late in the evening in UTC was published the next morning in Kolkata.</summary>
    [Fact]
    public void A_Post_Is_Published_On_The_Day_It_Is_Where_The_Website_Is()
    {
        var kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var post = Post() with
        {
            Listed = new ListedPost("/posts/a/", null, new DateTimeOffset(2026, 3, 13, 20, 0, 0, TimeSpan.Zero)),
        };

        PostRecord.Read(Sources(post, KeptEachMonth(_ => 1)), AddedAt, kolkata, AsOf)
            .Published.Should().Be(new DateOnly(2026, 3, 14));
    }

    /// <summary>
    /// The recent weeks run from a Monday, <see cref="PostRecord.RecentWeeks"/> weeks back counting the week
    /// running now as the last of them, whatever day of the week today is.
    /// </summary>
    [Fact]
    public void The_Recent_Weeks_Begin_On_A_Monday_And_Hold_This_Week_Among_Them()
    {
        var reading = Read(Sources(Post(), KeptEachMonth(_ => 1)));
        var thisMonday = new DateOnly(2026, 11, 2);

        reading.Days[0].Day.Should().Be(new DateOnly(2026, 8, 10));
        reading.Days[0].Day.DayOfWeek.Should().Be(DayOfWeek.Monday);
        reading.Days.Should().HaveCount(((PostRecord.RecentWeeks - 1) * 7) + 6);
        reading.Days.Count(day => day.Day >= thisMonday).Should().Be(6);
        PostRecord.WeeksFrom(thisMonday).Should().Be(new DateOnly(2026, 8, 10));
        PostRecord.WeeksFrom(new DateOnly(2026, 11, 8)).Should().Be(new DateOnly(2026, 8, 10));
        PostRecord.WeeksFrom(new DateOnly(2026, 11, 9)).Should().Be(new DateOnly(2026, 8, 17));
    }

    [Fact]
    public void The_Recent_Weeks_Run_To_Today_And_No_Further()
    {
        var reading = Read(Sources(Post(), KeptEachMonth(_ => 1)));

        reading.Days[^1].Day.Should().Be(Today);
        PostRecord.Weeks(AsOf, TimeZoneInfo.Utc)
            .Should().Be(new TimeRange(new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero), AsOf));
    }

    [Fact]
    public void A_Day_Before_The_Website_Was_First_Measured_Has_No_Figure()
    {
        var firstDay = new DateOnly(2026, 9, 15);
        var record = Record(firstDay, MonthsBetween(new DateOnly(2026, 9, 1), Running).Select(month => Recorded(month, null)));
        var sources = Sources(Post(), [], record) with
        {
            Days = [Point(firstDay.AddDays(-1), 4), Point(firstDay, 2)],
        };

        var days = Read(sources).Days.ToDictionary(day => day.Day, day => day.PageViews);

        days[new DateOnly(2026, 8, 10)].Should().BeNull();
        days[firstDay.AddDays(-1)].Should().BeNull();
        days[firstDay].Should().Be(2);
        days[firstDay.AddDays(1)].Should().Be(0);
    }

    [Fact]
    public void A_Day_Nothing_Was_Read_On_Is_Nought()
    {
        var sources = Sources(Post(), KeptEachMonth(_ => 1)) with { Days = [Point(new DateOnly(2026, 11, 3), 5)] };

        var days = Read(sources).Days;

        days.Should().OnlyContain(day => day.PageViews != null);
        days.Single(day => day.Day == new DateOnly(2026, 11, 3)).PageViews.Should().Be(5);
        days.Where(day => day.Day != new DateOnly(2026, 11, 3)).Should().OnlyContain(day => day.PageViews == 0);
    }

    /// <summary>
    /// Every day of the recent weeks is still held in full wherever the website is, so each is counted from
    /// all of its reports.
    /// </summary>
    [Fact]
    public void The_Recent_Weeks_Lie_Inside_The_Reports_Still_Held()
    {
        foreach (var name in new[] { "Etc/UTC", "Asia/Kolkata", "Pacific/Auckland", "Pacific/Honolulu" })
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(name);

            PostRecord.Weeks(AsOf, zone).From.Should().BeOnOrAfter(Retention.DetailFrom(AsOf, zone));
        }
    }

    [Fact]
    public void Nothing_Of_The_Website_Held_Is_No_Views_No_Months_And_No_Day_With_A_Figure()
    {
        var sources = Sources(Post(published: new DateOnly(2026, 3, 14)), [], new KeptMonths(null, []));

        var reading = Read(sources);

        sources.LiveFrom.Should().BeNull();
        reading.PageViews.Should().Be(0);
        reading.ViewsFrom.Should().BeNull();
        reading.FirstDay.Should().BeNull();
        reading.From.Should().BeNull();
        reading.Months.Should().BeEmpty();
        reading.Days.Should().HaveCount(((PostRecord.RecentWeeks - 1) * 7) + 6)
            .And.OnlyContain(day => day.PageViews == null);
        reading.Published.Should().Be(new DateOnly(2026, 3, 14));
    }

    /// <summary>
    /// A month counted before judging had finished with it, and still countable again, is where the post's
    /// people stop being complete.
    /// </summary>
    [Fact]
    public void A_Kept_Month_Counted_Before_Judging_Had_Finished_Holds_The_People_Back_While_It_Can_Be_Counted_Again()
    {
        var stopped = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
        var kept = KeptEachMonth(_ => 1)
            .Select(month => month.Month == LastKept ? month with { JudgedTo = stopped } : month);

        PostRecord.JudgedTo(Sources(Post(), [.. kept]), LiveJudgedTo, AsOf).Should().Be(stopped);
    }

    /// <summary>A month that can no longer be counted again will never be judged further, so it holds nothing back.</summary>
    [Fact]
    public void A_Kept_Month_That_Can_No_Longer_Be_Counted_Again_Holds_Nothing_Back()
    {
        var frozen = new DateTimeOffset(2025, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var kept = KeptEachMonth(_ => 1)
            .Select(month => month.Month == FirstMonth ? month with { JudgedTo = frozen } : month);

        PostRecord.JudgedTo(Sources(Post(), [.. kept]), LiveJudgedTo, AsOf).Should().Be(LiveJudgedTo);
        PostRecord.JudgedTo(Sources(Post(), [], new KeptMonths(null, [])), LiveJudgedTo, AsOf).Should().Be(AsOf);
    }

    [Fact]
    public void The_Reports_A_Posts_Page_Reads_Begin_At_The_Earlier_Of_The_Month_They_Take_Over_And_The_Recent_Weeks()
    {
        var august = new DateOnly(2026, 8, 1);
        var weeks = PostRecord.Weeks(AsOf, TimeZoneInfo.Utc);
        var fromOctober = Sources(Post(), KeptEachMonth(_ => 1));
        var fromAugust = Sources(Post(), [.. KeptEachMonth(_ => 1).Where(month => month.Month < august)]);

        PostRecord.ReportsFrom(fromOctober, weeks).Should().Be(weeks.From);
        PostRecord.ReportsFrom(fromAugust, weeks).Should().Be(Midnight(august));
        PostRecord.ReportsFrom(Sources(Post(), [], new KeptMonths(null, [])), weeks).Should().BeNull();
    }

    /// <summary>
    /// Where the reports answer only the month running now, its views are the views they count since it
    /// began, by the same grouping — and are not counted a second time.
    /// </summary>
    [Fact]
    public void A_Single_Month_Answered_From_The_Reports_Is_Its_Views_Without_Being_Counted_Twice()
    {
        var kept = KeptEachMonth(_ => 1).Append(PostKept(October, 1));
        var sources = Sources(Post(), [.. kept]) with { LiveViews = 5 };

        var reading = Read(sources);

        PostRecord.SpansMonths(sources.LiveFrom!, AsOf).Should().BeFalse();
        reading.Months[^1].Should().Be(new MonthReading(Running, 5, 7));
        reading.PageViews.Should().Be(13 + 5);
    }

    private static PostRecordReading Read(PostRecordSources sources) =>
        PostRecord.Read(sources, AddedAt, TimeZoneInfo.Utc, AsOf);

    /// <summary>What a post's record is read from, with where the reports take over decided as the page decides it.</summary>
    private static PostRecordSources Sources(
        FoundPost post,
        ImmutableArray<KeptMonthOfPost> kept,
        KeptMonths? record = null)
    {
        var held = record ?? Record(new DateOnly(2025, 11, 8), WebsiteMonths());

        return new PostRecordSources(
            post,
            held,
            kept,
            PostsInsights.LiveFrom(held.Months, [.. kept.Select(month => month.Month)], AsOf),
            0,
            [],
            []);
    }

    /// <summary>A post found by its path, published on the morning of a day where one is given.</summary>
    private static FoundPost Post(DateOnly? published = null, DateTimeOffset? listedAt = null) =>
        new(
            new ListedPost("/posts/a/", null, published is { } day ? Midnight(day).AddHours(9) : null),
            "https://example.com/posts/a/",
            "A post",
            false,
            listedAt ?? ListedLongAgo);

    /// <summary>The post's months kept to September 2026, each with the views given for it.</summary>
    private static ImmutableArray<KeptMonthOfPost> KeptEachMonth(Func<DateOnly, long> views) =>
        [.. MonthsBetween(FirstMonth, LastKept).Select(month => PostKept(month, views(month)))];

    /// <summary>
    /// A month of posts as it was kept: counted the day after it ended unless a test says when, judged whole,
    /// and holding the post's paths wherever it was read.
    /// </summary>
    private static KeptMonthOfPost PostKept(DateOnly month, long views, DateTimeOffset? countedAt = null)
    {
        var ends = Midnight(month.AddMonths(1));

        return new KeptMonthOfPost(month, views, views > 0, countedAt ?? ends.AddDays(1), new TimeRange(Midnight(month), ends), ends);
    }

    /// <summary>The website's months: kept to September 2026, the first of them from its first day.</summary>
    private static IEnumerable<RecordedMonth> WebsiteMonths() =>
        MonthsBetween(FirstMonth, Running).Select(month => Recorded(month, month <= LastKept ? WebsiteKept(month) : null));

    private static KeptFigures WebsiteKept(DateOnly month)
    {
        var ends = Midnight(month.AddMonths(1));

        return new KeptFigures(
            new TimeRange(Midnight(month), ends),
            month == FirstMonth ? WebsiteFirstDay : null,
            DaysMeasured.In(month, WebsiteFirstDay, month.AddMonths(1).AddDays(-1)),
            new MonthFigures(100, 50, null, 0, [.. new long[MonthFigures.Weekdays]], [.. new long[MonthFigures.Hours]]),
            [],
            ends);
    }

    /// <summary>A record whose oldest report still held arrived on a day, kept by an installation older than the website.</summary>
    private static KeptMonths Record(DateOnly? firstReportDay, IEnumerable<RecordedMonth> months) =>
        new(firstReportDay, [.. months]) { KeepingSince = AddedAt.AddMonths(-1) };

    private static RecordedMonth Recorded(DateOnly month, KeptFigures? kept) =>
        new(month, Midnight(month), Midnight(new DateOnly(month.Year, 1, 1)), Midnight(month.AddMonths(1)), kept);

    private static MonthReading Month(PostRecordReading reading, DateOnly month) =>
        reading.Months.Single(held => held.Month == month);

    /// <summary>Every month from one to the month running now.</summary>
    private static DateOnly[] MonthsFrom(DateOnly first) => [.. MonthsBetween(first, Running)];

    private static IEnumerable<DateOnly> MonthsBetween(DateOnly first, DateOnly last)
    {
        for (var month = first; month <= last; month = month.AddMonths(1))
        {
            yield return month;
        }
    }

    private static TimeSeriesPoint Point(DateOnly day, long value) => new(Midnight(day), value);

    private static DateTimeOffset Midnight(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
