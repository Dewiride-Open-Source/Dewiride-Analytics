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

    private ITelemetryQueries Queries => stack.Services.GetRequiredService<ITelemetryQueries>();

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    private static TimeRange Day() => new(Midnight, Midnight.AddDays(1));

    private static TenantScope Scope(Guid siteId, string timeZoneId = "Etc/UTC") =>
        new(siteId, Guid.NewGuid(), SiteRole.Viewer, timeZoneId);

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
