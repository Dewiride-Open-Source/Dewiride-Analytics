import type { HeatStep } from '@/lib/analytics/heat';

/**
 * A website's posting calendar: the days it runs over laid out a month to a block, a week to a
 * column with Monday at the top, how strongly each day is shaded, and how a keyboard moves through
 * it.
 *
 * Every day is a civil date on the website's own calendar, written `2026-11-03`. Nothing is read
 * from the browser's clock or zone: the engine says which days the calendar runs over, and the
 * arithmetic is done on dates taken as UTC, where a day is always twenty-four hours long.
 */

const A_DAY = 86_400_000;

/** Days in a week, which is how far a week's move goes. */
const A_WEEK = 7;

/** A day of the calendar and how many posts were published on it. */
export interface PostingDay {
  readonly day: string;
  readonly posts: number;
}

/** The first and the last day the calendar runs over, the last of them today. */
export interface CalendarRange {
  readonly from: string;
  readonly to: string;
}

/** A week of a month's block. */
export interface CalendarWeek {
  /** The Monday it begins on, whether or not that falls inside the month. */
  readonly monday: string;
  /** Its seven places, Monday first: each a day, or nothing outside the month or the range. */
  readonly days: readonly (PostingDay | null)[];
}

/** One month's block. */
export interface CalendarMonth {
  /** Written `2026-11`. */
  readonly month: string;
  /** Each week of the month the calendar reaches, oldest first. */
  readonly weeks: readonly CalendarWeek[];
}

/** How many posts and how many days the calendar's days add up to. */
export interface PostingTotals {
  readonly posts: number;
  readonly days: number;
}

/** A day as an instant, midnight UTC, for the arithmetic. */
function instantOf(day: string): number {
  return Date.parse(`${day}T00:00:00Z`);
}

/** An instant at midnight UTC as the day it begins. */
function dayAt(instant: number): string {
  return new Date(instant).toISOString().slice(0, 10);
}

/** The day so many days after another, or before it where the number is below nought. */
function daysAfter(day: string, days: number): string {
  return dayAt(instantOf(day) + days * A_DAY);
}

/** Where a day falls in its week, Monday nought and Sunday six. */
function placeInWeek(day: string): number {
  return (new Date(instantOf(day)).getUTCDay() + 6) % A_WEEK;
}

/** The first day of the month after a day's. */
function firstOfNextMonth(day: string): string {
  const date = new Date(instantOf(day));

  return dayAt(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 1));
}

/** The last day of a day's month. */
function lastOfMonth(day: string): string {
  return daysAfter(firstOfNextMonth(day), -1);
}

/** The earlier of two days. */
function earlier(one: string, other: string): string {
  return one <= other ? one : other;
}

/**
 * The same day of the month so many months away, or that month's last day where it is shorter: a
 * month back from the thirty-first of March is the last day of February.
 */
function monthsAfter(day: string, months: number): string {
  const date = new Date(instantOf(day));
  const target = Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + months, 1);
  const length = Number(lastOfMonth(dayAt(target)).slice(8));

  return daysAfter(dayAt(target), Math.min(date.getUTCDate(), length) - 1);
}

/**
 * The weeks of one month's block, Monday first.
 *
 * A week the month begins part-way through holds nothing before the first, and the month the range
 * ends in stops at its last day, so a week entirely after today is not drawn at all.
 */
function weeksOf(
  first: string,
  last: string,
  counted: ReadonlyMap<string, number>,
): CalendarWeek[] {
  const weeks: CalendarWeek[] = [];

  for (let monday = daysAfter(first, -placeInWeek(first)); monday <= last; ) {
    weeks.push(weekFrom(monday, first, last, counted));
    monday = daysAfter(monday, A_WEEK);
  }

  return weeks;
}

/** The week a Monday begins, its places holding the days that fall from the first to the last. */
function weekFrom(
  monday: string,
  first: string,
  last: string,
  counted: ReadonlyMap<string, number>,
): CalendarWeek {
  return {
    monday,
    days: Array.from({ length: A_WEEK }, (_, place) => {
      const day = daysAfter(monday, place);

      return day < first || day > last ? null : { day, posts: counted.get(day) ?? 0 };
    }),
  };
}

/**
 * The calendar as month blocks, oldest first: every month the range touches, each holding its days
 * inside the range with how many posts each saw.
 *
 * @param range The days the calendar runs over.
 * @param posted The days that saw a post, as the engine names them.
 */
export function calendarMonths(
  range: CalendarRange,
  posted: readonly PostingDay[],
): readonly CalendarMonth[] {
  const counted = new Map(posted.map(({ day, posts }) => [day, posts]));
  const months: CalendarMonth[] = [];

  for (let first = range.from; first <= range.to; first = firstOfNextMonth(first)) {
    months.push({
      month: first.slice(0, 7),
      weeks: weeksOf(first, earlier(lastOfMonth(first), range.to), counted),
    });
  }

  return months;
}

/** The step a day is shaded at: one a post, up to five or more on the darkest. */
const STEPS: readonly HeatStep[] = [0, 1, 2, 3, 4, 5];

/**
 * How strongly a day is shaded.
 *
 * Counted rather than measured against the busiest day, so that a day of one post looks the same in
 * every year of every website: a calendar is read for how often somebody posts, and the shade of one
 * post going lighter because of a busy week elsewhere would say they posted less.
 *
 * @param posts How many posts the day saw.
 */
export function postingStep(posts: number): HeatStep {
  return STEPS[Math.min(Math.max(posts, 0), STEPS.length - 1)] ?? 0;
}

/** What the calendar's days add up to: every post on it, and the days that saw one. */
export function postingTotals(posted: readonly PostingDay[]): PostingTotals {
  return posted.reduce(
    (totals, { posts }) =>
      posts > 0 ? { posts: totals.posts + posts, days: totals.days + 1 } : totals,
    { posts: 0, days: 0 },
  );
}

/** The days that saw a post, newest first, as the list beside the calendar names them. */
export function postingDaysNewestFirst(posted: readonly PostingDay[]): readonly PostingDay[] {
  return posted
    .filter(({ posts }) => posts > 0)
    .sort((one, other) => (one.day < other.day ? 1 : -1));
}

/** Where a key moves from a day. */
type Move = (day: string, range: CalendarRange) => string;

/**
 * Every key that moves through the calendar, and where it goes: up and down a day, as the days of
 * the week run down a column; across a week, as the columns do; a month for the keys that page; and
 * the ends of the range for the two that go all the way.
 */
const MOVES: ReadonlyMap<string, Move> = new Map<string, Move>([
  ['ArrowUp', (day) => daysAfter(day, -1)],
  ['ArrowDown', (day) => daysAfter(day, 1)],
  ['ArrowLeft', (day) => daysAfter(day, -A_WEEK)],
  ['ArrowRight', (day) => daysAfter(day, A_WEEK)],
  ['PageUp', (day) => monthsAfter(day, -1)],
  ['PageDown', (day) => monthsAfter(day, 1)],
  ['Home', (_, { from }) => from],
  ['End', (_, { to }) => to],
]);

/**
 * Where a key moves the keyboard from one day of the calendar, never past either end of it.
 *
 * @param day The day the keyboard is on.
 * @param key The key, as the browser names it.
 * @param range The days the calendar runs over.
 * @returns The day it moves to, or nothing where the key is not one that moves.
 */
export function movedTo(day: string, key: string, range: CalendarRange): string | null {
  const move = MOVES.get(key);

  return move === undefined ? null : heldWithin(move(day, range), range);
}

/**
 * A day kept inside the range, at its nearer end where it falls outside: where a move would take the
 * keyboard off the calendar, and where the day it was left on is no longer one of a calendar answered
 * again on a later day.
 *
 * @param day The day.
 * @param range The days the calendar runs over.
 */
export function heldWithin(day: string, { from, to }: CalendarRange): string {
  if (day < from) {
    return from;
  }

  return day > to ? to : day;
}
