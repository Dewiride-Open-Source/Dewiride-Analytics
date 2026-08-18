namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// The token every call in this suite is made under.
/// </summary>
/// <remarks>
/// These tests reach real servers, so a hung call would otherwise hold the run open until the
/// whole suite timed out. Passing the running test's own token makes a cancelled or timed-out run
/// stop where it is, and names the test that was in flight.
/// </remarks>
internal static class Cancellation
{
    /// <summary>Token for the test currently running.</summary>
    public static CancellationToken Token => TestContext.Current.CancellationToken;
}
