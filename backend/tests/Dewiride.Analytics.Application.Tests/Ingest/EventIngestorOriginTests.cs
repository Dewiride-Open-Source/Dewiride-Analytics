using Dewiride.Analytics.Application.Ingest;

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
}
