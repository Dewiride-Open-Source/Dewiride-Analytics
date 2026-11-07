using Dewiride.Analytics.Infrastructure.Posts;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers reading the dates websites write about themselves.
/// </summary>
public sealed class PublishedDateTests
{
    /// <summary>
    /// The present, handed to the reader rather than read off the machine, so that what counts as
    /// the future here is the same on every run.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Apia = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Apia");

    [Theory]
    [InlineData("2024-05-06", "2024-05-05T18:30:00+00:00")]
    [InlineData("2024-05-06T10:30", "2024-05-06T05:00:00+00:00")]
    [InlineData("2024-05-06T10:30:15", "2024-05-06T05:00:15+00:00")]
    [InlineData("2024-05-06 10:30:15", "2024-05-06T05:00:15+00:00")]
    public void A_Date_Without_A_Zone_Is_Read_In_The_Websites_Zone(string value, string expected)
    {
        PublishedDate.TryRead(value, Kolkata, Now, out var published).Should().BeTrue();

        published.Should().Be(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
        published.Offset.Should().Be(TimeSpan.Zero);
    }

    /// <summary>
    /// A website is not asked to pick one of the forms of a standard, and most of the near misses are
    /// one tool's idea of it. Every one of these says the same moment, whichever zone the website is in.
    /// </summary>
    /// <param name="value">The date, as a website wrote it.</param>
    [Theory]
    [InlineData("2024-05-06T10:30:00Z")]
    [InlineData("2024-05-06T10:30Z")]
    [InlineData("2024-05-06T10:30:00+00:00")]
    [InlineData("2024-05-06T10:30:00.000Z")]
    [InlineData("2024-05-06T10:30:00.1234567Z")]
    [InlineData("2024-05-06T10:30:00.123456789Z")]
    [InlineData("2024-05-06T16:00:00+05:30")]
    [InlineData("2024-05-06T16:00+05:30")]
    [InlineData("2024-05-06T05:30:00-05:00")]
    [InlineData("2024-05-06T16:00:00+0530")]
    [InlineData("2024-05-06T05:30:00-0500")]
    [InlineData("2024-05-06T11:30:00+01")]
    [InlineData("2024-05-06 10:30:00Z")]
    [InlineData("2024-05-06 16:00:00+05:30")]
    [InlineData("2024-05-06t10:30:00z")]
    [InlineData("  2024-05-06T10:30:00Z  ")]
    public void A_Date_With_An_Offset_Means_The_Moment_It_Says(string value)
    {
        PublishedDate.TryRead(value, NewYork, Now, out var published).Should().BeTrue();

        // Whatever fraction it carried is its own affair.
        published.Should().BeCloseTo(new DateTimeOffset(2024, 5, 6, 10, 30, 0, TimeSpan.Zero), TimeSpan.FromSeconds(1));
        published.Offset.Should().Be(TimeSpan.Zero);
    }

    /// <summary>
    /// The clocks go from two to three in New York, so half past two that morning is not a time
    /// anybody saw. It reads as the moment it would have been, on the far side of the hour.
    /// </summary>
    [Fact]
    public void A_Time_The_Clocks_Skipped_Is_Read_On_The_Far_Side_Of_The_Skip()
    {
        PublishedDate.TryRead("2024-03-10T02:30:00", NewYork, Now, out var published).Should().BeTrue();

        published.Should().Be(new DateTimeOffset(2024, 3, 10, 7, 30, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Samoa skipped the whole of a day in 2011, crossing the date line, so a date alone can fall in
    /// a skip too. Which standard time applies to a day that never happened depends on how much of
    /// that the machine's own zone data records, so what is pinned is that it reads, and within the day
    /// either side of the skip.
    /// </summary>
    [Fact]
    public void A_Day_The_Calendar_Skipped_Is_Still_Read()
    {
        PublishedDate.TryRead("2011-12-30", Apia, Now, out var published).Should().BeTrue();

        published.Should().BeCloseTo(new DateTimeOffset(2011, 12, 30, 10, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(1));
    }

    [Theory]
    [InlineData("1989-12-31T23:59:59Z", false)]
    [InlineData("1990-01-01T00:00:00Z", true)]
    [InlineData("2026-06-16T12:00:00Z", true)]
    [InlineData("2026-06-16T12:00:01Z", false)]
    [InlineData("2031-01-01", false)]
    [InlineData("1970-01-01", false)]
    [InlineData("0001-01-01", false)]
    [InlineData("9999-12-31", false)]
    public void A_Date_Before_1990_Or_Tomorrow_Is_Not_A_Publish_Date(string value, bool believed)
    {
        PublishedDate.TryRead(value, TimeZoneInfo.Utc, Now, out _).Should().Be(believed);
    }

    /// <summary>
    /// Near the ends of the calendar, working out the moment could fall off them, and a page must not
    /// be able to throw just by being dated the first day there has ever been.
    /// </summary>
    /// <param name="value">The date.</param>
    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("0001-01-01T00:00:00")]
    [InlineData("9999-12-31")]
    [InlineData("9999-12-31T23:59:59")]
    [InlineData("0001-01-01T00:00:00+14:00")]
    [InlineData("9999-12-31T23:59:59-14:00")]
    public void A_Date_At_Either_End_Of_The_Calendar_Does_No_Harm(string value)
    {
        var read = () => PublishedDate.TryRead(value, Kolkata, Now, out _);

        read.Should().NotThrow().Which.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("yesterday")]
    [InlineData("May 6, 2024")]
    [InlineData("06/05/2024")]
    [InlineData("5/6/2024 10:00:00 AM")]
    [InlineData("2024")]
    [InlineData("2024-05")]
    [InlineData("2024-13-01")]
    [InlineData("2024-02-30")]
    [InlineData("2024-05-06T25:00:00Z")]
    [InlineData("2024-05-06T10:30:00+99:00")]
    [InlineData("1714991400")]
    [InlineData("Mon, 06 May 2024 10:30:00 GMT")]
    public void Anything_Else_Is_Not_A_Date(string? value)
    {
        PublishedDate.TryRead(value, TimeZoneInfo.Utc, Now, out _).Should().BeFalse();
    }

    [Fact]
    public void A_Value_Longer_Than_Any_Date_Is_Not_Read_At_All()
    {
        var value = "2024-05-06T10:30:00Z" + new string(' ', 100);

        PublishedDate.TryRead(value, TimeZoneInfo.Utc, Now, out _).Should().BeFalse();
    }
}
