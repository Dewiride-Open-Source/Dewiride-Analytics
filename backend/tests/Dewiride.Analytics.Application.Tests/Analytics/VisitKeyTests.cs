using Dewiride.Analytics.Application.Analytics;

namespace Dewiride.Analytics.Application.Tests.Analytics;

/// <summary>
/// Covers how a visit is named, and what is refused as a name for one.
/// </summary>
/// <remarks>
/// A visit's identity reaches the engine from an address somebody typed, so parsing is the point at
/// which anything that is not one is turned away. Both halves travel as bound values afterwards, so
/// nothing here is the only thing standing between a hostile value and a statement — but a value
/// that cannot name a visit should be refused where it arrives rather than answered with an empty
/// list.
/// </remarks>
public sealed class VisitKeyTests
{
    private const string VisitorKey = "2f8a1c0b4d6e7f905a1b2c3d4e5f6071";

    private static readonly DateTimeOffset StartedAt =
        new(2026, 5, 1, 9, 30, 15, 250, TimeSpan.Zero);

    [Fact]
    public void An_Identity_Is_Written_As_The_Visitor_And_The_Instant_It_Began()
    {
        var visit = new VisitKey(VisitorKey, StartedAt);

        visit.ToString().Should().Be($"{VisitorKey}:{StartedAt.ToUnixTimeMilliseconds()}");
    }

    /// <summary>
    /// Reading an identity back has to produce what wrote it, or a verdict could not be matched to
    /// the activity behind it.
    /// </summary>
    [Fact]
    public void An_Identity_Read_Back_Is_The_One_That_Was_Written()
    {
        var written = new VisitKey(VisitorKey, StartedAt).ToString();

        VisitKey.TryParse(written, out var visit).Should().BeTrue();

        visit.VisitorKey.Should().Be(VisitorKey);
        visit.StartedAt.Should().Be(StartedAt);
    }

    /// <summary>
    /// Millisecond precision is what the store keeps, and the instant is half of what finds the
    /// visit again — rounding it to the second would find the wrong one or none at all.
    /// </summary>
    [Fact]
    public void An_Instant_Keeps_Its_Milliseconds()
    {
        VisitKey.TryParse($"{VisitorKey}:1777628415250", out var visit).Should().BeTrue();

        visit.StartedAt.Millisecond.Should().Be(250);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2f8a1c0b4d6e7f905a1b2c3d4e5f6071")]
    [InlineData(":1777628415250")]
    [InlineData("2f8a1c0b4d6e7f905a1b2c3d4e5f6071:")]
    [InlineData("2f8a1c0b4d6e7f905a1b2c3d4e5f6071:yesterday")]
    [InlineData("2f8a1c0b4d6e7f905a1b2c3d4e5f6071:-1")]
    [InlineData("2f8a1c0b4d6e7f905a1b2c3d4e5f6071: 1777628415250")]
    [InlineData("2f8a1c0b4d6e7f905a1b2c3d4e5f6071:1777628415250.0")]
    [InlineData("2F8A1C0B4D6E7F905A1B2C3D4E5F6071:1777628415250")]
    [InlineData("'; DROP TABLE events; --:1777628415250")]
    [InlineData("../../etc/passwd:1777628415250")]
    public void Anything_That_Is_Not_A_Visit_Is_Refused(string? value)
    {
        VisitKey.TryParse(value, out var visit).Should().BeFalse();

        visit.Should().Be(default(VisitKey));
    }

    /// <summary>
    /// A number too large to be an instant is a refusal rather than a throw. It arrives from an
    /// address somebody typed, and the difference is a bad request against a failed one.
    /// </summary>
    [Fact]
    public void An_Instant_Beyond_Any_Calendar_Is_Refused()
    {
        VisitKey.TryParse($"{VisitorKey}:9223372036854775807", out _).Should().BeFalse();
    }

    /// <summary>
    /// A key longer than any the engine derives is refused before it is examined, so nothing large
    /// is ever parsed.
    /// </summary>
    [Fact]
    public void A_Visitor_Key_Longer_Than_Any_Derived_One_Is_Refused()
    {
        var overlong = new string('a', 65);

        VisitKey.TryParse($"{overlong}:1777628415250", out _).Should().BeFalse();
    }

    [Fact]
    public void An_Identity_Cannot_Be_Built_Without_A_Visitor()
    {
        var act = () => new VisitKey(string.Empty, StartedAt);

        act.Should().Throw<ArgumentException>();
    }
}
