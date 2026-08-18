using Dewiride.Analytics.Classification.Detectors;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Tests.Detectors;

/// <summary>
/// Proves the detector reports what somebody did, and stays silent about what nobody watched.
/// </summary>
public sealed class EngagementDetectorTests
{
    private static readonly EngagementDetector Detector = new();

    [Fact]
    public void Time_Spent_Reading_Is_Reported_In_Whole_Seconds()
    {
        var found = Detector.Examine(Visits.AReader(engagedMs: 45_400));

        var reading = found.Single(signal => signal.Code == SignalCodes.ReadTime);

        reading.Direction.Should().Be(SignalDirection.TowardHuman);
        reading.Parameters["seconds"].Should().Be("45");
    }

    /// <summary>
    /// A page that flashed past was not read. The threshold is what stops a redirect chain from
    /// looking like attention.
    /// </summary>
    [Fact]
    public void A_Glance_Too_Brief_To_Have_Been_Reading_Is_Not_Reported_As_Reading()
    {
        var found = Detector.Examine(Visits.AReader(engagedMs: 900));

        found.Should().NotContain(signal => signal.Code == SignalCodes.ReadTime);
    }

    [Fact]
    public void A_Longer_Read_Counts_For_More_Than_A_Short_One()
    {
        var brief = Weight(Visits.AReader(engagedMs: 3_000), SignalCodes.ReadTime);
        var settled = Weight(Visits.AReader(engagedMs: 60_000), SignalCodes.ReadTime);

        settled.Should().BeGreaterThan(brief);
    }

    /// <summary>
    /// Presence, never content. Knowing somebody typed is useful; knowing what they typed is
    /// surveillance this product does not do, and there is nowhere here to put it.
    /// </summary>
    [Fact]
    public void Touching_And_Typing_Are_Reported_As_Having_Happened_And_Nothing_More()
    {
        var busy = Visits.AReader() with { HadPointerInteraction = true, HadKeyboardInteraction = true };

        var found = Detector.Examine(busy);

        found.Should().Contain(signal => signal.Code == SignalCodes.PointerUsed);
        found.Should().Contain(signal => signal.Code == SignalCodes.KeyboardUsed);
        found.Where(signal => signal.Code is SignalCodes.PointerUsed or SignalCodes.KeyboardUsed)
            .Should().OnlyContain(signal => signal.Parameters.Count == 0);
    }

    [Fact]
    public void A_Watched_Session_In_Which_Nothing_Happened_Is_Reported_As_Such()
    {
        var found = Detector.Examine(Untouched());

        found.Should().Contain(signal => signal.Code == SignalCodes.NoEngagement);
    }

    /// <summary>
    /// One reading nobody took is enough to make the silence meaningless. The thing that would
    /// have spoken was never listened for.
    /// </summary>
    [Fact]
    public void Silence_Means_Nothing_When_Any_Part_Of_It_Went_Unwatched()
    {
        var partlyWatched = Untouched() with { HadKeyboardInteraction = null };

        Detector.Examine(partlyWatched)
            .Should().NotContain(signal => signal.Code == SignalCodes.NoEngagement);
    }

    /// <summary>
    /// Somebody who opens one page, reads the answer and leaves has done nothing suspicious.
    /// Reporting that as a silent session would make the commonest honest visit look automated.
    /// </summary>
    [Fact]
    public void A_Single_Page_Nobody_Touched_Is_Not_Held_Against_It()
    {
        var onePage = Untouched() with { Requests = Visits.Pages(1, TimeSpan.Zero) };

        Detector.Examine(onePage)
            .Should().NotContain(signal => signal.Code == SignalCodes.NoEngagement);
    }

    private static SessionEvidence Untouched() =>
        Visits.AReader() with
        {
            EngagedMs = 200,
            MaxScrollDepthPercent = 0,
            HadPointerInteraction = false,
            HadKeyboardInteraction = false,
        };

    private static int Weight(SessionEvidence session, string code) =>
        Detector.Examine(session).Single(signal => signal.Code == code).Weight;
}
