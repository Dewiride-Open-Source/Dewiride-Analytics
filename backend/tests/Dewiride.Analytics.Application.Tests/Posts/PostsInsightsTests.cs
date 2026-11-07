using System.Collections.Immutable;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Posts;

namespace Dewiride.Analytics.Application.Tests.Posts;

/// <summary>
/// Covers what a website's posts are said to have done: which are the newest, which was read the most,
/// which views each is credited with, the calendar of posting, and where views stop being read from what
/// was kept and begin to be counted from the reports.
/// </summary>
public sealed class PostsInsightsTests
{
    private static readonly TimeZoneInfo Kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    /// <summary>Half past nine in the morning on the seventh of November, which is mid-afternoon in Kolkata.</summary>
    private static readonly DateTimeOffset AsOf = new(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// The ten newest posts with a date, newest first; of two published at the same instant, the first by
    /// path. A post that does not say when it was published is never among them.
    /// </summary>
    [Fact]
    public void The_Ten_Newest_With_A_Date_Are_The_Latest_And_A_Tie_Goes_By_Path()
    {
        ListedPost[] listed =
        [
            .. Enumerable.Range(1, 12).Select(day => Post($"/october/{day:00}/", Noon(2026, 10, day))),
            Post("/z/", Noon(2026, 11, 1)),
            Post("/y/", Noon(2026, 11, 1)),
            Post("/undated/"),
        ];

        var reading = Read(listed);

        reading.Latest.Select(post => post.Path).Should().Equal(
            "/y/", "/z/", "/october/12/", "/october/11/", "/october/10/", "/october/09/", "/october/08/",
            "/october/07/", "/october/06/", "/october/05/");
    }

    [Fact]
    public void The_Most_Read_Is_The_Post_With_The_Most_Views()
    {
        var reading = Read(
            [Post("/a/", Noon(2026, 10, 1)), Post("/b/", Noon(2026, 10, 2))],
            new PathViews("/a/", 3),
            new PathViews("/b/", 9));

        reading.MostRead.Should().Be(new ShownPost("/b/", new DateOnly(2026, 10, 2), 9));
    }

    /// <summary>
    /// Of two read as much, the one published first; any with a date before any without; and then the
    /// first by path.
    /// </summary>
    [Fact]
    public void Of_Two_Read_As_Much_The_One_Published_First_Is_The_Most_Read_And_An_Undated_One_Comes_Last()
    {
        var dated = Read(
            [Post("/a/"), Post("/c/", Noon(2026, 10, 5)), Post("/d/", Noon(2026, 10, 1))],
            new PathViews("/a/", 10),
            new PathViews("/c/", 10),
            new PathViews("/d/", 10));
        var undated = Read([Post("/b/"), Post("/a/")], new PathViews("/a/", 4), new PathViews("/b/", 4));

        dated.MostRead!.Path.Should().Be("/d/");
        undated.MostRead!.Path.Should().Be("/a/");
    }

    [Fact]
    public void No_Post_Is_The_Most_Read_Until_One_Has_Been_Read()
    {
        Read([Post("/a/", Noon(2026, 10, 1))]).MostRead.Should().BeNull();
        Read([Post("/a/", Noon(2026, 10, 1))], new PathViews("/a/", 0)).MostRead.Should().BeNull();
    }

    /// <summary>
    /// A post redirected to another path of the website is read where it landed, which is what a visit
    /// following its link is recorded under, as well as at its own path.
    /// </summary>
    [Fact]
    public void A_Post_Is_Read_At_Its_Own_Path_And_Where_It_Landed()
    {
        var reading = Read(
            [Post("/old/", Noon(2026, 10, 1), landed: "/new/"), Post("/same/", Noon(2026, 10, 2), landed: "/same/")],
            new PathViews("/old/", 2),
            new PathViews("/new/", 5),
            new PathViews("/same/", 3));

        reading.Latest.Should().Equal(
            new ShownPost("/same/", new DateOnly(2026, 10, 2), 3),
            new ShownPost("/old/", new DateOnly(2026, 10, 1), 7));
    }

    /// <summary>
    /// A post that landed at another listed post's own path is not credited with that post's views: a
    /// view belongs to one post.
    /// </summary>
    [Fact]
    public void A_Path_Another_Post_Is_Read_At_Belongs_To_That_Post_Alone()
    {
        var reading = Read(
            [Post("/a/", Noon(2026, 10, 1), landed: "/b/"), Post("/b/", Noon(2026, 10, 2))],
            new PathViews("/a/", 1),
            new PathViews("/b/", 4));

        reading.Latest.Select(post => (post.Path, post.PageViews)).Should().Equal(("/b/", 4L), ("/a/", 1L));
    }

    [Fact]
    public void The_Paths_Counted_Are_Each_Posts_Own_And_Where_It_Landed_Once_In_Ordinal_Order()
    {
        ListedPost[] listed =
        [
            Post("/b/", landed: "/b/"),
            Post("/a/", landed: "/z/"),
            Post("/c/", landed: "/a/"),
            Post("/B/"),
            Post("/d/", landed: "/z/"),
        ];

        PostsInsights.CountedPaths(listed).Should().Equal("/B/", "/a/", "/b/", "/c/", "/d/", "/z/");
    }

    /// <summary>A path's views kept for earlier months and counted from the reports since are added up.</summary>
    [Fact]
    public void Views_Kept_And_Views_Counted_From_The_Reports_Are_Added_Up()
    {
        Read([Post("/a/", Noon(2026, 10, 1))], new PathViews("/a/", 3), new PathViews("/a/", 4))
            .Latest.Single().PageViews.Should().Be(7);
    }

    [Fact]
    public void Posts_And_Dated_Count_What_The_Website_Lists()
    {
        var reading = Read([Post("/a/", Noon(2026, 10, 1)), Post("/b/", Noon(2026, 10, 2)), Post("/c/")]);

        reading.Posts.Should().Be(3);
        reading.Dated.Should().Be(2);
    }

    /// <summary>
    /// Twelve months of the website's calendar, the one running now among them, to today where the
    /// website is — here a day ahead of UTC — and each post on the day it was published there.
    /// </summary>
    [Fact]
    public void The_Calendar_Runs_From_The_First_Of_The_Month_Eleven_Months_Before_To_Today_Where_The_Website_Is()
    {
        var auckland = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");
        var evening = new DateTimeOffset(2026, 11, 7, 20, 0, 0, TimeSpan.Zero);
        var lastNight = new DateTimeOffset(2026, 11, 7, 19, 0, 0, TimeSpan.Zero);

        var reading = PostsInsights.Read([Post("/a/", lastNight)], [], auckland, evening);

        reading.CalendarFrom.Should().Be(new DateOnly(2025, 12, 1));
        reading.CalendarTo.Should().Be(new DateOnly(2026, 11, 8));
        reading.Calendar.Should().Equal(new PostingDay(new DateOnly(2026, 11, 8), 1));
        reading.Latest.Single().Published.Should().Be(new DateOnly(2026, 11, 8));
    }

    [Fact]
    public void Posts_Published_After_Today_Or_Before_The_Calendar_Are_Left_Off_It()
    {
        var reading = Read(
        [
            Post("/tomorrow/", new DateTimeOffset(2026, 11, 8, 0, 0, 0, Kolkata.BaseUtcOffset)),
            Post("/later-today/", new DateTimeOffset(2026, 11, 7, 23, 30, 0, Kolkata.BaseUtcOffset)),
            Post("/first-day/", new DateTimeOffset(2025, 12, 1, 0, 0, 0, Kolkata.BaseUtcOffset)),
            Post("/before/", new DateTimeOffset(2025, 11, 30, 23, 59, 0, Kolkata.BaseUtcOffset)),
        ]);

        reading.Calendar.Should().Equal(
            new PostingDay(new DateOnly(2025, 12, 1), 1),
            new PostingDay(new DateOnly(2026, 11, 7), 1));
    }

    [Fact]
    public void Posts_Published_On_One_Day_Are_Counted_Together_The_Oldest_Day_First()
    {
        var reading = Read(
        [
            Post("/a/", new DateTimeOffset(2026, 11, 3, 9, 0, 0, Kolkata.BaseUtcOffset)),
            Post("/b/", new DateTimeOffset(2026, 11, 1, 9, 0, 0, Kolkata.BaseUtcOffset)),
            Post("/c/", new DateTimeOffset(2026, 11, 3, 21, 0, 0, Kolkata.BaseUtcOffset)),
        ]);

        reading.Calendar.Should().Equal(
            new PostingDay(new DateOnly(2026, 11, 1), 1),
            new PostingDay(new DateOnly(2026, 11, 3), 2));
    }

    /// <summary>The month running now is never kept, so its views are counted from its reports.</summary>
    [Fact]
    public void Views_Of_The_Month_Running_Now_Are_Counted_From_Its_Reports()
    {
        var record = Record(new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 1));
        DateOnly[] kept = [new(2026, 9, 1), new(2026, 10, 1)];

        var liveFrom = PostsInsights.LiveFrom(record, kept, AsOf);

        liveFrom!.Month.Should().Be(new DateOnly(2026, 11, 1));
        PostsInsights.ViewsFrom(kept, liveFrom).Should().Be(new DateOnly(2026, 9, 1));
    }

    /// <summary>
    /// A finished month nothing was kept of the posts of is counted from its reports while they are all
    /// held, and so is every month after it, whatever was kept of them.
    /// </summary>
    [Fact]
    public void A_Finished_Month_Whose_Posts_Were_Never_Kept_Is_Counted_From_Its_Reports()
    {
        var record = Record(new DateOnly(2026, 8, 1), new DateOnly(2026, 11, 1));
        DateOnly[] kept = [new(2026, 8, 1), new(2026, 10, 1)];

        var liveFrom = PostsInsights.LiveFrom(record, kept, AsOf);

        liveFrom!.Month.Should().Be(new DateOnly(2026, 9, 1));
        PostsInsights.ViewsFrom(kept, liveFrom).Should().Be(new DateOnly(2026, 8, 1));
    }

    /// <summary>
    /// A month nothing was kept of the posts of, whose reports may already have begun to go, adds nothing,
    /// and views are said to be counted from the first month whose posts were kept.
    /// </summary>
    [Fact]
    public void A_Month_Whose_Posts_Were_Never_Kept_And_Can_No_Longer_Be_Counted_Is_Passed_Over()
    {
        var record = Record(new DateOnly(2025, 9, 1), new DateOnly(2026, 11, 1));
        DateOnly[] kept = [.. Enumerable.Range(0, 13).Select(step => new DateOnly(2025, 10, 1).AddMonths(step))];

        var liveFrom = PostsInsights.LiveFrom(record, kept, AsOf);

        liveFrom!.Month.Should().Be(new DateOnly(2026, 11, 1));
        PostsInsights.ViewsFrom(kept, liveFrom).Should().Be(new DateOnly(2025, 10, 1));
    }

    [Fact]
    public void Views_Are_Counted_From_The_Reports_Of_The_First_Month_Where_No_Month_Of_Posts_Was_Kept()
    {
        var record = Record(new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 1));

        var liveFrom = PostsInsights.LiveFrom(record, [], AsOf);

        liveFrom!.Month.Should().Be(new DateOnly(2026, 9, 1));
        PostsInsights.ViewsFrom([], liveFrom).Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void Nothing_Is_Counted_Where_Nothing_Of_The_Website_Is_Held()
    {
        PostsInsights.LiveFrom([], [], AsOf).Should().BeNull();
        PostsInsights.ViewsFrom([], null).Should().BeNull();
    }

    private static PostsReading Read(IReadOnlyCollection<ListedPost> listed, params PathViews[] views) =>
        PostsInsights.Read(listed, views, Kolkata, AsOf);

    private static ListedPost Post(string path, DateTimeOffset? published = null, string? landed = null) =>
        new(path, landed, published);

    private static DateTimeOffset Noon(int year, int month, int day) => new(year, month, day, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Every month from one to another, nothing kept of any, each beginning at midnight UTC.</summary>
    private static ImmutableArray<RecordedMonth> Record(DateOnly first, DateOnly last) =>
    [
        .. Enumerable.Range(0, ((last.Year - first.Year) * 12) + last.Month - first.Month + 1)
            .Select(step => first.AddMonths(step))
            .Select(month => new RecordedMonth(
                month,
                Midnight(month),
                Midnight(new DateOnly(month.Year, 1, 1)),
                Midnight(month.AddMonths(1)),
                null)),
    ];

    private static DateTimeOffset Midnight(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
