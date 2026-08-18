using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Reports a visitor looking for things that are not there.
/// </summary>
/// <remarks>
/// <para>
/// The one thing only a surface in the request path can see, and the clearest signal in the whole
/// engine. A scanner is recognisable almost entirely by what it asks for: a stream of requests
/// for administration panels, configuration files and credential stores that were never published.
/// A browser tracker cannot see any of it, because a page that does not exist does not run a
/// script.
/// </para>
/// <para>
/// The paths matched below are the ones that only appear in an automated sweep. None of them is
/// reachable by following a link on an ordinary site, so a request for one is not a mistake
/// somebody made — but the visitor's own path is never carried into a signal parameter, because
/// it is written by whoever is probing and has no business reaching a screen.
/// </para>
/// </remarks>
public sealed class ProbingDetector : IDetector
{
    /// <summary>How many missing pages a session must ask for before the pattern means anything.</summary>
    private const int MissingBeforeItCounts = 3;

    /// <summary>
    /// Fragments that appear only in a sweep for a way in.
    /// </summary>
    /// <remarks>
    /// Matched against the lower-cased path, so a probe cannot dodge the check by changing case —
    /// which is among the first things a scanner tries.
    /// </remarks>
    private static readonly ImmutableArray<string> SoughtByIntruders =
    [
        "/.env",
        "/.git/",
        "/.aws/",
        "/.ssh/",
        "/wp-admin",
        "/wp-login",
        "/wp-config",
        "/xmlrpc.php",
        "/phpmyadmin",
        "/administrator/",
        "/config.php",
        "/.well-known/security.txt/../",
        "/vendor/phpunit",
        "/actuator/env",
        "/server-status",
        "/.svn/",
        "/credentials",
        "/id_rsa",
    ];

    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var found = ImmutableArray.CreateBuilder<Signal>();

        var missing = session.Requests.Count(request => request.StatusCode is >= 400 and < 500);

        if (missing >= MissingBeforeItCounts)
        {
            // Weighed by how much of the visit was spent failing. A big site will hand a genuine
            // crawler a few dead links; a visit that is mostly dead ends was not following links.
            var share = (double)missing / session.PageCount;
            var weight = share >= 0.5 ? 65 : 45;

            found.Add(Observed.Signal(
                SignalCodes.MissingPaths,
                SignalDirection.TowardAutomation,
                weight,
                ("missingCount", Observed.Number(missing)),
                ("pageCount", Observed.Number(session.PageCount))));
        }

        var intrusive = session.Requests.Count(request => IsSoughtByIntruders(request.Path));

        if (intrusive > 0)
        {
            found.Add(Observed.Signal(
                SignalCodes.SensitivePaths,
                SignalDirection.TowardAutomation,
                85,
                ("attemptCount", Observed.Number(intrusive))));
        }

        return found.ToImmutable();
    }

    private static bool IsSoughtByIntruders(string path) =>
        SoughtByIntruders.Any(fragment => path.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
