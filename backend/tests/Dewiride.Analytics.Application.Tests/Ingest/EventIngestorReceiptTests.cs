using Dewiride.Analytics.Application.Ingest;

namespace Dewiride.Analytics.Application.Tests.Ingest;

/// <summary>
/// Covers what a report's receipt says about its website, which the browser's collector passes on
/// so that the tracker listens for presses only where they are recorded.
/// </summary>
public sealed class EventIngestorReceiptTests
{
    [Fact]
    public async Task Says_A_Website_Records_Presses_Where_It_Does()
    {
        var receipt = await IngestHarness.ForSite(captureClicks: true).ReceiptOfAsync(IngestHarness.PageView());

        receipt.Should().Be(new IngestReceipt(IngestOutcome.Accepted, RecordsPresses: true));
    }

    [Fact]
    public async Task Says_No_Of_A_Website_That_Records_None()
    {
        var receipt = await IngestHarness.ForSite(captureClicks: false).ReceiptOfAsync(IngestHarness.PageView());

        receipt.Should().Be(new IngestReceipt(IngestOutcome.Accepted, RecordsPresses: false));
    }

    /// <summary>
    /// A report that was turned away says nothing about its website, whatever the website records:
    /// telling a refused report the website records presses would tell anybody which site
    /// identifiers are real.
    /// </summary>
    [Fact]
    public async Task Says_No_Where_The_Website_Does_Not_Exist()
    {
        var receipt = await IngestHarness.WithNoSuchSite().ReceiptOfAsync(IngestHarness.PageView());

        receipt.Should().Be(new IngestReceipt(IngestOutcome.Rejected, RecordsPresses: false));
    }

    [Fact]
    public async Task Says_No_To_A_Page_On_An_Origin_A_Recording_Website_Does_Not_Accept()
    {
        var receipt = await IngestHarness.ForSite(captureClicks: true).ReceiptOfAsync(
            IngestHarness.PageView(),
            IngestHarness.BrowserRequest(origin: "https://attacker.test"));

        receipt.Should().Be(new IngestReceipt(IngestOutcome.Rejected, RecordsPresses: false));
    }

    [Fact]
    public async Task Says_No_For_A_Recording_Website_No_Longer_Measured()
    {
        var receipt = await IngestHarness.ForSite(captureClicks: true, measuring: false)
            .ReceiptOfAsync(IngestHarness.PageView());

        receipt.Should().Be(new IngestReceipt(IngestOutcome.Rejected, RecordsPresses: false));
    }

    [Fact]
    public async Task Says_No_To_A_Report_That_Cannot_Be_Read()
    {
        var receipt = await IngestHarness.ForSite(captureClicks: true)
            .ReceiptOfAsync(IngestHarness.PageView(url: "not an address"));

        receipt.Should().Be(new IngestReceipt(IngestOutcome.Invalid, RecordsPresses: false));
    }
}
