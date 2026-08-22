using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Ingest;

/// <summary>
/// Covers the one question the collector asks about an account rather than about a report.
/// </summary>
/// <remarks>
/// A self-hosted installation always answers yes and none of this is reachable there. Where
/// somebody else is running the service it is what stops an account that has run out of allowance
/// from going on being measured, and the whole of what it may do is drop the report — quietly, in
/// exactly the words the collector uses for everything else.
/// </remarks>
public sealed class MeasurementAllowanceTests
{
    [Fact]
    public async Task Stores_A_Report_While_The_Account_Is_Still_Being_Measured()
    {
        var harness = IngestHarness.ForSite(measuring: true);

        var outcome = await harness.IngestAsync(IngestHarness.PageView());

        outcome.Should().Be(IngestOutcome.Accepted);
        harness.Stored.Should().ContainSingle();
    }

    [Fact]
    public async Task Drops_A_Report_Once_The_Account_Has_Stopped_Being_Measured()
    {
        var harness = IngestHarness.ForSite(measuring: false);

        var outcome = await harness.IngestAsync(IngestHarness.PageView());

        outcome.Should().Be(IngestOutcome.Rejected);
        harness.Stored.Should().BeEmpty();
    }

    /// <summary>
    /// A dropped report is dropped in silence. The collector answers a stored report and a refused
    /// one identically, and a line written for every report an unmeasured account is still sending
    /// would be a log file whose size somebody else chooses.
    /// </summary>
    [Fact]
    public async Task Says_Nothing_In_The_Log_About_A_Report_It_Dropped()
    {
        var harness = IngestHarness.ForSite(measuring: false);

        await harness.IngestAsync(IngestHarness.PageView());

        harness.Logged.Should().BeEmpty();
    }

    /// <summary>
    /// Asked before the site's own settings are consulted, because it is the wider question: an
    /// account that has stopped being measured stores nothing at all, whatever the site is set to
    /// collect.
    /// </summary>
    [Fact]
    public async Task Drops_Every_Kind_Of_Report_And_Not_Only_Page_Views()
    {
        var harness = IngestHarness.ForSite(measuring: false);

        var outcome = await harness.IngestAsync(
            IngestHarness.PageView(kind: EventKind.Engagement));

        outcome.Should().Be(IngestOutcome.Rejected);
        harness.Stored.Should().BeEmpty();
    }
}
