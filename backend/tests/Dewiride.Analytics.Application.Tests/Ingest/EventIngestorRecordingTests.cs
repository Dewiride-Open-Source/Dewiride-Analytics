using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Ingest;

/// <summary>
/// Covers what a stored event actually contains.
/// </summary>
public sealed class EventIngestorRecordingTests
{
    private const int MillisecondsPerDay = 24 * 60 * 60 * 1000;

    [Fact]
    public async Task Stamps_The_Server_Clock_Rather_Than_The_Client_One()
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with
        {
            ClientTimestampUnixMs = IngestHarness.Now.AddHours(3).ToUnixTimeMilliseconds(),
        };

        await harness.IngestAsync(command);

        harness.Single.ServerTimestamp.Should().Be(IngestHarness.Now);
        harness.Single.ClientTimestamp.Should().Be(IngestHarness.Now.AddHours(3));
    }

    /// <summary>
    /// The identifier sorts by creation time and is what makes a retried report land once rather
    /// than twice.
    /// </summary>
    [Fact]
    public async Task Gives_The_Event_A_Time_Ordered_Identifier()
    {
        var harness = IngestHarness.ForSite();

        await harness.IngestAsync(IngestHarness.PageView());

        harness.Single.EventId.Version.Should().Be(7);
        harness.Single.EventId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Records_No_Skew_When_The_Client_Claimed_No_Time()
    {
        var harness = IngestHarness.ForSite();

        await harness.IngestAsync(IngestHarness.PageView());

        harness.Single.ClientTimestamp.Should().BeNull();
        harness.Single.ClockSkewMs.Should().Be(0);
    }

    [Fact]
    public async Task Records_How_Far_The_Client_Clock_Is_Adrift()
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with
        {
            ClientTimestampUnixMs = IngestHarness.Now.AddMinutes(-5).ToUnixTimeMilliseconds(),
        };

        await harness.IngestAsync(command);

        harness.Single.ClockSkewMs.Should().Be(-5 * 60 * 1000);
    }

    /// <summary>
    /// A client claiming a date a year out is itself the signal. The exact magnitude beyond a day
    /// says nothing further, and a wider value would not fit the column.
    /// </summary>
    [Theory]
    [InlineData(400, MillisecondsPerDay)]
    [InlineData(-400, -MillisecondsPerDay)]
    public async Task Clamps_A_Wildly_Wrong_Client_Clock_To_A_Day(int daysAdrift, int expectedSkewMs)
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with
        {
            ClientTimestampUnixMs = IngestHarness.Now.AddDays(daysAdrift).ToUnixTimeMilliseconds(),
        };

        await harness.IngestAsync(command);

        harness.Single.ClockSkewMs.Should().Be(expectedSkewMs);
    }

    [Fact]
    public async Task Splits_The_Reported_Address_Into_Host_And_Path()
    {
        var harness = IngestHarness.ForSite();

        await harness.IngestAsync(IngestHarness.PageView("https://Example.COM/posts/hello?ref=news"));

        harness.Single.Host.Should().Be("example.com");
        harness.Single.Path.Should().Be("/posts/hello");
    }

    [Fact]
    public async Task Drops_The_Query_String_Unless_The_Site_Asked_To_Keep_It()
    {
        var harness = IngestHarness.ForSite(retainQueryStrings: false);

        await harness.IngestAsync(IngestHarness.PageView("https://example.com/posts/hello?utm_source=news"));

        harness.Single.QueryString.Should().BeNull();
    }

    [Fact]
    public async Task Keeps_The_Query_String_When_The_Site_Asked_To()
    {
        var harness = IngestHarness.ForSite(retainQueryStrings: true);

        await harness.IngestAsync(IngestHarness.PageView("https://example.com/posts/hello?utm_source=news"));

        harness.Single.QueryString.Should().Be("?utm_source=news");
    }

    [Fact]
    public async Task Records_No_Query_String_For_A_Page_That_Had_None()
    {
        var harness = IngestHarness.ForSite(retainQueryStrings: true);

        await harness.IngestAsync(IngestHarness.PageView("https://example.com/posts/hello"));

        harness.Single.QueryString.Should().BeNull();
    }

    [Fact]
    public async Task Records_The_Referrer_And_Its_Domain()
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with { Referrer = "https://News.EXAMPLE.org/story/42" };

        await harness.IngestAsync(command);

        harness.Single.Referrer.Should().Be("https://News.EXAMPLE.org/story/42");
        harness.Single.ReferrerDomain.Should().Be("news.example.org");
    }

    [Fact]
    public async Task Records_No_Referrer_Domain_When_There_Was_No_Referrer()
    {
        var harness = IngestHarness.ForSite();

        await harness.IngestAsync(IngestHarness.PageView());

        harness.Single.Referrer.Should().BeNull();
        harness.Single.ReferrerDomain.Should().BeNull();
    }

    [Theory]
    [InlineData(2048)]
    [InlineData(4096)]
    public async Task Truncates_An_Oversized_Referrer(int length)
    {
        var harness = IngestHarness.ForSite();
        var referrer = "https://news.example.org/" + new string('a', length);
        var command = IngestHarness.PageView() with { Referrer = referrer };

        await harness.IngestAsync(command);

        harness.Single.Referrer.Should().HaveLength(2048);
    }

    [Fact]
    public async Task Truncates_A_Language_Tag_Nobody_Could_Have_Declared()
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with { Language = new string('x', 200) };

        await harness.IngestAsync(command);

        harness.Single.Language.Should().HaveLength(35);
    }

    [Fact]
    public async Task Truncates_An_Oversized_Correlation_Identifier()
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with { CorrelationId = new string('c', 500) };

        await harness.IngestAsync(command);

        harness.Single.CorrelationId.Should().HaveLength(64);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Treats_A_Blank_Optional_Field_As_Absent(string blank)
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with
        {
            Language = blank,
            CorrelationId = blank,
            Referrer = blank,
        };

        await harness.IngestAsync(command);

        harness.Single.Language.Should().BeNull();
        harness.Single.CorrelationId.Should().BeNull();
        harness.Single.Referrer.Should().BeNull();
    }

    [Fact]
    public async Task Records_Interaction_Presence_As_Reported()
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with
        {
            HadPointerInteraction = true,
            HadKeyboardInteraction = false,
            DeclaredWebDriver = null,
        };

        await harness.IngestAsync(command);

        harness.Single.HadPointerInteraction.Should().BeTrue();
        harness.Single.HadKeyboardInteraction.Should().BeFalse();
        harness.Single.DeclaredWebDriver.Should().BeNull();
    }

    [Fact]
    public async Task Records_What_The_Server_Observed_About_The_Request()
    {
        var harness = IngestHarness.ForSite();
        var context = new IngestContext
        {
            Surface = IngestSurface.CloudflareWorker,
            UserAgent = "curl/8.7.1",
            IpAddress = "198.51.100.24",
            RequestOrigin = "https://example.com",
            StatusCode = 404,
            ContentType = "text/html",
            ResponseBytes = 1536,
        };

        await harness.IngestAsync(IngestHarness.PageView(), context);

        var stored = harness.Single;
        stored.Surface.Should().Be(IngestSurface.CloudflareWorker);
        stored.UserAgent.Should().Be("curl/8.7.1");
        stored.IpAddress.Should().Be("198.51.100.24");
        stored.StatusCode.Should().Be(404);
        stored.ContentType.Should().Be("text/html");
        stored.ResponseBytes.Should().Be(1536);
    }

    /// <summary>
    /// A user agent is written by whoever is visiting the site. It is stored as it arrived, and
    /// the reason that is safe is that it never reaches the store as anything but a bound value.
    /// </summary>
    [Fact]
    public async Task Stores_A_Hostile_User_Agent_Verbatim()
    {
        const string hostile = "'; DROP TABLE events; --";
        var harness = IngestHarness.ForSite();
        var context = IngestHarness.BrowserRequest() with { UserAgent = hostile };

        await harness.IngestAsync(IngestHarness.PageView(), context);

        harness.Single.UserAgent.Should().Be(hostile);
    }

    [Fact]
    public async Task Records_The_Visitor_Key_The_Factory_Derived()
    {
        var harness = IngestHarness.ForSite(visitorKey: "9f2a1c4e8b6d0a3f");

        await harness.IngestAsync(IngestHarness.PageView());

        harness.Single.VisitorKey.Should().Be("9f2a1c4e8b6d0a3f");
    }

    /// <summary>
    /// No key means this activity cannot be grouped, which is a true statement. Substituting one
    /// would invent a visitor.
    /// </summary>
    [Fact]
    public async Task Records_No_Visitor_Key_When_None_Could_Be_Derived()
    {
        var harness = IngestHarness.ForSite(visitorKey: null);

        await harness.IngestAsync(IngestHarness.PageView());

        harness.Single.VisitorKey.Should().BeNull();
    }

    [Theory]
    [InlineData(EventKind.PageView)]
    [InlineData(EventKind.Engagement)]
    [InlineData(EventKind.Exit)]
    public async Task Records_The_Kind_Of_Report_It_Was_Sent(EventKind kind)
    {
        var harness = IngestHarness.ForSite();

        await harness.IngestAsync(IngestHarness.PageView(kind: kind));

        harness.Single.Kind.Should().Be(kind);
    }

    [Fact]
    public async Task Files_The_Event_Under_The_Site_That_Was_Resolved()
    {
        var harness = IngestHarness.ForSite();

        await harness.IngestAsync(IngestHarness.PageView());

        harness.Single.SiteId.Should().Be(IngestHarness.SiteId);
    }

    /// <summary>
    /// Resolved on the way in rather than by a later job, because the address it is resolved from
    /// is erased 72 hours afterwards. An attribute missed here cannot be recovered: there would
    /// be nothing left to recover it from.
    /// </summary>
    [Fact]
    public async Task Records_Where_The_Visitors_Address_Resolved_To()
    {
        var harness = IngestHarness.ForSite();

        await harness.IngestAsync(IngestHarness.PageView());

        var stored = harness.Single;

        stored.CountryCode.Should().Be("IN");
        stored.Subdivision.Should().Be("MH");
        stored.City.Should().Be("Pune");
        stored.AutonomousSystem.Should().Be(24560u);
        stored.NetworkOwner.Should().Be("Bharti Airtel");
    }

    /// <summary>
    /// An address that resolves to nothing is the ordinary case on an installation being run
    /// locally, and on one behind a proxy that does not pass the visitor's address through.
    /// Nothing known is stored rather than something invented.
    /// </summary>
    [Fact]
    public async Task Records_Nothing_About_A_Place_That_Did_Not_Resolve()
    {
        var harness = IngestHarness.ForSite(network: NetworkAttributes.Unresolved);

        await harness.IngestAsync(IngestHarness.PageView());

        var stored = harness.Single;

        stored.CountryCode.Should().BeNull();
        stored.City.Should().BeNull();
        stored.AutonomousSystem.Should().Be(0u);
        stored.NetworkOwner.Should().BeNull();
    }

    [Fact]
    public async Task Records_What_The_Visit_Was_Made_On()
    {
        var harness = IngestHarness.ForSite();

        await harness.IngestAsync(IngestHarness.PageView());

        var stored = harness.Single;

        stored.DeviceClass.Should().Be(DeviceClass.Desktop);
        stored.BrowserFamily.Should().Be("Chrome");
        stored.OperatingSystem.Should().Be("Windows");
    }

    /// <summary>
    /// Kept apart from the device it helped decide, because the two disagreeing is informative in
    /// itself and folding them together would throw that away.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Records_Whether_The_Client_Declared_Itself_Handheld(bool? declared)
    {
        var harness = IngestHarness.ForSite();
        var request = IngestHarness.BrowserRequest() with
        {
            Hints = new ClientHints { Mobile = declared },
        };

        await harness.IngestAsync(IngestHarness.PageView(), request);

        harness.Single.DeclaredMobile.Should().Be(declared);
    }
}
