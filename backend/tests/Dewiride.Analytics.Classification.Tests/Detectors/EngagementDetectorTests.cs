using Dewiride.Analytics.Classification.Detectors;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;

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

    /// <summary>
    /// A program can press as easily as anybody, so on a visit that came straight in a press is on
    /// the record, pointing nowhere and weighing nothing.
    /// </summary>
    [Fact]
    public void A_Press_On_A_Visit_That_Came_Straight_In_Counts_For_Nothing()
    {
        var found = Detector.Examine(Visits.AReader() with { HadPointerInteraction = true, HadKeyboardInteraction = true });

        var presses = found.Where(signal => signal.Code is SignalCodes.PointerUsed or SignalCodes.KeyboardUsed).ToArray();

        presses.Should().HaveCount(2);
        presses.Should().OnlyContain(signal => signal.Direction == SignalDirection.Neutral);
        presses.Should().OnlyContain(signal => signal.Weight == 0);
    }

    /// <summary>A press on a visit another site sent counts toward a person: 55 for the pointer, 50 for a key.</summary>
    [Fact]
    public void A_Press_On_A_Visit_Another_Site_Sent_Counts_Toward_A_Person()
    {
        var found = Detector.Examine(Visits.AReader() with
        {
            SentBy = SourceChannel.Search,
            HadPointerInteraction = true,
            HadKeyboardInteraction = true,
        });

        var pointer = found.Single(signal => signal.Code == SignalCodes.PointerUsed);
        var key = found.Single(signal => signal.Code == SignalCodes.KeyboardUsed);

        pointer.Direction.Should().Be(SignalDirection.TowardHuman);
        pointer.Weight.Should().Be(55);
        key.Direction.Should().Be(SignalDirection.TowardHuman);
        key.Weight.Should().Be(50);
    }

    /// <summary>
    /// How far down counts the first screen, so on a visit that came straight in it is too light to
    /// count until the pages have stayed. Where another site sent the visit it counts at 45, however
    /// long the page stayed.
    /// </summary>
    [Theory]
    [InlineData(SourceChannel.Direct, 0, 20)]
    [InlineData(SourceChannel.Direct, 14_999, 20)]
    [InlineData(SourceChannel.Direct, 15_000, 45)]
    [InlineData(SourceChannel.Search, 0, 45)]
    [InlineData(SourceChannel.Assistant, 0, 45)]
    [InlineData(SourceChannel.Social, 0, 45)]
    [InlineData(SourceChannel.Link, 0, 45)]
    public void How_Far_Down_A_Page_Was_Seen_Counts_By_Whether_Anything_Sent_The_Visit_Or_It_Stayed(
        SourceChannel sentBy,
        int engagedMs,
        int weight)
    {
        var visit = Visits.AGlance() with { SentBy = sentBy, EngagedMs = engagedMs, MaxScrollDepthPercent = 60 };

        var seen = Detector.Examine(visit).Single(signal => signal.Code == SignalCodes.Scrolled);

        seen.Weight.Should().Be(weight);
        seen.Direction.Should().Be(SignalDirection.TowardHuman);
        seen.Parameters["percent"].Should().Be("60");
    }

    /// <summary>
    /// Time a page was left open is not a stay, however long: it is the time said as the pages were
    /// left that decides.
    /// </summary>
    [Theory]
    [InlineData(60_000, 45_001, 20)]
    [InlineData(60_000, 45_000, 45)]
    public void Time_Left_Open_Is_Not_A_Stay(int engagedMs, int leftOpenMs, int weight)
    {
        var visit = Visits.AGlance() with { EngagedMs = engagedMs, LeftOpenMs = leftOpenMs, MaxScrollDepthPercent = 60 };

        Weight(visit, SignalCodes.Scrolled).Should().Be(weight);
    }

    /// <summary>
    /// A moment on a page counts only where another site sent the visit there, whatever else it did:
    /// a press does not stand in for a stay.
    /// </summary>
    [Theory]
    [InlineData(SourceChannel.Search, 2_000, 40)]
    [InlineData(SourceChannel.Assistant, 2_000, 40)]
    [InlineData(SourceChannel.Social, 2_000, 40)]
    [InlineData(SourceChannel.Link, 2_000, 40)]
    [InlineData(SourceChannel.Link, 14_999, 40)]
    [InlineData(SourceChannel.Link, 15_000, 60)]
    [InlineData(SourceChannel.Direct, 2_000, 20)]
    [InlineData(SourceChannel.Direct, 14_999, 20)]
    [InlineData(SourceChannel.Direct, 15_000, 60)]
    public void A_Short_Stay_Counts_Only_Where_Another_Site_Sent_The_Visit(SourceChannel sentBy, int engagedMs, int weight)
    {
        var visit = Visits.AGlance() with
        {
            SentBy = sentBy,
            EngagedMs = engagedMs,
            HadPointerInteraction = true,
            HadKeyboardInteraction = true,
        };

        Weight(visit, SignalCodes.ReadTime).Should().Be(weight);
    }

    /// <summary>
    /// A reader of the verdict shown what looks like somebody reading is owed the reason it did not
    /// settle the question. It is said only where that is what happened, of a visit that is over,
    /// and it does no work: it points nowhere and weighs nothing.
    /// </summary>
    [Theory]
    [InlineData(SourceChannel.Direct, 3_000, 0, 0, false, false, true)]
    [InlineData(SourceChannel.Direct, 2_000, 0, 0, false, false, true)]
    [InlineData(SourceChannel.Direct, 14_999, 0, 0, false, false, true)]
    [InlineData(SourceChannel.Direct, 483, 0, 34, false, false, true)]
    [InlineData(SourceChannel.Direct, 483, 0, 25, false, false, true)]
    [InlineData(SourceChannel.Direct, 483, 0, 0, true, false, true)]
    [InlineData(SourceChannel.Direct, 483, 0, 0, false, true, true)]
    [InlineData(SourceChannel.Direct, 16_000, 1_999, 0, false, false, true)]
    [InlineData(SourceChannel.Direct, 15_500, 1_000, 0, false, false, true)]
    [InlineData(SourceChannel.Direct, 8_000, 3_000, 40, false, false, true)]
    [InlineData(SourceChannel.Direct, 14_999, 2_000, 0, false, false, true)]
    [InlineData(SourceChannel.Direct, 0, 0, 0, false, false, false)]
    [InlineData(SourceChannel.Direct, 1_999, 0, 24, false, false, false)]
    [InlineData(SourceChannel.Direct, 3_000, 1_500, 0, false, false, false)]
    [InlineData(SourceChannel.Direct, 15_000, 0, 60, true, false, false)]
    [InlineData(SourceChannel.Direct, 15_000, 2_000, 60, false, false, false)]
    [InlineData(SourceChannel.Direct, 3_000, null, 34, true, false, false)]
    [InlineData(SourceChannel.Search, 3_000, 0, 60, true, false, false)]
    [InlineData(SourceChannel.Search, 483, 0, 34, false, false, false)]
    public void A_Brief_Stay_Says_Why_It_Did_Not_Count(
        SourceChannel sentBy,
        int engagedMs,
        int? leftOpenMs,
        int depth,
        bool pressed,
        bool typed,
        bool said)
    {
        var visit = Visits.AGlance() with
        {
            SentBy = sentBy,
            EngagedMs = engagedMs,
            LeftOpenMs = leftOpenMs,
            MaxScrollDepthPercent = (byte)depth,
            HadPointerInteraction = pressed,
            HadKeyboardInteraction = typed,
        };

        var found = Detector.Examine(visit).Where(signal => signal.Code == SignalCodes.BriefStay).ToArray();

        found.Should().HaveCount(said ? 1 : 0);
        found.Should().OnlyContain(signal => signal.Direction == SignalDirection.Neutral && signal.Weight == 0);
    }

    /// <summary>
    /// Where two seconds or more were left open and would have made the fifteen seconds, the sentence
    /// about that time gives the reason, so a brief stay is not said beside it.
    /// </summary>
    [Fact]
    public void A_Brief_Stay_Is_Not_Said_Where_Time_Left_Open_Already_Gives_The_Reason()
    {
        var visit = Visits.AGlance() with { EngagedMs = 45_000, LeftOpenMs = 45_000, MaxScrollDepthPercent = 60 };

        var found = Detector.Examine(visit);

        found.Should().Contain(signal => signal.Code == SignalCodes.LeftOpen);
        found.Should().NotContain(signal => signal.Code == SignalCodes.BriefStay);
    }

    /// <summary>
    /// "Nothing was touched" cannot be said of a visit on which something was, whatever a press counts
    /// for: it can keep a visit from being called silent.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_Press_Stops_A_Visit_Being_Called_Silent(bool touched, bool typed)
    {
        Detector.Examine(Untouched() with { HadPointerInteraction = touched, HadKeyboardInteraction = typed })
            .Should().NotContain(signal => signal.Code == SignalCodes.NoEngagement);
    }

    /// <summary>The same goes for a page seen a quarter of the way down, even by being shown.</summary>
    [Fact]
    public void A_Page_Merely_Shown_Stops_A_Visit_Being_Called_Silent()
    {
        Detector.Examine(Untouched() with { MaxScrollDepthPercent = 34 })
            .Should().NotContain(signal => signal.Code == SignalCodes.NoEngagement);
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
