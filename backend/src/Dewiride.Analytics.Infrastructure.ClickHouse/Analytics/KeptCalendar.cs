using ClickHouse.Driver.ADO.Readers;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Writes the calendar of a website's whole record — every month from the first it has a record
/// of to the one running now — beside what was kept of each month.
/// </summary>
/// <remarks>
/// <para>
/// The record begins at the earlier of its first kept month and the month of the oldest report
/// still held, so neither a month kept long after its reports went nor a month nobody has counted
/// yet is left out. A month inside the record that nothing was kept of comes back with nothing
/// kept, rather than being skipped: the caller answers it from its reports, or says it is missing.
/// </para>
/// <para>
/// Every instant comes from the store, the one authority on where a month begins in a zone, by the
/// same expression the series of the dashboard name their buckets with — so a month read from here
/// and the same month counted from its reports begin at one instant.
/// </para>
/// <para>
/// Each month is read as its newest count, whatever the store has merged so far: a month counted
/// twice is two rows until the store gets round to replacing one with the other, and nothing here
/// waits for that.
/// </para>
/// <para>
/// Used by the compiler that answers a person and by the one that counts for the system alike, so
/// the months a reader is shown are the months the counting chose from.
/// </para>
/// </remarks>
internal static class KeptCalendar
{
    /// <summary>
    /// Writes the statement.
    /// </summary>
    /// <param name="kept">
    /// What to read of each kept month, in the order it comes back. Written in this assembly and
    /// never by a caller.
    /// </param>
    /// <returns>
    /// The statement. Each row is a month, its first instant, the first instant of its year, the
    /// first instant after it, the day the oldest report still held arrived, whether anything was
    /// kept of it, and then <paramref name="kept"/>. It binds <c>site_id</c>, <c>to_ms</c> — the
    /// moment the record is read at — and <c>time_zone</c>.
    /// </returns>
    public static string Statement(IReadOnlyList<KeptColumn> kept)
    {
        ArgumentNullException.ThrowIfNull(kept);

        var read = string.Concat(kept.Select(column => $"\n            {column.Stored} AS kept_{column.Name},"));
        var named = string.Concat(kept.Select(column => $",\n    kept_{column.Name} AS {column.Name}"));

        return $$"""
            WITH
                kept AS
                (
                    SELECT
                        month AS kept_month,{{read}}
                        1 AS counted
                    FROM consolidated_months
                    WHERE site_id = {site_id:UUID}
                      AND month <= toStartOfMonth(toDate(fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC'), {time_zone:String}))
                    ORDER BY month, counted_at DESC
                    LIMIT 1 BY month
                ),
                reported AS
                (
                    SELECT toDate(server_ts, {time_zone:String}) AS reported_day
                    FROM events
                    WHERE site_id = {site_id:UUID}
                      AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
                    ORDER BY server_ts
                    LIMIT 1
                ),
                reach AS
                (
                    SELECT
                        (SELECT minOrNull(kept_month) FROM kept) AS first_kept,
                        (SELECT anyOrNull(reported_day) FROM reported) AS first_report_day,
                        toStartOfMonth(first_report_day) AS first_reported,
                        toStartOfMonth(toDate(fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC'), {time_zone:String})) AS running_month
                ),
                calendar AS
                (
                    SELECT
                        arrayJoin(arrayMap(
                            step -> addMonths(first_month, step),
                            range(toUInt32(dateDiff('month', first_month, running_month) + 1)))) AS calendar_month,
                        first_report_day
                    FROM
                    (
                        SELECT
                            assumeNotNull(coalesce(least(first_kept, first_reported), first_kept, first_reported)) AS first_month,
                            running_month,
                            first_report_day
                        FROM reach
                        WHERE first_kept IS NOT NULL
                           OR first_report_day IS NOT NULL
                    )
                )
            SELECT
                calendar_month AS month,
                toDateTime(calendar_month, {time_zone:String}) AS begins,
                toDateTime(toStartOfYear(calendar_month), {time_zone:String}) AS year_begins,
                toDateTime(addMonths(calendar_month, 1), {time_zone:String}) AS ends,
                first_report_day,
                counted{{named}}
            FROM calendar
            LEFT JOIN kept ON calendar.calendar_month = kept.kept_month
            ORDER BY month
            """;
    }
}

/// <summary>
/// One thing to read of a kept month.
/// </summary>
/// <param name="Stored">The column it is stored in.</param>
/// <param name="Name">What it comes back as.</param>
internal readonly record struct KeptColumn(string Stored, string Name);

/// <summary>
/// Where each part of a row of <see cref="KeptCalendar"/> is, and how it is read.
/// </summary>
internal static class KeptRows
{
    /// <summary>The first of the month.</summary>
    public const int Month = 0;

    /// <summary>The month's first instant.</summary>
    public const int Begins = 1;

    /// <summary>The first instant of the month's year.</summary>
    public const int YearBegins = 2;

    /// <summary>The first instant after the month.</summary>
    public const int Ends = 3;

    /// <summary>The day the oldest report still held arrived.</summary>
    public const int FirstReportDay = 4;

    /// <summary>Whether anything was kept of the month.</summary>
    public const int Counted = 5;

    /// <summary>The first of the columns read of a kept month.</summary>
    public const int FirstKept = 6;

    /// <summary>Whether anything was kept of the month a row describes.</summary>
    /// <param name="reader">The reader, on the row.</param>
    /// <returns><see langword="true"/> where it was.</returns>
    public static bool WasKept(ClickHouseDataReader reader) => reader.GetByte(Counted) == 1;

    /// <summary>Reads a day.</summary>
    /// <param name="reader">The reader, on the row.</param>
    /// <param name="column">Where the day is.</param>
    /// <returns>The day.</returns>
    public static DateOnly Day(ClickHouseDataReader reader, int column) =>
        DateOnly.FromDateTime(reader.GetDateTime(column));

    /// <summary>Reads a day that may be absent.</summary>
    /// <param name="reader">The reader, on the row.</param>
    /// <param name="column">Where the day is.</param>
    /// <returns>The day, or nothing.</returns>
    public static DateOnly? DayOrNothing(ClickHouseDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : Day(reader, column);
}
