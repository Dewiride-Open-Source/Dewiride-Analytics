using System.Globalization;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Reads the dates websites write about themselves.
/// </summary>
/// <remarks>
/// <para>
/// A date with an offset says what moment it means. One without, which is what a date alone is, can
/// only mean something on the website's own calendar, so that is the zone it is read in: the
/// first of the month in Sydney is still the last of the month in Rome. Where such a time falls in the
/// hour the clocks skip, the zone's standard offset is what applies to it, which reads it as the moment
/// that hour would have been, on the far side of the skip.
/// </para>
/// <para>
/// Beyond ISO 8601 and the W3C's profile of it, what is read is a small number of near misses that are
/// common and unambiguous: a space for the <c>T</c>, an offset with no colon, an offset of hours alone, and
/// more fractional digits than anybody needs. Anything else is not read, and a guess is worse than no
/// date at all: a date wrong by a day shows on a calendar as a fact.
/// </para>
/// </remarks>
internal static class PublishedDate
{
    /// <summary>
    /// Longest value read. The longest form anybody writes is not half of this, and everything
    /// arriving here came from a website.
    /// </summary>
    private const int LongestValue = 64;

    private const int DateLength = 10;
    private const int LongestFraction = 7;

    /// <summary>
    /// How far from the ends of what the calendar type can hold a date has to be, for no offset to be
    /// able to take it past them.
    /// </summary>
    private const int DaysClearOfTheCalendarsEnds = 2;

    private static readonly DateTimeOffset Earliest = new(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] Unzoned =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
    ];

    private static readonly string[] Zoned =
    [
        "yyyy-MM-dd'T'HH:mm'Z'",
        "yyyy-MM-dd'T'HH:mmzzz",
        "yyyy-MM-dd'T'HH:mmzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzz",
    ];

    /// <summary>
    /// Reads the date a page says it was published.
    /// </summary>
    /// <remarks>
    /// A date from before there was a web to publish on, or from further ahead than time zones can
    /// explain, is a mistake or a placeholder and is treated as saying nothing.
    /// </remarks>
    /// <param name="value">What the page says, which may be anything.</param>
    /// <param name="zone">The website's reporting zone.</param>
    /// <param name="now">The present moment.</param>
    /// <param name="published">When it was published, in UTC.</param>
    /// <returns>Whether it says, believably.</returns>
    public static bool TryRead(string? value, TimeZoneInfo zone, DateTimeOffset now, out DateTimeOffset published) =>
        TryParse(value, zone, out published)
        && published >= Earliest
        && published <= now.AddDays(1);

    /// <summary>
    /// Reads a date, whatever date it is.
    /// </summary>
    /// <param name="value">The date as it was written.</param>
    /// <param name="zone">The zone to read it in where it does not state an offset.</param>
    /// <param name="instant">The moment it means, in UTC.</param>
    /// <returns>Whether it could be read.</returns>
    public static bool TryParse(string? value, TimeZoneInfo zone, out DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(zone);

        instant = default;

        if (string.IsNullOrWhiteSpace(value) || value.Length > LongestValue)
        {
            return false;
        }

        var regular = InRegularForm(value);

        if (DateTimeOffset.TryParseExact(
                regular,
                Zoned,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var stated))
        {
            instant = stated.ToUniversalTime();

            return true;
        }

        return DateTime.TryParseExact(
                regular,
                Unzoned,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local)
            && TryPlace(local, zone, out instant);
    }

    /// <summary>
    /// Puts the common near misses in the form the formats above expect. Whatever is not a near miss
    /// comes out unchanged enough to go on not being read.
    /// </summary>
    private static string InRegularForm(string value)
    {
        var text = value.Trim().ToUpperInvariant();

        if (text.Length > DateLength && text[DateLength] == ' ')
        {
            text = string.Concat(text.AsSpan(0, DateLength), "T", text.AsSpan(DateLength + 1));
        }

        return WithColonInItsOffset(WithoutExtraFractionalDigits(text));
    }

    /// <summary>
    /// Takes a time's fraction down to what the calendar type holds, where it runs on past that, as
    /// nanoseconds do. A time is not read any differently for the difference.
    /// </summary>
    private static string WithoutExtraFractionalDigits(string text)
    {
        var point = text.IndexOf('.', StringComparison.Ordinal);

        if (point < 0)
        {
            return text;
        }

        var digits = text.AsSpan(point + 1).IndexOfAnyExceptInRange('0', '9');
        var length = digits < 0 ? text.Length - point - 1 : digits;

        return length > LongestFraction ? text.Remove(point + 1 + LongestFraction, length - LongestFraction) : text;
    }

    /// <summary>
    /// Writes an offset of four digits with its colon, which is how ISO 8601's basic form arrives at
    /// the end of a time in its extended form, from anything using PHP's own notion of the format.
    /// </summary>
    private static string WithColonInItsOffset(string text)
    {
        var sign = text.Length - 5;

        return sign > DateLength
            && text[sign] is '+' or '-'
            && !text.AsSpan(sign + 1).ContainsAnyExceptInRange('0', '9')
            ? text.Insert(sign + 3, ":")
            : text;
    }

    /// <summary>
    /// Works out the moment a time on a zone's calendar means.
    /// </summary>
    private static bool TryPlace(DateTime local, TimeZoneInfo zone, out DateTimeOffset instant)
    {
        instant = default;

        if (local < DateTime.MinValue.AddDays(DaysClearOfTheCalendarsEnds)
            || local > DateTime.MaxValue.AddDays(-DaysClearOfTheCalendarsEnds))
        {
            return false;
        }

        instant = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();

        return true;
    }
}
