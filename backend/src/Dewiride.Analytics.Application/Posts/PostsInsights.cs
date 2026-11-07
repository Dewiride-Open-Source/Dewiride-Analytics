using System.Collections.Immutable;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Application.Posts;

/// <summary>
/// What a website's posts have done: how many it lists, the newest and the most read with their views,
/// and the days of the last twelve months a post was published on.
/// </summary>
/// <remarks>
/// <para>
/// A post's views are put together from two sources, as the rest of the website's record is: the views
/// kept month by month for the months before the first one answered from its reports, and the reports
/// themselves from there to the present. So a post's views are never behind the record's own figures,
/// and reach back as far as its posts were kept.
/// </para>
/// <para>
/// A post is read at its own path, and at the path its page answered at where the website redirected
/// it to another of its own paths, because that is where a browser following the link ends and what a
/// visit is recorded under. A path that is another listed post's own belongs to that post, so a view is
/// never counted for two posts that way.
/// </para>
/// </remarks>
public static class PostsInsights
{
    /// <summary>How many of the newest posts are shown.</summary>
    public const int NewestShown = 10;

    /// <summary>How many months the calendar of posting covers, the one running now among them.</summary>
    public const int CalendarMonths = 12;

    /// <summary>
    /// Works out the paths a website's posts are counted at: each post's own path, and the path a post
    /// answered at where it is not another post's own.
    /// </summary>
    /// <param name="listed">The posts the website lists now.</param>
    /// <returns>The paths, distinct and in ordinal order.</returns>
    public static ImmutableArray<string> CountedPaths(IReadOnlyCollection<ListedPost> listed)
    {
        ArgumentNullException.ThrowIfNull(listed);

        var own = OwnPaths(listed);

        return
        [
            .. own
                .Concat(listed.Select(post => Landed(post, own)).OfType<string>())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Decides where a post's views begin to be counted from the reports.
    /// </summary>
    /// <remarks>
    /// The first month of the record that is the one running now, or a finished month nothing was kept
    /// of the posts of while every report of it is still held. Every month from there on is still held,
    /// so all of them are counted from their reports, and the posts kept for any of them are left
    /// unread rather than counted twice.
    /// </remarks>
    /// <param name="record">The website's record, month by month, oldest first.</param>
    /// <param name="postMonths">The months whose posts were kept.</param>
    /// <param name="asOf">The moment the record is read at.</param>
    /// <returns>The month, or nothing where the record is empty.</returns>
    public static RecordedMonth? LiveFrom(
        ImmutableArray<RecordedMonth> record,
        IReadOnlyCollection<DateOnly> postMonths,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(postMonths);

        var kept = postMonths.ToHashSet();
        var running = record.Length - 1;

        return record
            .Where((month, index) =>
                index == running
                || (!kept.Contains(month.Month) && asOf < Retention.CountableUntil(month.Begins)))
            .FirstOrDefault();
    }

    /// <summary>
    /// The month a post's views are counted from: the first whose posts were kept, or the first answered
    /// from its reports where that is earlier.
    /// </summary>
    /// <param name="postMonths">The months whose posts were kept.</param>
    /// <param name="liveFrom">Where views begin to be counted from the reports, as <see cref="LiveFrom"/> decides.</param>
    /// <returns>The month, or nothing where the record is empty and no post has a view.</returns>
    public static DateOnly? ViewsFrom(IReadOnlyCollection<DateOnly> postMonths, RecordedMonth? liveFrom)
    {
        ArgumentNullException.ThrowIfNull(postMonths);

        return liveFrom is null
            ? null
            : postMonths.Where(month => month < liveFrom.Month).DefaultIfEmpty(liveFrom.Month).Min();
    }

    /// <summary>
    /// Works out what a website's posts have done.
    /// </summary>
    /// <param name="listed">The posts the website lists now.</param>
    /// <param name="views">
    /// The pages delivered at each path counted, from what was kept and from the reports; a path may
    /// come more than once, and its figures are added up.
    /// </param>
    /// <param name="zone">The website's reporting zone, which the days a post was published fall on.</param>
    /// <param name="asOf">The moment the posts are read at, which decides what today is.</param>
    /// <returns>What the posts have done.</returns>
    public static PostsReading Read(
        IReadOnlyCollection<ListedPost> listed,
        IEnumerable<PathViews> views,
        TimeZoneInfo zone,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(zone);

        var own = OwnPaths(listed);
        var counted = Totals(views);
        var posts = listed
            .Select(post => new Counted(post, new ShownPost(post.Path, Published(post, zone), ViewsOf(post, own, counted))))
            .ToList();
        var today = ZoneDays.Of(asOf, zone);
        var calendarFrom = new DateOnly(today.Year, today.Month, 1).AddMonths(1 - CalendarMonths);

        return new PostsReading(
            listed.Count,
            listed.Count(post => post.PublishedAt is not null),
            Newest(posts),
            MostRead(posts),
            calendarFrom,
            today,
            Calendar(posts, calendarFrom, today));
    }

    private static HashSet<string> OwnPaths(IEnumerable<ListedPost> listed) =>
        listed.Select(post => post.Path).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The path a post answered at through a redirect, where it counts for that post: not its own path,
    /// and not another listed post's own.
    /// </summary>
    private static string? Landed(ListedPost post, HashSet<string> own) =>
        post.LandedPath is { } landed
        && !string.Equals(landed, post.Path, StringComparison.Ordinal)
        && !own.Contains(landed)
            ? landed
            : null;

    private static Dictionary<string, long> Totals(IEnumerable<PathViews> views) =>
        views
            .GroupBy(view => view.Path, StringComparer.Ordinal)
            .ToDictionary(path => path.Key, path => path.Sum(view => view.PageViews), StringComparer.Ordinal);

    private static long ViewsOf(ListedPost post, HashSet<string> own, Dictionary<string, long> counted) =>
        counted.GetValueOrDefault(post.Path)
        + (Landed(post, own) is { } landed ? counted.GetValueOrDefault(landed) : 0);

    private static DateOnly? Published(ListedPost post, TimeZoneInfo zone) =>
        post.PublishedAt is { } at ? ZoneDays.Of(at, zone) : null;

    /// <summary>The newest posts with a publishing date, and of two published at once, the first by path.</summary>
    private static ImmutableArray<ShownPost> Newest(IEnumerable<Counted> posts) =>
    [
        .. posts
            .Where(post => post.Post.PublishedAt is not null)
            .OrderByDescending(post => post.Post.PublishedAt)
            .ThenBy(post => post.Post.Path, StringComparer.Ordinal)
            .Take(NewestShown)
            .Select(post => post.Shown),
    ];

    /// <summary>
    /// The post read the most; of two read as much, the one published first, any with a date before any
    /// without, and then the first by path.
    /// </summary>
    private static ShownPost? MostRead(IEnumerable<Counted> posts) =>
        posts
            .Where(post => post.Shown.PageViews > 0)
            .OrderByDescending(post => post.Shown.PageViews)
            .ThenBy(post => post.Post.PublishedAt is null)
            .ThenBy(post => post.Post.PublishedAt)
            .ThenBy(post => post.Post.Path, StringComparer.Ordinal)
            .Select(post => post.Shown)
            .FirstOrDefault();

    /// <summary>The days from the first to the last of the calendar a post was published on, the oldest first.</summary>
    private static ImmutableArray<PostingDay> Calendar(IEnumerable<Counted> posts, DateOnly from, DateOnly to) =>
    [
        .. posts
            .Select(post => post.Shown.Published)
            .OfType<DateOnly>()
            .Where(day => day >= from && day <= to)
            .GroupBy(day => day)
            .OrderBy(day => day.Key)
            .Select(day => new PostingDay(day.Key, day.Count())),
    ];

    /// <summary>A post, and what is shown of it.</summary>
    private sealed record Counted(ListedPost Post, ShownPost Shown);
}

/// <summary>
/// What a website's posts have done.
/// </summary>
/// <param name="Posts">How many posts the website lists.</param>
/// <param name="Dated">How many of them say when they were published.</param>
/// <param name="Latest">Up to ten of the posts with a publishing date, the newest first.</param>
/// <param name="MostRead">The post read the most, or nothing where none has been read.</param>
/// <param name="CalendarFrom">The calendar's first day: the first of the month eleven months before this one.</param>
/// <param name="CalendarTo">The calendar's last day: today, on the website's calendar.</param>
/// <param name="Calendar">The days of the calendar a post was published on, the oldest first.</param>
public sealed record PostsReading(
    int Posts,
    int Dated,
    ImmutableArray<ShownPost> Latest,
    ShownPost? MostRead,
    DateOnly CalendarFrom,
    DateOnly CalendarTo,
    ImmutableArray<PostingDay> Calendar);

/// <summary>
/// One post, as it is shown.
/// </summary>
/// <param name="Path">The path the collector knows its page by.</param>
/// <param name="Published">The day it was published, on the website's calendar, or nothing where it did not say.</param>
/// <param name="PageViews">Pages delivered at it, at its own path and where it landed.</param>
public sealed record ShownPost(string Path, DateOnly? Published, long PageViews);

/// <summary>
/// One day posts were published on.
/// </summary>
/// <param name="Day">The day, on the website's calendar.</param>
/// <param name="Posts">How many posts were published on it.</param>
public readonly record struct PostingDay(DateOnly Day, int Posts);
