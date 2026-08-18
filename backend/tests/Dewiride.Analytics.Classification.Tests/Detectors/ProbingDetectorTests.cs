using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Detectors;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Classification.Tests.Detectors;

/// <summary>
/// Proves the detector recognises a sweep for a way in, and does not turn a site's broken links
/// into an accusation.
/// </summary>
public sealed class ProbingDetectorTests
{
    private static readonly ProbingDetector Detector = new();

    [Fact]
    public void Asking_For_The_Places_Only_An_Intruder_Looks_For_Is_Reported()
    {
        var found = Detector.Examine(Visits.AScanner());

        var probing = found.Single(signal => signal.Code == SignalCodes.SensitivePaths);

        probing.Direction.Should().Be(SignalDirection.TowardAutomation);
        probing.Parameters["attemptCount"].Should().Be("4");
    }

    /// <summary>
    /// Changing the case of a path is among the first things a sweep tries, and it must not be
    /// enough to walk past the check.
    /// </summary>
    [Fact]
    public void Changing_The_Spelling_Of_A_Path_Does_Not_Get_Past_It()
    {
        var disguised = Asking("/WP-Admin/Setup-Config.PHP", "/.GIT/config", "/.Env");

        Detector.Examine(disguised).Should().Contain(signal => signal.Code == SignalCodes.SensitivePaths);
    }

    /// <summary>
    /// The path is written by whoever is probing. It decides what the detector reports and it
    /// never travels with the report, because nothing a visitor wrote belongs on a screen.
    /// </summary>
    [Fact]
    public void What_Was_Asked_For_Never_Travels_With_The_Report()
    {
        var found = Detector.Examine(Asking("/.env?<script>alert(1)</script>", "/.git/config", "/wp-login.php"));

        found.SelectMany(signal => signal.Parameters.Values)
            .Should().OnlyContain(value => !value.Contains("script", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_Site_With_A_Couple_Of_Broken_Links_Is_Not_Accused_Of_Anything()
    {
        var mostlyFine = new SessionEvidence
        {
            SessionKey = "reader",
            StartedAt = Visits.Noon,
            EndedAt = Visits.Noon.AddMinutes(6),
            Requests =
            [
                .. Visits.Pages(18, TimeSpan.FromMinutes(6)),
                new ObservedRequest(Visits.Noon.AddMinutes(3), "/posts/moved", 404),
                new ObservedRequest(Visits.Noon.AddMinutes(4), "/posts/gone", 404),
            ],
            Surfaces = [IngestSurface.CloudflareWorker],
        };

        Detector.Examine(mostlyFine).Should().BeEmpty();
    }

    /// <summary>
    /// A visit that is mostly dead ends was not following links, and a big site handing a genuine
    /// crawler the odd broken one is a different situation entirely.
    /// </summary>
    [Fact]
    public void A_Visit_Made_Mostly_Of_Dead_Ends_Weighs_More_Than_One_With_A_Few()
    {
        var mostly = Detector.Examine(Missing(present: 1, absent: 9))
            .Single(signal => signal.Code == SignalCodes.MissingPaths).Weight;

        var some = Detector.Examine(Missing(present: 20, absent: 4))
            .Single(signal => signal.Code == SignalCodes.MissingPaths).Weight;

        mostly.Should().BeGreaterThan(some);
    }

    private static SessionEvidence Asking(params string[] paths) => new()
    {
        SessionKey = "probe",
        StartedAt = Visits.Noon,
        EndedAt = Visits.Noon.AddSeconds(paths.Length),
        Requests =
        [
            .. paths.Select((path, index) =>
                new ObservedRequest(Visits.Noon.AddSeconds(index), path, (short)404)),
        ],
        Surfaces = [IngestSurface.CloudflareWorker],
    };

    private static SessionEvidence Missing(int present, int absent)
    {
        var requests = ImmutableArray.CreateBuilder<ObservedRequest>();

        requests.AddRange(Visits.Pages(present, TimeSpan.FromMinutes(1)));

        for (var index = 0; index < absent; index++)
        {
            requests.Add(new ObservedRequest(Visits.Noon.AddSeconds(index), $"/gone/{index}", 404));
        }

        return new SessionEvidence
        {
            SessionKey = "mixed",
            StartedAt = Visits.Noon,
            EndedAt = Visits.Noon.AddMinutes(1),
            Requests = requests.ToImmutable(),
            Surfaces = [IngestSurface.CloudflareWorker],
        };
    }
}
