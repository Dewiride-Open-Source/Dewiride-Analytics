using Dewiride.Analytics.Application.Ingest;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Application.Tests.Ingest;

/// <summary>
/// Covers which pages may file reports under a site's identifier.
/// </summary>
/// <remarks>
/// A site identifier is printed in the page source of every page it measures, so anybody can read
/// one. This check is what stops a stranger pointing a firehose at somebody else's numbers, and
/// the near-miss cases matter most: a domain that merely ends with the site's domain is a
/// different domain, and a site that has declared an explicit list has declared it instead of the
/// default, not in addition to it.
/// </remarks>
public sealed class EventIngestorOriginTests
{
    [Fact]
    public async Task Accepts_A_Report_From_The_Site_Itself()
    {
        var harness = IngestHarness.ForSite(domain: "example.com");

        var outcome = await harness.IngestAsync(
            IngestHarness.PageView(),
            IngestHarness.BrowserRequest("https://example.com"));

        outcome.Should().Be(IngestOutcome.Accepted);
    }

    [Fact]
    public async Task Accepts_A_Report_From_A_Subdomain_Of_The_Site()
    {
        var harness = IngestHarness.ForSite(domain: "example.com");

        var outcome = await harness.IngestAsync(
            IngestHarness.PageView("https://docs.example.com/guide"),
            IngestHarness.BrowserRequest("https://docs.example.com"));

        outcome.Should().Be(IngestOutcome.Accepted);
    }

    /// <summary>
    /// The near miss this check exists for. A suffix match alone would accept it.
    /// </summary>
    [Theory]
    [InlineData("https://notexample.com")]
    [InlineData("https://myexample.com")]
    [InlineData("https://example.com.attacker.test")]
    public async Task Rejects_A_Report_From_A_Domain_That_Merely_Resembles_The_Site(string origin)
    {
        var harness = IngestHarness.ForSite(domain: "example.com");

        var outcome = await harness.IngestAsync(
            IngestHarness.PageView(),
            IngestHarness.BrowserRequest(origin));

        outcome.Should().Be(IngestOutcome.Rejected);
        harness.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task Falls_Back_To_The_Reported_Page_When_There_Is_No_Browser_Origin()
    {
        var harness = IngestHarness.ForSite(domain: "example.com");

        var outcome = await harness.IngestAsync(
            IngestHarness.PageView("https://example.com/posts/hello"),
            IngestHarness.BrowserRequest(origin: null));

        outcome.Should().Be(IngestOutcome.Accepted);
    }

    [Fact]
    public async Task Rejects_A_Page_On_Another_Domain_When_There_Is_No_Browser_Origin()
    {
        var harness = IngestHarness.ForSite(domain: "example.com");

        var outcome = await harness.IngestAsync(
            IngestHarness.PageView("https://attacker.test/page"),
            IngestHarness.BrowserRequest(origin: null));

        outcome.Should().Be(IngestOutcome.Rejected);
    }

    [Fact]
    public async Task An_Explicit_Origin_List_Admits_What_It_Names()
    {
        string[] allowed = ["docs.example.com"];
        var harness = IngestHarness.ForSite(domain: "example.com", allowedOrigins: allowed);

        var outcome = await harness.IngestAsync(
            IngestHarness.PageView("https://docs.example.com/guide"),
            IngestHarness.BrowserRequest("https://docs.example.com"));

        outcome.Should().Be(IngestOutcome.Accepted);
    }

    /// <summary>
    /// An explicit list replaces the default rather than adding to it, so the site's own domain
    /// is admitted only if it appears on the list. This is also the check that would have caught
    /// a cached site whose origin list came back empty.
    /// </summary>
    [Fact]
    public async Task An_Explicit_Origin_List_Excludes_The_Site_Domain_It_Does_Not_Name()
    {
        string[] allowed = ["docs.example.com"];
        var harness = IngestHarness.ForSite(domain: "example.com", allowedOrigins: allowed);

        var outcome = await harness.IngestAsync(
            IngestHarness.PageView("https://example.com/posts/hello"),
            IngestHarness.BrowserRequest("https://example.com"));

        outcome.Should().Be(IngestOutcome.Rejected);
        harness.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task An_Origin_Is_Compared_By_Host_Rather_Than_By_The_Whole_Address()
    {
        var harness = IngestHarness.ForSite(domain: "example.com");

        var outcome = await harness.IngestAsync(
            IngestHarness.PageView(),
            IngestHarness.BrowserRequest("https://Example.COM:8443"));

        outcome.Should().Be(IngestOutcome.Accepted);
    }

    /// <summary>
    /// The refusal is silent to the sender and has to stay that way, so the only account of it is
    /// the one the machine's owner can read. Without it the whole diagnosis of "the snippet is
    /// installed and the dashboard shows nothing" is reading the source.
    /// </summary>
    [Fact]
    public async Task A_Refused_Origin_Says_Why_In_The_Log()
    {
        var harness = IngestHarness.ForSite(domain: "example.com");

        await harness.IngestAsync(
            IngestHarness.PageView("http://localhost:3000/posts/hello"),
            IngestHarness.BrowserRequest("http://localhost:3000"));

        var written = harness.Logged.Should().ContainSingle().Subject;

        written.Level.Should().Be(LogLevel.Debug);
        written.Message.Should().Contain("localhost").And.Contain("example.com");
    }

    /// <summary>
    /// An identifier nobody has registered is the other half of the same silence, and is reported
    /// separately: it is the answer for a snippet carrying a stale identifier, where the address
    /// the report came from is beside the point.
    /// </summary>
    [Fact]
    public async Task An_Unknown_Site_Says_So_In_The_Log()
    {
        var harness = IngestHarness.WithNoSuchSite();

        await harness.IngestAsync(IngestHarness.PageView());

        var written = harness.Logged.Should().ContainSingle().Subject;

        written.Level.Should().Be(LogLevel.Debug);
        written.Message.Should().Contain(IngestHarness.SiteId.ToString());
    }

    /// <summary>
    /// The host in that line is written by whoever sent the request, and a log is read one record
    /// to a line by people and by collectors alike — so a newline in it would let a stranger write
    /// whatever they liked into the operator's own record of what happened.
    /// </summary>
    [Fact]
    public async Task A_Refused_Origin_Cannot_Forge_A_Line_In_The_Log()
    {
        var harness = IngestHarness.ForSite(domain: "example.com");

        await harness.IngestAsync(
            IngestHarness.PageView("https://elsewhere.test/posts/hello"),
            IngestHarness.BrowserRequest("evil.test\nfail: everything is broken"));

        var written = harness.Logged.Should().ContainSingle().Subject;

        written.Message.Should().NotContain("\n").And.NotContain("\r");
        written.Message.Should().Contain("evil.test");
    }

    /// <summary>
    /// Nothing is written for a report that was stored. A line per accepted page view would turn
    /// the log into a second, worse copy of the telemetry store.
    /// </summary>
    [Fact]
    public async Task An_Accepted_Report_Writes_Nothing_To_The_Log()
    {
        var harness = IngestHarness.ForSite(domain: "example.com");

        var outcome = await harness.IngestAsync(IngestHarness.PageView());

        outcome.Should().Be(IngestOutcome.Accepted);
        harness.Logged.Should().BeEmpty();
    }
}
