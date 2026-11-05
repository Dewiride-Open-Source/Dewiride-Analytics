using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Classification.Tests;

/// <summary>
/// The promises this product makes, stated as tests.
/// </summary>
/// <remarks>
/// These are not tests of the arithmetic. Each one is a claim the product makes to a customer
/// about what it will and will not say, and the engine is only worth shipping while every one of
/// them holds.
/// </remarks>
public sealed class TrafficClassifierTests
{
    private static readonly TrafficClassifier Engine = TrafficClassifier.Current();

    [Fact]
    public void Somebody_Reading_A_Site_Is_Not_Called_Automation()
    {
        var verdict = Engine.Classify(Visits.AReader());

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().BeOneOf(EvidenceStrength.Moderate, EvidenceStrength.Strong);
    }

    /// <summary>
    /// Somebody who came straight in, ran the tracker, stayed for most of a minute and saw most of
    /// the way down a page has three things agreeing, and is stated firmly. The press beside them
    /// is not one of the three.
    /// </summary>
    [Fact]
    public void Somebody_Reading_Well_Down_A_Page_Is_Stated_Firmly()
    {
        var verdict = Engine.Classify(Visits.AReader());

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Strong);
        verdict.Supporting.Single(signal => signal.Code == SignalCodes.PointerUsed)
            .Direction.Should().Be(SignalDirection.Neutral);
    }

    /// <summary>
    /// The visit this product was flattering in the field: one page, the tracker ran, and nothing
    /// else was seen. A browser under a script does exactly this, so the honest answer is that
    /// nothing was settled — never a person on slight signs.
    /// </summary>
    [Fact]
    public void One_Page_Whose_Browser_Ran_The_Tracker_And_Nothing_Else_Is_Answered_As_Could_Not_Be_Told()
    {
        var verdict = Engine.Classify(Visits.AGlance());

        verdict.Category.Should().Be(TrafficCategory.Unknown);
        verdict.Strength.Should().Be(EvidenceStrength.Weak);
        verdict.Supporting.Select(signal => signal.Code).Should().Equal(SignalCodes.ScriptExecuted);
    }

    /// <summary>
    /// How far down a page was seen counts the first screen, so on a visit that came straight in a
    /// page merely shown says nothing about anybody.
    /// </summary>
    [Fact]
    public void A_Page_Seen_A_Quarter_Of_The_Way_Down_That_Came_Straight_In_And_Did_Not_Stay_Is_Not_A_Person()
    {
        var verdict = Engine.Classify(Visits.AGlance() with { MaxScrollDepthPercent = 60 });

        verdict.Category.Should().Be(TrafficCategory.Unknown);
        verdict.Strength.Should().Be(EvidenceStrength.Weak);
        verdict.Supporting.Single(signal => signal.Code == SignalCodes.Scrolled).Weight.Should().BeLessThan(25);
    }

    /// <summary>
    /// One page opened straight and closed within half a second in an ordinary window, a third of it
    /// counted as seen. The reader is told why it did not count.
    /// </summary>
    [Fact]
    public void A_Page_Shown_And_Closed_At_Once_Is_Not_A_Person()
    {
        var verdict = Engine.Classify(Visits.APageShownAndClosedAtOnce());

        verdict.Category.Should().Be(TrafficCategory.Unknown);
        verdict.Strength.Should().Be(EvidenceStrength.Weak);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.BriefStay);
    }

    /// <summary>
    /// The reason a short stay did not settle the question belongs beside an answer it did not
    /// settle: the same page over a rented network is decided by the network, whatever its stay.
    /// </summary>
    [Fact]
    public void The_Reason_A_Short_Stay_Did_Not_Count_Is_Not_Given_Where_Something_Else_Settled_It()
    {
        var verdict = Engine.Classify(Visits.APageShownAndClosedAtOnce() with
        {
            AutonomousSystem = Visits.MicrosoftNetwork,
        });

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
        verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.BriefStay);
        verdict.Contradicting.Should().NotContain(signal => signal.Code == SignalCodes.BriefStay);
    }

    /// <summary>
    /// The fleet measured on real traffic, in the window that made its pages look seen: one page
    /// opened straight in Chrome on Linux, 3000 pixels tall, closed within half a second. Nothing about
    /// the visit weighs as much as a window taller and narrower than an ordinary screen gives.
    /// </summary>
    [Fact]
    public void A_Window_Taller_Than_A_Screen_Is_Something_Automated()
    {
        var verdict = Engine.Classify(Visits.APageInAWindowThreeThousandTall());

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
        verdict.Supporting.Single(signal => signal.Code == SignalCodes.OversizedWindow)
            .Parameters["height"].Should().Be("3000");
        verdict.Contradicting.Select(signal => signal.Code)
            .Should().BeEquivalentTo(SignalCodes.ScriptExecuted, SignalCodes.Scrolled);
    }

    /// <summary>
    /// A visit opens on its page view, and one whose report of being left reached the collector ahead
    /// of that page view has only the page view: no time and no depth. The window is still known.
    /// </summary>
    [Fact]
    public void A_Window_Taller_Than_A_Screen_Is_Something_Automated_Where_Nothing_Else_Was_Measured()
    {
        var verdict = Engine.Classify(Visits.APageInAWindowThreeThousandTall() with
        {
            EngagedMs = null,
            LeftOpenMs = null,
            MaxScrollDepthPercent = null,
            HadPointerInteraction = null,
            HadKeyboardInteraction = null,
        });

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
    }

    /// <summary>
    /// A browser really was on screen for fifteen seconds and let the page go, as somebody reading
    /// does, and the size of a window is not worth more than that: a person, with the window shown
    /// against it and holding it to some signs.
    /// </summary>
    [Theory]
    [InlineData(15_000, 34)]
    [InlineData(15_000, 0)]
    public void Fifteen_Seconds_Outweigh_The_Window(int engagedMs, int depth)
    {
        var verdict = Engine.Classify(Visits.APageInAWindowThreeThousandTall() with
        {
            EngagedMs = engagedMs,
            MaxScrollDepthPercent = (byte)depth,
        });

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
        verdict.Contradicting.Should().ContainSingle().Which.Code.Should().Be(SignalCodes.OversizedWindow);
    }

    /// <summary>A moment short of fifteen seconds does not.</summary>
    [Fact]
    public void A_Moment_Short_Of_Fifteen_Seconds_Does_Not_Outweigh_The_Window()
    {
        var verdict = Engine.Classify(Visits.APageInAWindowThreeThousandTall() with { EngagedMs = 14_999 });

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
    }

    /// <summary>
    /// Time a page stayed open with no sign of being left is not a stay, so it outweighs nothing here
    /// either, and the time is shown beside the window as pointing the same way.
    /// </summary>
    [Fact]
    public void Time_Left_Open_Does_Not_Outweigh_The_Window()
    {
        var verdict = Engine.Classify(Visits.APageInAWindowThreeThousandTall() with
        {
            EngagedMs = 60_000,
            LeftOpenMs = 60_000,
        });

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.LeftOpen);
    }

    /// <summary>
    /// A press counts for nothing on a visit that came straight in, so it does not stand against the
    /// window either. It is on the record beside the answer, pointing nowhere.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void A_Press_Does_Not_Outweigh_The_Window(bool touched, bool typed)
    {
        var verdict = Engine.Classify(Visits.APageInAWindowThreeThousandTall() with
        {
            HadPointerInteraction = touched,
            HadKeyboardInteraction = typed,
        });

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
        verdict.Supporting.Single(signal => signal.Code == (touched ? SignalCodes.PointerUsed : SignalCodes.KeyboardUsed))
            .Direction.Should().Be(SignalDirection.Neutral);
    }

    /// <summary>
    /// The reason a short stay did not count belongs beside an answer it left open, and this answer
    /// was settled: a reader is told about the window instead.
    /// </summary>
    [Fact]
    public void The_Reason_A_Short_Stay_Did_Not_Count_Is_Not_Given_Beside_The_Window()
    {
        var verdict = Engine.Classify(Visits.APageInAWindowThreeThousandTall());

        verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.BriefStay);
        verdict.Contradicting.Should().NotContain(signal => signal.Code == SignalCodes.BriefStay);
    }

    /// <summary>
    /// A rented network and a window taller than a screen are two heavy things pointing the same way,
    /// so short of a stay it is stated firmly. A stay does not outweigh the network, and it does stand
    /// against the answer.
    /// </summary>
    [Theory]
    [InlineData(483, EvidenceStrength.Strong)]
    [InlineData(14_999, EvidenceStrength.Strong)]
    [InlineData(15_000, EvidenceStrength.Moderate)]
    public void A_Window_Taller_Than_A_Screen_Over_A_Rented_Network_Is_Something_Automated(
        int engagedMs,
        EvidenceStrength strength)
    {
        var verdict = Engine.Classify(Visits.APageInAWindowThreeThousandTall() with
        {
            EngagedMs = engagedMs,
            AutonomousSystem = Visits.MicrosoftNetwork,
        });

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        verdict.Strength.Should().Be(strength);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.OversizedWindow);
    }

    /// <summary>
    /// The window joins what a visitor said about itself rather than replacing it: a crawler that named
    /// itself is still that crawler, and only suspected, stated more firmly for having two things
    /// behind it.
    /// </summary>
    [Fact]
    public void A_Crawler_That_Named_Itself_Keeps_Its_Category_In_A_Window_Taller_Than_A_Screen()
    {
        var verdict = Engine.Classify(Visits.APageInAWindowThreeThousandTall() with
        {
            UserAgent = "Mozilla/5.0 (compatible; GPTBot/1.2; +https://openai.com/gptbot)",
        });

        verdict.Category.Should().Be(TrafficCategory.SuspectedAiCrawler);
        verdict.Strength.Should().Be(EvidenceStrength.Strong);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.OversizedWindow);
    }

    /// <summary>
    /// An identity settled against what a company says about its own addresses is not weighed, so the
    /// size of a window changes nothing about it: here the window such a crawler reported on real
    /// traffic, running the tracker.
    /// </summary>
    [Fact]
    public void A_Crawler_Whose_Owner_Vouches_For_It_Stays_Verified_In_A_Window_Taller_Than_A_Screen()
    {
        var crawler = Visits.AConfirmedCrawler("Google") with { Surfaces = [IngestSurface.BrowserTracker] };

        var verdict = Engine.Classify(crawler with { ViewportWidth = 1_024, ViewportHeight = 12_140 });

        verdict.Category.Should().Be(Engine.Classify(crawler).Category);
        verdict.Strength.Should().Be(EvidenceStrength.Verified);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.OversizedWindow);
    }

    /// <summary>
    /// The same page in an ordinary window is the answer the rule for a short stay gives, and neither a
    /// tall window on a screen held upright, with 320 pixels of side panel open or none, nor a phone's layout moves it.
    /// </summary>
    [Theory]
    [InlineData(1_440, 900, null)]
    [InlineData(1_440, 2_445, null)]
    [InlineData(1_000, 2_560, null)]
    [InlineData(1_600, 2_717, null)]
    [InlineData(1_408, 2_957, null)]
    [InlineData(1_320, 2_563, Visits.ChromeOnAPhone)]
    [InlineData(1_200, 3_000, Visits.ChromeOnAPhone)]
    public void A_Window_No_Taller_Than_A_Screen_Moves_Nothing(int width, int height, string? userAgent)
    {
        var page = Visits.APageInAWindowThreeThousandTall() with { ViewportWidth = width, ViewportHeight = height };
        var verdict = Engine.Classify(userAgent is null ? page : page with { UserAgent = userAgent });

        verdict.Category.Should().Be(TrafficCategory.Unknown);
        verdict.Strength.Should().Be(EvidenceStrength.Weak);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.BriefStay);
    }

    /// <summary>
    /// The line the rule is stated by. A visit that came straight in over an ordinary network, with
    /// nothing else pointing toward automation, is a person from fifteen seconds said as its pages
    /// were left, and not a moment before.
    /// </summary>
    [Theory]
    [InlineData(14_999, 0, TrafficCategory.Unknown, EvidenceStrength.Weak)]
    [InlineData(14_999, 80, TrafficCategory.Unknown, EvidenceStrength.Weak)]
    [InlineData(15_000, 0, TrafficCategory.LikelyHuman, EvidenceStrength.Moderate)]
    [InlineData(15_000, 24, TrafficCategory.LikelyHuman, EvidenceStrength.Moderate)]
    [InlineData(15_000, 25, TrafficCategory.LikelyHuman, EvidenceStrength.Strong)]
    [InlineData(15_000, 80, TrafficCategory.LikelyHuman, EvidenceStrength.Strong)]
    public void A_Visit_That_Came_Straight_In_Is_A_Person_From_Fifteen_Seconds(
        int engagedMs,
        int depth,
        TrafficCategory category,
        EvidenceStrength strength)
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            EngagedMs = engagedMs,
            MaxScrollDepthPercent = (byte)depth,
        });

        verdict.Category.Should().Be(category);
        verdict.Strength.Should().Be(strength);
    }

    /// <summary>
    /// The same fifteen seconds counted after the time left open is taken away: a page open for a
    /// minute that said only ten of it as it was left has not stayed.
    /// </summary>
    [Fact]
    public void Time_Left_Open_Does_Not_Make_Up_A_Stay()
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            EngagedMs = 60_000,
            LeftOpenMs = 50_000,
            MaxScrollDepthPercent = 80,
        });

        verdict.Category.Should().Be(TrafficCategory.Unknown);
        verdict.Strength.Should().Be(EvidenceStrength.Weak);
    }

    /// <summary>
    /// On one page a press changes nothing on a visit that came straight in, at any length of stay
    /// and any depth: a program can press as easily as anybody.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 80)]
    [InlineData(5_000, 0)]
    [InlineData(5_000, 80)]
    [InlineData(14_999, 80)]
    [InlineData(15_000, 0)]
    [InlineData(15_000, 80)]
    public void On_One_Page_A_Press_Changes_Nothing_On_A_Visit_That_Came_Straight_In(int engagedMs, int depth)
    {
        var untouched = Visits.AGlance() with { EngagedMs = engagedMs, MaxScrollDepthPercent = (byte)depth };

        var unpressed = Engine.Classify(untouched);
        var pressed = Engine.Classify(untouched with { HadPointerInteraction = true, HadKeyboardInteraction = true });

        pressed.Category.Should().Be(unpressed.Category);
        pressed.Strength.Should().Be(unpressed.Strength);
    }

    /// <summary>
    /// The one thing a press still does on a visit that came straight in. Across several pages in
    /// under two seconds, "nothing was touched" cannot be said of a visit on which something was, so
    /// it is not called silent. It is not called a person either.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_Press_Keeps_A_Quick_Multi_Page_Visit_Out_Of_Something_Automated(bool touched, bool typed)
    {
        var untouched = Visits.AGlance() with
        {
            Requests = Visits.Pages(2, TimeSpan.FromSeconds(1)),
            EngagedMs = 900,
            MaxScrollDepthPercent = 10,
        };

        Engine.Classify(untouched).Category.Should().Be(TrafficCategory.SuspiciousAutomation);

        var pressed = Engine.Classify(untouched with { HadPointerInteraction = touched, HadKeyboardInteraction = typed });

        pressed.Category.Should().Be(TrafficCategory.Unknown);
        pressed.Strength.Should().Be(EvidenceStrength.Weak);
    }

    /// <summary>
    /// A page merely shown keeps a quick multi-page visit out of the same answer, for the same
    /// reason: "nothing was seen" cannot be said of it. The reader is told why it did not count.
    /// </summary>
    [Fact]
    public void A_Page_Merely_Shown_Keeps_A_Quick_Multi_Page_Visit_Out_Of_Something_Automated()
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            Requests = Visits.Pages(2, TimeSpan.FromSeconds(1)),
            EngagedMs = 900,
            LeftOpenMs = 0,
            MaxScrollDepthPercent = 34,
        });

        verdict.Category.Should().Be(TrafficCategory.Unknown);
        verdict.Strength.Should().Be(EvidenceStrength.Weak);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.BriefStay);
    }

    /// <summary>
    /// The two lines silence is stated by, on several pages where nothing was pressed: two seconds in
    /// all, or a quarter of a page seen, and "nothing happened" cannot be said.
    /// </summary>
    [Theory]
    [InlineData(1_999, 24, TrafficCategory.SuspiciousAutomation)]
    [InlineData(2_000, 24, TrafficCategory.Unknown)]
    [InlineData(1_999, 25, TrafficCategory.Unknown)]
    public void A_Quick_Multi_Page_Visit_Is_Silent_Only_Under_Two_Seconds_And_Short_Of_A_Quarter_Seen(
        int engagedMs,
        int depth,
        TrafficCategory expected)
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            Requests = Visits.Pages(2, TimeSpan.FromSeconds(1)),
            EngagedMs = engagedMs,
            MaxScrollDepthPercent = (byte)depth,
        });

        verdict.Category.Should().Be(expected);
        verdict.Strength.Should().Be(EvidenceStrength.Weak);
    }

    /// <summary>
    /// Five pages in a visit over inside a quarter of a minute is faster than anyone reads, and where it
    /// came straight in nothing it did on the way outweighs that, a press included. Fifteen seconds said
    /// as its pages were left does.
    /// </summary>
    [Theory]
    [InlineData(14_500, TrafficCategory.SuspiciousAutomation)]
    [InlineData(15_000, TrafficCategory.LikelyHuman)]
    public void Five_Quick_Pages_That_Came_Straight_In_Are_Something_Automated_Short_Of_Fifteen_Seconds(
        int engagedMs,
        TrafficCategory expected)
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            Requests = Visits.Pages(5, TimeSpan.FromSeconds(14)),
            EndedAt = Visits.Noon.AddSeconds(14),
            EngagedMs = engagedMs,
            MaxScrollDepthPercent = 60,
            HadPointerInteraction = true,
        });

        verdict.Category.Should().Be(expected);
    }

    /// <summary>
    /// A long visit that came straight in and stayed a moment on each page: taking that many pages
    /// outweighs the tracker having run, the only thing that counts toward a person, and it is
    /// something automated.
    /// </summary>
    [Fact]
    public void A_Long_Visit_That_Came_Straight_In_And_Did_Not_Stay_Is_Something_Automated()
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            Requests = Visits.Pages(25, TimeSpan.FromMinutes(30)),
            EndedAt = Visits.Noon.AddMinutes(30),
            EngagedMs = 14_000,
            MaxScrollDepthPercent = 60,
            HadPointerInteraction = true,
        });

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
    }

    /// <summary>
    /// The same fifteen-second line reaches a visit that asked for pages that were never there: a
    /// short stay does not outweigh that.
    /// </summary>
    [Fact]
    public void Pages_That_Were_Never_There_Outweigh_A_Short_Stay_That_Came_Straight_In()
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            Requests =
            [
                .. Visits.Pages(4, TimeSpan.FromMinutes(10)),
                .. Visits.Pages(3, TimeSpan.FromMinutes(10), status: 404),
            ],
            EngagedMs = 10_000,
            MaxScrollDepthPercent = 60,
            HadPointerInteraction = true,
        });

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
    }

    /// <summary>
    /// A visit another site sent counts what it did however short the stay: pinned by value across how
    /// long it stayed, how far down, what was pressed and whether it came over a rented network, so a
    /// change to the rules for direct traffic that reaches these shapes fails here.
    /// <see cref="A_Visit_Another_Site_Sent_Is_Weighed_As_It_Was_Whatever_Else_Was_Seen"/> holds pages
    /// left open, silence, rate, breadth, missing paths and a window taller than a screen.
    /// </summary>
    [Theory]
    [InlineData(0, 0, false, false, false, TrafficCategory.Unknown, EvidenceStrength.Weak)]
    [InlineData(0, 24, false, false, false, TrafficCategory.Unknown, EvidenceStrength.Weak)]
    [InlineData(0, 25, false, false, false, TrafficCategory.LikelyHuman, EvidenceStrength.Moderate)]
    [InlineData(0, 34, false, false, false, TrafficCategory.LikelyHuman, EvidenceStrength.Moderate)]
    [InlineData(0, 0, true, false, false, TrafficCategory.LikelyHuman, EvidenceStrength.Moderate)]
    [InlineData(0, 0, false, true, false, TrafficCategory.LikelyHuman, EvidenceStrength.Moderate)]
    [InlineData(0, 34, true, false, false, TrafficCategory.LikelyHuman, EvidenceStrength.Strong)]
    [InlineData(1_999, 34, false, false, false, TrafficCategory.LikelyHuman, EvidenceStrength.Moderate)]
    [InlineData(2_000, 0, false, false, false, TrafficCategory.LikelyHuman, EvidenceStrength.Moderate)]
    [InlineData(2_000, 34, false, false, false, TrafficCategory.LikelyHuman, EvidenceStrength.Strong)]
    [InlineData(14_999, 0, true, false, false, TrafficCategory.LikelyHuman, EvidenceStrength.Strong)]
    [InlineData(15_000, 0, false, false, false, TrafficCategory.LikelyHuman, EvidenceStrength.Moderate)]
    [InlineData(15_000, 34, false, false, false, TrafficCategory.LikelyHuman, EvidenceStrength.Strong)]
    [InlineData(0, 0, false, false, true, TrafficCategory.Unknown, EvidenceStrength.Strong)]
    [InlineData(0, 0, true, false, true, TrafficCategory.LikelyHuman, EvidenceStrength.Moderate)]
    [InlineData(0, 0, false, true, true, TrafficCategory.LikelyHuman, EvidenceStrength.Moderate)]
    [InlineData(14_999, 50, false, false, true, TrafficCategory.Unknown, EvidenceStrength.Strong)]
    [InlineData(15_000, 50, false, false, true, TrafficCategory.LikelyHuman, EvidenceStrength.Moderate)]
    public void A_Visit_Another_Site_Sent_Is_Judged_As_It_Was(
        int engagedMs,
        int depth,
        bool touched,
        bool typed,
        bool rented,
        TrafficCategory category,
        EvidenceStrength strength)
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            SentBy = SourceChannel.Search,
            EngagedMs = engagedMs,
            LeftOpenMs = 0,
            MaxScrollDepthPercent = (byte)depth,
            HadPointerInteraction = touched,
            HadKeyboardInteraction = typed,
            AutonomousSystem = rented ? Visits.MicrosoftNetwork : 0,
        });

        verdict.Category.Should().Be(category);
        verdict.Strength.Should().Be(strength);
        verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.BriefStay);
    }

    /// <summary>
    /// A visit another site sent weighed signal by signal, across what
    /// <see cref="A_Visit_Another_Site_Sent_Is_Judged_As_It_Was"/> does not reach: pages left open,
    /// several pages with nothing happening, pages taken fast, widely or missing, and a window taller
    /// than a screen. Pinned by value, so a change to the rules for direct traffic that reaches these
    /// shapes fails here.
    /// </summary>
    [Theory]
    [MemberData(nameof(SentVisits))]
    public void A_Visit_Another_Site_Sent_Is_Weighed_As_It_Was_Whatever_Else_Was_Seen(string visit)
    {
        var pinned = Sent[visit];

        var verdict = Engine.Classify(pinned.Visit);

        verdict.Category.Should().Be(pinned.Category);
        verdict.Strength.Should().Be(pinned.Strength);
        verdict.Supporting.Concat(verdict.Contradicting)
            .Select(signal => new Weighed(signal.Code, signal.Direction, signal.Weight))
            .Should().BeEquivalentTo(pinned.Signals);
    }

    /// <summary>The visits another site sent that are pinned signal by signal, by name.</summary>
    public static TheoryData<string> SentVisits => new(Sent.Keys);

    private static readonly Weighed Arrival = new(SignalCodes.ArrivedFromAnotherSite, SignalDirection.TowardHuman, 20);

    private static readonly Weighed TrackerRan = new(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35);

    private static readonly Weighed PointerPressed = new(SignalCodes.PointerUsed, SignalDirection.TowardHuman, 55);

    private static readonly Weighed KeyPressed = new(SignalCodes.KeyboardUsed, SignalDirection.TowardHuman, 50);

    private static readonly Weighed SeenDown = new(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45);

    private static readonly Weighed ShortRead = new(SignalCodes.ReadTime, SignalDirection.TowardHuman, 40);

    private static readonly Weighed LeftOpen = new(SignalCodes.LeftOpen, SignalDirection.TowardAutomation, 20);

    private static readonly Weighed Silence = new(SignalCodes.NoEngagement, SignalDirection.TowardAutomation, 50);

    private static readonly Weighed TakenFast = new(SignalCodes.RetrievalRate, SignalDirection.TowardAutomation, 50);

    private static readonly Weighed TakenWidely = new(SignalCodes.RetrievalBreadth, SignalDirection.TowardAutomation, 40);

    private static readonly Weighed MissingPages = new(SignalCodes.MissingPaths, SignalDirection.TowardAutomation, 45);

    private static readonly Weighed RentedNetwork = new(SignalCodes.HostingNetwork, SignalDirection.TowardAutomation, 65);

    private static readonly SessionEvidence SentGlance = Visits.AGlance() with { SentBy = SourceChannel.Search };

    private static readonly SessionEvidence SentAndSilent = SentGlance with
    {
        Requests = Visits.Pages(2, TimeSpan.FromSeconds(1)),
        EngagedMs = 900,
        MaxScrollDepthPercent = 10,
    };

    private static readonly SessionEvidence SentAndLeftOpen = SentGlance with
    {
        EngagedMs = 15_000,
        LeftOpenMs = 15_000,
        MaxScrollDepthPercent = 18,
    };

    private static readonly SessionEvidence SentAndFast = SentGlance with
    {
        Requests = Visits.Pages(5, TimeSpan.FromSeconds(14)),
        EndedAt = Visits.Noon.AddSeconds(14),
        EngagedMs = 14_500,
        MaxScrollDepthPercent = 60,
    };

    private static readonly Dictionary<string, PinnedVisit> Sent = new(StringComparer.Ordinal)
    {
        ["a page shown and closed at once, in a window taller than a screen"] = new(
            Visits.APageInAWindowThreeThousandTall() with { SentBy = SourceChannel.Search },
            TrafficCategory.LikelyHuman,
            EvidenceStrength.Moderate,
            [Arrival, TrackerRan, SeenDown]),
        ["several pages with nothing happening"] = new(
            SentAndSilent,
            TrafficCategory.SuspiciousAutomation,
            EvidenceStrength.Weak,
            [Arrival, TrackerRan, Silence]),
        ["several pages, just short of two seconds"] = new(
            SentAndSilent with { EngagedMs = 1_999, MaxScrollDepthPercent = 24 },
            TrafficCategory.SuspiciousAutomation,
            EvidenceStrength.Weak,
            [Arrival, TrackerRan, Silence]),
        ["several pages, two seconds in all"] = new(
            SentAndSilent with { EngagedMs = 2_000, MaxScrollDepthPercent = 24 },
            TrafficCategory.LikelyHuman,
            EvidenceStrength.Moderate,
            [Arrival, TrackerRan, ShortRead]),
        ["several pages with the pointer pressed"] = new(
            SentAndSilent with { HadPointerInteraction = true },
            TrafficCategory.LikelyHuman,
            EvidenceStrength.Moderate,
            [Arrival, TrackerRan, PointerPressed]),
        ["several pages with a key pressed"] = new(
            SentAndSilent with { HadKeyboardInteraction = true },
            TrafficCategory.LikelyHuman,
            EvidenceStrength.Moderate,
            [Arrival, TrackerRan, KeyPressed]),
        ["several pages, one merely shown a quarter of the way down"] = new(
            SentAndSilent with { MaxScrollDepthPercent = 25 },
            TrafficCategory.LikelyHuman,
            EvidenceStrength.Moderate,
            [Arrival, TrackerRan, SeenDown]),
        ["a page left open"] = new(
            SentAndLeftOpen,
            TrafficCategory.Unknown,
            EvidenceStrength.Weak,
            [Arrival, TrackerRan, LeftOpen]),
        ["a page left open seen a quarter of the way down"] = new(
            SentAndLeftOpen with { MaxScrollDepthPercent = 34 },
            TrafficCategory.LikelyHuman,
            EvidenceStrength.Moderate,
            [Arrival, TrackerRan, LeftOpen, SeenDown]),
        ["a page left open a long time, seen well down, the pointer pressed"] = new(
            SentAndLeftOpen with
            {
                EngagedMs = 180_000,
                LeftOpenMs = 180_000,
                MaxScrollDepthPercent = 80,
                HadPointerInteraction = true,
            },
            TrafficCategory.LikelyHuman,
            EvidenceStrength.Strong,
            [Arrival, TrackerRan, LeftOpen, SeenDown, PointerPressed]),
        ["a page left open over a rented network"] = new(
            SentAndLeftOpen with { MaxScrollDepthPercent = 50, AutonomousSystem = Visits.MicrosoftNetwork },
            TrafficCategory.Unknown,
            EvidenceStrength.Strong,
            [RentedNetwork, Arrival, TrackerRan, LeftOpen, SeenDown]),
        ["pages taken faster than anyone reads"] = new(
            SentAndFast,
            TrafficCategory.SuspiciousAutomation,
            EvidenceStrength.Weak,
            [Arrival, TrackerRan, TakenFast, ShortRead, SeenDown]),
        ["pages taken faster than anyone reads, the pointer pressed"] = new(
            SentAndFast with { HadPointerInteraction = true },
            TrafficCategory.LikelyHuman,
            EvidenceStrength.Moderate,
            [Arrival, TrackerRan, TakenFast, ShortRead, SeenDown, PointerPressed]),
        ["many pages, the pointer pressed"] = new(
            SentGlance with
            {
                Requests = Visits.Pages(25, TimeSpan.FromMinutes(30)),
                EndedAt = Visits.Noon.AddMinutes(30),
                EngagedMs = 14_000,
                MaxScrollDepthPercent = 60,
                HadPointerInteraction = true,
            },
            TrafficCategory.LikelyHuman,
            EvidenceStrength.Strong,
            [Arrival, TrackerRan, TakenWidely, ShortRead, SeenDown, PointerPressed]),
        ["pages that were never there, the pointer pressed"] = new(
            SentGlance with
            {
                Requests =
                [
                    .. Visits.Pages(4, TimeSpan.FromMinutes(10)),
                    .. Visits.Pages(3, TimeSpan.FromMinutes(10), status: 404),
                ],
                EngagedMs = 10_000,
                MaxScrollDepthPercent = 60,
                HadPointerInteraction = true,
            },
            TrafficCategory.LikelyHuman,
            EvidenceStrength.Strong,
            [Arrival, TrackerRan, MissingPages, ShortRead, SeenDown, PointerPressed]),
    };

    /// <summary>An observation and what it was weighed at.</summary>
    private readonly record struct Weighed(string Code, SignalDirection Direction, int Weight);

    /// <summary>A visit, what it is called, how firmly, and everything it was weighed on.</summary>
    private sealed record PinnedVisit(
        SessionEvidence Visit,
        TrafficCategory Category,
        EvidenceStrength Strength,
        Weighed[] Signals);

    /// <summary>
    /// The same short stay, told apart only by another site having been named as sending it.
    /// </summary>
    [Fact]
    public void Which_Site_Sent_A_Visit_Decides_A_Short_Stay()
    {
        var aMoment = Visits.AGlance() with { EngagedMs = 3_000 };

        Engine.Classify(aMoment).Category.Should().Be(TrafficCategory.Unknown);
        Engine.Classify(aMoment with { SentBy = SourceChannel.Link }).Category.Should().Be(TrafficCategory.LikelyHuman);
    }

    /// <summary>
    /// The same stay with the same press: a press adds to what another site sent, and adds nothing to
    /// a visit that came straight in.
    /// </summary>
    [Fact]
    public void Which_Site_Sent_A_Visit_Decides_What_A_Press_Adds_To_A_Stay()
    {
        var pressed = Visits.AGlance() with { EngagedMs = 15_000, HadPointerInteraction = true };

        Engine.Classify(pressed).Strength.Should().Be(EvidenceStrength.Moderate);
        Engine.Classify(pressed with { SentBy = SourceChannel.Search }).Strength.Should().Be(EvidenceStrength.Strong);
    }

    /// <summary>
    /// The same long read in the same window: nothing named a sender, and the window stands against the
    /// answer and holds it to some signs; another site sent it, and the window is not weighed at all.
    /// </summary>
    [Fact]
    public void Which_Site_Sent_A_Visit_Decides_Whether_A_Window_Counts()
    {
        var read = Visits.AReader() with { ViewportWidth = 1_200, ViewportHeight = 3_000 };

        var straight = Engine.Classify(read);
        var sent = Engine.Classify(read with { SentBy = SourceChannel.Search });

        straight.Strength.Should().Be(EvidenceStrength.Moderate);
        straight.Contradicting.Should().Contain(signal => signal.Code == SignalCodes.OversizedWindow);
        sent.Strength.Should().Be(EvidenceStrength.Strong);
        sent.Contradicting.Should().NotContain(signal => signal.Code == SignalCodes.OversizedWindow);
    }

    [Fact]
    public void A_Page_Somebody_Read_For_A_While_Is_A_Person()
    {
        var verdict = Engine.Classify(Visits.AGlance() with { EngagedMs = 16_000 });

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
    }

    /// <summary>
    /// Whatever a person did, the engine either says so with some evidence behind it or does not
    /// say so at all. The first of these are all people, so the promise is tested rather than
    /// assumed; the rest show the engine declining to say so.
    /// </summary>
    [Fact]
    public void A_Person_Is_Never_Reported_On_Slight_Signs()
    {
        SessionEvidence[] people =
        [
            Visits.AReader(),
            Visits.AGlance() with { EngagedMs = 16_000 },
            Visits.AGlance() with { SentBy = SourceChannel.Search, MaxScrollDepthPercent = 60 },
            Visits.AGlance() with { SentBy = SourceChannel.Search, HadPointerInteraction = true },
            Visits.AGlance() with { SentBy = SourceChannel.Search, EngagedMs = 3_000 },
        ];

        foreach (var person in people)
        {
            var verdict = Engine.Classify(person);

            verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
            verdict.Strength.Should().BeOneOf(EvidenceStrength.Moderate, EvidenceStrength.Strong);
        }

        SessionEvidence[] undecided =
        [
            Visits.AGlance(),
            Visits.AGlance() with { MaxScrollDepthPercent = 60 },
            Visits.AGlance() with { HadPointerInteraction = true },
            Visits.AGlance() with { EngagedMs = 3_000 },
        ];

        foreach (var glance in undecided)
        {
            Engine.Classify(glance).Category.Should().Be(TrafficCategory.Unknown);
        }
    }

    /// <summary>
    /// The visit fleets of browsers made on real traffic: one page opened straight, fifteen seconds
    /// on it that the tracker reported only while it stayed open, nothing pressed, the first screen
    /// a sixth of the page — and the page never said it was being left. A browser kept a page open;
    /// nothing says anybody read it, so the honest answer is that nothing was settled.
    /// </summary>
    [Fact]
    public void A_Page_Opened_And_Left_Open_With_Nothing_Else_Done_Is_Not_Called_A_Person()
    {
        var verdict = Engine.Classify(Visits.APageLeftOpen());

        verdict.Category.Should().Be(TrafficCategory.Unknown);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.LeftOpen);
        verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.ReadTime);
    }

    /// <summary>
    /// The same fifteen seconds, said by a page as it was left, are somebody reading.
    /// </summary>
    [Fact]
    public void The_Same_Page_Let_Go_Of_After_Fifteen_Seconds_Is_A_Person()
    {
        var verdict = Engine.Classify(Visits.APageLeftOpen() with { LeftOpenMs = 0 });

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.ReadTime);
    }

    /// <summary>
    /// A reader who came straight in, pressed something and saw well down a page, whose page never
    /// said it was left: three minutes of time nothing heard the end of, so nothing was settled.
    /// Laptops are shut on pages, and a report of leaving can be lost, which is why the answer is
    /// that the product could not tell rather than anything worse.
    /// </summary>
    [Fact]
    public void Somebody_Who_Came_Straight_In_And_Pressed_On_A_Page_Left_Open_Is_Not_Called_A_Person()
    {
        var verdict = Engine.Classify(Visits.AReader(pages: 1, engagedMs: 180_000) with { LeftOpenMs = 180_000 });

        verdict.Category.Should().Be(TrafficCategory.Unknown);
        verdict.Strength.Should().Be(EvidenceStrength.Weak);
        verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.ReadTime);
        verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.BriefStay);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.LeftOpen);
    }

    /// <summary>
    /// The same page another site sent a visit to is a person, with the page left open shown against
    /// the verdict rather than taken for reading.
    /// </summary>
    [Fact]
    public void Somebody_Another_Site_Sent_Who_Read_Well_Down_A_Page_Left_Open_Is_Still_A_Person()
    {
        var verdict = Engine.Classify(Visits.AReader(pages: 1, engagedMs: 180_000) with
        {
            LeftOpenMs = 180_000,
            SentBy = SourceChannel.Search,
        });

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.ReadTime);
        verdict.Contradicting.Should().ContainSingle().Which.Code.Should().Be(SignalCodes.LeftOpen);
    }

    /// <summary>
    /// Time left open is not reading even where it was all that outweighed something else: a visit
    /// that asked for three missing pages and said ten of its twenty-five seconds as its pages were
    /// left is weighed on those ten, as it would have been had it been open only that long.
    /// </summary>
    [Fact]
    public void Time_Left_Open_Does_Not_Outweigh_Anything_Pointing_Toward_Automation()
    {
        var visit = Visits.AGlance() with
        {
            Requests =
            [
                .. Visits.Pages(4, TimeSpan.FromMinutes(10)),
                .. Visits.Pages(3, TimeSpan.FromMinutes(10), status: 404),
            ],
            EngagedMs = 25_000,
            MaxScrollDepthPercent = 20,
        };

        Engine.Classify(visit).Category.Should().Be(TrafficCategory.LikelyHuman);
        Engine.Classify(visit with { LeftOpenMs = 15_000 }).Category.Should().Be(TrafficCategory.SuspiciousAutomation);
    }

    /// <summary>
    /// A page whose wait was cut short is automation however much else it did: the reading,
    /// scrolling and pressing all happened while its timers ran ahead of its clocks.
    /// </summary>
    [Fact]
    public void A_Page_Whose_Wait_Was_Cut_Short_Is_Something_Automated_Whatever_Else_It_Did()
    {
        var verdict = Engine.Classify(Visits.APageWhoseWaitWasCutShort() with { HadKeyboardInteraction = true });

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.WaitCutShort);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.KeyboardUsed);
        verdict.Contradicting.Should().Contain(signal => signal.Code == SignalCodes.ReadTime);

        var sent = Engine.Classify(Visits.APageWhoseWaitWasCutShort() with
        {
            HadKeyboardInteraction = true,
            SentBy = SourceChannel.Search,
        });

        sent.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        sent.Contradicting.Should().Contain(signal => signal.Code == SignalCodes.KeyboardUsed);
    }

    /// <summary>
    /// A reader whose first report took its whole wait on the longer of its two clocks is a reader,
    /// however short the other clock read.
    /// </summary>
    [Fact]
    public void A_Page_Whose_Wait_Took_Its_Fifteen_Seconds_Is_Not_Taken_For_One_Cut_Short()
    {
        var verdict = Engine.Classify(Visits.AReader() with { SoonestProgressMs = 15_000 });

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.WaitCutShort);
        verdict.Contradicting.Should().NotContain(signal => signal.Code == SignalCodes.WaitCutShort);
    }

    /// <summary>
    /// A visitor that introduces itself as a crawler under a name nobody has catalogued is a
    /// crawler, and is said to be one without the name it used being repeated.
    /// </summary>
    [Fact]
    public void A_Crawler_Naming_Itself_Only_As_A_Crawler_Is_Called_A_Crawler()
    {
        var verdict = Engine.Classify(Visits.ANamelessCrawler());

        verdict.Category.Should().Be(TrafficCategory.GenericWebCrawler);
        verdict.Supporting.Should().ContainSingle(signal => signal.Code == SignalCodes.DeclaredGenericCrawler)
            .Which.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void A_Nameless_Crawler_That_Also_Read_A_Page_Is_Still_A_Crawler()
    {
        var busy = Visits.ANamelessCrawler() with
        {
            Surfaces = [IngestSurface.CloudflareWorker, IngestSurface.BrowserTracker],
            EngagedMs = 30_000,
            MaxScrollDepthPercent = 90,
        };

        var verdict = Engine.Classify(busy);

        verdict.Category.Should().Be(TrafficCategory.GenericWebCrawler);
        verdict.Contradicting.Should().NotBeEmpty();
    }

    /// <summary>
    /// The paths a sweep asks for are the paths a site's owner asks for too. The difference is
    /// that the site answers the owner, and a request the site served is not probing.
    /// </summary>
    [Fact]
    public void A_Site_Owner_Signing_In_Is_Not_Probing_For_A_Way_In()
    {
        var verdict = Engine.Classify(Visits.AnOwnerSigningIn());

        verdict.Category.Should().NotBe(TrafficCategory.SecurityScanner);
        verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.SensitivePaths);
        verdict.Contradicting.Should().NotContain(signal => signal.Code == SignalCodes.SensitivePaths);
    }

    /// <summary>
    /// The rule the whole product rests on. A user agent is one line of text the visitor writes
    /// itself, so a name that has not been checked against the operator's published addresses is
    /// a claim — and the category has to keep saying so.
    /// </summary>
    [Theory]
    [InlineData("Mozilla/5.0 (compatible; GPTBot/1.2; +https://openai.com/gptbot)")]
    [InlineData("Mozilla/5.0 (compatible; ClaudeBot/1.0; +claudebot@anthropic.com)")]
    [InlineData("Mozilla/5.0 (compatible; PerplexityBot/1.0; +https://perplexity.ai/perplexitybot)")]
    public void An_Ai_Crawler_That_Names_Itself_Is_Only_Ever_Suspected(string userAgent)
    {
        var verdict = Engine.Classify(Visits.ANamedCrawler(userAgent));

        verdict.Category.Should().Be(TrafficCategory.SuspectedAiCrawler);
        verdict.Category.Should().NotBe(TrafficCategory.KnownAiCrawler);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.UnverifiedClaim);
    }

    /// <summary>
    /// Behaviour cannot establish identity, however much of it there is. The band is reserved for
    /// a request from an address its operator published, and nothing in this assembly can check
    /// that — it performs no I/O at all.
    /// </summary>
    [Fact]
    public void Nothing_Behavioural_Ever_Reaches_Verified()
    {
        SessionEvidence[] everything =
        [
            Visits.AReader(),
            Visits.AGlance(),
            Visits.AScanner(),
            Visits.Anonymous(),
            Visits.ANamelessCrawler(),
            Visits.AnOwnerSigningIn(),
            Visits.ANamedCrawler("Mozilla/5.0 (compatible; GPTBot/1.2; +https://openai.com/gptbot)"),
            Visits.ANamedCrawler("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)"),
        ];

        everything.Select(session => Engine.Classify(session).Strength)
            .Should().NotContain(EvidenceStrength.Verified);
    }

    [Fact]
    public void A_Search_Crawler_Naming_Itself_Is_Not_Filed_As_An_Ai_One()
    {
        var verdict = Engine.Classify(
            Visits.ANamedCrawler("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)"));

        verdict.Category.Should().Be(TrafficCategory.GenericWebCrawler);
    }

    /// <summary>
    /// Google runs one crawler for search and another for model training, and a publisher may
    /// feel entirely differently about the two. Reading the longer name correctly is the whole
    /// difference.
    /// </summary>
    [Fact]
    public void Googles_Training_Crawler_Is_Told_Apart_From_Its_Search_One()
    {
        var training = Engine.Classify(Visits.ANamedCrawler("Mozilla/5.0 (compatible; Google-Extended/1.0)"));
        var search = Engine.Classify(Visits.ANamedCrawler("Mozilla/5.0 (compatible; Googlebot/2.1)"));

        training.Category.Should().Be(TrafficCategory.SuspectedAiCrawler);
        search.Category.Should().Be(TrafficCategory.GenericWebCrawler);
    }

    [Fact]
    public void Something_Sweeping_For_A_Way_In_Is_Called_A_Scanner()
    {
        var verdict = Engine.Classify(Visits.AScanner());

        verdict.Category.Should().Be(TrafficCategory.SecurityScanner);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.SensitivePaths);
    }

    /// <summary>
    /// A session can look like several things at once, and the most specific thing that can be
    /// said about it is what it should be called. Something asking for a credential store is a
    /// scanner whatever else it did on the way past.
    /// </summary>
    [Fact]
    public void A_Scanner_That_Also_Scrolled_A_Page_Is_Still_A_Scanner()
    {
        var busy = Visits.AScanner() with
        {
            Surfaces = [IngestSurface.CloudflareWorker, IngestSurface.BrowserTracker],
            EngagedMs = 30_000,
            MaxScrollDepthPercent = 90,
            HadPointerInteraction = true,
            HadKeyboardInteraction = true,
        };

        var verdict = Engine.Classify(busy);

        verdict.Category.Should().Be(TrafficCategory.SecurityScanner);
    }

    /// <summary>
    /// A verdict that shows only what agrees with it is an argument rather than an assessment.
    /// The product's claim is that it shows its working, including the parts that cut against
    /// the answer.
    /// </summary>
    [Fact]
    public void Evidence_Pointing_The_Other_Way_Is_Kept_And_Holds_The_Conclusion_Back()
    {
        var busy = Visits.AScanner() with
        {
            Surfaces = [IngestSurface.CloudflareWorker, IngestSurface.BrowserTracker],
            EngagedMs = 30_000,
            MaxScrollDepthPercent = 90,
            HadPointerInteraction = true,
            HadKeyboardInteraction = true,
        };

        var verdict = Engine.Classify(busy);

        verdict.Contradicting.Should().NotBeEmpty();
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
    }

    /// <summary>
    /// The single most important line in the engine. A server-side surface sees no interaction
    /// because it cannot, and reading that silence as evidence would classify every real person
    /// on a site measured that way as automation.
    /// </summary>
    [Fact]
    public void A_Surface_That_Could_Not_Watch_Produces_No_Evidence_Of_Absence()
    {
        var verdict = Engine.Classify(Visits.Anonymous());

        verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.NoEngagement);
        verdict.Contradicting.Should().NotContain(signal => signal.Code == SignalCodes.NoEngagement);
    }

    /// <summary>
    /// A real answer rather than a failure. One ordinary page fetch that named an ordinary browser
    /// and did nothing remarkable does not support a conclusion, and saying so is worth more than
    /// a guess.
    /// </summary>
    [Fact]
    public void One_Unremarkable_Visit_Produces_An_Honest_Shrug()
    {
        var verdict = Engine.Classify(Visits.Anonymous());

        verdict.Category.Should().BeOneOf(TrafficCategory.Unknown, TrafficCategory.SuspiciousAutomation);
        verdict.Strength.Should().BeOneOf(EvidenceStrength.None, EvidenceStrength.Weak);
    }

    [Fact]
    public void A_Session_Nothing_Was_Observed_About_Is_Answered_As_Not_Yet_Known()
    {
        var nothing = new SessionEvidence
        {
            SessionKey = "empty",
            StartedAt = Visits.Noon,
            EndedAt = Visits.Noon,
            Requests = [],
            Surfaces = [IngestSurface.BrowserTracker],
            UserAgent = "Mozilla/5.0",
            Language = "en",
        };

        var verdict = Engine.Classify(nothing);

        verdict.Category.Should().Be(TrafficCategory.InsufficientEvidence);
        verdict.Strength.Should().Be(EvidenceStrength.None);
    }

    [Fact]
    public void A_Browser_Somebody_Is_Driving_With_Software_Says_So_And_Is_Believed()
    {
        var driven = Visits.AReader() with { DeclaredWebDriver = true };

        Engine.Classify(driven).Category.Should().Be(TrafficCategory.BrowserAutomation);
    }

    [Fact]
    public void Taking_The_Whole_Site_Without_Rendering_Any_Of_It_Is_Called_Scraping()
    {
        var sweep = new SessionEvidence
        {
            SessionKey = "sweep",
            StartedAt = Visits.Noon,
            EndedAt = Visits.Noon.AddMinutes(2),
            Requests = Visits.Pages(60, TimeSpan.FromMinutes(2)),
            Surfaces = [IngestSurface.CloudflareWorker],
            UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36",
            Language = "en-US",
        };

        Engine.Classify(sweep).Category.Should().Be(TrafficCategory.ContentScraper);
    }

    /// <summary>
    /// Every verdict carries the rules that produced it, so a number on a screen can still be
    /// explained a month after the rules changed.
    /// </summary>
    [Fact]
    public void Every_Verdict_Records_Which_Rules_Produced_It()
    {
        Engine.Classify(Visits.AReader()).RulesetVersion.Should().Be(RulesetVersion.Current);
    }

    /// <summary>
    /// The same session judged twice must produce the same answer, or the fixture suite is noise
    /// and re-judging stored history means nothing.
    /// </summary>
    [Fact]
    public void The_Same_Session_Judged_Twice_Gives_The_Same_Answer()
    {
        var session = Visits.AReader();

        var first = Engine.Classify(session);
        var second = Engine.Classify(session);

        second.Should().BeEquivalentTo(first);
    }

    /// <summary>
    /// The case this product got wrong in the field, and the reason the ruleset moved to two.
    /// </summary>
    /// <remarks>
    /// A real browser driven by a script, coming straight to the site, reading for forty-five
    /// seconds and scrolling most of the way down from a rented server. Its time and how far down it
    /// read say person, and the press beside them points nowhere. Calling it one is the single
    /// mistake a product about traffic authenticity cannot make.
    /// </remarks>
    [Fact]
    public void A_Visit_That_Reads_Like_A_Person_But_Came_Straight_From_A_Rented_Server_Is_Not_Called_A_Person()
    {
        var verdict = TrafficClassifier.Current().Classify(Visits.AReaderFromARentedServer());

        verdict.Category.Should().NotBe(TrafficCategory.LikelyHuman);
    }

    /// <summary>
    /// Somebody a search sent to a page, reading on a desktop rented in a cloud: a person, with the
    /// network shown against them and holding the answer to some signs.
    /// </summary>
    [Fact]
    public void Somebody_A_Search_Sent_Who_Then_Read_On_A_Cloud_Desktop_Is_A_Person()
    {
        var verdict = Engine.Classify(Visits.AReaderOnACloudDesktop(SourceChannel.Search));

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
        verdict.Contradicting.Should().Contain(signal => signal.Code == SignalCodes.HostingNetwork);
        verdict.Supporting.Single(signal => signal.Code == SignalCodes.ArrivedFromAnotherSite)
            .Parameters["kind"].Should().Be("search");
    }

    /// <summary>
    /// Somebody a search sent who read a page on a cloud desktop without pressing anything: most
    /// of a minute on it and most of the way down. Reading is what people do with a page, and it
    /// is enough without a click.
    /// </summary>
    [Fact]
    public void Somebody_A_Search_Sent_Who_Read_On_A_Cloud_Desktop_Without_Pressing_Anything_Is_A_Person()
    {
        var verdict = Engine.Classify(Visits.AReaderOnACloudDesktop(SourceChannel.Search) with
        {
            HadPointerInteraction = false,
        });

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
    }

    /// <summary>
    /// A short page another site sent a visit to, opened and left within seconds. A quarter of it
    /// was seen only because the first screen held that much, which is no sign of anybody reading,
    /// so over a rented network it is not a person.
    /// </summary>
    [Fact]
    public void A_Glance_At_A_Short_Page_Another_Site_Sent_To_A_Rented_Network_Is_Not_Called_A_Person()
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            AutonomousSystem = Visits.MicrosoftNetwork,
            SentBy = SourceChannel.Search,
            MaxScrollDepthPercent = 50,
            EngagedMs = 3_000,
        });

        verdict.Category.Should().Be(TrafficCategory.Unknown);
    }

    /// <summary>
    /// The line the rule is stated by. A page another site sent a visit to over a rented network,
    /// seen half way down with nothing pressed, counts as read from fifteen seconds in front of
    /// somebody and not a moment before.
    /// </summary>
    [Theory]
    [InlineData(14_999, TrafficCategory.Unknown)]
    [InlineData(15_000, TrafficCategory.LikelyHuman)]
    public void A_Page_Another_Site_Sent_To_A_Rented_Network_Counts_As_Read_From_Fifteen_Seconds(
        int engagedMs,
        TrafficCategory expected)
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            AutonomousSystem = Visits.MicrosoftNetwork,
            SentBy = SourceChannel.Search,
            MaxScrollDepthPercent = 50,
            EngagedMs = engagedMs,
        });

        verdict.Category.Should().Be(expected);
    }

    /// <summary>
    /// The same page another site sent over a rented network, seen half way down and with nothing
    /// pressed, where the fifteen seconds were reported only while the page stayed open: a browser
    /// kept the page open, which is not the page being read.
    /// </summary>
    [Fact]
    public void A_Page_Another_Site_Sent_To_A_Rented_Network_And_Left_Open_Is_Not_Counted_As_Read()
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            AutonomousSystem = Visits.MicrosoftNetwork,
            SentBy = SourceChannel.Search,
            MaxScrollDepthPercent = 50,
            EngagedMs = 15_000,
            LeftOpenMs = 15_000,
        });

        verdict.Category.Should().Be(TrafficCategory.Unknown);
    }

    /// <summary>
    /// The fifteen seconds that make a page read are counted after the time left open is taken
    /// away, not before: half a minute open with a moment over fifteen seconds of it left open is
    /// a moment short of a read.
    /// </summary>
    [Theory]
    [InlineData(15_001, TrafficCategory.Unknown)]
    [InlineData(15_000, TrafficCategory.LikelyHuman)]
    public void A_Page_Another_Site_Sent_To_A_Rented_Network_Counts_As_Read_From_Fifteen_Seconds_Said_As_It_Was_Left(
        int leftOpenMs,
        TrafficCategory expected)
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            AutonomousSystem = Visits.MicrosoftNetwork,
            SentBy = SourceChannel.Search,
            MaxScrollDepthPercent = 50,
            EngagedMs = 30_000,
            LeftOpenMs = leftOpenMs,
        });

        verdict.Category.Should().Be(expected);
    }

    /// <summary>
    /// A press means nothing on a visit that came straight in, and that holds on a rented network:
    /// what the visit did does not answer the network, so it is something automated.
    /// </summary>
    [Fact]
    public void A_Visit_That_Came_Straight_In_And_Pressed_A_Key_On_A_Rented_Network_Is_Something_Automated()
    {
        var verdict = Engine.Classify(Visits.AReaderFromARentedServer() with { HadKeyboardInteraction = true });

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.KeyboardUsed);
    }

    /// <summary>
    /// The same key press on a visit another site sent answers the network: a person, with the
    /// network shown against them.
    /// </summary>
    [Fact]
    public void A_Visit_Another_Site_Sent_On_Which_A_Key_Was_Pressed_On_A_Rented_Network_Is_A_Person()
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            AutonomousSystem = Visits.MicrosoftNetwork,
            SentBy = SourceChannel.Search,
            HadKeyboardInteraction = true,
        });

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
        verdict.Contradicting.Should().Contain(signal => signal.Code == SignalCodes.HostingNetwork);
    }

    /// <summary>
    /// Another site sent it and nothing more happened: not a person, and not automation on the
    /// strength of the network alone either — the product cannot tell.
    /// </summary>
    [Fact]
    public void A_Visit_Another_Site_Sent_To_A_Rented_Network_That_Did_Nothing_More_Is_Not_Recognised()
    {
        var verdict = Engine.Classify(Visits.AGlance() with
        {
            AutonomousSystem = Visits.MicrosoftNetwork,
            SentBy = SourceChannel.Search,
        });

        verdict.Category.Should().Be(TrafficCategory.Unknown);
        verdict.Strength.Should().Be(EvidenceStrength.Strong);
    }

    /// <summary>
    /// The shape measured on Microsoft's network: straight in, 26 seconds on one page, the pointer
    /// pressed, with and without a key. Something automated: the network outweighs everything it did.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Visit_That_Came_Straight_To_A_Cloud_Network_Is_Still_Something_Automated_Whatever_Was_Pressed(bool typed)
    {
        var verdict = Engine.Classify(Visits.AHandsOffVisitFromMicrosoftsCloud() with { HadKeyboardInteraction = typed });

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.HostingNetwork);
    }

    /// <summary>
    /// A browser that asked for no language is a second objection, and the visit is weighed as any
    /// other from a rented network whatever sent it.
    /// </summary>
    [Fact]
    public void A_Cloud_Visit_That_Asked_For_No_Language_Stays_Automated_Whatever_Sent_It()
    {
        var verdict = Engine.Classify(Visits.AReaderOnACloudDesktop(SourceChannel.Search) with { Language = null });

        verdict.Category.Should().NotBe(TrafficCategory.LikelyHuman);
        verdict.Category.Should().NotBe(TrafficCategory.Unknown);
    }

    /// <summary>
    /// Seen only by something on the site's server, with no browser ever running: a page delivered
    /// to whatever asked for it, which a referring address does nothing to make a reader.
    /// </summary>
    [Fact]
    public void A_Rented_Visit_Only_A_Server_Saw_Stays_Automation_Whatever_Sent_It()
    {
        var verdict = Engine.Classify(Visits.Anonymous() with
        {
            AutonomousSystem = Visits.MicrosoftNetwork,
            SentBy = SourceChannel.Search,
        });

        verdict.Category.Should().NotBe(TrafficCategory.LikelyHuman);
        verdict.Category.Should().NotBe(TrafficCategory.Unknown);
    }

    /// <summary>
    /// Which site sent a visit changes nothing where no press, short stay, page merely shown or window
    /// taller than a screen decides the answer, on a network nobody rents servers on. Where one does, it
    /// decides: <see cref="Which_Site_Sent_A_Visit_Decides_A_Short_Stay"/>,
    /// <see cref="Which_Site_Sent_A_Visit_Decides_What_A_Press_Adds_To_A_Stay"/> and
    /// <see cref="Which_Site_Sent_A_Visit_Decides_Whether_A_Window_Counts"/> hold those.
    /// </summary>
    [Theory]
    [MemberData(nameof(OrdinaryVisits))]
    public void Which_Site_Sent_A_Visit_Changes_Nothing_Where_No_Press_Short_Stay_Page_Merely_Shown_Or_Tall_Window_Decides_It(
        string visit)
    {
        var observed = Ordinary[visit]();
        var straight = Engine.Classify(observed with { SentBy = SourceChannel.Direct });

        foreach (var kind in new[] { SourceChannel.Search, SourceChannel.Assistant, SourceChannel.Social, SourceChannel.Link })
        {
            var sent = Engine.Classify(observed with { SentBy = kind });

            sent.Category.Should().Be(straight.Category, "{0} sent by {1}", visit, kind);
            sent.Strength.Should().Be(straight.Strength, "{0} sent by {1}", visit, kind);
        }
    }

    /// <summary>The visits on ordinary networks the rule must leave alone, by name.</summary>
    public static TheoryData<string> OrdinaryVisits => new(Ordinary.Keys);

    private static readonly Dictionary<string, Func<SessionEvidence>> Ordinary = new(StringComparer.Ordinal)
    {
        ["a reader"] = () => Visits.AReader(),
        ["a glance"] = Visits.AGlance,
        ["a crawler naming itself"] = () => Visits.ANamedCrawler(
            "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)"),
        ["a confirmed crawler"] = () => Visits.AConfirmedCrawler("Google"),
        ["a nameless crawler"] = Visits.ANamelessCrawler,
        ["a scanner"] = Visits.AScanner,
        ["an owner signing in"] = Visits.AnOwnerSigningIn,
        ["a visit nothing is known about"] = Visits.Anonymous,
    };

    [Fact]
    public void The_Same_Visit_From_A_Household_Network_Is_Still_A_Person()
    {
        var verdict = TrafficClassifier.Current().Classify(Visits.AReader());

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
    }

    /// <summary>
    /// The reading happened and is not thrown away for being inconvenient. It is kept, shown as
    /// pointing the other way, and the verdict is stated less firmly because of it.
    /// </summary>
    [Fact]
    public void What_It_Did_Is_Kept_As_Evidence_Against_The_Verdict()
    {
        var verdict = TrafficClassifier.Current().Classify(Visits.AReaderFromARentedServer());

        verdict.Contradicting.Should().Contain(signal => signal.Code == SignalCodes.ReadTime);
    }

    [Fact]
    public void Where_It_Came_From_Is_Given_As_The_Reason()
    {
        var verdict = TrafficClassifier.Current().Classify(Visits.AReaderFromARentedServer());

        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.HostingNetwork);
    }

    /// <summary>
    /// One observation, however good, does not reach the firmest band on its own — and evidence
    /// pointing the other way holds it back further. Rule twelve, enforced rather than intended.
    /// </summary>
    [Fact]
    public void It_Is_Not_Stated_More_Firmly_Than_One_Observation_Supports()
    {
        var verdict = TrafficClassifier.Current().Classify(Visits.AReaderFromARentedServer());

        verdict.Strength.Should().BeOneOf(EvidenceStrength.Weak, EvidenceStrength.Moderate);
        verdict.Strength.Should().NotBe(EvidenceStrength.Verified);
    }

    /// <summary>
    /// Behaviour never reaches the band reserved for an identity confirmed against an operator's
    /// published addresses, and a datacentre is behaviour like any other.
    /// </summary>
    [Fact]
    public void Arriving_From_A_Datacentre_Never_Reaches_Verified()
    {
        var verdict = TrafficClassifier.Current().Classify(Visits.AReaderFromARentedServer());

        verdict.Strength.Should().NotBe(EvidenceStrength.Verified);
    }
}
