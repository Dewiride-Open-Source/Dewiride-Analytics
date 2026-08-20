using System.Globalization;

namespace Dewiride.Analytics.Classification.Tests;

/// <summary>
/// Covers the stamp carried by every verdict and every aggregate row derived from one.
/// </summary>
/// <remarks>
/// Without it a number on a dashboard could not be reproduced or explained a month later, and
/// re-running a window against improved rules could not be told apart from a regression. The
/// rendered form is stored, so it has to be culture-independent: a machine whose locale writes a
/// comma for a decimal point must not write a different version string.
/// </remarks>
public sealed class RulesetVersionTests
{
    [Fact]
    public void Renders_As_Major_Dot_Minor()
    {
        new RulesetVersion(2, 7).ToString().Should().Be("2.7");
    }

    [Fact]
    public void Renders_The_Same_Whatever_The_Machine_Locale_Is()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            new RulesetVersion(2, 7).ToString().Should().Be("2.7");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("1.0", 1, 0)]
    [InlineData("2.7", 2, 7)]
    [InlineData("10.145", 10, 145)]
    public void Parses_The_Form_It_Renders(string value, int major, int minor)
    {
        RulesetVersion.Parse(value).Should().Be(new RulesetVersion(major, minor));
    }

    [Fact]
    public void Round_Trips_Through_Its_Rendered_Form()
    {
        var version = new RulesetVersion(4, 19);

        RulesetVersion.Parse(version.ToString()).Should().Be(version);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.")]
    [InlineData(".1")]
    [InlineData("1.2.3")]
    [InlineData("one.zero")]
    [InlineData("v1.0")]
    public void Refuses_Anything_That_Is_Not_Major_Dot_Minor(string value)
    {
        var act = () => RulesetVersion.Parse(value);

        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_A_Blank_Value(string blank)
    {
        var act = () => RulesetVersion.Parse(blank);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Orders_By_Major_First()
    {
        (new RulesetVersion(2, 0) > new RulesetVersion(1, 99)).Should().BeTrue();
    }

    [Fact]
    public void Orders_By_Minor_Within_A_Major()
    {
        (new RulesetVersion(1, 2) > new RulesetVersion(1, 1)).Should().BeTrue();
        (new RulesetVersion(1, 1) < new RulesetVersion(1, 2)).Should().BeTrue();
    }

    [Fact]
    public void Considers_Equal_Versions_Neither_Earlier_Nor_Later()
    {
        var version = new RulesetVersion(3, 4);

        (version >= new RulesetVersion(3, 4)).Should().BeTrue();
        (version <= new RulesetVersion(3, 4)).Should().BeTrue();
        (version > new RulesetVersion(3, 4)).Should().BeFalse();
    }

    [Fact]
    public void Sorts_Into_Release_Order()
    {
        RulesetVersion[] shuffled =
        [
            new(2, 0),
            new(1, 10),
            new(1, 2),
            new(10, 0),
        ];

        shuffled.Order().Should().Equal(new RulesetVersion(1, 2), new(1, 10), new(2, 0), new(10, 0));
    }

    /// <summary>
    /// A deliberate statement rather than a restatement of the constant.
    /// </summary>
    /// <remarks>
    /// Verdicts are filed under the ruleset that produced them, so moving this is a decision with
    /// consequences a customer can see: the same visit is judged one way under the old rules and
    /// another way under the new, and both answers stay on record. Failing here is the point —
    /// it obliges whoever changed the rules to say so here as well.
    /// </remarks>
    [Fact]
    public void The_Compiled_Ruleset_Is_The_One_That_Counts_A_Page_From_Every_Report_About_It()
    {
        RulesetVersion.Current.Should().Be(new RulesetVersion(3, 0));
    }
}
