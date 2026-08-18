using Dewiride.Analytics.Classification.Detectors;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Classification.Tests.Detectors;

/// <summary>
/// Proves the detector reports retrieval a person could not have performed, and leaves brisk
/// readers alone.
/// </summary>
public sealed class RetrievalDetectorTests
{
    private static readonly RetrievalDetector Detector = new();

    /// <summary>
    /// The threshold is set where nobody could be on the other side of it, not where an average
    /// person sits. A researcher opening a documentation site in a hurry must never trip it.
    /// </summary>
    [Fact]
    public void Somebody_Moving_Briskly_Through_A_Site_Is_Left_Alone()
    {
        var brisk = Sweep(pages: 12, across: TimeSpan.FromMinutes(5));

        Detector.Examine(brisk).Should().NotContain(signal => signal.Code == SignalCodes.RetrievalRate);
    }

    [Fact]
    public void Pages_Taken_Faster_Than_Anybody_Reads_Are_Reported()
    {
        var fast = Sweep(pages: 40, across: TimeSpan.FromSeconds(30));

        var rate = Detector.Examine(fast).Single(signal => signal.Code == SignalCodes.RetrievalRate);

        rate.Direction.Should().Be(SignalDirection.TowardAutomation);
        rate.Parameters["pageCount"].Should().Be("40");
        rate.Parameters["perMinute"].Should().Be("80");
    }

    /// <summary>
    /// A rate needs a span to be a rate. Requests that all landed in the same instant would
    /// otherwise produce an answer from arithmetic rather than from observation.
    /// </summary>
    [Fact]
    public void A_Session_With_No_Time_In_It_Has_No_Rate_To_Report()
    {
        var instant = Sweep(pages: 20, across: TimeSpan.Zero);

        Detector.Examine(instant).Should().NotContain(signal => signal.Code == SignalCodes.RetrievalRate);
    }

    [Fact]
    public void A_Handful_Of_Pages_Is_Too_Few_To_Say_Anything_About_Pace()
    {
        var few = Sweep(pages: 3, across: TimeSpan.FromSeconds(1));

        Detector.Examine(few).Should().NotContain(signal => signal.Code == SignalCodes.RetrievalRate);
    }

    [Fact]
    public void Covering_The_Whole_Site_In_One_Visit_Is_Reported()
    {
        var wide = Sweep(pages: 60, across: TimeSpan.FromHours(2));

        var breadth = Detector.Examine(wide).Single(signal => signal.Code == SignalCodes.RetrievalBreadth);

        breadth.Parameters["pageCount"].Should().Be("60");
    }

    private static SessionEvidence Sweep(int pages, TimeSpan across) => new()
    {
        SessionKey = "sweep",
        StartedAt = Visits.Noon,
        EndedAt = Visits.Noon + across,
        Requests = Visits.Pages(pages, across),
        Surfaces = [IngestSurface.CloudflareWorker],
    };
}
