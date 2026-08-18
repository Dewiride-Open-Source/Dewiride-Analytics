using Dewiride.Analytics.Domain.Classification;

namespace Dewiride.Analytics.Domain.Tests.Classification;

/// <summary>
/// Proves the bookmark behaves the way two engines working the same site need it to.
/// </summary>
public sealed class ClassificationProgressTests
{
    private static readonly DateTimeOffset Noon = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SiteId = Guid.Parse("0197c0de-0000-7000-8000-000000000001");

    [Fact]
    public void A_New_Bookmark_Starts_Where_It_Was_Told_To()
    {
        var progress = new ClassificationProgress(SiteId, 1, 0, Noon);

        progress.SiteId.Should().Be(SiteId);
        progress.RulesetMajor.Should().Be(1);
        progress.RulesetMinor.Should().Be(0);
        progress.ClassifiedThrough.Should().Be(Noon);
    }

    [Fact]
    public void Moving_It_Forward_Records_When_It_Moved()
    {
        var progress = new ClassificationProgress(SiteId, 1, 0, Noon);

        progress.AdvanceTo(Noon.AddHours(6), Noon.AddHours(7)).Should().BeTrue();

        progress.ClassifiedThrough.Should().Be(Noon.AddHours(6));
        progress.UpdatedAt.Should().Be(Noon.AddHours(7));
    }

    /// <summary>
    /// Two instances of the engine may work the same site without coordinating, and one of them
    /// may be a moment behind. A bookmark that could move backwards would send it over the same
    /// stretch for ever.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void It_Never_Moves_Backwards(int hours)
    {
        var progress = new ClassificationProgress(SiteId, 1, 0, Noon);
        progress.AdvanceTo(Noon.AddHours(6), Noon.AddHours(7));

        progress.AdvanceTo(Noon.AddHours(6 + hours), Noon.AddHours(8)).Should().BeFalse();

        progress.ClassifiedThrough.Should().Be(Noon.AddHours(6));
        progress.UpdatedAt.Should().Be(Noon.AddHours(7));
    }
}
