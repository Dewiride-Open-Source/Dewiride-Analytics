using Dewiride.Analytics.Application.Ingest;

namespace Dewiride.Analytics.Application.Tests.Ingest;

/// <summary>
/// Covers which websites a list of ignored senders covers, and which visitors it goes on turning
/// away once a visit from one of them has arrived.
/// </summary>
/// <remarks>
/// The near misses matter most. The sender is written by whoever sent the visit, so a name that
/// merely contains a listed one, or ends with it without a dot in between, belongs to somebody else
/// and must be kept.
/// </remarks>
public sealed class IgnoredSendersTests
{
    private static readonly Guid Site = new("0197c0de-0000-7000-8000-000000000001");

    private static readonly Guid AnotherSite = new("0197c0de-0000-7000-8000-000000000002");

    private const string Visitor = "9f2a1c4e8b6d0a3f";

    [Theory]
    [InlineData("aocr.org")]
    [InlineData("AOCR.org")]
    [InlineData("www.aocr.org")]
    [InlineData("aocr.org.")]
    [InlineData(" aocr.org ")]
    [InlineData("bücher.example")]
    [InlineData("xn--bcher-kva.example")]
    public void Takes_A_Host_Name(string value)
    {
        IgnoredSenders.IsHostName(value).Should().BeTrue();
    }

    /// <summary>
    /// Anything that is not a name somebody could send: an address, a pattern, a whole top-level
    /// domain, or a name with more than a host in it.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("org")]
    [InlineData("https://aocr.org")]
    [InlineData("aocr.org/spam")]
    [InlineData("aocr.org:443")]
    [InlineData("*.aocr.org")]
    [InlineData("a ocr.org")]
    [InlineData(".aocr.org")]
    [InlineData("aocr..org")]
    [InlineData("203.0.113.7")]
    [InlineData("[2001:db8::1]")]
    [InlineData("under_score.example")]
    public void Refuses_Anything_That_Is_Not_A_Host_Name(string? value)
    {
        IgnoredSenders.IsHostName(value).Should().BeFalse();
    }

    [Fact]
    public void Refuses_To_Be_Built_From_Something_That_Is_Not_A_Host_Name()
    {
        var building = () => new IgnoredSenders(["aocr.org", "https://spam.example"], new Visitors());

        building.Should().Throw<ArgumentException>().WithMessage("*https://spam.example*");
    }

    [Theory]
    [InlineData("aocr.org")]
    [InlineData("www.aocr.org")]
    [InlineData("WWW.AOCR.ORG")]
    [InlineData("deep.below.www.aocr.org")]
    [InlineData("www.aocr.org.")]
    public void Turns_Away_A_Visit_From_A_Listed_Website_Or_An_Address_Below_It(string sender)
    {
        var senders = new IgnoredSenders(["aocr.org"], new Visitors());

        senders.TurnsAway(Site, sender, Visitor).Should().BeTrue();
    }

    [Theory]
    [InlineData("notaocr.org")]
    [InlineData("aocr.org.example.net")]
    [InlineData("aocr.organic.example")]
    [InlineData("org")]
    [InlineData("www.google.com")]
    public void Keeps_A_Visit_From_A_Website_Merely_Named_Like_One(string sender)
    {
        var senders = new IgnoredSenders(["aocr.org"], new Visitors());

        senders.TurnsAway(Site, sender, Visitor).Should().BeFalse();
    }

    /// <summary>
    /// A browser sends a name written in another script in its ASCII form, so a name listed as it
    /// is read and the same name as it travels are one sender.
    /// </summary>
    [Fact]
    public void Knows_A_Name_Written_In_Another_Script_By_The_Form_A_Browser_Sends()
    {
        var senders = new IgnoredSenders(["bücher.example"], new Visitors());

        senders.TurnsAway(Site, "www.xn--bcher-kva.example", Visitor).Should().BeTrue();
    }

    [Fact]
    public void Remembers_The_Visitor_Whose_Visit_It_Turned_Away()
    {
        var visitors = new Visitors();

        new IgnoredSenders(["aocr.org"], visitors).TurnsAway(Site, "www.aocr.org", Visitor);

        visitors.Held.Should().Equal((Site, Visitor));
    }

    /// <summary>
    /// Only a page view names a sender. What its page sends next names none, and is turned away
    /// because of whose it is.
    /// </summary>
    [Fact]
    public void Turns_Away_What_A_Remembered_Visitor_Sends_Next()
    {
        var senders = new IgnoredSenders(["aocr.org"], new Visitors());

        senders.TurnsAway(Site, "www.aocr.org", Visitor);

        senders.TurnsAway(Site, sender: null, Visitor).Should().BeTrue();
    }

    [Fact]
    public void Keeps_What_Somebody_Else_Sends()
    {
        var senders = new IgnoredSenders(["aocr.org"], new Visitors());

        senders.TurnsAway(Site, "www.aocr.org", Visitor);

        senders.TurnsAway(Site, sender: null, "0000000000000000").Should().BeFalse();
    }

    /// <summary>
    /// A visitor key is derived per website, so the same key on another website is not the same
    /// visitor — and a visit turned away on one website says nothing about a visit to another.
    /// </summary>
    [Fact]
    public void Keeps_What_The_Same_Key_Sends_To_Another_Website()
    {
        var senders = new IgnoredSenders(["aocr.org"], new Visitors());

        senders.TurnsAway(Site, "www.aocr.org", Visitor);

        senders.TurnsAway(AnotherSite, sender: null, Visitor).Should().BeFalse();
    }

    /// <summary>
    /// With no key to remember a visitor by, the arrival is still turned away; what follows it
    /// cannot be told apart from anybody else's, and with no arrival before it never becomes a visit.
    /// </summary>
    [Fact]
    public void Turns_Away_An_Arrival_It_Cannot_Remember_And_Keeps_What_Cannot_Be_Placed()
    {
        var visitors = new Visitors();
        var senders = new IgnoredSenders(["aocr.org"], visitors);

        senders.TurnsAway(Site, "www.aocr.org", visitorKey: null).Should().BeTrue();
        senders.TurnsAway(Site, sender: null, visitorKey: null).Should().BeFalse();
        visitors.Held.Should().BeEmpty();
    }

    /// <summary>
    /// An installation that lists nothing pays nothing for the list: no report is turned away and
    /// the memory is never asked.
    /// </summary>
    [Fact]
    public void With_Nothing_Listed_Turns_Nothing_Away_And_Asks_Nothing()
    {
        var visitors = new Visitors();
        var senders = new IgnoredSenders([], visitors);

        senders.TurnsAway(Site, "www.aocr.org", Visitor).Should().BeFalse();
        senders.TurnsAway(Site, sender: null, Visitor).Should().BeFalse();
        visitors.Asked.Should().Be(0);
        visitors.Held.Should().BeEmpty();
    }

    /// <summary>A memory that never forgets, and counts how often it was asked.</summary>
    private sealed class Visitors : IIgnoredVisitors
    {
        private readonly List<(Guid Site, string Visitor)> _held = [];

        public IReadOnlyList<(Guid Site, string Visitor)> Held => _held;

        public int Asked { get; private set; }

        public void Remember(Guid siteId, string visitorKey) => _held.Add((siteId, visitorKey));

        public bool Recalls(Guid siteId, string visitorKey)
        {
            Asked++;
            return _held.Contains((siteId, visitorKey));
        }
    }
}
