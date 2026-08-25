using System.Collections.Immutable;
using System.Reflection;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Analytics;

/// <summary>
/// Covers what a caller may narrow a list of visits to.
/// </summary>
/// <remarks>
/// This is the boundary between what somebody asked for and what a statement is built from, and
/// everything past it is trusted. Two things are checked here and nowhere else: that a value the
/// engine never produces is refused rather than compared against a column that will never hold it,
/// and that a narrowing knows whether answering it means rebuilding a window's visits — which
/// decides which of two statements is sent.
/// </remarks>
public sealed class VisitNarrowingTests
{
    /// <summary>What is asked of a stored verdict, and costs nothing beyond the rows it leaves out.</summary>
    private static readonly string[] AskedOfTheVerdict =
    [
        nameof(VisitNarrowing.Categories),
        nameof(VisitNarrowing.LeastStrength),
        nameof(VisitNarrowing.LeastPages),
    ];

    /// <summary>What is asked of the activity a verdict was formed from, one example of each.</summary>
    private static readonly (string Dimension, VisitNarrowing Narrowing)[] AskedOfTheActivity =
    [
        (nameof(VisitNarrowing.Devices), new VisitNarrowing { Devices = [DeviceClass.Phone] }),
        (nameof(VisitNarrowing.SourceKinds), new VisitNarrowing { SourceKinds = [SourceChannel.Search] }),
        (nameof(VisitNarrowing.Browsers), new VisitNarrowing { Browsers = ["Firefox"] }),
        (nameof(VisitNarrowing.OperatingSystems), new VisitNarrowing { OperatingSystems = ["Android"] }),
        (nameof(VisitNarrowing.Countries), new VisitNarrowing { Countries = ["IN"] }),
        (nameof(VisitNarrowing.Towns), new VisitNarrowing { Towns = ["Jaipur"] }),
        (nameof(VisitNarrowing.Networks), new VisitNarrowing { Networks = ["Hetzner Online"] }),
        (nameof(VisitNarrowing.Sources), new VisitNarrowing { Sources = ["Google"] }),
        (nameof(VisitNarrowing.EntryPages), new VisitNarrowing { EntryPages = ["/pricing"] }),
    ];

    [Fact]
    public void Nothing_Asked_For_Narrows_Nothing()
    {
        VisitNarrowing.Nothing.Categories.Should().BeEmpty();
        VisitNarrowing.Nothing.LeastStrength.Should().BeNull();
        VisitNarrowing.Nothing.LeastPages.Should().Be(0);
        VisitNarrowing.Nothing.ReadsActivity.Should().BeFalse();
    }

    /// <summary>
    /// What the verdict itself holds is a comparison against a row that already exists. Reading
    /// activity to answer one would put the cost of the expensive statement on every default list.
    /// </summary>
    [Fact]
    public void Narrowing_By_What_The_Engine_Concluded_Reads_No_Activity()
    {
        var narrowing = new VisitNarrowing
        {
            Categories = [TrafficCategory.LikelyHuman],
            LeastStrength = EvidenceStrength.Moderate,
            LeastPages = 3,
        };

        narrowing.ReadsActivity.Should().BeFalse();
    }

    /// <summary>
    /// What a visitor was on, where they came from and where they arrived are properties of their
    /// events rather than of the verdict, so none of them can be answered without rebuilding the
    /// window's visits first.
    /// </summary>
    [Fact]
    public void Narrowing_By_What_A_Visit_Did_Reads_Its_Activity()
    {
        AskedOfTheActivity.Should()
            .AllSatisfy(asked => asked.Narrowing.ReadsActivity.Should().BeTrue(asked.Dimension));
    }

    /// <summary>
    /// A dimension added here and left out of the two lists above would be narrowed by a statement
    /// that never read what it describes, and the answer would hold visits the caller ruled out
    /// while looking exactly like the one they asked for.
    /// </summary>
    [Fact]
    public void Every_Narrowing_Is_Asked_Either_Of_The_Verdict_Or_Of_The_Activity()
    {
        var everything = typeof(VisitNarrowing)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is not null)
            .Select(property => property.Name);

        everything.Should().BeEquivalentTo(
            [.. AskedOfTheVerdict, .. AskedOfTheActivity.Select(asked => asked.Dimension)]);
    }

    /// <summary>
    /// The empty string is what the store holds where nothing was established, so asking for it
    /// asks to see the visits nothing is known about. Asking for no values at all asks nothing.
    /// </summary>
    [Fact]
    public void Asking_For_The_Visits_Nothing_Is_Known_About_Is_A_Narrowing()
    {
        new VisitNarrowing { Browsers = [""] }.ReadsActivity.Should().BeTrue();
    }

    [Fact]
    public void Asking_For_No_Values_At_All_Is_Not_A_Narrowing()
    {
        new VisitNarrowing { Browsers = [] }.ReadsActivity.Should().BeFalse();
    }

    /// <summary>
    /// A set nobody built is the same question as a set nobody filled, and reading one that was
    /// never built throws where every other dimension would have answered.
    /// </summary>
    [Fact]
    public void A_Dimension_Nobody_Built_Reads_As_One_Nobody_Filled()
    {
        var narrowing = new VisitNarrowing { Countries = default, Devices = default };

        narrowing.Countries.Should().BeEmpty();
        narrowing.Devices.Should().BeEmpty();
        narrowing.ReadsActivity.Should().BeFalse();
    }

    [Fact]
    public void Narrowing_To_A_Category_The_Engine_Cannot_Reach_Is_Refused()
    {
        var act = () => new VisitNarrowing { Categories = [(TrafficCategory)9999] };

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(VisitNarrowing.Categories));
    }

    [Fact]
    public void Narrowing_To_A_Strength_The_Engine_Does_Not_Report_Is_Refused()
    {
        var act = () => new VisitNarrowing { LeastStrength = (EvidenceStrength)9999 };

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(VisitNarrowing.LeastStrength));
    }

    [Fact]
    public void Narrowing_To_A_Kind_Of_Device_This_Product_Does_Not_Recognise_Is_Refused()
    {
        var act = () => new VisitNarrowing { Devices = [(DeviceClass)9999] };

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(VisitNarrowing.Devices));
    }

    [Fact]
    public void Narrowing_To_A_Kind_Of_Source_This_Product_Does_Not_Recognise_Is_Refused()
    {
        var act = () => new VisitNarrowing { SourceKinds = [(SourceChannel)9999] };

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(VisitNarrowing.SourceKinds));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Asking_For_Fewer_Than_No_Pages_Is_Refused(int leastPages)
    {
        var act = () => new VisitNarrowing { LeastPages = leastPages };

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(VisitNarrowing.LeastPages));
    }

    /// <summary>
    /// Bounded by how many values were named rather than by how long each one is. A page's address
    /// is as long as it is, and refusing a long one would refuse a real question about a real visit.
    /// </summary>
    [Fact]
    public void Naming_More_Values_Than_One_Question_Carries_Is_Refused()
    {
        var act = () => new VisitNarrowing { EntryPages = Many(VisitNarrowing.MostValues + 1) };

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(VisitNarrowing.EntryPages));
    }

    [Fact]
    public void Naming_As_Many_Values_As_One_Question_Carries_Is_Allowed()
    {
        var narrowing = new VisitNarrowing { EntryPages = Many(VisitNarrowing.MostValues) };

        narrowing.EntryPages.Should().HaveCount(VisitNarrowing.MostValues);
    }

    [Fact]
    public void A_Page_Address_Longer_Than_Anyone_Would_Type_Is_Still_A_Question()
    {
        var path = "/" + new string('a', 4000);

        new VisitNarrowing { EntryPages = [path] }.EntryPages.Should().ContainSingle().Which.Should().Be(path);
    }

    private static ImmutableArray<string> Many(int count) =>
        [.. Enumerable.Range(0, count).Select(number => $"/page-{number}")];
}
