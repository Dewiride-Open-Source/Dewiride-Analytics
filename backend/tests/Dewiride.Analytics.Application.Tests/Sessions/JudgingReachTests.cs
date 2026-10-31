using Dewiride.Analytics.Application.Sessions;

namespace Dewiride.Analytics.Application.Tests.Sessions;

/// <summary>
/// Covers where the verdicts on a website stop being complete.
/// </summary>
public sealed class JudgingReachTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);

    /// <summary>Judging that has fallen behind is as far as anything is settled.</summary>
    [Fact]
    public void Verdicts_Are_Complete_No_Further_Than_Judging_Has_Reached()
    {
        var behind = Now.AddHours(-6);

        JudgingReach.Settled(behind, Now, Idle).Should().Be(behind);
    }

    /// <summary>
    /// Nothing that began within a visit's timeout of the present can be known to be over, so it
    /// has no verdict yet however far judging has read.
    /// </summary>
    [Fact]
    public void Verdicts_Are_Never_Complete_Closer_To_The_Present_Than_A_Visit_Can_Be_Known_To_Be_Over()
    {
        JudgingReach.Settled(Now, Now, Idle).Should().Be(Now - Idle);
    }
}
