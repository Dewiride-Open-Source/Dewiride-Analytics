using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Application.Tests.Sessions;

/// <summary>
/// Proves what may be said about somebody who is still on the site.
/// </summary>
/// <remarks>
/// The whole product rests on not claiming more than the evidence carries, and a live screen is
/// where that is hardest: the evidence is still arriving, and the honest answer for most visitors is
/// nothing at all. These are the tests that make "we only say what cannot be taken back" a property
/// of the build rather than an intention.
/// </remarks>
public sealed class SettledIdentityTests
{
    private static readonly TrafficClassifier Engine = TrafficClassifier.Current();

    [Fact]
    public void A_Crawler_Whose_Owner_Vouches_For_It_Is_Named_At_Once()
    {
        var named = Read(Watching.ACrawlerWhoseOwnerVouchesForIt());

        named.Should().NotBeNull();
        named.Strength.Should().Be(EvidenceStrength.Verified);
        named.Category.Should().Be(TrafficCategory.KnownSearchCrawler);
    }

    [Fact]
    public void A_Crawler_That_Named_Itself_Is_Named_At_Once_And_Only_As_Suspected()
    {
        var named = Read(Watching.ACrawlerThatNamedItself());

        named.Should().NotBeNull();
        named.Category.Should().Be(TrafficCategory.SuspectedAiCrawler);
    }

    /// <summary>
    /// The words a visitor introduces itself with travel on every request it makes, so what they
    /// settle is settled from the first one.
    /// </summary>
    [Fact]
    public void A_Crawler_That_Called_Itself_One_Without_A_Name_Is_Named_At_Once()
    {
        var named = Read(Watching.ANamelessCrawler());

        named.Should().NotBeNull();
        named.Category.Should().Be(TrafficCategory.GenericWebCrawler);
    }

    [Fact]
    public void A_Browser_Saying_It_Is_Being_Driven_Is_Named_At_Once()
    {
        var named = Read(Watching.ADrivenBrowser());

        named.Should().NotBeNull();
        named.Category.Should().Be(TrafficCategory.BrowserAutomation);
    }

    [Fact]
    public void Something_Asking_For_The_Places_Only_An_Intruder_Looks_For_Is_Named_At_Once()
    {
        var named = Read(Watching.AScanner());

        named.Should().NotBeNull();
        named.Category.Should().Be(TrafficCategory.SecurityScanner);
    }

    [Fact]
    public void Somebody_Reading_Is_Not_Named_While_They_Are_Still_Reading()
    {
        Read(Watching.AReader()).Should().BeNull();
    }

    /// <summary>
    /// The case the rule exists for. On a site reported by both its own server and the browser, the
    /// server speaks first, and for that moment the engine's honest reading of a person is that
    /// nothing has executed — which it weighs towards machinery. Shown, it would put a real reader
    /// in the colour reserved for traffic nobody asked for, and take it back a second later.
    /// </summary>
    [Fact]
    public void A_Reader_Whose_Browser_Has_Not_Spoken_Yet_Is_Not_Named_Anything()
    {
        var visitor = Watching.AReaderWhoseBrowserHasNotSpokenYet();

        Engine.Classify(visitor).Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        Read(visitor).Should().BeNull();
    }

    /// <summary>
    /// How much of a visit was spent asking for pages that are not there is a share, and a share
    /// falls as the visit goes on — so a visitor named a scanner on it would stop being one while
    /// somebody watched. That is why the observation behind it is not one a live reading may name
    /// anybody by, and this is the traffic that proves it earns its exclusion.
    /// </summary>
    [Fact]
    public void A_Visitor_Whose_First_Pages_Were_Missing_Is_Never_Named_A_Scanner()
    {
        var visitor = Watching.AVisitorWhoseFirstPagesWereMissing();

        Engine.Classify(Watching.AsTheyWere(visitor, 3)).Category
            .Should().Be(TrafficCategory.SecurityScanner);

        Read(Watching.AsTheyWere(visitor, 3)).Should().BeNull();
        Read(visitor).Should().BeNull();
    }

    /// <summary>
    /// A site's owner opening their own administration pages asks for exactly the pages an intruder
    /// asks for, and is told apart from one by the site having answered. Given that answer, the
    /// engine names no scanner at any point of the visit and none once it is over; that the live
    /// reading hands it the same answer a finished visit does is proved against the store, in
    /// <c>LiveTrafficTests.A_Page_Both_Halves_Saw_Carries_The_Status_The_Site_Answered_With</c>.
    /// </summary>
    [Fact]
    public void An_Owner_Signing_In_Is_Never_Named_A_Scanner_Live_Or_Afterwards()
    {
        var owner = Watching.AnOwnerSigningIn();

        for (var requests = 1; requests <= owner.Requests.Length; requests++)
        {
            Read(Watching.AsTheyWere(owner, requests)).Should().BeNull();
        }

        Engine.Classify(owner).Category.Should().NotBe(TrafficCategory.SecurityScanner);
    }

    /// <summary>
    /// The property everything else here is in service of, replayed request by request over every
    /// kind of visitor these tests know about: once something has been named, it is still named when
    /// the visit goes on, and it is never afterwards called a person.
    /// </summary>
    [Fact]
    public void A_Conclusion_Shown_Once_Is_Never_Withdrawn()
    {
        foreach (var visitor in EveryKindOfVisitor())
        {
            var named = false;

            for (var requests = 1; requests <= visitor.Requests.Length; requests++)
            {
                var so = Read(Watching.AsTheyWere(visitor, requests));

                if (so is null)
                {
                    named.Should().BeFalse(
                        "a visitor named after {0} request(s) must still be named after {1}",
                        requests - 1,
                        requests);

                    continue;
                }

                named = true;

                so.Category.Should().Match(category =>
                    category != TrafficCategory.LikelyHuman
                    && category != TrafficCategory.Unknown
                    && category != TrafficCategory.InsufficientEvidence);
            }
        }
    }

    /// <summary>Every visitor these tests know how to build, so a new one is covered by the property.</summary>
    private static IEnumerable<SessionEvidence> EveryKindOfVisitor() =>
    [
        Watching.AReader(),
        Watching.AReaderWhoseBrowserHasNotSpokenYet(),
        Watching.ACrawlerThatNamedItself(),
        Watching.ANamelessCrawler(),
        Watching.ACrawlerWhoseOwnerVouchesForIt(),
        Watching.ADrivenBrowser(),
        Watching.AScanner(),
        Watching.AnOwnerSigningIn(),
        Watching.AVisitorWhoseFirstPagesWereMissing(),
    ];

    private static ClassificationVerdict? Read(SessionEvidence visitor) =>
        SettledIdentity.Of(Engine.Classify(visitor));
}
