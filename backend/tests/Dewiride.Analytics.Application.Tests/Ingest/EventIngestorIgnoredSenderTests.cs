using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Domain.Telemetry;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Application.Tests.Ingest;

/// <summary>
/// Covers a visit from a website the installation does not keep visits from: turned away as it
/// arrives, with everything its visitor sends until the visit is over, and answered as every other
/// report the collector does not keep is answered.
/// </summary>
public sealed class EventIngestorIgnoredSenderTests
{
    private static readonly IReadOnlyList<string> Spam = ["aocr.org"];

    [Fact]
    public async Task A_Page_View_Sent_From_An_Ignored_Website_Is_Not_Stored()
    {
        var harness = IngestHarness.Ignoring(Spam);

        var receipt = await harness.ReceiptOfAsync(Arrival("https://www.aocr.org/"));

        receipt.Should().Be(new IngestReceipt(IngestOutcome.Rejected, RecordsPresses: false));
        harness.Stored.Should().BeEmpty();
        harness.Remembered.Should().Equal((IngestHarness.SiteId, "9f2a1c4e8b6d0a3f"));
    }

    /// <summary>
    /// Only the page view says where a visit came from. Every report its page sends afterwards, and
    /// every page the visitor goes on to, names no ignored sender and is turned away all the same —
    /// or a visit dropped at the door would be on the screen of who is here now within fifteen
    /// seconds, as a visitor with no page.
    /// </summary>
    [Fact]
    public async Task Everything_Its_Visitor_Sends_Afterwards_Is_Not_Stored_Either()
    {
        var harness = IngestHarness.Ignoring(Spam);
        await harness.IngestAsync(Arrival("https://www.aocr.org/"));

        IngestCommand[] afterwards =
        [
            IngestHarness.PageView(kind: EventKind.Engagement) with { EngagedMs = 15_000 },
            IngestHarness.PageView(kind: EventKind.Exit) with { EngagedMs = 16_000 },
            IngestHarness.PageView(kind: EventKind.Action),
            Arrival("https://example.com/posts/hello") with { Url = "https://example.com/about" },
        ];

        foreach (var report in afterwards)
        {
            (await harness.IngestAsync(report)).Should().Be(IngestOutcome.Rejected);
        }

        harness.Stored.Should().BeEmpty();
    }

    /// <summary>
    /// The tracker names a sender only on a page view, but nothing a report carries is a promise: a
    /// report of any other kind that names an ignored website is turned away as a page view would be.
    /// </summary>
    [Fact]
    public async Task A_Report_Of_Any_Kind_That_Names_An_Ignored_Website_Is_Not_Stored()
    {
        var harness = IngestHarness.Ignoring(Spam);
        var progress = IngestHarness.PageView(kind: EventKind.Engagement) with
        {
            Referrer = "https://www.aocr.org/",
            EngagedMs = 15_000,
        };

        (await harness.IngestAsync(progress)).Should().Be(IngestOutcome.Rejected);
        harness.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Visit_From_Any_Other_Website_Is_Kept()
    {
        var harness = IngestHarness.Ignoring(Spam);

        (await harness.IngestAsync(Arrival("https://www.google.com/"))).Should().Be(IngestOutcome.Accepted);

        harness.Single.ReferrerDomain.Should().Be("www.google.com");
        harness.Remembered.Should().BeEmpty();
    }

    /// <summary>The name a visit gives is written by whoever sent it, so only an exact name counts.</summary>
    [Theory]
    [InlineData("https://notaocr.org/")]
    [InlineData("https://aocr.org.example.net/")]
    [InlineData("https://aocr.organic.example/")]
    public async Task A_Website_Merely_Named_Like_An_Ignored_One_Is_Kept(string referrer)
    {
        var harness = IngestHarness.Ignoring(Spam);

        (await harness.IngestAsync(Arrival(referrer))).Should().Be(IngestOutcome.Accepted);
    }

    [Theory]
    [InlineData("https://WWW.AOCR.ORG/")]
    [InlineData("https://aocr.org/landing?ref=campaign#top")]
    [InlineData("http://www.aocr.org:8080/")]
    [InlineData("https://www.aocr.org./")]
    public async Task An_Ignored_Website_Is_Known_However_Its_Address_Is_Written(string referrer)
    {
        var harness = IngestHarness.Ignoring(Spam);

        (await harness.IngestAsync(Arrival(referrer))).Should().Be(IngestOutcome.Rejected);
    }

    /// <summary>
    /// The address a visit says it came from is written by whoever sent it. Whatever it holds — a
    /// name in another script, one that cannot be written in ASCII, an address, a scheme no browser
    /// uses, nothing a URI parser accepts — reading the sender out of it never stops a report being
    /// kept.
    /// </summary>
    [Theory]
    [InlineData("https://xn--ls8h.la/")]
    [InlineData("https://bücher.example/")]
    [InlineData("https://xn--a.example/")]
    [InlineData("https://ex­ample.com/")]
    [InlineData("https://a​b.example/")]
    [InlineData("https://-x-.example/")]
    [InlineData("http://[2001:db8::1]/")]
    [InlineData("http://203.0.113.7:8080/")]
    [InlineData("android-app://com.google.android.gm/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://")]
    [InlineData("not a referrer at all")]
    public async Task Any_Address_A_Visit_Gives_Is_Read_Without_Fault(string referrer)
    {
        var harness = IngestHarness.Ignoring(Spam);

        (await harness.IngestAsync(Arrival(referrer))).Should().Be(IngestOutcome.Accepted);
    }

    /// <summary>
    /// A website listing its own address would otherwise turn away every visitor who went on to a
    /// second page, so a page reached from the website itself is never a sender's.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/posts/hello")]
    [InlineData("https://blog.example.com/posts/hello")]
    public async Task A_Page_Reached_From_The_Website_Itself_Is_Never_Turned_Away(string referrer)
    {
        var harness = IngestHarness.Ignoring(["example.com"]);

        (await harness.IngestAsync(Arrival(referrer))).Should().Be(IngestOutcome.Accepted);
        harness.Remembered.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Page_Reached_From_Another_Address_The_Website_Is_Published_At_Is_Never_Turned_Away()
    {
        var harness = IngestHarness.Ignoring(["example.net"], allowedOrigins: ["example.com", "example.net"]);

        (await harness.IngestAsync(Arrival("https://example.net/shop"))).Should().Be(IngestOutcome.Accepted);
    }

    [Fact]
    public async Task With_Nothing_Listed_A_Sender_Is_Only_Where_A_Visit_Came_From()
    {
        var harness = IngestHarness.ForSite();

        (await harness.IngestAsync(Arrival("https://www.aocr.org/"))).Should().Be(IngestOutcome.Accepted);

        harness.Single.ReferrerDomain.Should().Be("www.aocr.org");
    }

    /// <summary>
    /// A website's own server reports the visitor's referrer as the browser does, so a visit from an
    /// ignored website is turned away whichever way it was reported.
    /// </summary>
    [Fact]
    public async Task A_Visit_Reported_By_The_Website_s_Own_Server_Is_Turned_Away_Alike()
    {
        var harness = IngestHarness.Ignoring(Spam);
        var context = new IngestContext
        {
            Surface = IngestSurface.WordPressPlugin,
            UserAgent = IngestHarness.BrowserRequest().UserAgent,
            IpAddress = "198.51.100.24",
        };

        (await harness.IngestAsync(Arrival("https://www.aocr.org/"), context)).Should().Be(IngestOutcome.Rejected);
        harness.Stored.Should().BeEmpty();
    }

    /// <summary>
    /// With no key to remember the visitor by, the arrival is still turned away. What follows cannot
    /// be told apart from anybody else's report, so it is kept, and with no arrival before it, it
    /// never becomes a visit.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Who_Cannot_Be_Told_Apart_Is_Turned_Away_But_Not_Remembered()
    {
        var harness = IngestHarness.Ignoring(Spam, visitorKey: null);

        (await harness.IngestAsync(Arrival("https://www.aocr.org/"))).Should().Be(IngestOutcome.Rejected);
        (await harness.IngestAsync(IngestHarness.PageView(kind: EventKind.Engagement))).Should().Be(IngestOutcome.Accepted);

        harness.Remembered.Should().BeEmpty();
    }

    /// <summary>
    /// The machine's owner can read why a visit never arrived; the log does not repeat the name the
    /// visit gave, which whoever sent it wrote, and which the owner who listed it already knows.
    /// </summary>
    [Fact]
    public async Task Says_Why_In_The_Log_Without_Repeating_The_Name_The_Visit_Gave()
    {
        var harness = IngestHarness.Ignoring(Spam);

        await harness.IngestAsync(Arrival("https://www.aocr.org/"));

        var written = harness.Logged.Should().ContainSingle().Subject;

        written.Level.Should().Be(LogLevel.Debug);
        written.Id.Id.Should().Be(4103);
        written.Message.Should().Contain(IngestHarness.SiteId.ToString()).And.NotContain("aocr");
    }

    private static IngestCommand Arrival(string referrer) => IngestHarness.PageView() with { Referrer = referrer };
}
