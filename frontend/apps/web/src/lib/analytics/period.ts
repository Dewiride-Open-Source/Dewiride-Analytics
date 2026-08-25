/**
 * The periods the dashboard can be looked at over, and the exact window each one means.
 *
 * A window is resolved here rather than left to the engine's defaults because several questions are
 * asked about the same period — the totals, the graph, every list — and they have to agree. Reading
 * a graph whose days do not add up to the total above it is worse than not showing the graph.
 *
 * Every period is a run of whole calendar days in the site's own zone. That is the one shape that
 * covers both a period still running and one that has finished: the days are the same either way,
 * and only the far end differs.
 */

/** The periods offered by name, in the order they are listed. */
export const PRESETS = [
  'today',
  'yesterday',
  'last-7-days',
  'last-30-days',
  'last-90-days',
  'this-month',
  'last-month',
  'this-year',
] as const;

/** One of them. */
export type PeriodPreset = (typeof PRESETS)[number];

/**
 * What a screen is looking at: one of the named periods, or a stretch somebody chose.
 *
 * A chosen stretch names two calendar days in the site's zone, each written the way a date is:
 * `2026-08-24`.
 */
export type Period =
  | { readonly kind: 'preset'; readonly preset: PeriodPreset }
  | { readonly kind: 'chosen'; readonly first: string; readonly last: string };

/** The period a screen opens on. */
export const DEFAULT_PERIOD: Period = { kind: 'preset', preset: 'last-7-days' };

/** What a period is called in an address. */
export const PERIOD_KEY = 'period';

/** An inclusive run of whole calendar days in the site's zone, each written as `2026-08-24`. */
export interface DaySpan {
  readonly first: string;
  readonly last: string;
}

/** An inclusive start and an exclusive end, written the way the engine reads them. */
export interface AnalyticsWindow {
  readonly from: string;
  readonly to: string;
}

/** How finely a period is cut into buckets. */
export type Granularity = 'hour' | 'day';

/** Why a stretch of days is not a period anybody can be shown. */
export type SpanProblem = 'unreadable' | 'backwards' | 'future' | 'too-long';

/**
 * The longest stretch a period may cover.
 *
 * A year and a day, kept well under the longest the engine answers at once so that a stretch
 * crossing a clock change — which is a day of twenty-five hours — cannot push a period sitting
 * exactly at the limit over it and have the whole screen refused.
 */
export const LONGEST_SPAN_DAYS = 366;

/**
 * The longest period drawn an hour at a time.
 *
 * One day drawn as a single daily column is a graph with one point on it, and two days is two.
 * From three days a daily line has a shape, and an hourly one has more points than a phone can
 * draw apart.
 */
const HOURLY_UP_TO_DAYS = 2;

/**
 * What stands between the two ends of a chosen stretch when it is written down.
 *
 * Two full stops rather than a dash, which already separates the parts of a date and would leave a
 * stretch written as a run of eight numbers nobody could split the same way twice. Neither of them
 * has to be escaped to survive an address, so a link still reads as the two days it names.
 */
const SPAN_SEPARATOR = '..';

const HOUR = 3_600_000;
const DAY = 86_400_000;

/** A calendar day held as its parts, so month and year arithmetic stays exact. */
interface CivilDate {
  readonly year: number;
  readonly month: number;
  readonly day: number;
}

/** The same, as a run. */
interface CivilSpan {
  readonly first: CivilDate;
  readonly last: CivilDate;
}

/** Where each named period begins and ends, given what today is where the site is. */
const SPANS: Readonly<Record<PeriodPreset, (today: CivilDate) => CivilSpan>> = {
  today: (today) => ({ first: today, last: today }),
  yesterday: (today) => {
    const day = shiftDays(today, -1);

    return { first: day, last: day };
  },
  'last-7-days': (today) => ({ first: shiftDays(today, -6), last: today }),
  'last-30-days': (today) => ({ first: shiftDays(today, -29), last: today }),
  'last-90-days': (today) => ({ first: shiftDays(today, -89), last: today }),
  'this-month': (today) => ({ first: firstOfMonth(today), last: today }),
  'last-month': (today) => {
    const last = shiftDays(firstOfMonth(today), -1);

    return { first: firstOfMonth(last), last };
  },
  'this-year': (today) => ({ first: { year: today.year, month: 1, day: 1 }, last: today }),
};

/**
 * The days a period covers, in the site's own calendar.
 *
 * @param period What is being looked at.
 * @param timeZone The site's reporting zone.
 * @param now The moment to measure from.
 * @returns The first and last day the period covers, both included.
 */
export function spanFor(period: Period, timeZone: string, now: Date): DaySpan {
  const today = calendarDayIn(timeZone, now);

  if (period.kind === 'chosen') {
    return clampTo(period.first, period.last, today);
  }

  const span = SPANS[period.preset](today);

  return { first: writeDay(span.first), last: writeDay(span.last) };
}

/**
 * The window a period covers for a site, in that site's own days.
 *
 * The start is pinned to midnight in the site's zone so that the first day of the graph is a whole
 * day. Left unpinned it would begin part-way through a day and draw a first column that looks like
 * a collapse in traffic rather than a window that started at lunchtime.
 *
 * The end is whichever comes first of the midnight after the period's last day and the top of the
 * next hour. A period that has already finished is therefore closed and gives the same answer for
 * ever; one still running covers everything up to this moment while giving the same answer for a
 * whole hour, so a screen left open does not re-ask the same question on every render.
 *
 * The hour is rounded up strictly rather than to the nearest boundary at or after now. Asked at
 * exactly midnight, a period covering today would otherwise end where it starts, and a window with
 * no length in it is a question the engine is right to refuse.
 *
 * @param period What is being looked at.
 * @param timeZone The site's reporting zone.
 * @param now The moment to measure from.
 * @returns The window to ask about.
 */
export function windowFor(period: Period, timeZone: string, now: Date): AnalyticsWindow {
  const span = spanFor(period, timeZone, now);
  const from = startOfDayIn(timeZone, readDay(span.first));
  const closed = startOfDayIn(timeZone, shiftDays(readDay(span.last), 1));
  const running = Math.floor(now.getTime() / HOUR) * HOUR + HOUR;

  return {
    from: from.toISOString(),
    to: new Date(Math.min(closed.getTime(), running)).toISOString(),
  };
}

/**
 * How many days a period covers, counting both ends.
 *
 * @param span The days in question.
 */
export function daysIn(span: DaySpan): number {
  return daysBetween(readDay(span.first), readDay(span.last));
}

/**
 * How finely to cut a period.
 *
 * @param span The days in question.
 */
export function granularityFor(span: DaySpan): Granularity {
  return daysIn(span) <= HOURLY_UP_TO_DAYS ? 'hour' : 'day';
}

/**
 * What day it is where the site is, which is not always what day it is where the reader is.
 *
 * @param timeZone The site's reporting zone.
 * @param now The moment to measure from.
 */
export function todayIn(timeZone: string, now: Date): string {
  return writeDay(calendarDayIn(timeZone, now));
}

/**
 * The two moments a run of days is written between.
 *
 * Each is the midnight its day begins at where the site is, so writing either back out with the
 * site's zone gives that day and no other. Taken from any other instant, a day is the one before
 * or the one after for a reader far enough east or west of it.
 *
 * @param span The days in question.
 * @param timeZone The site's reporting zone.
 * @returns The moments the first and last day begin at.
 */
export function spanInstants(
  span: DaySpan,
  timeZone: string,
): { readonly first: Date; readonly last: Date } {
  return {
    first: startOfDayIn(timeZone, readDay(span.first)),
    last: startOfDayIn(timeZone, readDay(span.last)),
  };
}

/**
 * Why a stretch of days cannot be shown, or nothing at all when it can.
 *
 * Only ever a courtesy. Every stretch that reaches a window is pulled into range first, so this
 * exists to say what is wrong while somebody is still choosing rather than to keep a bad period
 * out — which is why it answers about the days themselves and leaves the window alone.
 *
 * Measured against the day it is where the site is rather than where the reader is. That is what
 * stops somebody sitting an hour behind their own website being told their own afternoon has not
 * happened yet.
 *
 * @param first The day the stretch starts on.
 * @param last The day it ends on.
 * @param today The day it is where the site is.
 */
export function problemWith(first: string, last: string, today: string): SpanProblem | null {
  const start = parseDay(first);
  const end = parseDay(last);
  const limit = parseDay(today);

  if (!start || !end || !limit) {
    return 'unreadable';
  }

  if (isAfter(start, end)) {
    return 'backwards';
  }

  if (isAfter(end, limit)) {
    return 'future';
  }

  return daysBetween(start, end) > LONGEST_SPAN_DAYS ? 'too-long' : null;
}

/**
 * Whether a word is one of the periods offered by name.
 *
 * @param value The word in question, from a list or from an address.
 */
export function isPreset(value: string): value is PeriodPreset {
  return (PRESETS as readonly string[]).includes(value);
}

/**
 * A period written the way it appears in an address.
 *
 * A named period is written under the name it is offered by and a chosen stretch as the two days
 * it runs between, so that a link somebody sends says what it is at a glance rather than carrying
 * a code only this product can read.
 *
 * @param period What is being looked at.
 */
export function writePeriod(period: Period): string {
  return period.kind === 'preset'
    ? period.preset
    : `${period.first}${SPAN_SEPARATOR}${period.last}`;
}

/**
 * Whether two periods are the same period.
 *
 * Compared as what they are written as rather than as objects, because two of them naming the same
 * days are the same period whether or not they are the same object. Read out of an address they
 * never are, so a screen comparing them any other way would find every period different from the
 * one before it and start again on every render.
 *
 * @param one A period.
 * @param other The one to compare it with.
 */
export function samePeriod(one: Period, other: Period): boolean {
  return writePeriod(one) === writePeriod(other);
}

/**
 * A period read back out of an address, or nothing where those are not a period.
 *
 * An address is written by whoever sent the link, so nothing here is taken on trust: a word that
 * names no period and a pair that is not two real days are both refused, and a screen given one
 * opens on the period it would have opened on anyway.
 *
 * @param written The period as it appears in the address.
 */
export function readPeriod(written: string): Period | null {
  if (isPreset(written)) {
    return { kind: 'preset', preset: written };
  }

  const [first, last, ...rest] = written.split(SPAN_SEPARATOR);

  if (rest.length > 0 || !first || !last) {
    return null;
  }

  return parseDay(first) !== null && parseDay(last) !== null
    ? { kind: 'chosen', first, last }
    : null;
}

/**
 * The address of a screen, carrying the period being looked at.
 *
 * The period travels with the reader rather than being asked for again on arrival, so that moving
 * from a website's totals to the visits behind them stays on the same days. It is the same
 * mechanism that makes a screen worth sending to somebody: what is in the address is what they see.
 *
 * A screen on the period everything opens on is left alone. An address that says what it would
 * have said anyway is one more thing in the bar for nothing.
 *
 * @param screen One of the product's own addresses, which never asks a question of its own.
 * @param period What is being looked at.
 */
export function withPeriod(screen: string, period: Period): string {
  return samePeriod(period, DEFAULT_PERIOD)
    ? screen
    : `${screen}?${PERIOD_KEY}=${encodeURIComponent(writePeriod(period))}`;
}

/**
 * The moment a calendar day begins in a given zone.
 *
 * Resolved in two passes. The first places midnight using the offset in force around the middle of
 * that day, which is wrong by an hour when the clocks change during it; the second re-reads the
 * offset at that first answer and places midnight again with it. Midday is within twelve hours of
 * local midnight everywhere, so the first pass always lands on the right day.
 */
function startOfDayIn(timeZone: string, day: CivilDate): Date {
  const midnightAsUtc = epochOf(day);
  const firstPass = midnightAsUtc - offsetAt(timeZone, new Date(midnightAsUtc + 12 * HOUR));

  return new Date(midnightAsUtc - offsetAt(timeZone, new Date(firstPass)));
}

/** The day a moment falls on in a zone. */
function calendarDayIn(timeZone: string, moment: Date): CivilDate {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(moment);

  const value = (type: Intl.DateTimeFormatPartTypes) =>
    Number(parts.find((part) => part.type === type)?.value ?? '0');

  return { year: value('year'), month: value('month'), day: value('day') };
}

/** How far a zone stands from UTC at a given moment, in milliseconds. */
function offsetAt(timeZone: string, moment: Date): number {
  const named = new Intl.DateTimeFormat('en', {
    timeZone,
    timeZoneName: 'longOffset',
  })
    .formatToParts(moment)
    .find((part) => part.type === 'timeZoneName')?.value;

  const measured = /GMT([+-])(\d{1,2}):(\d{2})/.exec(named ?? '');

  if (!measured) {
    return 0;
  }

  const [, sign, hours, minutes] = measured;
  const size = Number(hours) * HOUR + Number(minutes) * 60_000;

  return sign === '-' ? -size : size;
}

/**
 * The same day a number of days earlier or later.
 *
 * Counted in plain UTC rather than in the site's zone, because a calendar day is a label rather
 * than a length: the day before the fourteenth is the thirteenth whether that particular day ran
 * for twenty-three hours, twenty-four or twenty-five. Month and year ends fall out of it, so the
 * first of March less one day is the twenty-eighth or the twenty-ninth without a rule for either.
 */
function shiftDays(day: CivilDate, days: number): CivilDate {
  const moved = new Date(epochOf(day) + days * DAY);

  return {
    year: moved.getUTCFullYear(),
    month: moved.getUTCMonth() + 1,
    day: moved.getUTCDate(),
  };
}

/** The first of the month a day falls in. */
function firstOfMonth(day: CivilDate): CivilDate {
  return { year: day.year, month: day.month, day: 1 };
}

/**
 * A chosen stretch pulled into the range a website can answer for.
 *
 * The two ends are put in order, the far end is brought back to today — nobody is shown traffic
 * that has not happened yet — and the near end is brought forward where the stretch reaches
 * further back than a website's answers go. An address somebody edited by hand therefore still
 * opens a working screen instead of a refusal.
 */
function clampTo(first: string, last: string, today: CivilDate): DaySpan {
  const one = readDay(first);
  const other = readDay(last);
  const earlier = isAfter(one, other) ? other : one;
  const later = isAfter(one, other) ? one : other;
  const end = isAfter(later, today) ? today : later;
  const start = isAfter(earlier, end) ? end : earlier;
  const earliest = shiftDays(end, -(LONGEST_SPAN_DAYS - 1));

  return { first: writeDay(isAfter(earliest, start) ? earliest : start), last: writeDay(end) };
}

function isAfter(one: CivilDate, other: CivilDate): boolean {
  return epochOf(one) > epochOf(other);
}

/** How many days lie between two, counting both ends. */
function daysBetween(first: CivilDate, last: CivilDate): number {
  return Math.round((epochOf(last) - epochOf(first)) / DAY) + 1;
}

function epochOf(day: CivilDate): number {
  return Date.UTC(day.year, day.month - 1, day.day);
}

function writeDay(day: CivilDate): string {
  return `${pad(day.year, 4)}-${pad(day.month, 2)}-${pad(day.day, 2)}`;
}

/**
 * A day read back, or nothing where those are not the parts of a day.
 *
 * Written back out and compared with what arrived, so that a date naming the thirtieth of February
 * is refused rather than quietly becoming the second of March. The shape alone does not settle it:
 * every impossible day matches the pattern, and the calendar is the only thing that knows which of
 * them exist.
 */
function parseDay(written: string): CivilDate | null {
  const [, year, month, day] = /^(\d{4})-(\d{2})-(\d{2})$/.exec(written) ?? [];

  if (!year || !month || !day) {
    return null;
  }

  const read = { year: Number(year), month: Number(month), day: Number(day) };

  return writeDay(shiftDays(read, 0)) === written ? read : null;
}

/**
 * A day read back, falling to the beginning of the epoch where it cannot be.
 *
 * The clamping above then pulls that into range. A date is one of the few things a reader can put
 * in an address by hand, and a screen that refused to draw rather than showing them a sensible
 * period would be the wrong answer to a typo.
 */
function readDay(written: string): CivilDate {
  return parseDay(written) ?? { year: 1970, month: 1, day: 1 };
}

function pad(value: number, width: number): string {
  return String(value).padStart(width, '0');
}
