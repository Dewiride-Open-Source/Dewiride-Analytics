using System.Globalization;
using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Telemetry;

/// <summary>
/// Covers what can still be counted in full, given that the store deletes every report twelve
/// months after it arrived.
/// </summary>
public sealed class RetentionTests
{
    /// <summary>
    /// A stretch is counted from its own reports and, for its people, from verdicts on visits that
    /// began up to a day before it. The oldest of those goes twelve calendar months after it began,
    /// and counting stops a day before that.
    /// </summary>
    [Fact]
    public void A_Stretch_Can_Be_Counted_Until_A_Day_Before_Its_Oldest_Report_Could_Go()
    {
        var begins = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        Retention.CountableUntil(begins).Should().Be(new DateTimeOffset(2027, 2, 27, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// A month beginning at midnight in Kolkata begins the evening before in UTC, which is the clock
    /// reports are deleted by.
    /// </summary>
    [Fact]
    public void A_Stretch_Is_Counted_Until_A_Moment_On_The_Clock_Reports_Are_Deleted_By()
    {
        var augustInKolkata = new DateTimeOffset(2026, 7, 31, 18, 30, 0, TimeSpan.Zero);

        Retention.CountableUntil(augustInKolkata).Should().Be(new DateTimeOffset(2027, 7, 29, 18, 30, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// The store counts a year after the twenty-ninth of February as the twenty-eighth, and so does
    /// this: a moment it would delete a day early is a moment a count could miss.
    /// </summary>
    [Fact]
    public void A_Year_After_The_Twenty_Ninth_Of_February_Is_The_Twenty_Eighth()
    {
        var march = new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero);

        Retention.CountableUntil(march).Should().Be(new DateTimeOffset(2029, 2, 27, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Reports_Are_Kept_For_Twelve_Months()
    {
        Retention.ReportMonths.Should().Be(12);
    }

    /// <summary>
    /// The engine says where a website's detail begins, and the dashboard works the same day out for
    /// itself when it decides which days somebody may choose and which months they may open. Both are
    /// held to the same examples — these are the ones the dashboard's own tests give its kept-from day
    /// — so a stretch one of them offers is a stretch the other can answer.
    /// </summary>
    /// <param name="zone">The website's reporting zone.</param>
    /// <param name="now">The moment the question is asked.</param>
    /// <param name="firstDay">The first day the dashboard says is still held in full.</param>
    [Theory]
    [InlineData("Etc/UTC", "2026-08-18T09:37:12Z", "2025-08-20")]
    [InlineData("Etc/UTC", "2026-10-30T00:00:00Z", "2025-10-31")]
    [InlineData("Etc/UTC", "2026-08-18T20:00:00Z", "2025-08-20")]
    [InlineData("Asia/Kolkata", "2026-08-18T20:00:00Z", "2025-08-21")]
    [InlineData("America/New_York", "2026-08-18T20:00:00Z", "2025-08-20")]
    [InlineData("Etc/UTC", "2028-02-29T12:00:00Z", "2027-03-02")]
    public void Detail_Begins_On_The_Day_The_Dashboard_Keeps_From(string zone, string now, string firstDay)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(zone);
        var day = DateOnly.ParseExact(firstDay, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var detail = Retention.DetailFrom(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture), timeZone);

        detail.Should().Be(ZoneDays.StartOf(day, timeZone));
        ZoneDays.Of(detail, timeZone).Should().Be(day);
    }
}
