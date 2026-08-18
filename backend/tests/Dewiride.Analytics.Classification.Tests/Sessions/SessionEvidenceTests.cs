using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Classification.Tests.Sessions;

/// <summary>
/// Proves the visit the engine reasons about keeps the distinctions it was built to keep.
/// </summary>
public sealed class SessionEvidenceTests
{
    /// <summary>
    /// A sweep can ask for tens of thousands of pages, so the pages carried back are capped while
    /// the count stays exact. Reading the count off the array would let a big enough visit look
    /// like a small one, which is the wrong direction to be wrong in.
    /// </summary>
    [Fact]
    public void The_Page_Count_Is_Exact_Even_When_The_Pages_Themselves_Were_Capped()
    {
        var visit = Visit() with { Requests = Visits.Pages(3, TimeSpan.FromMinutes(1)), PageCount = 40_000 };

        visit.PageCount.Should().Be(40_000);
        visit.Requests.Should().HaveCount(3);
    }

    /// <summary>
    /// A hand-written fixture says what it means by listing the pages, and should not have to
    /// repeat itself.
    /// </summary>
    [Fact]
    public void Left_Unsaid_The_Page_Count_Is_How_Many_Pages_Were_Listed()
    {
        var visit = Visit() with { Requests = Visits.Pages(7, TimeSpan.FromMinutes(1)) };

        visit.PageCount.Should().Be(7);
    }

    [Fact]
    public void A_Visit_Nothing_Could_Watch_Reports_Nothing_Rather_Than_Reporting_Silence()
    {
        var visit = Visit();

        visit.HadPointerInteraction.Should().BeNull();
        visit.HadKeyboardInteraction.Should().BeNull();
        visit.DeclaredWebDriver.Should().BeNull();
    }

    private static SessionEvidence Visit() => new()
    {
        SessionKey = "visitor:1",
        StartedAt = Visits.Noon,
        EndedAt = Visits.Noon.AddMinutes(1),
        Requests = ImmutableArray<ObservedRequest>.Empty,
        Surfaces = [IngestSurface.CloudflareWorker],
    };
}
