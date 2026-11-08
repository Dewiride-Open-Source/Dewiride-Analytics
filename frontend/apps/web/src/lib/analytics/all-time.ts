import type { Insights } from '@/lib/api/schemas';
import type { Formatter } from '@/lib/charts/bucket-names';

/**
 * A record laid out as years of twelve months, a website's whole record or one post's; how its
 * figures, its months and the days of its weeks are written; and what stands on the screen about a
 * website's record while that is on its way, refused, or has nothing in it.
 */

/** What each month can be read as. */
export const MEASURES = ['total', 'average'] as const;

/** Page views in the month, or page views for each day of it measured. */
export type Measure = (typeof MEASURES)[number];

/** The months a record holds: a website's whole record, or one post's. */
export interface MonthsRecord {
  /** Every month of it, oldest first, the last of them the month running now. */
  readonly months: Insights['months'];
  /** Which of them were kept before their visits were all judged. */
  readonly unjudged: readonly string[];
}

/**
 * Where a month of a year stands against the website's record: before it began, a finished month with
 * its figure, the one running now, one with no figure because it was never counted or was kept before
 * its visits were all judged, or one still to come.
 */
export type MonthState = 'before' | 'measured' | 'running' | 'notCounted' | 'notJudged' | 'future';

/** One month of a year. */
export interface MonthCell {
  /** Written `2026-08`. */
  readonly month: string;
  readonly state: MonthState;
  /** Its page views, or nothing where it has no figure. */
  readonly pageViews: number | null;
  /** How many days its figure covers. */
  readonly days: number;
}

/** One year of the record. */
export interface YearRow {
  /** Written `2026`. */
  readonly year: string;
  /** January to December. */
  readonly months: readonly MonthCell[];
}

/** Months in a year, each written as it is in a month's name. */
const MONTHS_OF_A_YEAR = ['01', '02', '03', '04', '05', '06', '07', '08', '09', '10', '11', '12'];

/**
 * A month the record says something about, standing where it stands.
 *
 * A month inside the record with no figure of its own is left out, and is a month kept before its
 * visits were all judged only where the answer says so: otherwise nothing ever counted it.
 */
function monthWithin(
  month: string,
  held: Insights['months'][number] | undefined,
  running: string,
  unjudged: readonly string[],
): MonthCell {
  const pageViews = held?.pageViews ?? null;

  if (pageViews === null) {
    return {
      month,
      state: unjudged.includes(month) ? 'notJudged' : 'notCounted',
      pageViews,
      days: 0,
    };
  }

  return {
    month,
    state: month === running ? 'running' : 'measured',
    pageViews,
    days: held?.daysMeasured ?? 0,
  };
}

/**
 * A record as years of twelve months, the newest year first, so the month somebody is most likely to
 * want is the first thing they find. Months are compared as they are written, which sorts them in
 * time.
 *
 * @param record The record, as the engine answers it.
 * @returns Every year from the first month's to the running month's, or none for an empty record.
 */
export function yearsOf({ months, unjudged }: MonthsRecord): readonly YearRow[] {
  const first = months[0]?.month;
  const running = months.at(-1)?.month;

  if (first === undefined || running === undefined) {
    return [];
  }

  const held = new Map(months.map((month) => [month.month, month]));
  const newest = Number(running.slice(0, 4));
  const oldest = Number(first.slice(0, 4));

  return Array.from({ length: newest - oldest + 1 }, (_, back) => {
    const year = String(newest - back);

    return {
      year,
      months: MONTHS_OF_A_YEAR.map((written): MonthCell => {
        const month = `${year}-${written}`;

        if (month < first) {
          return { month, state: 'before', pageViews: null, days: 0 };
        }

        return month > running
          ? { month, state: 'future', pageViews: null, days: 0 }
          : monthWithin(month, held.get(month), running, unjudged);
      }),
    };
  });
}

/**
 * A day on a website's calendar, to be written in UTC, which keeps it the day it is wherever the
 * reader happens to be.
 *
 * @param day Written `2026-08-19`.
 */
export function writableDay(day: string): Date {
  return new Date(`${day}T00:00:00Z`);
}

/**
 * A month on a website's calendar written out in full, as the reader's language writes one:
 * `August 2026`.
 *
 * @param month Written `2026-08`.
 * @param format The reader's own formatter.
 */
export function writtenMonth(month: string, format: Formatter): string {
  return format.dateTime(writableDay(`${month}-01`), {
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  });
}

/**
 * Views over the days they were counted on, or nothing where there are no days to divide by.
 *
 * @param views How many pages were read.
 * @param days How many days that covers.
 */
function perDay(views: number, days: number): number | null {
  return days > 0 ? views / days : null;
}

/**
 * The figure one month shows.
 *
 * @param cell The month.
 * @param measure What the month is being read as.
 * @returns The figure, or nothing where the month has none.
 */
export function figureOf(cell: MonthCell, measure: Measure): number | null {
  if (cell.pageViews === null) {
    return null;
  }

  return measure === 'total' ? cell.pageViews : perDay(cell.pageViews, cell.days);
}

/**
 * The figure any run of months shows: what its months with figures add up to, or their page views
 * over the days those figures cover. A month left out counts on neither side, since nothing says what
 * it held or how many of its days were measured, and a run with no figures in it has none of its own.
 *
 * @param cells The months.
 * @param measure What the run is being read as.
 */
export function figureAcross(cells: readonly MonthCell[], measure: Measure): number | null {
  const figures = cells.filter((cell) => cell.pageViews !== null);

  if (figures.length === 0) {
    return null;
  }

  const views = figures.reduce((sum, cell) => sum + (cell.pageViews ?? 0), 0);
  const days = figures.reduce((sum, cell) => sum + cell.days, 0);

  return measure === 'total' ? views : perDay(views, days);
}

/**
 * The figure one year shows, read across its months as any run of them is.
 *
 * @param row The year.
 * @param measure What the year is being read as.
 */
export function yearFigure(row: YearRow, measure: Measure): number | null {
  return figureAcross(row.months, measure);
}

/** Stands in for a figure that is not there, in a cell or beside a year. */
export const NO_FIGURE = '–';

/** Below this an average is written with a decimal, since the difference between 1 and 1.4 is real. */
const WHOLE_FROM = 10;

/**
 * Below this a decimal rounds an average to nought, on a month somebody read, so it is written to its
 * first digit instead.
 */
const ROUNDS_TO_NOUGHT = 0.05;

/**
 * A figure as a month, a week or a year shows it.
 *
 * @param figure The figure, or nothing where there is none.
 * @param measure Whether it is a count of views or views for each day.
 * @param format The reader's own formatter.
 */
export function writeFigure(figure: number | null, measure: Measure, format: Formatter): string {
  if (figure === null) {
    return NO_FIGURE;
  }

  if (measure === 'average' && figure > 0 && figure < ROUNDS_TO_NOUGHT) {
    return format.number(figure, { maximumSignificantDigits: 1 });
  }

  return measure === 'average' && figure < WHOLE_FROM
    ? format.number(figure, { maximumFractionDigits: 1 })
    : format.number(figure, { maximumFractionDigits: 0 });
}

/** A Monday at midnight, which any day of the week and any hour can be written from. */
export const A_MONDAY = Date.UTC(2024, 0, 1);

const A_DAY = 86_400_000;

/**
 * A place in the week written out as the reader's language names the day, however long or short it
 * is asked for.
 *
 * @param place Where the day falls in the week, Monday nought and Sunday six.
 * @param width How much of the name to write.
 * @param format The reader's own formatter.
 */
export function weekdayName(
  place: number,
  width: 'long' | 'short' | 'narrow',
  format: Formatter,
): string {
  return format.dateTime(new Date(A_MONDAY + place * A_DAY), { weekday: width, timeZone: 'UTC' });
}

/**
 * The largest figure any month of the record shows, which every month is shaded against. A year's
 * own figure is not among them: it would hold every month to the bottom of the scale.
 *
 * @param rows The record.
 * @param measure What the months are being read as.
 */
export function largestOf(rows: readonly YearRow[], measure: Measure): number {
  return Math.max(
    0,
    ...rows.flatMap((row) => row.months.map((cell) => figureOf(cell, measure) ?? 0)),
  );
}

/**
 * How far short of the present a record kept to people may stop before something other than its
 * visitors is holding it back: a day, in milliseconds.
 *
 * Every such record stops short with nothing wrong, by three things added together. A visit is held
 * open until it has been quiet for the installation's idle timeout, which is twelve hours at most.
 * Judging then runs on an interval, which is an hour at most. And a run takes time of its own. A day
 * is beyond anything the first two can come to and leaves a run hours to finish in, so a record
 * further behind than this is waiting on judging, or on a month kept before judging had finished
 * with it being counted again. It is also a line a reader can be told.
 */
export const JUDGING_BEHIND_AFTER = 24 * 60 * 60 * 1000;

/** What any question about a record gave back, as far as it has got. */
interface Answer {
  readonly data: Insights | undefined;
  readonly error: Error | null;
}

/** What stands on the screen about a website's record. */
export type RecordShown =
  | { readonly state: 'waiting' }
  | { readonly state: 'refused'; readonly error: Error | null }
  | { readonly state: 'nothing' }
  | { readonly state: 'absent' }
  | { readonly state: 'ready'; readonly insights: Insights };

/** Whether a record holds no page views and no visitors, and leaves no month out of saying so. */
function holdsNothing({ pageViews, visitors, missing, unjudged }: Insights): boolean {
  return pageViews === 0 && visitors === 0 && missing.length === 0 && unjudged.length === 0;
}

/**
 * What stands on the screen, from everybody's record and the record of whoever the screen is kept to.
 *
 * A website with nothing of it held, or nothing in what is held, is waiting for its first visit,
 * whoever the screen is kept to. Anything else is decided by the screen's own answer: figures to show,
 * or months it says were left out, are the record. Nought with nothing left out is only an answer once
 * everybody's record says whether anybody came, and is then either the same empty website or one in
 * which nobody was judged to be a person. An answer with nothing to show that could not be had is
 * said to be refused; an answer already read stays on the screen when asking again fails.
 *
 * @param everybody Everybody's record, as it is being asked for.
 * @param own The record of whoever the screen is kept to, as it is being asked for.
 */
export function recordShown(everybody: Answer, own: Answer): RecordShown {
  if (everybody.data?.firstDay === null) {
    return { state: 'nothing' };
  }

  if (own.data === undefined) {
    return own.error === null ? { state: 'waiting' } : { state: 'refused', error: own.error };
  }

  if (!holdsNothing(own.data)) {
    return { state: 'ready', insights: own.data };
  }

  if (everybody.data === undefined) {
    return everybody.error === null
      ? { state: 'waiting' }
      : { state: 'refused', error: everybody.error };
  }

  return holdsNothing(everybody.data) ? { state: 'nothing' } : { state: 'absent' };
}
