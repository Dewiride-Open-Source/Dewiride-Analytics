using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves the compiled statements answer the questions they are supposed to.
/// </summary>
/// <remarks>
/// The SQL suite approves what the compiler writes; this one runs it. Both are needed: a
/// statement can be exactly the text somebody approved and still count the wrong thing.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class TelemetryQueryTests(AnalyticsStackFixture stack)
{
    /// <summary>A zone with no offset and no changes, where a website's Monday is UTC's.</summary>
    private const string Utc = "Etc/UTC";

    /// <summary>A zone that changes its clocks twice a year, an hour each way, at one in the morning UTC.</summary>
    private const string London = "Europe/London";

    /// <summary>A zone that never changes its clocks, half an hour off the hour from UTC.</summary>
    private const string Kolkata = "Asia/Kolkata";

    private static readonly DateTimeOffset Midnight = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_Overview_Counts_Page_Views_Visitors_And_Everything_Reported()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            PageView(siteId, Midnight.AddHours(1), "visitor-a"),
            PageView(siteId, Midnight.AddHours(2), "visitor-a"),
            PageView(siteId, Midnight.AddHours(3), "visitor-b"),
            Engagement(siteId, Midnight.AddHours(3), "visitor-b"));

        var overview = await Queries.GetOverviewAsync(Scope(siteId), new OverviewQuery(Day()), Cancellation.Token);

        overview.PageViews.Should().Be(3);
        overview.Visitors.Should().Be(2);
        overview.Events.Should().Be(4);
    }

    [Fact]
    public async Task An_Overview_Sees_Only_The_Site_It_Was_Authorised_For()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        await WriteAsync(
            PageView(mine, Midnight.AddHours(1), "visitor-a"),
            PageView(theirs, Midnight.AddHours(1), "visitor-b"),
            PageView(theirs, Midnight.AddHours(2), "visitor-c"));

        var overview = await Queries.GetOverviewAsync(Scope(mine), new OverviewQuery(Day()), Cancellation.Token);

        overview.PageViews.Should().Be(1);
    }

    /// <summary>
    /// A surface that could not derive a key has not observed an anonymous visitor; it has
    /// observed nothing about who was there. Counting those together would invent one busy
    /// phantom out of everybody the product cannot identify.
    /// </summary>
    [Fact]
    public async Task Visitors_Excludes_Reports_That_Carry_No_Visitor_Key()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            PageView(siteId, Midnight.AddHours(1), "visitor-a"),
            PageView(siteId, Midnight.AddHours(2), visitorKey: null),
            PageView(siteId, Midnight.AddHours(3), visitorKey: null));

        var overview = await Queries.GetOverviewAsync(Scope(siteId), new OverviewQuery(Day()), Cancellation.Token);

        overview.PageViews.Should().Be(3);
        overview.Visitors.Should().Be(1);
    }

    /// <summary>
    /// The window is half-open, so an event on the boundary belongs to the next window and not to
    /// this one. Counting it twice is how "yesterday against today" quietly inflates.
    /// </summary>
    [Fact]
    public async Task The_Window_Includes_Its_First_Instant_And_Excludes_Its_Last()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            PageView(siteId, Midnight, "visitor-a"),
            PageView(siteId, Midnight.AddDays(1), "visitor-b"));

        var overview = await Queries.GetOverviewAsync(Scope(siteId), new OverviewQuery(Day()), Cancellation.Token);

        overview.PageViews.Should().Be(1);
    }

    [Fact]
    public async Task An_Empty_Window_Reports_Zero_Rather_Than_Nothing()
    {
        var overview = await Queries.GetOverviewAsync(
            Scope(Guid.NewGuid()),
            new OverviewQuery(Day()),
            Cancellation.Token);

        overview.PageViews.Should().Be(0);
        overview.Visitors.Should().Be(0);
        overview.Events.Should().Be(0);
    }

    /// <summary>
    /// A chart with holes in it is a chart that has to be repaired in the browser, and every
    /// consumer would repair it slightly differently.
    /// </summary>
    [Fact]
    public async Task An_Hourly_Series_Fills_The_Hours_Nothing_Happened_In()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            PageView(siteId, Midnight.AddHours(1), "visitor-a"),
            PageView(siteId, Midnight.AddHours(1).AddMinutes(30), "visitor-b"),
            PageView(siteId, Midnight.AddHours(4), "visitor-c"));

        var series = await Queries.GetTimeSeriesAsync(
            Scope(siteId),
            new TimeSeriesQuery(
                new TimeRange(Midnight, Midnight.AddHours(6)),
                TimeGranularity.Hour,
                TimeSeriesMetric.PageViews),
            Cancellation.Token);

        series.Select(point => point.Value).Should().Equal(0, 2, 0, 0, 1, 0);
        series.Select(point => point.BucketStart).Should().BeInAscendingOrder();
        series[0].BucketStart.Should().Be(Midnight);
    }

    [Fact]
    public async Task A_Daily_Series_Counts_Distinct_Visitors_Per_Day()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            PageView(siteId, Midnight.AddHours(1), "visitor-a"),
            PageView(siteId, Midnight.AddHours(2), "visitor-a"),
            PageView(siteId, Midnight.AddDays(1).AddHours(1), "visitor-b"),
            PageView(siteId, Midnight.AddDays(1).AddHours(2), "visitor-c"));

        var series = await Queries.GetTimeSeriesAsync(
            Scope(siteId),
            new TimeSeriesQuery(
                new TimeRange(Midnight, Midnight.AddDays(2)),
                TimeGranularity.Day,
                TimeSeriesMetric.Visitors),
            Cancellation.Token);

        series.Select(point => point.Value).Should().Equal(1, 2);
    }

    /// <summary>
    /// Days are cut in the zone the site's owner thinks in. Reported in UTC, a reader in Kolkata
    /// would see their morning counted against the previous day for ever.
    /// </summary>
    [Fact]
    public async Task Days_Are_Cut_In_The_Site_Own_Time_Zone()
    {
        var siteId = Guid.NewGuid();

        // Half past nine in the evening in London is three in the morning of the next day in
        // Kolkata, which is five and a half hours ahead.
        await WriteAsync(PageView(siteId, Midnight.AddHours(21).AddMinutes(30), "visitor-a"));

        var series = await Queries.GetTimeSeriesAsync(
            Scope(siteId, "Asia/Kolkata"),
            new TimeSeriesQuery(
                new TimeRange(Midnight, Midnight.AddDays(2)),
                TimeGranularity.Day,
                TimeSeriesMetric.PageViews),
            Cancellation.Token);

        series.Should().HaveCountGreaterThan(1);
        series[0].Value.Should().Be(0);
        series[1].Value.Should().Be(1);
    }

    /// <summary>
    /// A week begins at midnight on Monday where the website is. In Kolkata that is half past six
    /// on Sunday evening in UTC and in Kathmandu a quarter past six, so two reports minutes apart
    /// either side of it belong to two different weeks.
    /// </summary>
    /// <param name="timeZoneId">Where the website is.</param>
    /// <param name="hour">The hour of Sunday evening, in UTC, its week begins at.</param>
    /// <param name="minute">The minute past that hour.</param>
    [Theory]
    [InlineData(Kolkata, 18, 30)]
    [InlineData("Asia/Kathmandu", 18, 15)]
    public async Task A_Week_Begins_On_Monday_Where_The_Website_Is(string timeZoneId, int hour, int minute)
    {
        var siteId = Guid.NewGuid();
        var calendar = new ZoneCalendar(timeZoneId);
        var monday = ZoneCalendar.MondayOf(calendar.DateOf(Now)).AddDays(-7);
        var begins = calendar.MidnightOn(monday);

        await WriteAsync(
            PageView(siteId, begins.AddMinutes(-5), "visitor-a"),
            PageView(siteId, begins.AddMinutes(5), "visitor-b"));

        var series = await Queries.GetTimeSeriesAsync(
            Scope(siteId, timeZoneId),
            new TimeSeriesQuery(
                new TimeRange(begins.AddHours(-1), begins.AddHours(1)),
                TimeGranularity.Week,
                TimeSeriesMetric.PageViews),
            Cancellation.Token);

        series.Select(point => point.BucketStart).Should().Equal(calendar.MidnightOn(monday.AddDays(-7)), begins);
        series.Select(point => point.Value).Should().Equal(1, 1);
        begins.UtcDateTime.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        begins.UtcDateTime.TimeOfDay.Should().Be(new TimeSpan(hour, minute, 0));
    }

    /// <summary>
    /// A week is named after its Monday even when the window opens later in it, and counts only
    /// what the window holds: a report from before the window opened is not in the week, however
    /// much of the week the window leaves out.
    /// </summary>
    [Fact]
    public async Task The_First_Week_Holds_Only_What_The_Window_Holds()
    {
        var siteId = Guid.NewGuid();
        var calendar = new ZoneCalendar(Utc);
        var monday = calendar.MidnightOn(ZoneCalendar.MondayOf(calendar.DateOf(Now)).AddDays(-14));
        var wednesday = monday.AddDays(2);

        await WriteAsync(
            PageView(siteId, monday.AddHours(12), "visitor-a"),
            PageView(siteId, wednesday.AddHours(12), "visitor-b"),
            PageView(siteId, wednesday.AddDays(1).AddHours(12), "visitor-c"));

        var series = await Queries.GetTimeSeriesAsync(
            Scope(siteId),
            new TimeSeriesQuery(new TimeRange(wednesday, monday.AddDays(7)), TimeGranularity.Week, TimeSeriesMetric.PageViews),
            Cancellation.Token);

        series.Should().ContainSingle();
        series[0].BucketStart.Should().Be(monday);
        series[0].Value.Should().Be(2);
    }

    /// <summary>
    /// A month begins at midnight on the first where the website is, on whichever side of a change
    /// of the clocks that midnight falls: two reports an hour apart either side of it belong to two
    /// months, each named by its own first midnight. Placed at the latest month to begin on a new
    /// offset, which is a spring month in one half of the year and an autumn one in the other.
    /// </summary>
    [Fact]
    public async Task A_Month_Begins_At_Midnight_On_The_First_Either_Side_Of_A_Clock_Change()
    {
        var siteId = Guid.NewGuid();
        var london = new ZoneCalendar(London);
        var month = london.LatestMonthBeginningOnANewOffset(Now.AddHours(-1));
        var begins = london.MidnightOn(month);

        await WriteAsync(
            PageView(siteId, begins.AddMinutes(-30), "visitor-a"),
            PageView(siteId, begins.AddMinutes(30), "visitor-b"));

        var series = await Queries.GetTimeSeriesAsync(
            Scope(siteId, London),
            new TimeSeriesQuery(
                new TimeRange(begins.AddHours(-1), begins.AddHours(1)),
                TimeGranularity.Month,
                TimeSeriesMetric.PageViews),
            Cancellation.Token);

        series.Select(point => point.BucketStart).Should().Equal(london.MidnightOn(month.AddMonths(-1)), begins);
        series.Select(point => point.Value).Should().Equal(1, 1);
    }

    /// <summary>
    /// A week nothing happened in is a week the store invents, and only an invented one shows
    /// whether it steps on the website's calendar: stepping seven times twenty-four hours from the
    /// Monday before the clocks change lands an hour either side of the Monday after it. The window
    /// reaches across the last two changes, so one goes forward and one goes back whenever this
    /// runs; the weeks around the earlier change hold nothing at all, because it can be too long ago
    /// for the store to keep reports from.
    /// </summary>
    [Fact]
    public async Task A_Week_Nobody_Visited_After_The_Clocks_Change_Still_Begins_At_Midnight_On_Monday()
    {
        var siteId = Guid.NewGuid();
        var london = new ZoneCalendar(London);
        var latest = london.LatestClockChange(Now.AddDays(-35));
        var first = ZoneCalendar.MondayOf(london.DateOf(london.LatestClockChange(latest.AddHours(-1))));
        var changed = ZoneCalendar.MondayOf(london.DateOf(latest));
        var weeks = ((changed.DayNumber - first.DayNumber) / 7) + 3;

        // A report in the week the clocks last changed, before they did, and one two weeks later,
        // so the week straight after the change sits empty between two that are not.
        await WriteAsync(
            PageView(siteId, london.MidnightOn(changed.AddDays(1)).AddHours(12), "visitor-a"),
            PageView(siteId, london.MidnightOn(changed.AddDays(16)).AddHours(12), "visitor-b"));

        var series = await Queries.GetTimeSeriesAsync(
            Scope(siteId, London),
            new TimeSeriesQuery(
                new TimeRange(london.MidnightOn(first), london.MidnightOn(first.AddDays(7 * weeks))),
                TimeGranularity.Week,
                TimeSeriesMetric.PageViews),
            Cancellation.Token);

        var begins = series.Select(point => point.BucketStart).ToArray();

        begins.Should().HaveCount(weeks).And.OnlyHaveUniqueItems();
        begins.Should().OnlyContain(start =>
            london.IsMidnight(start) && london.DateOf(start).DayOfWeek == DayOfWeek.Monday);
        begins.Should().Equal(Enumerable.Range(0, weeks).Select(week => london.MidnightOn(first.AddDays(7 * week))));
        series.Select(point => point.Value).Should().Equal(
            Enumerable.Range(0, weeks).Select(week => week == weeks - 3 || week == weeks - 1 ? 1L : 0L));
    }

    /// <summary>
    /// A month nothing happened in begins at midnight on the first even when the clocks changed in
    /// the month before it. The window reaches from the month of the earlier of the last two
    /// changes to the month after the latest, so both directions of change are stepped across into
    /// an empty month, with months that hold reports on either side of the empty ones between.
    /// </summary>
    [Fact]
    public async Task A_Month_Nobody_Visited_After_The_Clocks_Change_Still_Begins_At_Midnight_On_The_First()
    {
        var siteId = Guid.NewGuid();
        var london = new ZoneCalendar(London);
        var latest = london.LatestClockChange(Now.AddDays(-35));
        var first = ZoneCalendar.FirstOf(london.DateOf(london.LatestClockChange(latest.AddHours(-1))));
        var changed = ZoneCalendar.FirstOf(london.DateOf(latest));
        var months = MonthsFrom(first, changed) + 2;

        // The earlier change can be too long ago for the store to keep reports from, so the first
        // report waits three months after it; the second is early in the month of the latest change.
        await WriteAsync(
            PageView(siteId, london.MidnightOn(first.AddMonths(3).AddDays(9)).AddHours(12), "visitor-a"),
            PageView(siteId, london.MidnightOn(changed.AddDays(9)).AddHours(12), "visitor-b"));

        var series = await Queries.GetTimeSeriesAsync(
            Scope(siteId, London),
            new TimeSeriesQuery(
                new TimeRange(london.MidnightOn(first), london.MidnightOn(first.AddMonths(months))),
                TimeGranularity.Month,
                TimeSeriesMetric.PageViews),
            Cancellation.Token);

        var begins = series.Select(point => point.BucketStart).ToArray();

        begins.Should().HaveCount(months).And.OnlyHaveUniqueItems();
        begins.Should().OnlyContain(start => london.IsMidnight(start) && london.DateOf(start).Day == 1);
        begins.Should().Equal(Enumerable.Range(0, months).Select(month => london.MidnightOn(first.AddMonths(month))));
        series.Select(point => point.Value).Should().Equal(
            Enumerable.Range(0, months).Select(month => month == 3 || month == months - 2 ? 1L : 0L));
    }

    /// <summary>
    /// Kolkata never changes its clocks, but its months still begin at half past six in the evening
    /// of the last day of the month before in UTC, and the months are of different lengths: a fill
    /// stepping a month at a time on UTC's calendar would carry the thirtieth into a month that
    /// begins on the thirty-first, and draw an empty month from the wrong evening.
    /// </summary>
    [Fact]
    public async Task A_Month_Nobody_Visited_Still_Begins_At_Midnight_On_The_First_Where_The_Website_Is()
    {
        const int months = 6;

        var siteId = Guid.NewGuid();
        var kolkata = new ZoneCalendar(Kolkata);
        var first = ZoneCalendar.FirstOf(kolkata.DateOf(Now)).AddMonths(-months);

        await WriteAsync(
            PageView(siteId, kolkata.MidnightOn(first.AddDays(9)).AddHours(12), "visitor-a"),
            PageView(siteId, kolkata.MidnightOn(first.AddMonths(2).AddDays(9)).AddHours(12), "visitor-b"));

        var series = await Queries.GetTimeSeriesAsync(
            Scope(siteId, Kolkata),
            new TimeSeriesQuery(
                new TimeRange(kolkata.MidnightOn(first), kolkata.MidnightOn(first.AddMonths(months))),
                TimeGranularity.Month,
                TimeSeriesMetric.PageViews),
            Cancellation.Token);

        var begins = series.Select(point => point.BucketStart).ToArray();

        begins.Should().HaveCount(months).And.OnlyHaveUniqueItems();
        begins.Should().OnlyContain(start => kolkata.IsMidnight(start) && kolkata.DateOf(start).Day == 1);
        begins.Should().Equal(Enumerable.Range(0, months).Select(month => kolkata.MidnightOn(first.AddMonths(month))));
        series.Select(point => point.Value).Should().Equal(1, 0, 1, 0, 0, 0);
    }

    /// <summary>
    /// The day the clocks change is twenty-three or twenty-five hours long and still one day, and the
    /// days either side of it that nothing happened in still begin at midnight.
    /// </summary>
    [Fact]
    public async Task A_Day_Nobody_Visited_After_The_Clocks_Change_Still_Begins_At_Midnight()
    {
        var siteId = Guid.NewGuid();
        var london = new ZoneCalendar(London);
        var changed = london.DateOf(london.LatestClockChange(Now.AddDays(-35)));
        var first = changed.AddDays(-2);

        await WriteAsync(
            PageView(siteId, london.MidnightOn(first).AddHours(12), "visitor-a"),
            PageView(siteId, london.MidnightOn(changed.AddDays(2)).AddHours(12), "visitor-b"));

        var series = await Queries.GetTimeSeriesAsync(
            Scope(siteId, London),
            new TimeSeriesQuery(
                new TimeRange(london.MidnightOn(first), london.MidnightOn(changed.AddDays(3))),
                TimeGranularity.Day,
                TimeSeriesMetric.PageViews),
            Cancellation.Token);

        var begins = series.Select(point => point.BucketStart).ToArray();

        begins.Should().HaveCount(5).And.OnlyHaveUniqueItems();
        begins.Should().OnlyContain(start => london.IsMidnight(start));
        begins.Should().Equal(Enumerable.Range(0, 5).Select(day => london.MidnightOn(first.AddDays(day))));
        series.Select(point => point.Value).Should().Equal(1, 0, 0, 0, 1);
    }

    /// <summary>
    /// Where the clocks go forward at midnight, as Santiago's and Cairo's do, the day they change
    /// on has no midnight and begins at one in the morning. It is still one day, and every day after
    /// it still begins where its own midnight is, whether or not anything happened in it: stepping
    /// from the instants rather than the dates would draw each later day an hour early, beside the
    /// day it is, and the series would grow a bucket a day. Placed around the latest such day at
    /// least four days back, so the days after it have all happened.
    /// </summary>
    [Fact]
    public async Task A_Day_The_Clocks_Begin_After_Midnight_Is_Still_One_Day_And_So_Is_Every_Day_After_It()
    {
        var siteId = Guid.NewGuid();
        var (calendar, skipped) = ZoneCalendar.LatestDayWithoutAMidnight(Now.AddDays(-4));
        DateOnly[] days = [.. Enumerable.Range(-3, 7).Select(skipped.AddDays)];

        // One report on the day the clocks skip midnight and one two days later, so a counted day
        // sits on either side of a day only the fill names.
        await WriteAsync(
            PageView(siteId, calendar.FirstInstantOn(skipped).AddHours(12), "visitor-a"),
            PageView(siteId, calendar.FirstInstantOn(skipped.AddDays(2)).AddHours(12), "visitor-b"));

        var series = await Queries.GetTimeSeriesAsync(
            Scope(siteId, calendar.TimeZoneId),
            new TimeSeriesQuery(
                new TimeRange(calendar.FirstInstantOn(days[0]), calendar.FirstInstantOn(skipped.AddDays(4))),
                TimeGranularity.Day,
                TimeSeriesMetric.PageViews),
            Cancellation.Token);

        series.Select(point => point.BucketStart).Should().Equal(days.Select(calendar.FirstInstantOn));
        series.Select(point => point.Value).Should().Equal(0, 0, 0, 1, 0, 1, 0);
    }

    /// <summary>
    /// A visitor key covers one UTC day, and a bucket counts each key once. Away from UTC one key's
    /// day spans two of the website's own, so somebody who reads late one evening in Kolkata and
    /// again just after midnight is one visitor in the week and one in each of the two days: the
    /// week holds fewer visitors than its days added up, and a week's figure is never their sum.
    /// </summary>
    [Fact]
    public async Task A_Week_Can_Hold_Fewer_Visitors_Than_Its_Days_Added_Up()
    {
        var siteId = Guid.NewGuid();
        var kolkata = new ZoneCalendar(Kolkata);
        var monday = ZoneCalendar.MondayOf(kolkata.DateOf(Now)).AddDays(-14);
        var thursday = kolkata.MidnightOn(monday.AddDays(3));
        var week = new TimeRange(kolkata.MidnightOn(monday), kolkata.MidnightOn(monday.AddDays(7)));

        // Eleven on Wednesday night and one on Thursday morning in Kolkata are both Wednesday in
        // UTC, so both reports carry the key that day was given.
        await WriteAsync(
            PageView(siteId, thursday.AddHours(-1), "visitor-a"),
            PageView(siteId, thursday.AddHours(1), "visitor-a"));

        var days = await Queries.GetTimeSeriesAsync(
            Scope(siteId, Kolkata),
            new TimeSeriesQuery(week, TimeGranularity.Day, TimeSeriesMetric.Visitors),
            Cancellation.Token);
        var weeks = await Queries.GetTimeSeriesAsync(
            Scope(siteId, Kolkata),
            new TimeSeriesQuery(week, TimeGranularity.Week, TimeSeriesMetric.Visitors),
            Cancellation.Token);

        thursday.UtcDateTime.AddHours(-1).Date.Should().Be(thursday.UtcDateTime.AddHours(1).Date);
        days.Sum(point => point.Value).Should().Be(2);
        weeks.Should().ContainSingle().Which.Value.Should().Be(1);
    }

    private ITelemetryQueries Queries => stack.Services.GetRequiredService<ITelemetryQueries>();

    /// <summary>The moment the test runs, which every relative date here is placed from.</summary>
    private DateTimeOffset Now => stack.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    /// <summary>How many whole months one first of the month is after another.</summary>
    /// <param name="earlier">The earlier first of the month.</param>
    /// <param name="later">The later one.</param>
    /// <returns>The number of months between them.</returns>
    private static int MonthsFrom(DateOnly earlier, DateOnly later) =>
        ((later.Year - earlier.Year) * 12) + later.Month - earlier.Month;

    private static TimeRange Day() => new(Midnight, Midnight.AddDays(1));

    private static TenantScope Scope(Guid siteId, string timeZoneId = "Etc/UTC") =>
        new(siteId, Guid.NewGuid(), SiteRole.Viewer, timeZoneId, DateTimeOffset.UnixEpoch);

    private static RawEvent PageView(Guid siteId, DateTimeOffset at, string? visitorKey) =>
        Event(siteId, at, visitorKey, EventKind.PageView);

    private static RawEvent Engagement(Guid siteId, DateTimeOffset at, string? visitorKey) =>
        Event(siteId, at, visitorKey, EventKind.Engagement);

    private static RawEvent Event(Guid siteId, DateTimeOffset at, string? visitorKey, EventKind kind) => new()
    {
        EventId = Guid.CreateVersion7(at),
        SiteId = siteId,
        Kind = kind,
        Surface = IngestSurface.BrowserTracker,
        ServerTimestamp = at,
        VisitorKey = visitorKey,
        Host = "example.com",
        Path = "/posts/hello",
    };
}
