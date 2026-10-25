using Dewiride.Analytics.Domain.Classification;

namespace Dewiride.Analytics.Domain.Tests.Classification;

/// <summary>
/// Proves where a bookmark starts. How it moves is the store's to enforce, because only the store
/// sees every instance of the engine, and it is proved against a real database.
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
        progress.ReadThrough.Should().Be(Noon);
        progress.UpdatedAt.Should().Be(Noon);
    }
}
