using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Ingest;

/// <summary>
/// Covers what is kept of a control a visitor operated.
/// </summary>
public sealed class OperatedControlTests
{
    /// <summary>Longest control name kept, matching the limit inside the ingestor.</summary>
    private const int LongestLabel = 64;

    [Fact]
    public async Task Records_What_Was_Operated_And_What_It_Said()
    {
        var harness = IngestHarness.ForSite();

        await harness.IngestAsync(Press() with { ActionLabel = "Subscribe" });

        harness.Single.Kind.Should().Be(EventKind.Action);
        harness.Single.ActionControl.Should().Be(ControlKind.Button);
        harness.Single.ActionLabel.Should().Be("Subscribe");
    }

    [Fact]
    public async Task Records_Where_A_Control_Pointed()
    {
        var harness = IngestHarness.ForSite();
        var command = Press() with
        {
            ActionControl = ControlKind.Link,
            ActionLabel = "Read the source",
            ActionTarget = "github.com",
            ActionTargetKind = TargetKind.External,
        };

        await harness.IngestAsync(command);

        harness.Single.ActionTarget.Should().Be("github.com");
        harness.Single.ActionTargetKind.Should().Be(TargetKind.External);
    }

    /// <summary>
    /// The page a control sits on is where the press happened, and is taken from the report's own
    /// address like every other kind of report.
    /// </summary>
    [Fact]
    public async Task Records_The_Page_The_Press_Happened_On()
    {
        var harness = IngestHarness.ForSite();

        await harness.IngestAsync(Press("https://example.com/pricing"));

        harness.Single.Path.Should().Be("/pricing");
    }

    /// <summary>
    /// The tracker cuts a name down before sending it, but the tracker runs on somebody else's
    /// page and nothing it sends is a promise.
    /// </summary>
    [Fact]
    public async Task Cuts_A_Name_Longer_Than_Any_Control_Would_Be_Given()
    {
        var harness = IngestHarness.ForSite();

        await harness.IngestAsync(Press() with { ActionLabel = new string('a', 5000) });

        harness.Single.ActionLabel.Should().HaveLength(LongestLabel);
    }

    /// <summary>
    /// A caller filling in fields that do not belong to what it says it is reporting is claiming
    /// something it cannot have observed, and the claim is dropped rather than stored beside a
    /// page view nobody would expect to carry it.
    /// </summary>
    [Theory]
    [InlineData(EventKind.PageView)]
    [InlineData(EventKind.Engagement)]
    [InlineData(EventKind.Exit)]
    public async Task Ignores_A_Control_Claimed_By_Anything_That_Is_Not_A_Press(EventKind kind)
    {
        var harness = IngestHarness.ForSite();
        var command = Press() with
        {
            Kind = kind,
            ActionLabel = "Subscribe",
            ActionTarget = "github.com",
            ActionTargetKind = TargetKind.External,
        };

        await harness.IngestAsync(command);

        harness.Single.ActionControl.Should().Be(ControlKind.Unknown);
        harness.Single.ActionLabel.Should().BeNull();
        harness.Single.ActionTarget.Should().BeNull();
        harness.Single.ActionTargetKind.Should().Be(TargetKind.None);
    }

    /// <summary>
    /// A setting that governs what is kept has to govern what is written. Storing the press and
    /// leaving it out of every later question would collect exactly what the site asked not to
    /// have collected.
    /// </summary>
    [Fact]
    public async Task Writes_Nothing_At_All_For_A_Site_That_Records_No_Presses()
    {
        var harness = IngestHarness.ForSite(captureClicks: false);

        var outcome = await harness.IngestAsync(Press());

        outcome.Should().Be(IngestOutcome.Rejected);
        harness.Stored.Should().BeEmpty();
    }

    /// <summary>
    /// The switch governs presses and nothing else. A site that has turned them off is still
    /// measured in every other way.
    /// </summary>
    [Fact]
    public async Task Still_Records_Everything_Else_For_A_Site_That_Records_No_Presses()
    {
        var harness = IngestHarness.ForSite(captureClicks: false);

        var outcome = await harness.IngestAsync(IngestHarness.PageView());

        outcome.Should().Be(IngestOutcome.Accepted);
        harness.Single.Kind.Should().Be(EventKind.PageView);
    }

    /// <summary>
    /// A control this product does not recognise still had somebody press it, and a press left out
    /// would report a quieter page than the one people used.
    /// </summary>
    [Fact]
    public async Task Records_A_Press_On_Something_It_Cannot_Name()
    {
        var harness = IngestHarness.ForSite();

        await harness.IngestAsync(Press() with { ActionControl = ControlKind.Unknown, ActionLabel = null });

        harness.Single.Kind.Should().Be(EventKind.Action);
        harness.Single.ActionControl.Should().Be(ControlKind.Unknown);
        harness.Single.ActionLabel.Should().BeNull();
    }

    private static IngestCommand Press(string url = "https://example.com/posts/hello") =>
        IngestHarness.PageView(url, EventKind.Action) with { ActionControl = ControlKind.Button };
}
