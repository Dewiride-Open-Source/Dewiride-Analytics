namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// Days on a website's calendar, and the instants they begin at.
/// </summary>
public static class ZoneDays
{
    /// <summary>
    /// The day it is at an instant, on a website's calendar.
    /// </summary>
    /// <param name="instant">The instant.</param>
    /// <param name="zone">The website's reporting zone.</param>
    /// <returns>The day.</returns>
    public static DateOnly Of(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    /// <summary>
    /// The first instant of a day on a website's calendar.
    /// </summary>
    /// <remarks>
    /// Its midnight, where the clocks show one. Where they skip midnight it is the instant they
    /// resume, and where they show midnight twice it is the first of the two — which is where the
    /// telemetry store begins a day too, so a day counted there and a day worked out here are the
    /// same stretch of time.
    /// </remarks>
    /// <param name="day">The day.</param>
    /// <param name="zone">The website's reporting zone.</param>
    /// <returns>The instant.</returns>
    public static DateTimeOffset StartOf(DateOnly day, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var midnight = day.ToDateTime(TimeOnly.MinValue);

        if (zone.IsAmbiguousTime(midnight))
        {
            return new DateTimeOffset(midnight, zone.GetAmbiguousTimeOffsets(midnight).Max());
        }

        var shown = midnight;

        // A change of the clocks is a whole number of minutes, so the first minute past midnight the
        // clocks show is the instant they resumed.
        while (zone.IsInvalidTime(shown))
        {
            shown = shown.AddMinutes(1);
        }

        return new DateTimeOffset(shown, zone.GetUtcOffset(shown));
    }
}
