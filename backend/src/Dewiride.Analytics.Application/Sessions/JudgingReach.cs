namespace Dewiride.Analytics.Application.Sessions;

/// <summary>
/// Where the verdicts on a website stop being complete.
/// </summary>
public static class JudgingReach
{
    /// <summary>
    /// The earlier of how far judging has reached and the last instant a visit can be known to be over.
    /// </summary>
    /// <remarks>
    /// Nothing that began within an idle timeout of the present can be known to be over, so nothing
    /// after that has a verdict yet. Nor does anything past where judging has actually reached, which
    /// falls behind the first whenever the engine has been stopped, is working through a backlog, or
    /// has not started on the website at all.
    /// </remarks>
    /// <param name="caughtUp">How far judging has reached.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="idleTimeout">How long a visitor may be quiet before a visit counts as over.</param>
    /// <returns>The instant.</returns>
    public static DateTimeOffset Settled(DateTimeOffset caughtUp, DateTimeOffset now, TimeSpan idleTimeout)
    {
        var settled = now - idleTimeout;

        return caughtUp < settled ? caughtUp : settled;
    }
}
