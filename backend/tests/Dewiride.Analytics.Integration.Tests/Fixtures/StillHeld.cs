namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Where a test that needs only some day the store still holds puts its reports.
/// </summary>
/// <remarks>
/// Reports age out of the store twelve months after they arrive, so a fixed date sooner or later
/// stops being one the store still holds, and the test starts failing for that reason alone. Six
/// months back leaves every day such a test reaches for in the past and well inside what is kept,
/// whenever the suite runs. Its day of the week changes from month to month, so a test that cares
/// which day of the week it is works that out for itself.
/// </remarks>
internal static class StillHeld
{
    /// <summary>The first instant, in UTC, of the first of the month six months before this one.</summary>
    public static readonly DateTimeOffset Midnight = Reckon();

    private static DateTimeOffset Reckon()
    {
        var now = TimeProvider.System.GetUtcNow();

        return new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-6);
    }
}
