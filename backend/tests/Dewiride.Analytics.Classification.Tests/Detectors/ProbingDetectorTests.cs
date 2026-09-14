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

    /// <summary>
    /// A site's owner asks for exactly the paths a sweep asks for, and the difference is that the
    /// site answers them. A refusal is the sweep it usually is; a page served was a page that
    /// exists and was meant for whoever asked.
    /// </summary>
    [Theory]
    [InlineData(200, false)]
    [InlineData(301, false)]
    [InlineData(302, false)]
    [InlineData(401, true)]
    [InlineData(403, true)]
    [InlineData(404, true)]
    [InlineData(500, true)]
    public void Only_A_Request_The_Site_Refused_Counts_As_Probing(int status, bool counted)
    {
        var asked = Asking((short)status, "/.env", "/.git/config", "/wp-login.php");

        var found = Detector.Examine(asked).Where(signal => signal.Code == SignalCodes.SensitivePaths).ToArray();

        if (counted)
        {
            found.Should().ContainSingle().Which.Parameters["attemptCount"].Should().Be("3");
        }
        else
        {
            found.Should().BeEmpty();
        }
    }

    /// <summary>
    /// No status at all means only the page's own browser reported it, which is a page that was
    /// served and rendered far enough to run the tracker. A sweep executes nothing.
    /// </summary>
    [Fact]
    public void A_Page_Only_The_Browser_Saw_Was_Served_And_Is_Not_Probing()
    {
        var served = Asking((short?)null, "/wp-admin/", "/wp-login.php", "/wp-admin/post.php") with
        {
            Surfaces = [IngestSurface.BrowserTracker],
        };

        Detector.Examine(served).Should().NotContain(signal => signal.Code == SignalCodes.SensitivePaths);
    }

    [Fact]
    public void An_Owner_Opening_Their_Own_Administration_Panel_Is_Not_A_Sweep()
    {
        Detector.Examine(Visits.AnOwnerSigningIn()).Should().BeEmpty();
    }

    [Fact]
    public void A_Sweep_That_Also_Found_One_Real_Page_Is_Still_A_Sweep()
    {
        var sweep = Asking("/.env", "/.git/config", "/phpmyadmin/index.php") with
        {
            Requests =
            [
                .. Asking("/.env", "/.git/config", "/phpmyadmin/index.php").Requests,
                new ObservedRequest(Visits.Noon.AddSeconds(3), "/wp-login.php", 200),
            ],
        };

        Detector.Examine(sweep).Single(signal => signal.Code == SignalCodes.SensitivePaths)
            .Parameters["attemptCount"].Should().Be("3");
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

    private static SessionEvidence Asking(params string[] paths) => Asking((short)404, paths);

    private static SessionEvidence Asking(short? status, params string[] paths) => new()
    {
        SessionKey = "probe",
        StartedAt = Visits.Noon,
        EndedAt = Visits.Noon.AddSeconds(paths.Length),
        Requests =
        [
            .. paths.Select((path, index) =>
                new ObservedRequest(Visits.Noon.AddSeconds(index), path, status)),
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
