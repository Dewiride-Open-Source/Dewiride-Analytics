namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// The day a website's record begins on: the day of its first visit, where that can be known.
/// </summary>
/// <remarks>
/// <para>
/// The engine decides the day when it keeps a month, and the dashboard's record decides by the same
/// rule (<see cref="NothingWentUncounted"/>) where nothing kept says yet, so the day a screen names
/// before a month is kept is the day the month will carry.
/// </para>
/// <para>
/// A report is kept for twelve months, so the oldest report still held is the first a website ever
/// sent only where nothing it sent earlier can have gone without being counted. That holds where this
/// installation began keeping months while the month the website was added in could still be counted
/// in full: from then on every month it sent anything in was counted while all of it was held, so a
/// website with no first day kept has sent nothing older than what is held now. For a website added
/// longer before that, the oldest report says only how far back the store reaches, and is not taken
/// for the first.
/// </para>
/// <para>
/// A first day once kept stands. While the report it was taken from is still held, and the month that
/// report falls in on the calendar the website keeps now can still be counted, it is read again on
/// that calendar, so a change of zone moves it to the day it falls on there — by two days at most,
/// the widest two zones can differ — and the month it moves into keeps it. Where that month can no
/// longer be counted the day could be kept nowhere, so it stays as it was kept.
/// </para>
/// </remarks>
public static class FirstDays
{
    /// <summary>The furthest behind UTC any zone's clock runs.</summary>
    private static readonly TimeSpan WestmostOffset = TimeSpan.FromHours(-12);

    /// <summary>The furthest ahead of UTC any zone's clock runs.</summary>
    private static readonly TimeSpan EastmostOffset = TimeSpan.FromHours(14);

    /// <summary>
    /// The day a website's record begins on, where it can be known.
    /// </summary>
    /// <param name="kept">The earliest first day kept with any of its months, or nothing.</param>
    /// <param name="oldestHeld">
    /// The day the oldest report still held arrived on, on the calendar the website keeps now, or
    /// nothing where none is held.
    /// </param>
    /// <param name="oldestHeldCanBeCounted">
    /// Whether the month <paramref name="oldestHeld"/> falls in, on the calendar the website keeps now,
    /// can still be counted.
    /// </param>
    /// <param name="addedAt">When the website was added: the earliest any report of it can have arrived.</param>
    /// <param name="keepingSince">
    /// When this installation began keeping months, or nothing where that is not known.
    /// </param>
    /// <param name="asOf">The moment the question is asked at.</param>
    /// <returns>The day, or nothing where it cannot be known.</returns>
    public static DateOnly? Of(
        DateOnly? kept,
        DateOnly? oldestHeld,
        bool oldestHeldCanBeCounted,
        DateTimeOffset addedAt,
        DateTimeOffset? keepingSince,
        DateTimeOffset asOf)
    {
        if (kept is { } known)
        {
            // The kept day began no earlier than the day before it began on the clock reports are
            // stamped by, wherever the website was, so its first report is held until at least this.
            var firstGoes = Retention.ExpiresAfter(
                new DateTimeOffset(known.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1));

            return oldestHeldCanBeCounted
                && asOf < firstGoes
                && oldestHeld is { } held
                && Math.Abs(held.DayNumber - known.DayNumber) <= 2
                    ? held
                    : known;
        }

        return NothingWentUncounted(addedAt, keepingSince, asOf) ? oldestHeld : null;
    }

    /// <summary>
    /// Whether every report a website has sent was either counted into a kept month or is still held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where it is not known when keeping began, it is taken to begin now, which is the earliest
    /// anything can vouch for.
    /// </para>
    /// <para>
    /// The month the website was added in is taken on the calendar that puts it earliest: the month
    /// its added instant falls in on the clock furthest behind UTC, beginning when that month begins
    /// on the clock furthest ahead. Every zone's month the website was added in begins then or later,
    /// and the months were counted on whatever calendar the website kept at the time, so the answer
    /// is the same before and after any change of zone. The price is a stricter answer than the
    /// website's own calendar gives: a website added in the first hours of a month on its own
    /// calendar — twelve, and as many more as its clock runs ahead of UTC, or fewer as it runs behind —
    /// is taken as added in the month before, so keeping that began up to about a month before its own
    /// month stopped being countable leaves it without a first day; and any website is refused where
    /// keeping began within fourteen hours, less its clock's lead on UTC, of its own month's limit.
    /// </para>
    /// </remarks>
    /// <param name="addedAt">When the website was added.</param>
    /// <param name="keepingSince">When this installation began keeping months, or nothing.</param>
    /// <param name="asOf">The moment the question is asked at.</param>
    /// <returns>Whether nothing it sent can have gone uncounted.</returns>
    public static bool NothingWentUncounted(DateTimeOffset addedAt, DateTimeOffset? keepingSince, DateTimeOffset asOf)
    {
        var began = keepingSince is { } since && since < asOf ? since : asOf;
        var westmost = addedAt.ToOffset(WestmostOffset);
        var addedIn = new DateTimeOffset(westmost.Year, westmost.Month, 1, 0, 0, 0, EastmostOffset);

        return began < Retention.CountableUntil(addedIn);
    }
}
