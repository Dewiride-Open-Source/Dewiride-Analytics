using Dewiride.Analytics.Classification.Detectors;

namespace Dewiride.Analytics.Classification.Tests.Detectors;

/// <summary>
/// Proves the detector reports a page whose wait was cut short, and nothing else.
/// </summary>
public sealed class TimingDetectorTests
{
    private static readonly TimingDetector Detector = new();

    [Fact]
    public void A_Wait_Over_Sooner_Than_The_Page_Own_Clocks_Allow_Is_Reported()
    {
        var found = Detector.Examine(Visits.APageWhoseWaitWasCutShort());

        var cutShort = found.Should().ContainSingle().Which;

        cutShort.Code.Should().Be(SignalCodes.WaitCutShort);
        cutShort.Direction.Should().Be(SignalDirection.TowardAutomation);
        cutShort.Parameters.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("seconds", "2"));
    }

    /// <summary>
    /// A person cannot cut a page's wait short, so it outweighs anything a person does — the
    /// heaviest of which is reading a while.
    /// </summary>
    [Fact]
    public void A_Wait_Cut_Short_Outweighs_Anything_A_Person_Does()
    {
        var cutShort = Detector.Examine(Visits.APageWhoseWaitWasCutShort()).Single();
        var heaviestHuman = new EngagementDetector()
            .Examine(Visits.AReader(engagedMs: 3_600_000) with { HadKeyboardInteraction = true })
            .Max(signal => signal.Weight);

        cutShort.Weight.Should().BeGreaterThan(heaviestHuman);
    }

    /// <summary>
    /// The tracker waits fifteen seconds. A report at the end of that wait, or a moment short of it
    /// because the clock it was read from is coarse, says nothing.
    /// </summary>
    [Theory]
    [InlineData(15_000)]
    [InlineData(14_000)]
    [InlineData(60_000)]
    public void A_Wait_That_Took_Its_Time_Is_Not_Reported(int soonestMs)
    {
        var session = Visits.AReader() with { SoonestProgressMs = soonestMs };

        Detector.Examine(session).Should().BeEmpty();
    }

    [Fact]
    public void A_Wait_Over_More_Than_A_Second_Early_Is_Reported()
    {
        var session = Visits.AReader() with { SoonestProgressMs = 13_999 };

        Detector.Examine(session).Should().ContainSingle()
            .Which.Parameters["seconds"].Should().Be("14");
    }

    /// <summary>
    /// A sentence saying a wait was over within no time at all would claim a precision the clock
    /// never had.
    /// </summary>
    [Fact]
    public void The_Seconds_A_Wait_Was_Over_Within_Are_Never_Nought()
    {
        var session = Visits.AReader() with { SoonestProgressMs = 0 };

        Detector.Examine(session).Single().Parameters["seconds"].Should().Be("1");
    }

    [Fact]
    public void Nothing_Is_Said_Where_No_Page_Reported_How_It_Was_Going()
    {
        Detector.Examine(Visits.AReader() with { SoonestProgressMs = null }).Should().BeEmpty();
        Detector.Examine(Visits.ANamedCrawler("curl/8.9.1")).Should().BeEmpty();
    }
}
