using Dewiride.Analytics.Application.Analytics;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Which bucket of a series an instant falls in on the calendar the website keeps, how far it is
/// from one bucket to the next, and the instant each bucket begins at.
/// </summary>
/// <remarks>
/// <para>
/// A bucket is first named on the website's own calendar: by the local date a day, a week or a
/// month begins on — a week on its Monday, as ISO 8601 has weeks, wherever in the world the website
/// is, and a month on its first — or, for a bucket an hour wide, by the top of its hour there. The
/// store documents rounding a date down to its Monday or to the first of its month without a zone
/// of the rounding's own, so the zone is applied once, where an instant becomes a local date.
/// </para>
/// <para>
/// A series fills the buckets nothing happened in by that name, a local date at a time, and only
/// then puts every bucket, counted or invented, at the instant it begins at. Filling the instants
/// instead goes wrong wherever the clocks skip midnight, as they do in Santiago and in Cairo on the
/// night they go forward: the store begins such a day at the first instant it has, one in the
/// morning, but a day's step from the midnight before lands an hour earlier, so every bucket
/// invented after it would sit an hour away from where a counted one begins, and each later day
/// would come back twice.
/// </para>
/// <para>
/// An hour has no date of its own to be filled by, so it is filled as an instant, sixty minutes at a
/// time from the top of the window's first hour. That is the top of every local hour wherever the
/// clocks change by a whole hour. Lord Howe Island changes them by half an hour, which neither that
/// step nor the store's own rounding to the hour follows, so an hourly series there that begins on
/// the day of a change, or reaches across one, fills hours half an hour away from the hours it
/// counts. It is the one zone that does, and the dashboard asks for hours only over two days or
/// fewer.
/// </para>
/// <para>
/// Written once here because both series cut their buckets by it — the one counted from activity
/// and the one counted from verdicts — so a week means one thing on either half of a chart.
/// </para>
/// </remarks>
internal static class CalendarBuckets
{
    /// <summary>The website's own time zone, as every statement that cuts buckets binds it.</summary>
    private const string Zone = "{time_zone:String}";

    /// <summary>
    /// Writes the bucket holding an instant, as the website's calendar names it.
    /// </summary>
    /// <param name="granularity">How wide each bucket is.</param>
    /// <param name="instant">
    /// An expression for the instant. A fixed fragment written by a compiler in this assembly and
    /// never by a caller.
    /// </param>
    /// <returns>
    /// The expression: the local date a day, a week or a month begins on, or the top of an hour as a
    /// date with time in the website's zone.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The granularity is not one the vocabulary defines.</exception>
    public static string Holding(TimeGranularity granularity, string instant) => CutBy(granularity).Bucket(instant);

    /// <summary>
    /// Writes the instant a bucket begins at, from the name <see cref="Holding"/> gave it.
    /// </summary>
    /// <param name="granularity">How wide each bucket is.</param>
    /// <param name="bucket">
    /// An expression for the bucket, as <see cref="Holding"/> writes it. A fixed fragment written by
    /// a compiler in this assembly and never by a caller.
    /// </param>
    /// <returns>The expression, a date with time in the website's zone.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The granularity is not one the vocabulary defines.</exception>
    public static string BeginningOf(TimeGranularity granularity, string bucket) => CutBy(granularity).Beginning(bucket);

    /// <summary>
    /// Writes how far it is from one bucket to the next, in the terms <see cref="Holding"/> names
    /// buckets in.
    /// </summary>
    /// <remarks>
    /// A calendar interval rather than a number of seconds or of days, so that a month of any length
    /// steps to the first of the next one.
    /// </remarks>
    /// <param name="granularity">How wide each bucket is.</param>
    /// <returns>The interval.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The granularity is not one the vocabulary defines.</exception>
    public static string Step(TimeGranularity granularity) => CutBy(granularity).Interval;

    /// <summary>
    /// Decides how buckets of one size are cut.
    /// </summary>
    /// <remarks>
    /// Which bucket an instant is in, where that bucket begins and how far away the next one is are
    /// decided together, so a size cannot be taught one without the others.
    /// </remarks>
    /// <param name="granularity">How wide each bucket is.</param>
    /// <returns>The cut.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The granularity is not one the vocabulary defines.</exception>
    private static Cut CutBy(TimeGranularity granularity) => granularity switch
    {
        TimeGranularity.Hour => new(static instant => $"toStartOfHour({instant}, {Zone})", static hour => hour, "INTERVAL 1 HOUR"),
        TimeGranularity.Day => new(LocalDate, Midnight, "INTERVAL 1 DAY"),
        TimeGranularity.Week => new(static instant => $"toMonday({LocalDate(instant)})", Midnight, "INTERVAL 1 WEEK"),
        TimeGranularity.Month => new(static instant => $"toStartOfMonth({LocalDate(instant)})", Midnight, "INTERVAL 1 MONTH"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(granularity),
            granularity,
            "Ask for buckets of an hour, a day, a week or a month."),
    };

    /// <summary>Writes the date an instant falls on where the website is.</summary>
    /// <param name="instant">An expression for the instant.</param>
    /// <returns>The expression, a date.</returns>
    private static string LocalDate(string instant) => $"toDate({instant}, {Zone})";

    /// <summary>
    /// Writes the instant a date begins at where the website is: its midnight, or the first instant
    /// the clocks show on that date where they skip midnight.
    /// </summary>
    /// <param name="date">An expression for the date.</param>
    /// <returns>The expression, a date with time in the website's zone.</returns>
    private static string Midnight(string date) => $"toDateTime({date}, {Zone})";

    /// <summary>
    /// How buckets of one size are cut.
    /// </summary>
    /// <param name="Bucket">Writes the bucket holding an instant, as the website's calendar names it.</param>
    /// <param name="Beginning">Writes the instant a bucket so named begins at.</param>
    /// <param name="Interval">How far it is from one bucket to the next, in the terms they are named in.</param>
    private readonly record struct Cut(
        Func<string, string> Bucket,
        Func<string, string> Beginning,
        string Interval);
}
