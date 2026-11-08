import { writableDay } from '@/lib/analytics/all-time';
import { type Change, changeBetween } from '@/lib/analytics/change';
import type { PostInsights } from '@/lib/api/schemas';

/**
 * A post's recent weeks: its days laid out Monday to Sunday, the newest week first, with what each
 * week came to, its views for each day, and how that moved against the week before. A day before the
 * post was published is not a day nobody read it, and is said to be before it rather than shown as
 * nought.
 *
 * Every day is a civil date on the website's own calendar, written `2026-11-03`. Nothing is read from
 * the browser's clock or zone: the last day the engine names is today, and the arithmetic is done on
 * dates taken as UTC, where a day is always twenty-four hours long.
 */

/**
 * How many weeks a post's recent weeks run to, the one running now among them. The engine answers
 * this many and no more, and a test holds the two to the same number.
 */
export const RECENT_WEEKS = 13;

/**
 * Every way a week's views for each day can be said to have moved against the week before's: one of
 * the three directions, or a rise from a week with none.
 */
export const WEEK_MOVES = ['up', 'down', 'level', 'none'] as const;

/** One of them. */
export type WeekMove = (typeof WEEK_MOVES)[number];

/**
 * Where a day stands: before the website was first measured, before the post was published, a
 * finished day with its figure, today with its figure so far, or a day of this week still to come.
 */
export type DayState = 'before' | 'unpublished' | 'counted' | 'today' | 'future';

/** One day of a week. */
export interface DayCell {
  /** Written `2026-11-03`. */
  readonly day: string;
  readonly state: DayState;
  /**
   * Its views, or nothing where it has no figure to show: before the website was measured, before
   * the post was published, or still to come.
   */
  readonly pageViews: number | null;
}

/** One Monday to Sunday week. */
export interface WeekRow {
  /** The Monday it begins on. */
  readonly first: string;
  /** The Sunday it ends on. */
  readonly last: string;
  /**
   * Its seven days, Monday first. The days before the post was published hold no figure and are said
   * to be before it, wherever nobody can have read it on them.
   */
  readonly days: readonly DayCell[];
  /** Every view of its days, today's so far among them. */
  readonly total: number;
  /**
   * Its views for each of its finished days with a figure, unrounded; nothing before one of its days
   * has finished. Today is not among them: a day still running set against whole ones would read as
   * a fall every morning. Nor, in the week the post was published in, are the days before it was,
   * whether they are said to be before it or hold a figure: every view of the week's finished days is
   * divided by those of them on or after the post's first day, so that week has nothing until one of
   * them has finished. A week that ends before that day, shown because somebody read the post in it,
   * divides by all of its finished days.
   */
  readonly perDay: number | null;
  /**
   * How that moved against the week before, or nothing where there is no week before it to set it
   * against, or nothing of its own to set.
   */
  readonly change: Change | null;
  /** Whether it is the week running now. */
  readonly running: boolean;
}

/** One day of a post's recent weeks, as the engine answers it. */
type PostDay = PostInsights['days'][number];

/** A week before its move against the week before it is known. */
type Week = Omit<WeekRow, 'change'>;

/** Days in a week, and a week's places, Monday nought and Sunday six. */
const A_WEEK = 7;

/** A move too small to show and a week of nought after a week of nought alike: no change at all. */
const LEVEL: Change = { direction: 'level', fraction: 0 };

/** The day so many days after another, or before it where the number is below nought. */
function shifted(day: string, days: number): string {
  const moved = writableDay(day);

  moved.setUTCDate(moved.getUTCDate() + days);

  return moved.toISOString().slice(0, 10);
}

/** Where a day falls in its week, Monday nought and Sunday six. */
function placeInWeek(day: string): number {
  return (writableDay(day).getUTCDay() + A_WEEK - 1) % A_WEEK;
}

/**
 * A day the engine answered, standing where it stands against today and against the day the post was
 * published, where nobody can have read it before that day.
 */
function cellOf({ day, pageViews }: PostDay, today: string, published: string | null): DayCell {
  if (pageViews === null) {
    return { day, state: 'before', pageViews };
  }

  if (published !== null && day < published) {
    return { day, state: 'unpublished', pageViews: null };
  }

  return { day, state: day === today ? 'today' : 'counted', pageViews };
}

/** Days with no figure of their own, from one day on: before the record, or still to come. */
function blanks(from: string, length: number, state: 'before' | 'future'): DayCell[] {
  return Array.from({ length }, (_, at) => ({ day: shifted(from, at), state, pageViews: null }));
}

/**
 * Every day the weeks run over, in order: the days answered, with the week the first of them falls in
 * filled out back to its Monday and the week running now out to its Sunday.
 */
function laidOut(
  days: readonly PostDay[],
  first: string,
  today: string,
  published: string | null,
): DayCell[] {
  const lead = placeInWeek(first);
  const trail = A_WEEK - 1 - placeInWeek(today);

  return [
    ...blanks(shifted(first, -lead), lead, 'before'),
    ...days.map((day) => cellOf(day, today, published)),
    ...blanks(shifted(today, 1), trail, 'future'),
  ];
}

/** What a run of days comes to. */
function sumOf(cells: readonly DayCell[]): number {
  return cells.reduce((sum, cell) => sum + (cell.pageViews ?? 0), 0);
}

/**
 * One week, from its seven days.
 *
 * Its views for each day are every view of its finished days over those of them on or after the
 * post's first day, so the week the post was published in is not averaged over days before it
 * existed, and has nothing to show until one of its days since has finished. A week that ends before
 * that day, shown only because somebody read the post in it, is taken over all of its finished days.
 */
function weekOf(
  days: readonly DayCell[],
  monday: string,
  today: string,
  from: string | null,
): Week {
  const last = shifted(monday, A_WEEK - 1);
  const finished = days.filter((cell) => cell.state === 'counted');
  const over =
    from === null || last < from ? finished : finished.filter((cell) => cell.day >= from);

  return {
    first: monday,
    last,
    days,
    total: sumOf(days),
    perDay: over.length === 0 ? null : sumOf(finished) / over.length,
    running: days.some((cell) => cell.day === today),
  };
}

/**
 * Whether a week is one from before the post that says nothing: it ends before the post's first day,
 * or there is no such day, and nobody read the post in it. A week in which somebody read it is kept
 * whenever it falls, and every day of it with a figure says it.
 */
function beforeThePost(week: Week, from: string | null): boolean {
  return (
    (from === null || week.last < from) && !week.days.some((cell) => (cell.pageViews ?? 0) > 0)
  );
}

/** How a week's views for each day moved against the week before's, where both have any. */
function movedOn(week: Week, previous: Week): Change | null {
  if (week.perDay === null || previous.perDay === null) {
    return null;
  }

  return changeBetween(week.perDay, previous.perDay) ?? LEVEL;
}

/**
 * A post's recent weeks, the week running now first.
 *
 * Each week is cut Monday to Sunday. The weeks before the post's first day are left out up to the
 * first in which anybody read it, so a post published last week is not shown a quarter of a year of
 * noughts; after that, every week is shown, and the week it was published in is averaged over the
 * days since it was. The oldest week shown has nothing to be set against.
 *
 * Given the day the post was published, every day before it with a figure is said to be before it
 * was published, and holds none. Nobody can have read the post on such a day, so what each week comes
 * to, how it moved and which weeks are shown are the same either way; a day before the website was
 * measured is still said to be that.
 *
 * @param days Every day from a Monday to today, as the engine answers them.
 * @param from The post's first day, or nothing where it has none.
 * @param published The day the post was published, where no day before it can have held one of its
 * views; nothing, where any of them may have, and every day with a figure is shown as it was counted.
 * @returns The weeks, or none where nothing would be shown.
 */
export function weeksOf(
  days: readonly PostDay[],
  from: string | null,
  published: string | null = null,
): readonly WeekRow[] {
  const first = days[0]?.day;
  const today = days.at(-1)?.day;

  if (first === undefined || today === undefined) {
    return [];
  }

  const cells = laidOut(days, first, today, published);
  const monday = cells[0]?.day ?? first;
  const weeks = Array.from({ length: Math.ceil(cells.length / A_WEEK) }, (_, at) =>
    weekOf(cells.slice(at * A_WEEK, (at + 1) * A_WEEK), shifted(monday, at * A_WEEK), today, from),
  );
  const begins = weeks.findIndex((week) => !beforeThePost(week, from));

  if (begins === -1) {
    return [];
  }

  const shown = weeks.slice(begins);

  return shown
    .map((week, at): WeekRow => {
      const previous = shown[at - 1];

      return { ...week, change: previous === undefined ? null : movedOn(week, previous) };
    })
    .toReversed();
}

/**
 * The most any day of the weeks was read, which every day is shaded against.
 *
 * @param rows The weeks.
 */
export function busiestDay(rows: readonly WeekRow[]): number {
  return Math.max(0, ...rows.flatMap((row) => row.days.map((cell) => cell.pageViews ?? 0)));
}

/**
 * Which way a week's move is said to have gone: its direction, or a rise from none where the week
 * before held nothing to measure it against.
 *
 * @param change How the week moved.
 */
export function weekMove(change: Change): WeekMove {
  return change.fraction === null ? 'none' : change.direction;
}
