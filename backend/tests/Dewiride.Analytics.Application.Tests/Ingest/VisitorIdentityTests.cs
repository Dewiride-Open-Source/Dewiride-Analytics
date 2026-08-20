using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Ingest;

/// <summary>
/// Covers who the ingestor decides a report was about, which is what a visitor count counts.
/// </summary>
public sealed class VisitorIdentityTests
{
    /// <summary>A network that rents servers, with two of the addresses it rents.</summary>
    private static readonly NetworkAttributes Rented =
        new("SG", string.Empty, "Singapore", 45102, "ALIBABA-CN-NET Alibaba US Technology Co., Ltd.");

    /// <summary>A network that carries households.</summary>
    private static readonly NetworkAttributes Homes =
        new("IN", "MH", "Pune", 55836, "RELIANCEJIO-IN Reliance Jio Infocomm Limited");

    /// <summary>
    /// One program reading a site through a pool of rented addresses is one program. Counted by
    /// address it becomes as many visitors as the pool is wide, and each of its reports about a
    /// page lands under a different one — leaving one visit holding a page nobody read and another
    /// holding a reading of no page.
    /// </summary>
    [Fact]
    public async Task Recognises_A_Pool_Of_Rented_Addresses_As_One_Visitor()
    {
        var harness = IngestHarness.ForSite(network: Rented);

        await harness.IngestAsync(IngestHarness.PageView(), IngestHarness.BrowserRequest(address: "47.238.1.1"));
        var first = harness.Connection;

        await harness.IngestAsync(IngestHarness.PageView(), IngestHarness.BrowserRequest(address: "8.219.64.13"));

        harness.Connection.Should().Be(first);
    }

    /// <summary>
    /// The other half of the same decision, and the one that keeps it honest: nothing about an
    /// ordinary network changes, so two households stay two visitors.
    /// </summary>
    [Fact]
    public async Task Keeps_Two_Ordinary_Addresses_Apart()
    {
        var harness = IngestHarness.ForSite(network: Homes);

        await harness.IngestAsync(IngestHarness.PageView(), IngestHarness.BrowserRequest(address: "203.0.113.7"));
        var first = harness.Connection;

        await harness.IngestAsync(IngestHarness.PageView(), IngestHarness.BrowserRequest(address: "203.0.113.8"));

        harness.Connection.Should().NotBe(first);
        first.Should().Be("203.0.113.7");
    }
}
