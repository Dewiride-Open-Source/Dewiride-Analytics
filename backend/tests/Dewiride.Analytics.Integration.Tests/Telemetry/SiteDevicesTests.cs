using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that what an audience read on is counted once per person, whatever saw them.
/// </summary>
/// <remarks>
/// The device split and the software list are the same question at two levels of openness: a
/// device is one of five kinds the engine names, a browser is one of a list that grows. Both are
/// settled once per visitor, because the two halves of the measurement read a user agent each and
/// a report forwarded by a site's own server frequently carries none.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SiteDevicesTests(AnalyticsStackFixture stack)
{
    private static readonly DateTimeOffset Midnight = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Devices_Are_Counted_By_How_Many_People_Were_On_Them()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Used(siteId, Midnight.AddHours(1), "visitor-a", DeviceClass.Phone, "Chrome", "Android"),
            Used(siteId, Midnight.AddHours(1), "visitor-b", DeviceClass.Phone, "Safari", "iOS"),
            Used(siteId, Midnight.AddHours(2), "visitor-c", DeviceClass.Phone, "Chrome", "Android"),
            Used(siteId, Midnight.AddHours(3), "visitor-d", DeviceClass.Desktop, "Firefox", "Windows"));

        var devices = await SplitOfDevices(siteId);

        devices.Select(device => device.Device).Should().Equal(DeviceClass.Phone, DeviceClass.Desktop);
        devices.Select(device => device.Visitors).Should().Equal(3, 1);
    }

    /// <summary>
    /// The property only a real store proves. Each half of the measurement reads the device from
    /// whatever user agent it saw, and a report forwarded by a site's own server frequently
    /// carries none — so a reader watched by both would otherwise be counted once on a phone and
    /// once on nothing at all.
    /// </summary>
    [Fact]
    public async Task A_Visitor_One_Half_Could_Not_Identify_Is_Still_Given_One_Device()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Used(
                siteId,
                Midnight.AddHours(1),
                "visitor-a",
                DeviceClass.Phone,
                "Chrome",
                "Android",
                surface: IngestSurface.BrowserTracker),
            Used(
                siteId,
                Midnight.AddHours(1),
                "visitor-a",
                DeviceClass.Unknown,
                null,
                null,
                surface: IngestSurface.NextJsMiddleware));

        var devices = await SplitOfDevices(siteId);

        devices.Should().ContainSingle();
        devices[0].Device.Should().Be(DeviceClass.Phone);
        devices[0].Visitors.Should().Be(1);
        devices[0].PageViews.Should().Be(1);
    }

    /// <summary>
    /// Much of what reaches a website is not a device at all. Hiding those visits would describe a
    /// different audience from the one that was there.
    /// </summary>
    [Fact]
    public async Task A_Device_Nothing_Could_Establish_Is_A_Row_Rather_Than_A_Silence()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Used(siteId, Midnight.AddHours(1), "visitor-a", DeviceClass.Desktop, "Chrome", "Windows"),
            Used(siteId, Midnight.AddHours(2), "crawler-b", DeviceClass.Unknown, null, null),
            Used(siteId, Midnight.AddHours(3), "crawler-c", DeviceClass.Unknown, null, null));

        var devices = await SplitOfDevices(siteId);

        devices[0].Device.Should().Be(DeviceClass.Unknown);
        devices[0].Visitors.Should().Be(2);
        devices.Should().HaveCount(2);
    }

    /// <summary>
    /// Every visitor is on exactly one row, which is what lets the interface state one total and
    /// draw shares that add up to it.
    /// </summary>
    [Fact]
    public async Task Every_Visitor_Appears_On_Exactly_One_Row()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Used(siteId, Midnight.AddHours(1), "visitor-a", DeviceClass.Phone, "Chrome", "Android"),
            Used(siteId, Midnight.AddHours(2), "visitor-b", DeviceClass.Tablet, "Safari", "iPadOS"),
            Used(siteId, Midnight.AddHours(3), "visitor-c", DeviceClass.Other, null, null),
            Used(siteId, Midnight.AddHours(4), "visitor-d", DeviceClass.Unknown, null, null));

        var devices = await SplitOfDevices(siteId);

        devices.Sum(device => device.Visitors).Should().Be(4);
        devices.Select(device => device.Device).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Browsers_Are_Listed_By_How_Many_People_Used_Them()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Used(siteId, Midnight.AddHours(1), "visitor-a", DeviceClass.Desktop, "Chrome", "Windows"),
            Used(siteId, Midnight.AddHours(1), "visitor-b", DeviceClass.Desktop, "Chrome", "macOS"),
            Used(siteId, Midnight.AddHours(2), "visitor-c", DeviceClass.Desktop, "Firefox", "Linux"));

        var browsers = await PageOfSoftware(siteId);

        browsers.Names.Select(name => name.Name).Should().Equal("Chrome", "Firefox");
        browsers.Names.Select(name => name.Visitors).Should().Equal(2, 1);
    }

    [Fact]
    public async Task Systems_Are_Listed_When_The_List_Is_Grouped_By_System()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Used(siteId, Midnight.AddHours(1), "visitor-a", DeviceClass.Desktop, "Chrome", "Windows"),
            Used(siteId, Midnight.AddHours(1), "visitor-b", DeviceClass.Desktop, "Firefox", "Windows"),
            Used(siteId, Midnight.AddHours(2), "visitor-c", DeviceClass.Desktop, "Chrome", "Linux"));

        var systems = await PageOfSoftware(siteId, SoftwareGrouping.OperatingSystem);

        systems.Names.Select(name => name.Name).Should().Equal("Windows", "Linux");
        systems.Names.Select(name => name.Visitors).Should().Equal(2, 1);
    }

    /// <summary>
    /// A software list is a fact about people. Ranked by pages, whichever browser happened to be
    /// running the busiest crawler would head a list that claims to say what readers use.
    /// </summary>
    [Fact]
    public async Task A_Browser_With_More_People_Outranks_One_With_More_Pages()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Used(siteId, Midnight.AddHours(1), "reader-1", DeviceClass.Phone, "Safari", "iOS"),
            Used(siteId, Midnight.AddHours(1), "reader-2", DeviceClass.Phone, "Safari", "iOS"),
            Used(siteId, Midnight.AddHours(1), "busy", DeviceClass.Desktop, "Chrome", "Windows", path: "/a"),
            Used(siteId, Midnight.AddHours(2), "busy", DeviceClass.Desktop, "Chrome", "Windows", path: "/b"),
            Used(siteId, Midnight.AddHours(3), "busy", DeviceClass.Desktop, "Chrome", "Windows", path: "/c"),
            Used(siteId, Midnight.AddHours(4), "busy", DeviceClass.Desktop, "Chrome", "Windows", path: "/d"));

        var browsers = await PageOfSoftware(siteId);

        browsers.Names[0].Name.Should().Be("Safari");
        browsers.Names[0].Visitors.Should().Be(2);
        browsers.Names[1].Name.Should().Be("Chrome");
        browsers.Names[1].PageViews.Should().Be(4);
    }

    /// <summary>
    /// A browser nothing could be established about is a row, for the reason the unresolved device
    /// is one: an install seeing nothing but crawlers should be able to see that it is.
    /// </summary>
    [Fact]
    public async Task A_Browser_Nothing_Could_Establish_Is_A_Row_Rather_Than_A_Silence()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Used(siteId, Midnight.AddHours(1), "visitor-a", DeviceClass.Desktop, "Chrome", "Windows"),
            Used(siteId, Midnight.AddHours(2), "crawler-b", DeviceClass.Unknown, null, null));

        var browsers = await PageOfSoftware(siteId);

        browsers.Names.Select(name => name.Name).Should().Contain(string.Empty);
        browsers.TotalNames.Should().Be(2);
    }

    /// <summary>
    /// The figures beside the rows describe every name the window held, so a share taken on the
    /// third screenful is a share of the site rather than of the screen.
    /// </summary>
    [Fact]
    public async Task The_Figures_Cover_Names_The_Slice_Was_Cut_Off_Before_Reaching()
    {
        var siteId = Guid.NewGuid();
        var events = Enumerable.Range(0, 12)
            .Select(rank => Used(
                siteId,
                Midnight.AddMinutes(rank),
                $"visitor-{rank:00}",
                DeviceClass.Desktop,
                $"Browser-{rank:00}",
                "Windows"))
            .ToArray();

        await WriteAsync(events);

        var browsers = await PageOfSoftware(siteId, limit: 4);

        browsers.Names.Should().HaveCount(4);
        browsers.TotalNames.Should().Be(12);
        browsers.TotalVisitors.Should().Be(12);
        browsers.MostVisitors.Should().Be(1);
    }

    /// <summary>
    /// The whole list has to be reachable, and reachable exactly once. Without a total ordering
    /// two names with equal audiences could swap between one slice and the next, showing one twice
    /// and never showing the other.
    /// </summary>
    [Fact]
    public async Task Every_Name_Is_Reached_Exactly_Once_By_Walking_The_Slices()
    {
        var siteId = Guid.NewGuid();
        var events = Enumerable.Range(0, 21)
            .Select(rank => Used(
                siteId,
                Midnight.AddMinutes(rank),
                $"visitor-{rank:00}",
                DeviceClass.Desktop,
                $"Browser-{rank:00}",
                "Windows"))
            .ToArray();

        await WriteAsync(events);

        var walked = new List<string>();

        for (var offset = 0; offset < 21; offset += 5)
        {
            var slice = await PageOfSoftware(siteId, limit: 5, offset: offset);
            walked.AddRange(slice.Names.Select(name => name.Name));
        }

        walked.Should().HaveCount(21);
        walked.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task A_Slice_Past_The_End_Of_The_List_Is_Simply_Empty()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(Used(siteId, Midnight.AddHours(1), "visitor-a", DeviceClass.Phone, "Chrome", "Android"));

        var browsers = await PageOfSoftware(siteId, offset: 50);

        browsers.Names.Should().BeEmpty();
        browsers.TotalVisitors.Should().Be(0);
    }

    /// <summary>
    /// Activity nobody could attribute to a visitor says nothing about who was using what, so it
    /// takes no part in a count of who was using what.
    /// </summary>
    [Fact]
    public async Task Activity_With_No_Visitor_Behind_It_Counts_Towards_Nothing()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Used(siteId, Midnight.AddHours(1), "visitor-a", DeviceClass.Phone, "Chrome", "Android"),
            Used(siteId, Midnight.AddHours(2), null, DeviceClass.Desktop, "Firefox", "Linux"));

        var devices = await SplitOfDevices(siteId);
        var browsers = await PageOfSoftware(siteId);

        devices.Select(device => device.Device).Should().Equal(DeviceClass.Phone);
        browsers.TotalVisitors.Should().Be(1);
    }

    [Fact]
    public async Task Activity_Outside_The_Window_Is_Left_Out()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Used(siteId, Midnight.AddHours(1), "visitor-a", DeviceClass.Phone, "Chrome", "Android"),
            Used(siteId, Midnight.AddDays(-1), "visitor-b", DeviceClass.Desktop, "Firefox", "Linux"),
            Used(siteId, Midnight.AddDays(2), "visitor-c", DeviceClass.Tablet, "Safari", "iPadOS"));

        var devices = await SplitOfDevices(siteId);

        devices.Select(device => device.Device).Should().Equal(DeviceClass.Phone);
    }

    [Fact]
    public async Task Another_Sites_Devices_Are_Never_Counted()
    {
        var siteId = Guid.NewGuid();
        var neighbour = Guid.NewGuid();

        await WriteAsync(
            Used(siteId, Midnight.AddHours(1), "visitor-a", DeviceClass.Phone, "Chrome", "Android"),
            Used(neighbour, Midnight.AddHours(1), "visitor-b", DeviceClass.Desktop, "Firefox", "Linux"));

        var devices = await SplitOfDevices(siteId);
        var browsers = await PageOfSoftware(siteId);

        devices.Select(device => device.Device).Should().Equal(DeviceClass.Phone);
        browsers.Names.Select(name => name.Name).Should().Equal("Chrome");
    }

    [Fact]
    public async Task A_Window_With_No_Traffic_Answers_With_Nothing_And_A_Nought()
    {
        var siteId = Guid.NewGuid();

        var devices = await SplitOfDevices(siteId);
        var browsers = await PageOfSoftware(siteId);

        devices.Should().BeEmpty();
        browsers.Names.Should().BeEmpty();
        browsers.TotalVisitors.Should().Be(0);
        browsers.TotalNames.Should().Be(0);
        browsers.MostVisitors.Should().Be(0);
    }

    /// <summary>
    /// Nothing in these columns was written by a client — a match returns the engine's own
    /// catalogue word — but the column is grouped on rather than built into the statement all the
    /// same, which is what this proves.
    /// </summary>
    [Fact]
    public async Task A_Browser_Named_To_Break_The_Statement_Is_Counted_Like_Any_Other()
    {
        var siteId = Guid.NewGuid();
        const string hostile = "'); DROP TABLE events; --";

        await WriteAsync(Used(siteId, Midnight.AddHours(1), "visitor-a", DeviceClass.Desktop, hostile, "Windows"));

        var browsers = await PageOfSoftware(siteId);

        browsers.Names.Select(name => name.Name).Should().Equal(hostile);
    }

    private Task<IReadOnlyList<SiteDeviceKindRow>> SplitOfDevices(Guid siteId) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteDeviceKindsAsync(
            Scope(siteId),
            new SiteDeviceKindsQuery(new TimeRange(Midnight, Midnight.AddDays(1))),
            Cancellation.Token);

    private Task<SiteSoftware> PageOfSoftware(
        Guid siteId,
        SoftwareGrouping grouping = SoftwareGrouping.Browser,
        int limit = 10,
        int offset = 0) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteSoftwareAsync(
            Scope(siteId),
            new SiteSoftwareQuery(new TimeRange(Midnight, Midnight.AddDays(1)), grouping, limit, offset),
            Cancellation.Token);

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    private static TenantScope Scope(Guid siteId) =>
        new(siteId, Guid.NewGuid(), SiteRole.Viewer, "Etc/UTC");

    private static RawEvent Used(
        Guid siteId,
        DateTimeOffset at,
        string? visitorKey,
        DeviceClass device,
        string? browser,
        string? system,
        string path = "/",
        IngestSurface surface = IngestSurface.BrowserTracker) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = EventKind.PageView,
            Surface = surface,
            ServerTimestamp = at,
            VisitorKey = visitorKey,
            Host = "example.com",
            Path = path,
            DeviceClass = device,
            BrowserFamily = browser,
            OperatingSystem = system,
        };
}
