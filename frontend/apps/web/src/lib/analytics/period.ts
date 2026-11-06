import type { ChartBy } from '@/lib/analytics/chart-by';
import { type Population, withPopulation } from '@/lib/analytics/people-only';

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
  'all-time',
] as const;

/** One of them. */
export type PeriodPreset = (typeof PRESETS)[number];

/**
 * The named periods with a stretch before them to be measured against: every one but all time,
 * which has nothing before it.
 */
export type MeasuredPreset = Exclude<PeriodPreset, 'all-time'>;

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

/**
 * How finely a period can be cut into buckets, finest first.
 *
 * Every bucket is cut where the website is. A week runs from Monday to Sunday, the way the
 * international standard counts one, and a month and a year are calendar ones. A bucket is named by
 * the moment its hour, day, week, month or year begins, and the first and last of a period hold only
 * the days the period reaches.
 */
export const GRANULARITIES = ['hour', 'day', 'week', 'month', 'year'] as const;

/** One of them. */
export type Granularity = (typeof GRANULARITIES)[number];

/**
 * How finely all time is cut: by the month, and by the year once the months run across one.
 *
 * Nothing finer. All time reaches back past the days a website still holds one by one, and what it
 * keeps of the months before them is each month's totals.
 */
export const ALL_TIME_GRANULARITIES = ['month', 'year'] as const;

/** One of them. */
export type AllTimeGranularity = (typeof ALL_TIME_GRANULARITIES)[number];

/** Why a stretch of days is not a period anybody can be shown. */
export type SpanProblem = 'unreadable' | 'backwards' | 'future' | 'too-long' | 'too-old';

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
 * The shortest period offered a week at a time: four weeks.
 *
 * A picture needs three buckets before it has a shape, and twenty-eight days always hold at least
 * three whole Monday to Sunday weeks, whichever day of the week they begin on.
 */
export const WEEKS_FROM_DAYS = 28;

/**
 * The shortest period offered a month at a time.
 *
 * Ninety days always reach into at least three calendar months, since no two months together run
 * past sixty-two days — and ninety days is the screen's own longest running period, so the last 90
 * days is where months begin.
 */
export const MONTHS_FROM_DAYS = 90;

/**
 * How many months the engine keeps the reports a period is counted from.
 *
 * Every report is deleted that long after it arrived, and every verdict that long after its visit
 * began. Held against the engine's own schema by a test, so the two cannot drift apart.
 */
export const KEPT_MONTHS = 12;

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
const DAYS_IN_A_WEEK = 7;

/**
 * How far before a day the reports that decide who its visitors were can reach.
 *
 * A visit is judged from everything its visitor did, and a visit runs for a day at the longest,
 * so the people counted on one day can rest on reports from the day before it.
 */
const LONGEST_VISIT = DAY;

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

/** A period's own days and the stretch immediately before them, which it is measured against. */
interface Stretches {
  readonly span: CivilSpan;
  readonly earlier: CivilSpan;
}

/** A period's own days and the part of them that has happened, which an earlier window matches. */
interface Happened {
  readonly span: DaySpan;
  /** The window those days are asked about, ending wherever the period has reached. */
  readonly window: AnalyticsWindow;
}

/** The window of an earlier stretch, cut to match the part of a period that has happened. */
type Matching = (happened: Happened, earlier: DaySpan, timeZone: string) => AnalyticsWindow;

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
  // The days every list reads under all time: a year back from today, which always reaches past the
  // first day still kept, so the lists begin on that day and go exactly as far back as the visits
  // themselves are kept. The figures above them reach back across the website's whole record —
  // from its first month kept, or still held, to now — from the totals kept of each month.
  'all-time': (today) => ({ first: shiftMonths(today, -KEPT_MONTHS), last: today }),
};

/** The run of days each named period with one before it is compared with. */
const EARLIER: Readonly<Record<MeasuredPreset, (span: CivilSpan) => CivilSpan>> = {
  today: byLength,
  yesterday: byLength,
  'last-7-days': byLength,
  'last-30-days': byLength,
  'last-90-days': byLength,
  'this-month': (span) => {
    const first = firstOfMonthBefore(span.first);

    return { first, last: { ...first, day: Math.min(span.last.day, daysInMonth(first)) } };
  },
  'last-month': (span) => ({
    first: firstOfMonthBefore(span.first),
    last: shiftDays(span.first, -1),
  }),
  'this-year': (span) => {
    const last = { ...span.last, year: span.last.year - 1 };

    return {
      first: { year: span.first.year - 1, month: 1, day: 1 },
      last: { ...last, day: Math.min(last.day, daysInMonth(last)) },
    };
  },
};

/**
 * The named periods a stretch somebody chose is recognised as, in the order they are tried.
 *
 * A single day first, so that the first of a month is today rather than this month. The running
 * periods are never among them: thirty days ending today is a stretch somebody chose, and naming it
 * "the last 30 days" would quietly make it move tomorrow.
 */
const NAMED_STRETCHES: readonly PeriodPreset[] = [
  'today',
  'yesterday',
  'this-month',
  'last-month',
  'this-year',
];

/**
 * The ways a period may be cut, by the shortest period each set is offered on, longest first.
 *
 * Settled by how long a period is and by nothing else, so a running period — the last 7, 30 or 90
 * days — is offered the same ways every day, while a calendar period gains them as it grows: This
 * month offers weeks from its twenty-eighth day. Two days or fewer offer no choice at all: they are
 * drawn an hour at a time.
 */
const CUTS: readonly { readonly from: number; readonly cuts: readonly ChartBy[] }[] = [
  { from: MONTHS_FROM_DAYS, cuts: ['day', 'week', 'month'] },
  { from: WEEKS_FROM_DAYS, cuts: ['day', 'week'] },
  { from: HOURLY_UP_TO_DAYS + 1, cuts: ['day'] },
];

/** The ways all time is cut while every day it covers falls in one calendar year. */
const MONTHS: readonly ChartBy[] = ['month'];

/** The ways all time is cut once the days it covers run across the first of January. */
const MONTHS_AND_YEARS: readonly ChartBy[] = ['month', 'year'];

/**
 * The named period each way of cutting is first offered on: the shortest of the running periods
 * long enough for it, and all time for a year, which no other period offers.
 *
 * A running period rather than a calendar one, because its length never changes — the screen it
 * moves to offers the way somebody chose whatever the date is.
 */
const SHORTEST: Readonly<Record<ChartBy, PeriodPreset>> = {
  day: 'last-7-days',
  week: 'last-30-days',
  month: 'last-90-days',
  year: 'all-time',
};

/**
 * The stretch a period's dashed lines are drawn from, for each way the period is cut.
 *
 * A dashed line is read off the bucket it is drawn beside, so each earlier bucket has to be the
 * same kind of bucket as its partner. An hour and a day are: the stretch the cards are measured
 * against is the line's stretch too. A week is moved back by whole weeks, so each earlier week
 * falls on the same weekdays and is cut by the period on the same days as the one it is drawn
 * beside; a month or a year by whole months, so each runs over the same days of the month. Each is
 * moved back about as far as the stretch the cards are measured against — to the nearest whole
 * week, or by whole calendar months — and never by less than its own length, so the line never
 * covers a day the picture also covers.
 */
const LINED_UP: Readonly<Record<Granularity, (stretches: Stretches) => CivilSpan>> = {
  hour: ({ earlier }) => earlier,
  day: ({ earlier }) => earlier,
  week: weeksBefore,
  month: monthsBefore,
  year: monthsBefore,
};

/**
 * How the window a period's dashed lines are drawn from is cut to the part of the period that has
 * happened, for each way the period is cut.
 *
 * An hour and a day are drawn from the stretch the cards are measured against, and cut exactly as
 * the cards' window is, after as long as the period has run. That stretch is as long as the period
 * for a running or a chosen one, though a month set beside the month before can be a few days longer
 * or shorter. A week, a month and a year are moved back by whole weeks or months, which need not
 * leave it as long — the ninety days to the first of December, moved back four months, run to
 * ninety-one — so it is cut on its own last day instead, at the moment of that day the period has
 * reached on its own.
 */
const MATCHED: Readonly<Record<Granularity, Matching>> = {
  hour: cutToLength,
  day: cutToLength,
  week: cutToLastDay,
  month: cutToLastDay,
  year: cutToLastDay,
};

/**
 * The days a period covers, in the site's own calendar.
 *
 * Never reaching back before the first day still kept. A chosen stretch is pulled forward to it,
 * and so are all time's lists, which reach a year back and so always begin on it. Of the other
 * named periods only this year can begin earlier, and only on the last day of the year, when its
 * first of January is already partly gone and the year is read from the second.
 *
 * @param period What is being looked at.
 * @param timeZone The site's reporting zone.
 * @param now The moment to measure from.
 * @returns The first and last day the period covers, both included.
 */
export function spanFor(period: Period, timeZone: string, now: Date): DaySpan {
  const today = calendarDayIn(timeZone, now);
  const kept = firstKeptDay(timeZone, now);

  if (period.kind === 'chosen') {
    return clampTo(period.first, period.last, today, kept);
  }

  const span = SPANS[period.preset](today);

  return writeSpan({ first: laterOf(span.first, kept), last: span.last });
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
  const closed = windowForSpan(spanFor(period, timeZone, now), timeZone);
  const running = Math.floor(now.getTime() / HOUR) * HOUR + HOUR;

  return {
    from: closed.from,
    to: new Date(Math.min(Date.parse(closed.to), running)).toISOString(),
  };
}

/**
 * The whole window a run of days covers, from its first midnight to the one after its last day.
 *
 * Closed at both ends, whether or not the last of those days has finished. It is the calendar
 * shape of a period rather than the part of it that has happened, which is what lets a period
 * still running be measured against the same stretch of an earlier one.
 *
 * @param span The days in question.
 * @param timeZone The site's reporting zone.
 */
export function windowForSpan(span: DaySpan, timeZone: string): AnalyticsWindow {
  return {
    from: startOfDayIn(timeZone, readDay(span.first)).toISOString(),
    to: startOfDayIn(timeZone, shiftDays(readDay(span.last), 1)).toISOString(),
  };
}

/**
 * The first day a website still holds in full, in its own calendar.
 *
 * Reports are deleted a fixed number of months after they arrive, so the oldest day the engine
 * holds is always partly gone. The first day counted from here begins at least a whole visit
 * after the oldest report still kept: a day from here on has every report of what was read on it,
 * and every report that decides who did the reading.
 *
 * It is where every stretch on the screen stops reaching back. A period is never measured against
 * days older than this — a comparison with days whose reports have gone is a comparison with
 * nothing, and reads as growth from nothing — and a stretch somebody chose never begins before it.
 *
 * @param timeZone The site's reporting zone.
 * @param now The moment to measure from.
 */
export function keptFrom(timeZone: string, now: Date): string {
  return writeDay(firstKeptDay(timeZone, now));
}

/**
 * The days a period is measured against: the stretch immediately before it, or nothing where that
 * stretch reaches back before the first day still kept — and nothing for all time, which has no
 * stretch before it.
 *
 * One rule, applied by what a period is. A period named after a calendar — a month, a year —
 * steps back by one of those, so this month is set beside last month and the days line up by
 * their dates. Everything else steps back by its own length, so a run of thirty days is set
 * beside the thirty before it.
 *
 * Where a date has no equivalent in the earlier month it is pulled back to the last day that
 * month has: the thirty-first of March is compared with the twenty-eighth of February rather than
 * with a day that does not exist.
 *
 * @param period What is being looked at.
 * @param timeZone The site's reporting zone.
 * @param now The moment to measure from.
 */
export function previousSpan(period: Period, timeZone: string, now: Date): DaySpan | null {
  const stretches = stretchesOf(period, timeZone, now);

  return stretches === null ? null : whereKept(stretches.earlier, timeZone, now);
}

/**
 * The window a period is measured against, or nothing where there is nothing to measure it
 * against.
 *
 * A period still running is compared with the same part of the earlier one rather than with the
 * whole of it. Today at ten in the morning has had ten hours in it and yesterday had twenty-four,
 * so measuring one against the other would report a collapse in traffic every morning and a
 * recovery every night.
 *
 * The same expression settles both cases. A period that has finished has run for its whole
 * calendar length, so the earlier period's own end is the nearer of the two and the comparison is
 * date-for-date; one still running has not, so the earlier window is cut to the part that matches.
 * A day the clocks changed on cannot skew a finished comparison, because the calendar end is what
 * wins there.
 *
 * @param period What is being looked at.
 * @param timeZone The site's reporting zone.
 * @param now The moment to measure from.
 */
export function previousWindow(
  period: Period,
  timeZone: string,
  now: Date,
): AnalyticsWindow | null {
  const earlier = previousSpan(period, timeZone, now);

  return earlier === null
    ? null
    : cutToLength(happenedIn(period, timeZone, now), earlier, timeZone);
}

/**
 * The days a period's dashed lines are drawn from, cut the way the picture is cut — or nothing
 * where they would reach back before the first day still kept, or where there is no stretch before
 * the period at all.
 *
 * By the hour and by the day they are the days the cards are measured against. By the week and by
 * the month they are moved back by whole weeks or whole months instead, so that each earlier bucket
 * covers the same weekdays, or the same days of the month, as the bucket it is drawn beside. The
 * cards and the line may then name slightly different days, and each says which days it means.
 *
 * Settled apart from the cards: the line can be drawn where the cards have nothing to compare with,
 * and the cards can compare where the line would reach too far back to be drawn.
 *
 * @param period What is being looked at.
 * @param granularity How finely the picture is cut.
 * @param timeZone The site's reporting zone.
 * @param now The moment to measure from.
 */
export function comparedSpan(
  period: Period,
  granularity: Granularity,
  timeZone: string,
  now: Date,
): DaySpan | null {
  const stretches = stretchesOf(period, timeZone, now);

  return stretches === null ? null : whereKept(LINED_UP[granularity](stretches), timeZone, now);
}

/**
 * The window a period's dashed lines are drawn from, cut to the part of it that matches the part
 * of this period that has happened — or nothing where there is no stretch to draw them from.
 *
 * By the hour and by the day it is cut exactly as the window the cards are measured against is,
 * after as long as this period has run. By the week and by the month it is cut on its own last day,
 * at the moment of that day this period has reached on its last, so the last earlier bucket covers
 * the same part of its week or month as the bucket it is drawn beside; a stretch moved back by
 * whole months can be a day longer or shorter than this one, and cut by length its last bucket
 * would end on the wrong day or not be drawn at all. Once this period has finished, the whole of
 * the earlier stretch is drawn.
 *
 * @param period What is being looked at.
 * @param granularity How finely the picture is cut.
 * @param timeZone The site's reporting zone.
 * @param now The moment to measure from.
 */
export function comparedWindow(
  period: Period,
  granularity: Granularity,
  timeZone: string,
  now: Date,
): AnalyticsWindow | null {
  const earlier = comparedSpan(period, granularity, timeZone, now);

  return earlier === null
    ? null
    : MATCHED[granularity](happenedIn(period, timeZone, now), earlier, timeZone);
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
 * How many buckets a picture of some days is cut into, known before any answer is.
 *
 * The buckets the engine cuts: every hour the window reaches into where the website is, or every
 * day, Monday to Sunday week, calendar month or calendar year the days reach into — however little
 * of it they cover, which is how all time's first month is cut at the day the website was first
 * measured on and still counted, and how today's window, ending on the hour in UTC, reaches part of
 * the way into an hour of a website half an hour ahead of it.
 *
 * @param span The days the picture covers.
 * @param window The window it is counted over, which is what an hour at a time is cut from.
 * @param granularity How finely it is cut.
 */
export function bucketsIn(
  span: DaySpan,
  window: AnalyticsWindow,
  granularity: Granularity,
): number {
  const first = readDay(span.first);
  const last = readDay(span.last);

  switch (granularity) {
    case 'hour':
      return Math.ceil((Date.parse(window.to) - Date.parse(window.from)) / HOUR);
    case 'day':
      return daysBetween(first, last);
    case 'week':
      return Math.ceil(daysBetween(mondayOf(first), last) / DAYS_IN_A_WEEK);
    case 'month':
      return (last.year - first.year) * 12 + last.month - first.month + 1;
    default:
      return last.year - first.year + 1;
  }
}

/**
 * Whether a period runs across more than one calendar year.
 *
 * A day written as `3 Jan` reads unambiguously within a year and ambiguously across one, and a
 * period may be a year and a day long. Where it crosses, the year is written beside the day.
 *
 * @param span The days in question.
 */
export function crossesYears(span: DaySpan): boolean {
  return span.first.slice(0, 4) !== span.last.slice(0, 4);
}

/**
 * The calendar month a day falls in, from its first day to its last.
 *
 * @param day The day in question, written the way a period names one.
 */
export function monthOf(day: string): DaySpan {
  const civil = readDay(day);

  return writeSpan({ first: firstOfMonth(civil), last: { ...civil, day: daysInMonth(civil) } });
}

/**
 * The ways a period may be cut, its own way first, or none where it is only ever drawn an hour at
 * a time.
 *
 * A picture needs three buckets before it has a shape, so each way is offered only on periods that
 * always hold three of them.
 *
 * @param span The days in question.
 */
export function cutsFor(span: DaySpan): readonly ChartBy[] {
  const days = daysIn(span);

  return CUTS.find((offer) => days >= offer.from)?.cuts ?? [];
}

/**
 * How finely to cut a period: the way somebody chose where the period offers it, and the period's
 * own way where it does not.
 *
 * A choice the period cannot honour is set aside rather than forgotten. Somebody who reads their
 * quarters by the week and narrows one to a single week has the choice back the moment the period
 * is long enough for it again.
 *
 * @param span The days in question.
 * @param chosen The way somebody chose, or nothing where they left it to the period.
 */
export function granularityFor(span: DaySpan, chosen: ChartBy | null): Granularity {
  return cutOf(cutsFor(span), chosen);
}

/**
 * How finely to cut a picture offered the given ways: the way somebody chose where it is one of
 * them, and the first of them where it is not — or an hour at a time where none is offered.
 *
 * @param offered The ways the picture can be cut, its own way first.
 * @param chosen The way somebody chose, or nothing where they left it to the picture.
 */
export function cutOf(offered: readonly ChartBy[], chosen: ChartBy | null): Granularity {
  return offered.find((cut) => cut === chosen) ?? offered[0] ?? 'hour';
}

/**
 * The ways all time may be cut, its own way first: by the month, and by the year as well once the
 * days of the website's record run across a first of January.
 *
 * Never by the day or the week. Of the months the visits themselves have gone from, what is kept is
 * each month's totals, and nothing about the days inside it.
 *
 * @param record The days of the website's record, from its first day to today, or nothing while
 * that is not known — which offers the months alone, the one way any record can be cut.
 */
export function allTimeCutsFor(record: DaySpan | null): readonly ChartBy[] {
  return record !== null && crossesYears(record) ? MONTHS_AND_YEARS : MONTHS;
}

/**
 * The named period a way of cutting the picture is first offered on, for a screen whose period is
 * too short for it.
 *
 * @param cut The way somebody wants the picture cut.
 */
export function shortestPeriodFor(cut: ChartBy): PeriodPreset {
  return SHORTEST[cut];
}

/**
 * Whether a period is all time: the website's whole record, from its first month kept, or still
 * held, to the month running now, read from what is kept of each month rather than from the visits
 * themselves.
 *
 * @param period What is being looked at.
 */
export function isAllTime(period: Period): boolean {
  return period.kind === 'preset' && period.preset === 'all-time';
}

/**
 * Whether a named period has a stretch before it to be measured against, which every one has but
 * all time.
 *
 * @param preset The period in question.
 */
export function hasEarlier(preset: PeriodPreset): preset is MeasuredPreset {
  return Object.hasOwn(EARLIER, preset);
}

/**
 * The day a moment falls in where the site is, written the way a period names one.
 *
 * A bucket on the chart is named by the instant it begins at, and the day it belongs to is settled
 * here rather than by the reader's clock: a day counted in Kolkata begins the evening before for
 * anybody reading in London, and is still that day.
 *
 * @param timeZone The site's reporting zone.
 * @param moment The instant in question.
 */
export function dayOf(timeZone: string, moment: Date): string {
  return writeDay(calendarDayIn(timeZone, moment));
}

/**
 * What day it is where the site is, which is not always what day it is where the reader is.
 *
 * @param timeZone The site's reporting zone.
 * @param now The moment to measure from.
 */
export function todayIn(timeZone: string, now: Date): string {
  return dayOf(timeZone, now);
}

/**
 * A period under the name the list offers it by, where it runs over exactly the days of one.
 *
 * A stretch pressed on the picture, or typed into both boxes of the chooser, that is today,
 * yesterday, this month, last month or this year where the website is comes back as that named
 * period, so the control reads as it would had the reader picked it from the list. A single day is
 * tried first, so the first of a month is today rather than this month. Any other stretch and a
 * named period come back as they went in, and a running period is never named: thirty days ending
 * today is still the thirty days somebody chose once tomorrow comes.
 *
 * The days each name stands for are the days that named period covers on screen, worked out the
 * same way, so the two can never disagree about when last month was — or about where this year
 * begins on its last day.
 *
 * Settled at the moment of choosing and never when an address is read: a link names the days its
 * sender put in it.
 *
 * @param period What somebody chose.
 * @param timeZone The site's reporting zone.
 * @param now The moment it was chosen.
 */
export function namedPeriod(period: Period, timeZone: string, now: Date): Period {
  if (period.kind === 'preset') {
    return period;
  }

  const named = NAMED_STRETCHES.find((preset) => {
    const span = spanFor({ kind: 'preset', preset }, timeZone, now);

    return span.first === period.first && span.last === period.last;
  });

  return named === undefined ? period : { kind: 'preset', preset: named };
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
 * Measured against the days as they are where the site is rather than where the reader is. That is
 * what stops somebody sitting an hour behind their own website being told their own afternoon has
 * not happened yet.
 *
 * A stretch is refused for its age before its length. The days still kept never run to more than
 * the longest stretch a screen can show, so a stretch too long to be shown always begins before the
 * first day still kept as well — and the sentence about its age names the first day that would
 * work, where the one about its length would only lead to a second refusal.
 *
 * @param first The day the stretch starts on.
 * @param last The day it ends on.
 * @param kept The days a stretch may be chosen from: the first still kept, and today where the
 * site is.
 */
export function problemWith(first: string, last: string, kept: DaySpan): SpanProblem | null {
  const start = parseDay(first);
  const end = parseDay(last);
  const earliest = parseDay(kept.first);
  const today = parseDay(kept.last);

  if (!start || !end || !earliest || !today) {
    return 'unreadable';
  }

  if (isAfter(start, end)) {
    return 'backwards';
  }

  if (isAfter(end, today)) {
    return 'future';
  }

  if (isAfter(earliest, start)) {
    return 'too-old';
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

/** What a screen is looking at, as far as a link between two screens carries it. */
export interface ScreenChoices {
  readonly period: Period;
  readonly population: Population;
}

/**
 * The address of a screen, carrying what is being looked at.
 *
 * The period and the population travel with the reader rather than being asked for again on
 * arrival, so that moving from a website's totals to the visits behind them stays on the same days
 * and the same people. It is the same mechanism that makes a screen worth sending to somebody:
 * what is in the address is what they see.
 *
 * A choice that is what every screen opens on is left out. An address that says what it would have
 * said anyway is one more thing in the bar for nothing. So is a choice the screen is not given,
 * because it answers about something that choice cannot narrow.
 *
 * @param screen One of the product's own addresses, which never asks a question of its own.
 * @param choices What the screen is handed of what is being looked at.
 */
export function withChoices(
  screen: string,
  { period = DEFAULT_PERIOD, population = 'everybody' }: Partial<ScreenChoices>,
): string {
  const asked = new URLSearchParams();

  if (!samePeriod(period, DEFAULT_PERIOD)) {
    asked.set(PERIOD_KEY, writePeriod(period));
  }

  const written = withPopulation(asked, population).toString();

  return written.length === 0 ? screen : `${screen}?${written}`;
}

/**
 * A period's own days and the stretch before them, both as calendar days — or nothing for a period
 * with no stretch before it.
 */
function stretchesOf(period: Period, timeZone: string, now: Date): Stretches | null {
  const days = spanFor(period, timeZone, now);
  const span = { first: readDay(days.first), last: readDay(days.last) };

  if (period.kind === 'chosen') {
    return { span, earlier: byLength(span) };
  }

  return hasEarlier(period.preset) ? { span, earlier: EARLIER[period.preset](span) } : null;
}

/** A stretch written out, or nothing where it begins before the first day still kept. */
function whereKept(span: CivilSpan, timeZone: string, now: Date): DaySpan | null {
  return isAfter(firstKeptDay(timeZone, now), span.first) ? null : writeSpan(span);
}

/** A period's own days, and the window asked about them so far. */
function happenedIn(period: Period, timeZone: string, now: Date): Happened {
  return { span: spanFor(period, timeZone, now), window: windowFor(period, timeZone, now) };
}

/**
 * The window of an earlier stretch, cut to the same length as the part of the period being looked
 * at that has happened.
 */
function cutToLength({ window }: Happened, earlier: DaySpan, timeZone: string): AnalyticsWindow {
  const whole = windowForSpan(earlier, timeZone);
  const elapsed = Date.parse(window.to) - Date.parse(window.from);
  const to = Math.min(Date.parse(whole.to), Date.parse(whole.from) + elapsed);

  return { from: whole.from, to: new Date(to).toISOString() };
}

/**
 * The window of an earlier stretch, cut on its own last day at the moment of that day the period
 * being looked at has reached on its last — or the whole of it, where the period has finished.
 *
 * Settled on the period's end rather than by the time the period has run for, which is the same
 * thing only where the two stretches are equally long.
 */
function cutToLastDay(
  { span, window }: Happened,
  earlier: DaySpan,
  timeZone: string,
): AnalyticsWindow {
  const whole = windowForSpan(earlier, timeZone);
  const reachedTo = Date.parse(window.to);

  if (reachedTo >= Date.parse(windowForSpan(span, timeZone).to)) {
    return whole;
  }

  const intoLastDay = reachedTo - startOfDayIn(timeZone, readDay(span.last)).getTime();
  const to = Math.min(
    Date.parse(whole.to),
    startOfDayIn(timeZone, readDay(earlier.last)).getTime() + intoLastDay,
  );

  return { from: whole.from, to: new Date(to).toISOString() };
}

/**
 * A period moved back by whole weeks: as many as the stretch it is measured against steps back,
 * to the nearest week, and never fewer than the weeks it takes to clear the period itself.
 */
function weeksBefore({ span, earlier }: Stretches): CivilSpan {
  const steppedBack = daysBetween(earlier.first, span.first) - 1;
  const weeks = Math.max(
    Math.round(steppedBack / DAYS_IN_A_WEEK),
    Math.ceil(daysBetween(span.first, span.last) / DAYS_IN_A_WEEK),
  );

  return {
    first: shiftDays(span.first, -DAYS_IN_A_WEEK * weeks),
    last: shiftDays(span.last, -DAYS_IN_A_WEEK * weeks),
  };
}

/**
 * A period moved back by whole calendar months: as many as the stretch it is measured against
 * steps back, and never fewer than the months the period itself reaches into.
 */
function monthsBefore({ span, earlier }: Stretches): CivilSpan {
  const months = Math.max(
    monthsBetween(earlier.first, span.first),
    monthsBetween(span.first, span.last) + 1,
  );

  return { first: shiftMonths(span.first, -months), last: shiftMonths(span.last, -months) };
}

/**
 * The first day a website still holds in full: the first midnight where the site is that falls a
 * whole visit or more after the oldest report the engine still keeps.
 */
function firstKeptDay(timeZone: string, now: Date): CivilDate {
  const oldest = monthsEarlier(now, KEPT_MONTHS) + LONGEST_VISIT;
  const day = calendarDayIn(timeZone, new Date(oldest));

  return startOfDayIn(timeZone, day).getTime() < oldest ? shiftDays(day, 1) : day;
}

/**
 * The same moment a number of calendar months earlier, on the clock reports are kept by.
 *
 * Counted in UTC, which is what every report is stamped and deleted by, and the way the engine
 * counts a month: a day the earlier month does not have becomes the last day it does, so a year
 * before the twenty-ninth of February is the twenty-eighth.
 */
function monthsEarlier(moment: Date, months: number): number {
  const day = {
    year: moment.getUTCFullYear(),
    month: moment.getUTCMonth() + 1,
    day: moment.getUTCDate(),
  };

  return epochOf(shiftMonths(day, -months)) + (moment.getTime() - epochOf(day));
}

/**
 * The moment a calendar day begins in a given zone: its first midnight, or the first instant its
 * clocks show where they skip midnight — where the engine begins the same day.
 *
 * Placed from the offset in force twelve hours either side of the day's midnight, each answer
 * re-read at the moment it gives, and the earliest of them that falls on the day is taken. A day
 * the clocks go back to midnight on, as they do in Havana and the Azores in autumn, has two
 * midnights and begins at the first; a day whose midnight the clocks skip begins when they resume.
 * The answer placed from after midnight always falls on the day, so there is always one to take.
 */
function startOfDayIn(timeZone: string, day: CivilDate): Date {
  const midnightAsUtc = epochOf(day);
  const onTheDay = [-12 * HOUR, 12 * HOUR]
    .map((around) => {
      const firstPass = midnightAsUtc - offsetAt(timeZone, new Date(midnightAsUtc + around));

      return midnightAsUtc - offsetAt(timeZone, new Date(firstPass));
    })
    .filter((moment) => epochOf(calendarDayIn(timeZone, new Date(moment))) === midnightAsUtc);

  return new Date(Math.min(...onTheDay));
}

/**
 * The formatters every day and offset in a zone is read with, built once for each zone.
 *
 * Building one costs far more than using it, and a picture cut by the week asks where a day begins
 * several times for every bucket in it. A formatter never changes once built, and there are only as
 * many as there are zones, so they are kept for as long as the page is open.
 */
const DAY_FORMATS = new Map<string, Intl.DateTimeFormat>();
const OFFSET_FORMATS = new Map<string, Intl.DateTimeFormat>();

/** The formatter a zone's days are read with. */
function dayFormat(timeZone: string): Intl.DateTimeFormat {
  const built =
    DAY_FORMATS.get(timeZone) ??
    new Intl.DateTimeFormat('en-CA', {
      timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    });

  DAY_FORMATS.set(timeZone, built);

  return built;
}

/** The formatter a zone's distance from UTC is read with. */
function offsetFormat(timeZone: string): Intl.DateTimeFormat {
  const built =
    OFFSET_FORMATS.get(timeZone) ??
    new Intl.DateTimeFormat('en', { timeZone, timeZoneName: 'longOffset' });

  OFFSET_FORMATS.set(timeZone, built);

  return built;
}

/** The day a moment falls on in a zone. */
function calendarDayIn(timeZone: string, moment: Date): CivilDate {
  const parts = dayFormat(timeZone).formatToParts(moment);

  const value = (type: Intl.DateTimeFormatPartTypes) =>
    Number(parts.find((part) => part.type === type)?.value ?? '0');

  return { year: value('year'), month: value('month'), day: value('day') };
}

/** How far a zone stands from UTC at a given moment, in milliseconds. */
function offsetAt(timeZone: string, moment: Date): number {
  const named = offsetFormat(timeZone)
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

/** The Monday that begins the week a day falls in, which is the day itself on a Monday. */
function mondayOf(day: CivilDate): CivilDate {
  const sinceMonday = (new Date(epochOf(day)).getUTCDay() + DAYS_IN_A_WEEK - 1) % DAYS_IN_A_WEEK;

  return shiftDays(day, -sinceMonday);
}

/**
 * The same day a number of calendar months earlier or later, pulled back to the last day of its
 * month where that month is shorter: a month before the thirty-first of March is the
 * twenty-eighth of February.
 */
function shiftMonths(day: CivilDate, months: number): CivilDate {
  const moved = new Date(Date.UTC(day.year, day.month - 1 + months, 1));
  const month = { year: moved.getUTCFullYear(), month: moved.getUTCMonth() + 1, day: 1 };

  return { ...month, day: Math.min(day.day, daysInMonth(month)) };
}

/** How many calendar months one day's month is after another's. */
function monthsBetween(first: CivilDate, last: CivilDate): number {
  return (last.year - first.year) * 12 + (last.month - first.month);
}

/** The first of the month a day falls in. */
function firstOfMonth(day: CivilDate): CivilDate {
  return { year: day.year, month: day.month, day: 1 };
}

/** The first of the month before the one a day falls in. */
function firstOfMonthBefore(day: CivilDate): CivilDate {
  return firstOfMonth(shiftDays(firstOfMonth(day), -1));
}

/** How many days the month a day falls in has. */
function daysInMonth(day: CivilDate): number {
  return new Date(Date.UTC(day.year, day.month, 0)).getUTCDate();
}

/** The run of days of the same length immediately before a given one. */
function byLength(span: CivilSpan): CivilSpan {
  const length = daysBetween(span.first, span.last);

  return { first: shiftDays(span.first, -length), last: shiftDays(span.last, -length) };
}

/**
 * A chosen stretch pulled into the range a website can answer for.
 *
 * The two ends are put in order, the far end is brought back to today — nobody is shown traffic
 * that has not happened yet — and the near end is brought forward to the first day still kept. A
 * stretch lying wholly before that day becomes that day alone, rather than a stretch running
 * backwards. An address somebody edited by hand, or a link sent long enough ago, therefore still
 * opens a working screen instead of a refusal.
 */
function clampTo(first: string, last: string, today: CivilDate, kept: CivilDate): DaySpan {
  const one = readDay(first);
  const other = readDay(last);
  const earlier = isAfter(one, other) ? other : one;
  const later = isAfter(one, other) ? one : other;
  const end = laterOf(kept, isAfter(later, today) ? today : later);
  const start = laterOf(earlier, kept);

  return writeSpan({ first: isAfter(start, end) ? end : start, last: end });
}

function isAfter(one: CivilDate, other: CivilDate): boolean {
  return epochOf(one) > epochOf(other);
}

/** Whichever of two days comes later. */
function laterOf(one: CivilDate, other: CivilDate): CivilDate {
  return isAfter(one, other) ? one : other;
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

function writeSpan(span: CivilSpan): DaySpan {
  return { first: writeDay(span.first), last: writeDay(span.last) };
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
