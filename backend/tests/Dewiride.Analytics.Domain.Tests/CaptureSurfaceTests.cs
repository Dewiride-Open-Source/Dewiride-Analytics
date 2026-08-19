using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Domain.Tests;

/// <summary>
/// Holds the line between the two halves of the measurement.
/// </summary>
/// <remarks>
/// Which side a surface falls on decides how much traffic a customer is told they had: the halves
/// both see every page a person reads, so one of the two accounts is a second sighting rather than
/// a second visit. A surface filed on the wrong side is not a tidiness problem — it doubles a
/// number on the front page of the product, or halves it.
/// </remarks>
public sealed class CaptureSurfaceTests
{
    [Theory]
    [InlineData(IngestSurface.BrowserTracker)]
    [InlineData(IngestSurface.NoScriptPixel)]
    public void The_Visitor_Own_Software_Reports_From_Their_Browser(IngestSurface surface)
    {
        IngestSurfaces.RunsInVisitorBrowser(surface).Should().BeTrue();
    }

    [Theory]
    [InlineData(IngestSurface.CloudflareWorker)]
    [InlineData(IngestSurface.WordPressPlugin)]
    [InlineData(IngestSurface.NetlifyEdge)]
    [InlineData(IngestSurface.VercelEdge)]
    [InlineData(IngestSurface.AspNetCoreMiddleware)]
    [InlineData(IngestSurface.NextJsMiddleware)]
    [InlineData(IngestSurface.LogImport)]
    [InlineData(IngestSurface.ServerSide)]
    public void Everything_Watching_From_The_Request_Path_Does_Not(IngestSurface surface)
    {
        IngestSurfaces.RunsInVisitorBrowser(surface).Should().BeFalse();
    }

    /// <summary>
    /// A surface with no established provenance is not a claim that a browser was there.
    /// </summary>
    [Fact]
    public void An_Unattributed_Report_Is_Not_Treated_As_Coming_From_A_Browser()
    {
        IngestSurfaces.RunsInVisitorBrowser(IngestSurface.Unknown).Should().BeFalse();
    }

    /// <summary>
    /// Adding a surface without deciding which half it belongs to would silently file it with the
    /// request path, so this fails until somebody has said.
    /// </summary>
    [Fact]
    public void Every_Surface_Has_Been_Placed_On_One_Side_Or_The_Other()
    {
        var placed = Enum.GetValues<IngestSurface>()
            .Count(surface => IngestSurfaces.RunsInVisitorBrowser(surface));

        placed.Should().Be(2);
    }
}
