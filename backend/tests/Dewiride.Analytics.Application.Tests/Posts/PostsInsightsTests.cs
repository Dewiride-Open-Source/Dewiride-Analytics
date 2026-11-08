using System.Collections.Immutable;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Posts;

namespace Dewiride.Analytics.Application.Tests.Posts;

/// <summary>
/// Covers what a website's posts are said to have done: which are the newest, which was read the most,
/// which views each is credited with, the calendar of posting, where views stop being read from what
/// was kept and begin to be counted from the reports, and how every post is listed a page at a time.
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

    /// <summary>
    /// One post redirected to another of the website's paths is read there as well as at its own, as every
    /// post is counted.
    /// </summary>
    [Fact]
    public void One_Post_Is_Read_At_Its_Own_Path_And_Where_It_Landed()
    {
        PostsInsights.PathsOf(Found(Post("/old/", landed: "/new/"))).Should().Equal("/old/", "/new/");
    }

    /// <summary>
    /// One post that landed at another listed post's own path is read at its own path alone, as Insights
    /// counts it: a view there belongs to the other post.
    /// </summary>
    [Fact]
    public void One_Post_That_Landed_On_A_Listed_Post_Is_Read_At_Its_Own_Path_Alone()
    {
        var post = Post("/a/", landed: "/b/");

        PostsInsights.PathsOf(Found(post, landedOnAPost: true)).Should().Equal("/a/");
        PostsInsights.CountedPaths([post, Post("/b/")]).Should().Equal("/a/", "/b/");
    }

    [Fact]
    public void One_Post_That_Answered_At_Its_Own_Path_Is_Read_There_Alone()
    {
        PostsInsights.PathsOf(Found(Post("/same/", landed: "/same/"), landedOnAPost: true)).Should().Equal("/same/");
        PostsInsights.PathsOf(Found(Post("/plain/"))).Should().Equal("/plain/");
    }

    [Fact]
    public void The_Reports_Are_Read_From_The_Month_They_Take_Over_To_The_Moment_Of_Reading()
    {
        var record = Record(new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 1));

        PostsInsights.LiveWindow(record[1], AsOf)
            .Should().Be(new TimeRange(Midnight(new DateOnly(2026, 10, 1)), AsOf));
    }

    /// <summary>At the first instant of a month nothing of it has been reported yet, so nothing is read.</summary>
    [Fact]
    public void Nothing_Is_Read_From_The_Reports_Of_A_Month_That_Has_Not_Begun()
    {
        var running = Record(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1))[0];

        PostsInsights.LiveWindow(running, running.Begins).Should().BeNull();
    }

    /// <summary>
    /// Every post the website lists is on the list, each credited exactly as Insights credits it: a post
    /// redirected elsewhere read where it landed, a path another listed post owns left to that post, and a
    /// post that does not say when it was published listed as well.
    /// </summary>
    [Fact]
    public void Every_Post_Is_Listed_With_The_Views_Insights_Credits_It()
    {
        ListedPost[] listed =
        [
            Post("/old/", Noon(2026, 10, 1), landed: "/new/"),
            Post("/a/", Noon(2026, 10, 2), landed: "/b/"),
            Post("/b/", Noon(2026, 10, 3)),
            Post("/undated/"),
        ];
        PathViews[] views =
        [
            new("/old/", 2), new("/new/", 5), new("/a/", 1), new("/b/", 4), new("/undated/", 3), new("/undated/", 1),
        ];

        var reading = PostsInsights.Read(listed, views, Kolkata, AsOf);
        var page = PostsInsights.List(listed, views, Kolkata, new PostsListing(PostsOrder.Newest, 0, 100));
        ShownPost[] expected = [.. reading.Latest, new ShownPost("/undated/", null, 4)];

        page.Shown.Should().Equal(expected);
        page.Shown.Select(post => post.PageViews).Should().Equal(4, 1, 7, 4);
        page.Posts.Should().Be(4);
        page.Matching.Should().Be(4);
    }

    [Fact]
    public void The_Most_Read_Come_First_Then_The_One_Published_First_Then_The_Undated_Then_By_Path()
    {
        var page = Page(
            [
                Post("/c/", Noon(2026, 10, 5)),
                Post("/b/"),
                Post("/f/", Noon(2026, 10, 9)),
                Post("/d/", Noon(2026, 10, 1)),
                Post("/a/"),
                Post("/e/", Noon(2026, 10, 9)),
            ],
            new PostsListing(PostsOrder.MostRead, 0, 100),
            new PathViews("/a/", 10),
            new PathViews("/b/", 10),
            new PathViews("/c/", 10),
            new PathViews("/d/", 10),
            new PathViews("/e/", 20));

        page.Shown.Select(post => post.Path).Should().Equal("/e/", "/d/", "/c/", "/a/", "/b/", "/f/");
    }

    /// <summary>
    /// However a tie at the top is broken — by the day published, by having a date at all, or by path —
    /// the first post listed by the most read is the one Insights calls the most read.
    /// </summary>
    [Fact]
    public void The_First_Post_Listed_By_The_Most_Read_Is_The_Most_Read_Post()
    {
        ListedPost[][] ties =
        [
            [Post("/a/"), Post("/c/", Noon(2026, 10, 5)), Post("/d/", Noon(2026, 10, 1))],
            [Post("/b/"), Post("/a/")],
            [Post("/z/", Noon(2026, 10, 1)), Post("/y/", Noon(2026, 10, 1))],
        ];

        foreach (var listed in ties)
        {
            PathViews[] views = [.. listed.Select(post => new PathViews(post.Path, 6))];

            var mostRead = PostsInsights.Read(listed, views, Kolkata, AsOf).MostRead;
            var first = PostsInsights.List(listed, views, Kolkata, new PostsListing(PostsOrder.MostRead, 0, 1));

            mostRead.Should().NotBeNull();
            first.Shown.Should().Equal(mostRead);
        }
    }

    /// <summary>
    /// The newest first, however much each was read; of two published at the same instant, the first by path;
    /// and the posts that do not say when they were published after every one that does, by path.
    /// </summary>
    [Fact]
    public void The_Newest_Come_First_A_Tie_By_Path_And_The_Undated_Last_By_Path()
    {
        var page = Page(
            [
                Post("/undated-b/"),
                Post("/old/", Noon(2026, 10, 1)),
                Post("/z/", Noon(2026, 11, 1)),
                Post("/undated-a/"),
                Post("/y/", Noon(2026, 11, 1)),
            ],
            new PostsListing(PostsOrder.Newest, 0, 100),
            new PathViews("/undated-b/", 50));

        page.Shown.Select(post => post.Path).Should().Equal("/y/", "/z/", "/old/", "/undated-a/", "/undated-b/");
    }

    /// <summary>
    /// A search narrows the list and its count to the posts it found that the website still lists; the
    /// count of every post the website lists stays as it is.
    /// </summary>
    [Fact]
    public void Only_The_Posts_Searched_For_Are_Listed_And_Counted()
    {
        var page = Page(
            [Post("/a/", Noon(2026, 10, 1)), Post("/b/", Noon(2026, 10, 2)), Post("/c/", Noon(2026, 10, 3))],
            new PostsListing(PostsOrder.MostRead, 0, 100) { Matching = ["/c/", "/a/", "/no-longer-listed/"] },
            new PathViews("/a/", 1),
            new PathViews("/b/", 9),
            new PathViews("/c/", 2));

        page.Posts.Should().Be(3);
        page.Matching.Should().Be(2);
        page.Shown.Select(post => post.Path).Should().Equal("/c/", "/a/");
    }

    /// <summary>
    /// A post that landed at another listed post's own path is credited with its own views alone when it is
    /// searched for and the other post is not: the path is the other post's, and a search does not change
    /// whose it is.
    /// </summary>
    [Fact]
    public void A_Post_Searched_For_Is_Never_Credited_With_A_Path_Another_Listed_Post_Owns()
    {
        var page = Page(
            [Post("/a/", Noon(2026, 10, 1), landed: "/b/"), Post("/b/", Noon(2026, 10, 2))],
            new PostsListing(PostsOrder.MostRead, 0, 100) { Matching = ["/a/"] },
            new PathViews("/a/", 1),
            new PathViews("/b/", 4));

        page.Shown.Should().Equal(new ShownPost("/a/", new DateOnly(2026, 10, 1), 1));
        page.MostPageViews.Should().Be(1);
    }

    [Fact]
    public void A_Page_Is_The_Slice_Asked_For_In_Order()
    {
        ListedPost[] listed = [.. Enumerable.Range(1, 7).Select(day => Post($"/{day}/", Noon(2026, 10, day)))];
        PathViews[] views = [.. Enumerable.Range(1, 7).Select(day => new PathViews($"/{day}/", day * 10))];

        var mostRead = PostsInsights.List(listed, views, Kolkata, new PostsListing(PostsOrder.MostRead, 2, 3));
        var newest = PostsInsights.List(listed, views, Kolkata, new PostsListing(PostsOrder.Newest, 5, 3));

        mostRead.Shown.Select(post => post.Path).Should().Equal("/5/", "/4/", "/3/");
        newest.Shown.Select(post => post.Path).Should().Equal("/2/", "/1/");
    }

    [Fact]
    public void A_Page_Past_The_End_Is_Empty_And_Still_Says_How_Many_Match()
    {
        var page = Page(
            [Post("/a/", Noon(2026, 10, 1)), Post("/b/", Noon(2026, 10, 2)), Post("/c/")],
            new PostsListing(PostsOrder.MostRead, 3, 25),
            new PathViews("/b/", 5));

        page.Shown.Should().BeEmpty();
        page.Posts.Should().Be(3);
        page.Matching.Should().Be(3);
        page.MostPageViews.Should().Be(5);
    }

    /// <summary>
    /// The scale of every bar is the most any post that matched was read — not the most read on the page,
    /// nor a post the search left out — and nought where no post matched.
    /// </summary>
    [Fact]
    public void Every_Bar_Is_Drawn_Against_The_Most_Any_Matching_Post_Was_Read_Whatever_The_Page()
    {
        ListedPost[] listed =
        [
            Post("/a/", Noon(2026, 10, 1)), Post("/b/", Noon(2026, 10, 2)), Post("/c/", Noon(2026, 10, 3)),
        ];
        PathViews[] views = [new("/a/", 3), new("/b/", 9), new("/c/", 6)];

        var second = PostsInsights.List(listed, views, Kolkata, new PostsListing(PostsOrder.MostRead, 1, 1));
        var searched = PostsInsights.List(
            listed,
            views,
            Kolkata,
            new PostsListing(PostsOrder.Newest, 1, 1) { Matching = ["/a/", "/c/"] });
        var none = PostsInsights.List(
            listed,
            views,
            Kolkata,
            new PostsListing(PostsOrder.MostRead, 0, 25) { Matching = [] });

        second.Shown.Should().Equal(new ShownPost("/c/", new DateOnly(2026, 10, 3), 6));
        second.MostPageViews.Should().Be(9);
        searched.Shown.Should().Equal(new ShownPost("/a/", new DateOnly(2026, 10, 1), 3));
        searched.MostPageViews.Should().Be(6);
        none.Shown.Should().BeEmpty();
        none.Matching.Should().Be(0);
        none.MostPageViews.Should().Be(0);
    }

    [Fact]
    public void A_Page_That_Starts_Before_The_First_Post_Holds_None_Or_Is_In_No_Order_Is_Refused()
    {
        ListedPost[] listed = [Post("/a/")];

        var before = () => PostsInsights.List(listed, [], Kolkata, new PostsListing(PostsOrder.MostRead, -1, 25));
        var empty = () => PostsInsights.List(listed, [], Kolkata, new PostsListing(PostsOrder.MostRead, 0, 0));
        var unordered = () => PostsInsights.List(listed, [], Kolkata, new PostsListing(default, 0, 25));

        before.Should().Throw<ArgumentOutOfRangeException>();
        empty.Should().Throw<ArgumentOutOfRangeException>();
        unordered.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static PostsReading Read(IReadOnlyCollection<ListedPost> listed, params PathViews[] views) =>
        PostsInsights.Read(listed, views, Kolkata, AsOf);

    private static PostsPage Page(
        IReadOnlyCollection<ListedPost> listed,
        PostsListing listing,
        params PathViews[] views) =>
        PostsInsights.List(listed, views, Kolkata, listing);

    private static ListedPost Post(string path, DateTimeOffset? published = null, string? landed = null) =>
        new(path, landed, published);

    /// <summary>A post found by its path, listed since the summer.</summary>
    private static FoundPost Found(ListedPost post, bool landedOnAPost = false) =>
        new(post, $"https://example.com{post.Path}", null, landedOnAPost, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

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
