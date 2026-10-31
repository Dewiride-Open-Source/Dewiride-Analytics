namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// The calendar a website keeps, worked out from the operating system's own zone data rather than
/// from the store under test.
/// </summary>
/// <remarks>
/// <para>
/// A test of where the store begins a day, a week or a month needs the right answer from somewhere
/// else, or it proves only that the store agrees with itself.
/// </para>
/// <para>
/// A test placed around a change of the clocks cannot name the date of one either. Reports age out
/// of the store twelve months after they arrive, so a fixed date sooner or later stops being one the
/// store still holds, and the test starts failing for that reason alone. Changes are found backwards
/// from the moment the test runs instead.
/// </para>
/// </remarks>
/// <param name="timeZoneId">The zone, by the identifier a website is configured with.</param>
internal sealed class ZoneCalendar(string timeZoneId)
{
    /// <summary>
    /// How finely a change of the clocks is looked for. Every zone in use changes its clocks on a
    /// quarter of an hour in UTC, so stepping by one finds the first instant on the new offset
    /// exactly.
    /// </summary>
    private static readonly TimeSpan Resolution = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Zones whose clocks skip midnight when they go forward: Santiago's on the first Sunday after
    /// the first of September and Cairo's on the last Friday of April, so that between them one has
    /// done so in the last eight months whenever a test runs, well inside what the store keeps.
    /// </summary>
    private static readonly string[] SkippingMidnight = ["America/Santiago", "Africa/Cairo"];

    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

    /// <summary>The zone, by the identifier a website is configured with.</summary>
    public string TimeZoneId { get; } = timeZoneId;

    /// <summary>
    /// Finds the latest date, among zones whose clocks skip midnight, that begins after midnight.
    /// </summary>
    /// <remarks>
    /// A zone skips midnight once a year at most, and a report placed around a day nearly a year
    /// old is close to being let go of, so the latest day is taken from whichever zone skipped
    /// midnight most recently. A zone that no longer skips it is passed over while another still
    /// does.
    /// </remarks>
    /// <param name="notAfter">The latest instant the date may hold.</param>
    /// <returns>The calendar of the zone the date was found in, and the date.</returns>
    /// <exception cref="InvalidOperationException">None of the zones skipped a midnight in the year before.</exception>
    public static (ZoneCalendar Calendar, DateOnly Day) LatestDayWithoutAMidnight(DateTimeOffset notAfter)
    {
        (ZoneCalendar Calendar, DateOnly Day)? latest = null;

        foreach (var calendar in SkippingMidnight.Select(id => new ZoneCalendar(id)))
        {
            if (calendar.DayWithoutAMidnight(notAfter) is { } day && (latest is null || day > latest.Value.Day))
            {
                latest = (calendar, day);
            }
        }

        return latest ?? throw new InvalidOperationException(
            $"None of {string.Join(", ", SkippingMidnight)} skipped a midnight in the year before "
            + $"{notAfter:O}, so no test can be placed around a day that begins after one.");
    }

    /// <summary>The Monday the week holding a date begins on, as ISO 8601 has weeks.</summary>
    /// <param name="date">The date.</param>
    /// <returns>The Monday on or before it.</returns>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>The first of the month holding a date.</summary>
    /// <param name="date">The date.</param>
    /// <returns>The first of its month.</returns>
    public static DateOnly FirstOf(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>The date an instant falls on where the website is.</summary>
    /// <param name="instant">The instant.</param>
    /// <returns>Its local date.</returns>
    public DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _zone).DateTime);

    /// <summary>Whether an instant is midnight where the website is.</summary>
    /// <param name="instant">The instant.</param>
    /// <returns><see langword="true"/> when a local day begins at it.</returns>
    public bool IsMidnight(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, _zone).TimeOfDay == TimeSpan.Zero;

    /// <summary>The instant a date begins at where the website is.</summary>
    /// <param name="date">The date.</param>
    /// <returns>Its local midnight.</returns>
    /// <exception cref="ArgumentException">The clocks skip midnight on that date.</exception>
    public DateTimeOffset MidnightOn(DateOnly date) =>
        new(
            TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), _zone),
            TimeSpan.Zero);

    /// <summary>
    /// The first instant of a date where the website is: its midnight, or the first instant the
    /// clocks show on that date where they skip midnight.
    /// </summary>
    /// <remarks>
    /// Where the clocks go back to midnight, so that it comes twice, the earlier of the two: the
    /// date has already begun by the second.
    /// </remarks>
    /// <param name="date">The date.</param>
    /// <returns>The instant.</returns>
    public DateTimeOffset FirstInstantOn(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        while (_zone.IsInvalidTime(local))
        {
            local += Resolution;
        }

        var offset = _zone.IsAmbiguousTime(local)
            ? _zone.GetAmbiguousTimeOffsets(local).Max()
            : _zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>
    /// Finds the latest date, on or before the one an instant falls on, whose midnight the clocks
    /// skip.
    /// </summary>
    /// <param name="notAfter">The latest instant the date may hold.</param>
    /// <returns>The date, or <see langword="null"/> when the zone skipped no midnight in the year before.</returns>
    private DateOnly? DayWithoutAMidnight(DateTimeOffset notAfter)
    {
        var latest = DateOf(notAfter);

        for (var date = latest; date > latest.AddYears(-1); date = date.AddDays(-1))
        {
            if (_zone.IsInvalidTime(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified)))
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the latest change of the clocks at or before an instant.
    /// </summary>
    /// <param name="notAfter">The latest the change may be.</param>
    /// <returns>The first instant on the new offset.</returns>
    /// <exception cref="InvalidOperationException">The zone did not change its clocks in the year before.</exception>
    public DateTimeOffset LatestClockChange(DateTimeOffset notAfter)
    {
        var earliest = notAfter.AddYears(-1);
        var instant = new DateTimeOffset(notAfter.UtcTicks - (notAfter.UtcTicks % Resolution.Ticks), TimeSpan.Zero);

        for (; instant > earliest; instant -= Resolution)
        {
            if (_zone.GetUtcOffset(instant) != _zone.GetUtcOffset(instant - Resolution))
            {
                return instant;
            }
        }

        throw new InvalidOperationException(
            $"{_zone.Id} did not change its clocks in the year before {notAfter:O}, so it cannot "
            + "place a test around a change.");
    }

    /// <summary>
    /// Finds the latest month whose first midnight is on a different offset from the first midnight
    /// of the month before it.
    /// </summary>
    /// <param name="notAfter">The latest that first midnight may be.</param>
    /// <returns>The first of that month.</returns>
    /// <exception cref="InvalidOperationException">No month in the year before began on a new offset.</exception>
    public DateOnly LatestMonthBeginningOnANewOffset(DateTimeOffset notAfter)
    {
        var earliest = DateOf(notAfter).AddYears(-1);

        for (var month = FirstOf(DateOf(notAfter)); month > earliest; month = month.AddMonths(-1))
        {
            var begins = MidnightOn(month);

            if (_zone.GetUtcOffset(begins) != _zone.GetUtcOffset(MidnightOn(month.AddMonths(-1))))
            {
                return month;
            }
        }

        throw new InvalidOperationException(
            $"No month in the year before {notAfter:O} began on a new offset in {_zone.Id}, so it "
            + "cannot place a test either side of one.");
    }
}
