using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Telemetry;

/// <summary>
/// Covers what is accepted as the name of a visitor.
/// </summary>
/// <remarks>
/// Two addresses somebody can type carry one: the identity of a finished visit, and the visitor a
/// live trail is opened for. Both are refused here when they are the wrong shape, which is why the
/// shape is stated once — a second copy of it would eventually accept on one screen what it turned
/// away on the other. Neither is the only thing standing between a hostile value and a statement,
/// since a key always reaches the store as a bound value.
/// </remarks>
public sealed class VisitorKeysTests
{
    [Fact]
    public void A_Derived_Key_Is_A_Name_For_A_Visitor()
    {
        VisitorKeys.IsWellFormed("2f8a1c0b4d6e7f905a1b2c3d4e5f6071").Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("2F8A1C0B4D6E7F905A1B2C3D4E5F6071")]
    [InlineData("2f8a1c0b4d6e7f905a1b2c3d4e5f607g")]
    [InlineData("2f8a1c0b-4d6e-7f90-5a1b-2c3d4e5f6071")]
    [InlineData(" 2f8a1c0b4d6e7f905a1b2c3d4e5f6071")]
    [InlineData("'; DROP TABLE events; --")]
    [InlineData("../../etc/passwd")]
    [InlineData("%2e%2e%2f")]
    public void Anything_That_Is_Not_A_Derived_Key_Is_Refused(string value)
    {
        VisitorKeys.IsWellFormed(value).Should().BeFalse();
    }

    /// <summary>
    /// Refused before it is examined, so nothing large is ever walked character by character on the
    /// strength of somebody having typed it.
    /// </summary>
    [Fact]
    public void A_Key_Longer_Than_Any_Derived_One_Is_Refused()
    {
        VisitorKeys.IsWellFormed(new string('a', VisitorKeys.LongestKey)).Should().BeTrue();
        VisitorKeys.IsWellFormed(new string('a', VisitorKeys.LongestKey + 1)).Should().BeFalse();
    }
}
