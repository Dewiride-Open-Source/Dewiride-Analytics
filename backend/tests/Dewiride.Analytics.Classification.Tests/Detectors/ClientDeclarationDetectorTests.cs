using Dewiride.Analytics.Classification.Detectors;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Classification.Tests.Detectors;

/// <summary>
/// What a browser's own account of itself gives away, and where it does not.
/// </summary>
public sealed class ClientDeclarationDetectorTests
{
    private static readonly ClientDeclarationDetector Detector = new();

    /// <summary>What Safari on an iPhone says it is.</summary>
    private const string SafariOnAPhone =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) "
        + "Version/18.0 Mobile/15E148 Safari/604.1";

    /// <summary>What Chrome on an Android tablet says it is: Android, and no mark of a phone.</summary>
    private const string ChromeOnATablet =
        "Mozilla/5.0 (Linux; Android 13; SM-X910) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/148.0.0.0 Safari/537.36";

    /// <summary>
    /// The windows real traffic reported that the rule is stated by, over the long side of a large
    /// desktop screen and more than 2.2 times as tall as wide.
    /// </summary>
    [Theory]
    [InlineData(1_200, 3_000)]
    [InlineData(1_280, 3_000)]
    [InlineData(1_024, 12_140)]
    public void A_Window_Taller_And_Narrower_Than_A_Screen_Is_Reported(int width, int height)
    {
        Reported(Straight(width, height)).Should().NotBeNull();
    }

    /// <summary>
    /// A product that overflows a whole number would turn a very tall window into a small one, or a
    /// very wide window into a narrow one. The collector keeps any size a browser reports.
    /// </summary>
    [Theory]
    [InlineData(1, 500_000_000, true)]
    [InlineData(1, int.MaxValue, true)]
    [InlineData(int.MaxValue, int.MaxValue, false)]
    public void A_Window_No_Screen_Could_Hold_Is_Weighed_Without_Overflowing(int width, int height, bool reported)
    {
        (Reported(Straight(width, height)) is not null).Should().Be(reported);
    }

    /// <summary>A window nothing could be seen in is as tall and narrow as a window gets.</summary>
    [Fact]
    public void A_Window_With_No_Width_At_All_Is_Reported()
    {
        Reported(Straight(0, 3_000)).Should().NotBeNull();
    }

    /// <summary>
    /// The line itself: 2,560 is the long side of a large desktop screen held upright, and it is read
    /// as ordinary however narrow the window.
    /// </summary>
    [Theory]
    [InlineData(1_000, 2_560, false)]
    [InlineData(1, 2_560, false)]
    [InlineData(1_000, 2_561, true)]
    public void A_Window_Is_Measured_Against_The_Long_Side_Of_A_Large_Screen(int width, int height, bool reported)
    {
        (Reported(Straight(width, height)) is not null).Should().Be(reported);
    }

    /// <summary>The shape's own line, on the exact pixel: 2.2 times as tall as wide is not more than that.</summary>
    [Theory]
    [InlineData(1_200, 2_640, false)]
    [InlineData(1_199, 2_640, true)]
    public void A_Window_Is_Measured_Against_A_Shape_Of_Two_Point_Two(int width, int height, bool reported)
    {
        (Reported(Straight(width, height)) is not null).Should().Be(reported);
    }

    /// <summary>
    /// The shape is what tells a screen zoomed out from a window opened to order. Page zoom and a
    /// display's own scaling change what a browser reports without changing its shape, and a 16:9 or
    /// 16:10 screen held upright with the browser filling it stays at or below 1.78 at any zoom.
    /// </summary>
    [Theory]
    [InlineData(1_600, 2_717)]
    [InlineData(1_728, 2_957)]
    [InlineData(2_160, 3_725)]
    public void A_Screen_Held_Upright_And_Zoomed_Out_Is_Not_Reported(int width, int height)
    {
        Reported(Straight(width, height)).Should().BeNull();
    }

    /// <summary>
    /// Anything docked beside the page narrows the window, and a reader on an upright screen is not
    /// taken for a program for opening a side panel: 320 pixels of one, and the most that screen spares.
    /// </summary>
    [Theory]
    [InlineData(1_408, 2_957, false)]
    [InlineData(1_345, 2_957, false)]
    [InlineData(1_344, 2_957, true)]
    public void A_Side_Panel_Open_On_A_Screen_Held_Upright_Is_Not_Reported_Up_To_A_Fifth_Of_Its_Width(
        int width,
        int height,
        bool reported)
    {
        (Reported(Straight(width, height)) is not null).Should().Be(reported);
    }

    /// <summary>Shapes no taller than wide, or nearly so, however many pixels they come to.</summary>
    [Theory]
    [InlineData(3_500, 3_000)]
    [InlineData(2_560, 2_765)]
    [InlineData(2_560, 2_727)]
    public void A_Window_That_Is_Tall_Without_Being_Narrow_Is_Not_Reported(int width, int height)
    {
        Reported(Straight(width, height)).Should().BeNull();
    }

    /// <summary>
    /// What a page too wide for a phone reports is a figure from how the page was laid out: 412 x 800 at
    /// a scale of 0.31, which stops short of the line, and a long narrow phone of 360 x 840, which does not.
    /// </summary>
    [Theory]
    [InlineData(1_320, 2_563)]
    [InlineData(1_161, 2_710)]
    public void A_Phone_Laying_Out_A_Page_Too_Wide_For_It_Is_Not_Reported(int width, int height)
    {
        Reported(Straight(width, height) with { UserAgent = Visits.ChromeOnAPhone }).Should().BeNull();
    }

    /// <summary>
    /// The mark is what leaves a visitor out, wherever it stands in what they wrote and whatever
    /// else they say, in the same window that would otherwise be reported.
    /// </summary>
    [Theory]
    [InlineData(Visits.ChromeOnAPhone)]
    [InlineData(SafariOnAPhone)]
    [InlineData("Mobi")]
    public void A_Visitor_Marked_As_A_Phone_Is_Not_Reported(string userAgent)
    {
        Reported(Straight(1_200, 3_000) with { UserAgent = userAgent }).Should().BeNull();
    }

    /// <summary>
    /// Nothing else is taken for the mark: not another case of it, not naming Android, which a tablet
    /// does with no mark of a phone, and not saying nothing.
    /// </summary>
    [Theory]
    [InlineData("mobile")]
    [InlineData("MOBI")]
    [InlineData(ChromeOnATablet)]
    [InlineData(null)]
    public void Only_The_Mark_Itself_Says_A_Visitor_Is_A_Phone(string? userAgent)
    {
        Reported(Straight(1_200, 3_000) with { UserAgent = userAgent }).Should().NotBeNull();
    }

    /// <summary>
    /// A visit another site sent is judged as it would be whatever size its window was, for every kind
    /// of site that can send one.
    /// </summary>
    [Theory]
    [InlineData(SourceChannel.Search)]
    [InlineData(SourceChannel.Assistant)]
    [InlineData(SourceChannel.Social)]
    [InlineData(SourceChannel.Link)]
    public void A_Visit_Another_Site_Sent_Is_Not_Reported(SourceChannel sentBy)
    {
        Reported(Straight(1_200, 3_000) with { SentBy = sentBy }).Should().BeNull();
    }

    /// <summary>A size nobody reported is no size at all, however the other side reads.</summary>
    [Theory]
    [InlineData(null, 3_000)]
    [InlineData(1_200, null)]
    [InlineData(null, null)]
    public void A_Window_Nobody_Measured_Is_Not_Reported(int? width, int? height)
    {
        var visit = Visits.AGlance() with { ViewportWidth = width, ViewportHeight = height };

        Reported(visit).Should().BeNull();
    }

    /// <summary>What a reader of the verdict is told, and how much it counts for.</summary>
    [Fact]
    public void It_Points_Toward_Automation_At_A_Weight_That_Counts_And_Carries_The_Height()
    {
        var signal = Reported(Straight(1_200, 3_000));

        signal.Should().NotBeNull();
        signal.Direction.Should().Be(SignalDirection.TowardAutomation);
        signal.Weight.Should().Be(55);
        signal.Parameters.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("height", "3000"));
    }

    /// <summary>A window is reported beside what a browser otherwise declares, and neither displaces the other.</summary>
    [Fact]
    public void It_Stands_Beside_What_A_Browser_Otherwise_Declares()
    {
        var found = Detector.Examine(Straight(1_200, 3_000) with { DeclaredWebDriver = true, Language = null });

        found.Select(signal => (signal.Code, signal.Direction, signal.Weight)).Should().BeEquivalentTo(
        [
            (SignalCodes.DeclaredWebDriver, SignalDirection.TowardAutomation, 75),
            (SignalCodes.NoLanguageDeclared, SignalDirection.TowardAutomation, 25),
            (SignalCodes.OversizedWindow, SignalDirection.TowardAutomation, 55),
        ]);
    }

    private static SessionEvidence Straight(int width, int height) =>
        Visits.AGlance() with { ViewportWidth = width, ViewportHeight = height };

    private static Signal? Reported(SessionEvidence visit) =>
        Detector.Examine(visit).SingleOrDefault(signal => signal.Code == SignalCodes.OversizedWindow);
}
