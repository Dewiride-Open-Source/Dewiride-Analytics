using Dewiride.Analytics.Classification.Detectors;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Classification.Tests.Detectors;

/// <summary>
/// What the engine makes of another site having sent a visit.
/// </summary>
public sealed class ArrivalDetectorTests
{
    private readonly ArrivalDetector detector = new();

    [Theory]
    [InlineData(SourceChannel.Search, "search")]
    [InlineData(SourceChannel.Assistant, "assistant")]
    [InlineData(SourceChannel.Social, "social")]
    [InlineData(SourceChannel.Link, "link")]
    public void A_Visit_Another_Site_Sent_Is_Reported_With_The_Kind_Of_Site_It_Was(SourceChannel sentBy, string kind)
    {
        var found = detector.Examine(Visits.AReader() with { SentBy = sentBy });

        var arrival = found.Should().ContainSingle().Subject;

        arrival.Code.Should().Be(SignalCodes.ArrivedFromAnotherSite);
        arrival.Parameters["kind"].Should().Be(kind);
    }

    /// <summary>
    /// Coming straight in is not a finding. Typing an address, opening a bookmark and following a
    /// link from an application all look the same, and none of them is a sign of anything.
    /// </summary>
    [Fact]
    public void A_Visit_That_Came_Straight_In_Is_Not_A_Finding()
    {
        detector.Examine(Visits.AReader()).Should().BeEmpty();
    }

    [Fact]
    public void It_Points_Toward_A_Person_And_Never_Toward_Automation()
    {
        var found = detector.Examine(Visits.AReader() with { SentBy = SourceChannel.Link });

        found.Single().Direction.Should().Be(SignalDirection.TowardHuman);
    }

    /// <summary>
    /// Below 25, the weight at which an observation counts toward a conclusion. A referring address
    /// costs nothing to forge, so it may tip a visit only where everything else has been weighed
    /// already.
    /// </summary>
    [Fact]
    public void It_Is_Too_Light_To_Count_Toward_A_Conclusion_On_Its_Own()
    {
        var arrival = detector.Examine(Visits.AReader() with { SentBy = SourceChannel.Search }).Single();

        arrival.Weight.Should().BeLessThan(25);
    }

    /// <summary>
    /// The kind of site and nothing else. The address it was read from was written by the visitor's
    /// own browser, and the site it names is not the engine's business.
    /// </summary>
    [Fact]
    public void It_Carries_The_Kind_Of_Site_And_Nothing_Else()
    {
        var arrival = detector.Examine(Visits.AReader() with { SentBy = SourceChannel.Social }).Single();

        arrival.Parameters.Keys.Should().Equal("kind");
    }

    /// <summary>
    /// Every kind a visit can be sent by has a spelling, so a kind added to the product cannot reach
    /// a verdict as an empty reason.
    /// </summary>
    [Fact]
    public void Every_Kind_Of_Sending_Site_Has_A_Spelling()
    {
        foreach (var kind in Enum.GetValues<SourceChannel>().Where(kind => kind != SourceChannel.Direct))
        {
            detector.Examine(Visits.AReader() with { SentBy = kind })
                .Should().ContainSingle(because: "{0} is a kind of site that sends visits", kind);
        }
    }
}
