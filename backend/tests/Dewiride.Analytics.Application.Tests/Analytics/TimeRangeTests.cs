using Dewiride.Analytics.Application.Analytics;

namespace Dewiride.Analytics.Application.Tests.Analytics;

/// <summary>
/// Covers the window every telemetry question is asked over.
/// </summary>
/// <remarks>
/// The window is half-open by construction. Consecutive windows therefore tile without sharing a
/// boundary instant, which is the arithmetic that otherwise quietly inflates every
/// "yesterday against today" comparison an analytics product shows.
/// </remarks>
public sealed class TimeRangeTests
{
    private static readonly DateTimeOffset Midnight = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Keeps_The_Window_It_Was_Given()
    {
        var range = new TimeRange(Midnight, Midnight.AddDays(1));

        range.From.Should().Be(Midnight);
        range.To.Should().Be(Midnight.AddDays(1));
        range.Duration.Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public void Constructor_Rejects_An_Empty_Window()
    {
        var act = () => new TimeRange(Midnight, Midnight);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("to");
    }

    [Fact]
    public void Constructor_Rejects_An_Inverted_Window()
    {
        var act = () => new TimeRange(Midnight, Midnight.AddHours(-1));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("to");
    }

    [Fact]
    public void Contains_Includes_The_First_Instant()
    {
        var range = new TimeRange(Midnight, Midnight.AddDays(1));

        range.Contains(Midnight).Should().BeTrue();
    }

    [Fact]
    public void Contains_Excludes_The_Last_Instant()
    {
        var range = new TimeRange(Midnight, Midnight.AddDays(1));

        range.Contains(Midnight.AddDays(1)).Should().BeFalse();
    }

    /// <summary>
    /// The property the half-open form exists for: an instant on the boundary belongs to exactly
    /// one of two adjacent windows.
    /// </summary>
    [Fact]
    public void Adjacent_Windows_Tile_Without_Overlapping()
    {
        var boundary = Midnight.AddDays(1);
        var first = new TimeRange(Midnight, boundary);
        var second = new TimeRange(boundary, boundary.AddDays(1));

        first.Contains(boundary).Should().BeFalse();
        second.Contains(boundary).Should().BeTrue();
    }

    [Fact]
    public void EndingAt_Looks_Back_From_The_Given_Instant()
    {
        var range = TimeRange.EndingAt(Midnight, TimeSpan.FromHours(6));

        range.From.Should().Be(Midnight.AddHours(-6));
        range.To.Should().Be(Midnight);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EndingAt_Rejects_A_Duration_That_Does_Not_Move_Forwards(int hours)
    {
        var act = () => TimeRange.EndingAt(Midnight, TimeSpan.FromHours(hours));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("duration");
    }
}
