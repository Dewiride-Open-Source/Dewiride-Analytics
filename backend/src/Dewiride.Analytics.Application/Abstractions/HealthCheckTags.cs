namespace Dewiride.Analytics.Application.Abstractions;

/// <summary>
/// Tags that group health checks by the question they answer.
/// </summary>
/// <remarks>
/// Shared rather than repeated as a literal, because a tag is matched by string at the point the
/// probe endpoint is mapped: a typo does not fail to compile, it silently produces a probe that
/// checks nothing and reports healthy.
/// </remarks>
public static class HealthCheckTags
{
    /// <summary>
    /// Marks a check that must pass before the process can serve traffic correctly.
    /// </summary>
    /// <remarks>
    /// Checks carrying this tag are excluded from the liveness probe on purpose. A store that has
    /// briefly gone away is a reason to stop sending requests to this instance, not a reason for
    /// an orchestrator to kill and restart it — restarting cannot fix a database that is down,
    /// and doing so during a store's own restart turns a short outage into a crash loop.
    /// </remarks>
    public const string Readiness = "ready";
}
