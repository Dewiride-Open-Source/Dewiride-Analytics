using Dewiride.Analytics.Application.Ingest;

namespace Dewiride.Analytics.Application.Tests.Ingest;

/// <summary>
/// Covers what the collector refuses to store.
/// </summary>
/// <remarks>
/// The collector is a public endpoint with no authentication, so this is the trust boundary and
/// everything crossing it is written by someone who may prefer not to be counted honestly. The
/// line these tests draw is deliberate: impossible readings are refused, and merely surprising
/// ones are kept, because a report that does not add up is evidence about whatever produced it.
/// </remarks>
public sealed class EventIngestorValidationTests
{
    [Theory]
    [InlineData("/posts/hello")]
    [InlineData("example.com/posts/hello")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("data:text/html,<script>")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejects_A_Url_That_Is_Not_An_Absolute_Web_Address(string url)
    {
        var harness = IngestHarness.ForSite();

        var outcome = await harness.IngestAsync(IngestHarness.PageView(url));

        outcome.Should().Be(IngestOutcome.Invalid);
        harness.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_A_Url_Longer_Than_The_Limit()
    {
        var harness = IngestHarness.ForSite();
        var url = "https://example.com/" + new string('a', 2048);

        var outcome = await harness.IngestAsync(IngestHarness.PageView(url));

        outcome.Should().Be(IngestOutcome.Invalid);
    }

    [Fact]
    public async Task Accepts_A_Url_At_The_Limit()
    {
        var harness = IngestHarness.ForSite();
        const string prefix = "https://example.com/";
        var url = prefix + new string('a', 2048 - prefix.Length);

        var outcome = await harness.IngestAsync(IngestHarness.PageView(url));

        outcome.Should().Be(IngestOutcome.Accepted);
    }

    [Fact]
    public async Task Rejects_A_Negative_Viewport()
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with { ViewportWidth = -1, ViewportHeight = 900 };

        var outcome = await harness.IngestAsync(command);

        outcome.Should().Be(IngestOutcome.Invalid);
    }

    [Fact]
    public async Task Rejects_Negative_Engaged_Time()
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with { EngagedMs = -1 };

        var outcome = await harness.IngestAsync(command);

        outcome.Should().Be(IngestOutcome.Invalid);
    }

    [Fact]
    public async Task Rejects_A_Scroll_Depth_Past_The_Bottom_Of_The_Page()
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with { ScrollDepthPercent = 150 };

        var outcome = await harness.IngestAsync(command);

        outcome.Should().Be(IngestOutcome.Invalid);
    }

    [Fact]
    public async Task Accepts_A_Scroll_Depth_Of_Exactly_The_Whole_Page()
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with { ScrollDepthPercent = 100 };

        var outcome = await harness.IngestAsync(command);

        outcome.Should().Be(IngestOutcome.Accepted);
    }

    /// <summary>
    /// A viewport nothing could render and a page held open for a fortnight are kept on purpose.
    /// Discarding them here would hide exactly the reports this product exists to notice.
    /// </summary>
    [Fact]
    public async Task Keeps_Readings_That_Are_Implausible_Rather_Than_Impossible()
    {
        var harness = IngestHarness.ForSite();
        var command = IngestHarness.PageView() with
        {
            ViewportWidth = 999_999,
            ViewportHeight = 999_999,
            EngagedMs = 1_209_600_000,
        };

        var outcome = await harness.IngestAsync(command);

        outcome.Should().Be(IngestOutcome.Accepted);
        harness.Single.ViewportWidth.Should().Be(999_999);
        harness.Single.EngagedMs.Should().Be(1_209_600_000);
    }

    [Fact]
    public async Task Rejects_A_Report_For_A_Site_That_Does_Not_Exist()
    {
        var harness = IngestHarness.WithNoSuchSite();

        var outcome = await harness.IngestAsync(IngestHarness.PageView());

        outcome.Should().Be(IngestOutcome.Rejected);
        harness.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_A_Null_Report()
    {
        var harness = IngestHarness.ForSite();

        var act = async () => await harness.Ingestor.IngestAsync(
            null!,
            IngestHarness.BrowserRequest(),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Refuses_A_Null_Observation()
    {
        var harness = IngestHarness.ForSite();

        var act = async () => await harness.Ingestor.IngestAsync(
            IngestHarness.PageView(),
            null!,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
