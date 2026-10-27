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
    /// Fifteen seconds is where a reading counts in full; a moment short of it is still a short read.
    /// </summary>
    [Fact]
    public void A_Reading_Counts_In_Full_From_Fifteen_Seconds_And_Not_Before()
    {
        var almost = Weight(Visits.AReader(engagedMs: 14_999), SignalCodes.ReadTime);
        var reached = Weight(Visits.AReader(engagedMs: 15_000), SignalCodes.ReadTime);
        var settled = Weight(Visits.AReader(engagedMs: 60_000), SignalCodes.ReadTime);

        almost.Should().BeLessThan(reached);
        reached.Should().Be(settled);
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

    /// <summary>
    /// A page somebody let go of says so as it goes. What a page reported after it last said so is
    /// a browser keeping the page open, and the reading is what came before.
    /// </summary>
    [Fact]
    public void Time_A_Page_Was_Left_Open_Is_Not_Counted_As_Reading()
    {
        var found = Detector.Examine(Visits.AReader(engagedMs: 45_000) with { LeftOpenMs = 15_000 });

        found.Single(signal => signal.Code == SignalCodes.ReadTime).Parameters["seconds"].Should().Be("30");
    }

    [Fact]
    public void A_Visit_Whose_Every_Second_Was_Left_Open_Has_No_Reading()
    {
        var found = Detector.Examine(Visits.APageLeftOpen());

        found.Should().NotContain(signal => signal.Code == SignalCodes.ReadTime);
    }

    /// <summary>
    /// The seconds a reader is shown for the visit are never dropped in silence: the verdict says
    /// they were a page left open, and says it too lightly for it to count against anybody.
    /// </summary>
    [Fact]
    public void A_Page_Left_Open_Is_Said_So_Pointing_Away_From_A_Person_Too_Lightly_To_Count()
    {
        var leftOpen = Detector.Examine(Visits.APageLeftOpen()).Single(signal => signal.Code == SignalCodes.LeftOpen);

        leftOpen.Direction.Should().Be(SignalDirection.TowardAutomation);
        leftOpen.Weight.Should().BeLessThan(25);
        leftOpen.Parameters.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("seconds", "15"));
    }

    /// <summary>
    /// The seconds said to have been left open are those, not the visit's whole time, and the
    /// reading beside them is what remains.
    /// </summary>
    [Fact]
    public void Time_Left_Open_Is_Said_Beside_The_Reading_That_Remains()
    {
        var found = Detector.Examine(Visits.AReader(engagedMs: 45_000) with { LeftOpenMs = 15_000 });

        found.Single(signal => signal.Code == SignalCodes.LeftOpen).Parameters["seconds"].Should().Be("15");
        found.Single(signal => signal.Code == SignalCodes.ReadTime).Parameters["seconds"].Should().Be("30");
    }

    /// <summary>
    /// A moment left open is not worth a sentence, on the same terms as a moment read.
    /// </summary>
    [Theory]
    [InlineData(1_999, false)]
    [InlineData(2_000, true)]
    public void Time_Left_Open_Is_Said_From_Two_Seconds(int leftOpenMs, bool said)
    {
        var found = Detector.Examine(Visits.AReader(engagedMs: 45_000) with { LeftOpenMs = leftOpenMs });

        found.Any(signal => signal.Code == SignalCodes.LeftOpen).Should().Be(said);
    }

    /// <summary>
    /// Whether a reading counts in full is decided on the time said as the pages were left, not on
    /// the time they were open.
    /// </summary>
    [Fact]
    public void A_Reading_Counts_In_Full_From_Fifteen_Seconds_Said_As_The_Pages_Were_Left()
    {
        var almost = Weight(Visits.AReader(engagedMs: 30_000) with { LeftOpenMs = 15_001 }, SignalCodes.ReadTime);
        var reached = Weight(Visits.AReader(engagedMs: 30_000) with { LeftOpenMs = 15_000 }, SignalCodes.ReadTime);

        almost.Should().BeLessThan(reached);
    }

    [Fact]
    public void A_Reading_Whose_Pages_Were_All_Let_Go_Of_Is_Counted_Whole_And_Nothing_Is_Said_Of_Pages_Left_Open()
    {
        var found = Detector.Examine(Visits.AReader(engagedMs: 45_000) with { LeftOpenMs = 0 });

        found.Single(signal => signal.Code == SignalCodes.ReadTime).Parameters["seconds"].Should().Be("45");
        found.Should().NotContain(signal => signal.Code == SignalCodes.LeftOpen);
    }

    /// <summary>
    /// Somebody still on the site has pages open because they are still there. Where nothing could
    /// say how much was left open, all of the time is reading.
    /// </summary>
    [Fact]
    public void Where_Nothing_Could_Say_What_Was_Left_Open_All_Of_The_Time_Is_Reading()
    {
        var found = Detector.Examine(Visits.AReader(engagedMs: 45_000) with { LeftOpenMs = null });

        found.Single(signal => signal.Code == SignalCodes.ReadTime).Parameters["seconds"].Should().Be("45");
        found.Should().NotContain(signal => signal.Code == SignalCodes.LeftOpen);
    }

    /// <summary>
    /// A page kept open in front of a browser is something happening, even where it is not somebody
    /// reading. Called silence, a reader whose laptop was shut on two pages would be a visit nobody
    /// was ever at.
    /// </summary>
    [Fact]
    public void Pages_Left_Open_Are_Not_Called_Silence()
    {
        var twoPagesLeftOpen = Untouched() with { EngagedMs = 30_000, LeftOpenMs = 30_000 };

        Detector.Examine(twoPagesLeftOpen)
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
