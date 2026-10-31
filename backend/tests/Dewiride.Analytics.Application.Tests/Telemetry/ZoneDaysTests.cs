using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Telemetry;

/// <summary>
/// Covers where a day begins on a website's calendar.
/// </summary>
/// <remarks>
/// The telemetry store begins a day at its first instant on the clock — its midnight, the moment the
/// clocks resume where they skip midnight, and the first of two where they show it twice — and a day
/// worked out here has to be the same stretch of time, or a period chosen on the dashboard and the
/// months counted in the store would not meet.
/// </remarks>
public sealed class ZoneDaysTests
{
    [Fact]
    public void An_Ordinary_Day_Begins_At_Midnight_Where_The_Website_Is()
    {
        var kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

        ZoneDays.StartOf(new DateOnly(2026, 10, 30), kolkata)
            .Should().Be(new DateTimeOffset(2026, 10, 29, 18, 30, 0, TimeSpan.Zero));
    }

    /// <summary>Santiago's clocks go from the end of Saturday straight to one on Sunday morning.</summary>
    [Fact]
    public void A_Day_Whose_Midnight_The_Clocks_Skip_Begins_When_They_Resume()
    {
        var santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

        ZoneDays.StartOf(new DateOnly(2026, 9, 6), santiago)
            .Should().Be(new DateTimeOffset(2026, 9, 6, 4, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Havana's and the Azores' clocks go back from one in the morning to midnight.</summary>
    [Fact]
    public void A_Day_The_Clocks_Show_Midnight_Twice_On_Begins_At_The_First()
    {
        var havana = TimeZoneInfo.FindSystemTimeZoneById("America/Havana");
        var azores = TimeZoneInfo.FindSystemTimeZoneById("Atlantic/Azores");

        ZoneDays.StartOf(new DateOnly(2026, 11, 1), havana)
            .Should().Be(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero));
        ZoneDays.StartOf(new DateOnly(2026, 10, 25), azores)
            .Should().Be(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void The_Day_An_Instant_Falls_On_Is_Read_Where_The_Website_Is()
    {
        var kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

        ZoneDays.Of(new DateTimeOffset(2026, 10, 29, 18, 30, 0, TimeSpan.Zero), kolkata).Should().Be(new DateOnly(2026, 10, 30));
        ZoneDays.Of(new DateTimeOffset(2026, 10, 29, 18, 29, 59, TimeSpan.Zero), kolkata).Should().Be(new DateOnly(2026, 10, 29));
    }
}
