import type { createFormatter, DateTimeFormatOptions } from 'next-intl';
import type { BucketDays } from '@/lib/analytics/buckets';
import { type Granularity, spanInstants } from '@/lib/analytics/period';

/**
 * What the buckets of a picture are called.
 *
 * Each is written for the three places it is read: briefly under the axis, where it sits beside
 * its neighbours; down the first column of the table, a row to itself; and in full over the card a
 * hover opens, where it is read on its own. All three are written from the same days, so the card
 * over a bucket, the axis beneath it and its row in the table always name the same one.
 *
 * Every name is written by the reader's own formatter in the website's zone, and never assembled
 * from pieces: the reader's language puts the parts of a date, and of a run of dates, in its own
 * order and joins them with its own words.
 */

/** What a screen is handed to write a moment in the reader's language. */
export type Formatter = ReturnType<typeof createFormatter>;

/** How the buckets of one picture are written. */
export interface BucketWriting {
  /** The site's own zone, so a bucket is read back in the day it was counted in. */
  readonly timeZoneId: string;
  readonly granularity: Granularity;
  /** Whether the period covers more than one day, which decides whether an hour carries its date. */
  readonly manyDays: boolean;
  /** Whether it runs across more than one calendar year, which decides whether a date carries one. */
  readonly manyYears: boolean;
}

/** Every bucket of a picture, written for the three places it is read. */
export interface BucketNames {
  /** Briefly, under the axis. */
  readonly labels: readonly string[];
  /** Down the first column of the table, a row to each. */
  readonly rows: readonly string[];
  /** In full, over the card a hover opens. */
  readonly titles: readonly string[];
}

/** Writes one bucket. */
type Namer = (bucket: BucketDays, writing: BucketWriting, format: Formatter) => string;

/**
 * How a bucket is written down the first column of the table, by how wide it is.
 *
 * An hour needs no date beside it when every other bucket is from the same day, and needs one the
 * moment they are not; none of them carries minutes, because an hour always begins on the hour.
 * A week is always the run of days it covers, since the date it begins on alone would read as a
 * day. A month or a year is its name, or the days it covers where the period reaches only part of it
 * — the first month of the last ninety days is seldom a whole one, nor the year a website was first
 * measured in, and naming either as if it were would claim days that were never counted. A date
 * carries its year once the period runs across one.
 */
const ROWS: Readonly<Record<Granularity, Namer>> = {
  hour: (bucket, { timeZoneId: timeZone, manyDays }, format) =>
    format.dateTime(
      new Date(bucket.start),
      manyDays
        ? { timeZone, day: 'numeric', month: 'short', hour: 'numeric' }
        : { timeZone, hour: 'numeric' },
    ),
  day: (bucket, writing, format) =>
    format.dateTime(new Date(bucket.start), { ...dated(writing), day: 'numeric', month: 'short' }),
  week: (bucket, writing, format) => run(bucket, writing, format, 'short'),
  month: (bucket, writing, format) =>
    bucket.whole
      ? monthName(bucket, writing, format, 'short')
      : run(bucket, writing, format, 'short'),
  year: (bucket, writing, format) =>
    bucket.whole ? yearName(bucket, writing, format) : run(bucket, writing, format, 'short'),
};

/**
 * How a bucket is written under the axis: as down the table, save for a year the period reaches
 * only part of.
 *
 * That year is the months it covers, as a run of them, rather than its days. A picture of years has
 * only a handful of buckets, and every one is named beneath the drawing, so each name has to fit
 * beside its neighbours on a phone — two runs of days do not, and a year between them goes unnamed.
 * Inside a single month it stays its days, since a run of one month would read as the whole of it.
 * The table and the card a hover opens name its days either way.
 */
const LABELS: Readonly<Record<Granularity, Namer>> = {
  ...ROWS,
  year: (bucket, writing, format) => {
    if (bucket.whole) {
      return yearName(bucket, writing, format);
    }

    return calendarMonthOf(bucket.first) === calendarMonthOf(bucket.last)
      ? run(bucket, writing, format, 'short')
      : monthRun(bucket, writing, format);
  },
};

/**
 * How a bucket is written over the card a hover opens.
 *
 * A day carries its weekday — a rise on a Tuesday is a question about Tuesday, and the axis has no
 * room to say so at phone width — and an hour carries its day as well, because the card is read
 * away from the axis that would otherwise say which day it is. A week is its dates in full, and a
 * month or a year its name in full, or its dates where the period reaches only part of it. A bucket
 * the period reaches for a single day is that day, and is written as one.
 */
const TITLES: Readonly<Record<Granularity, Namer>> = {
  hour: (bucket, writing, format) =>
    format.dateTime(new Date(bucket.start), { ...dayInFull(writing), hour: 'numeric' }),
  day: (bucket, writing, format) => format.dateTime(new Date(bucket.start), dayInFull(writing)),
  week: (bucket, writing, format) =>
    bucket.first === bucket.last
      ? oneDay(bucket, writing, format)
      : run(bucket, writing, format, 'long'),
  month: inFull((bucket, writing, format) => monthName(bucket, writing, format, 'long')),
  year: inFull(yearName),
};

/**
 * A month or a year written in full: by its own name where the period reaches the whole of it, as
 * the one day it is where the period reaches one, and as the days it covers otherwise.
 *
 * @param named Writes the bucket's own name.
 */
function inFull(named: Namer): Namer {
  return (bucket, writing, format) => {
    if (bucket.first === bucket.last) {
      return oneDay(bucket, writing, format);
    }

    return bucket.whole ? named(bucket, writing, format) : run(bucket, writing, format, 'long');
  };
}

/**
 * Every bucket of a picture, written for the two places it is read.
 *
 * @param days Each bucket and the days of the period it covers.
 * @param writing How the picture is cut, and what it covers.
 * @param format The reader's own formatter.
 */
export function bucketNames(
  days: readonly BucketDays[],
  writing: BucketWriting,
  format: Formatter,
): BucketNames {
  const label = LABELS[writing.granularity];
  const row = ROWS[writing.granularity];
  const title = TITLES[writing.granularity];

  return {
    labels: days.map((bucket) => label(bucket, writing, format)),
    rows: days.map((bucket) => row(bucket, writing, format)),
    titles: days.map((bucket) => title(bucket, writing, format)),
  };
}

/**
 * Months named in full with their years, and joined as the reader's language joins a list: `June
 * 2025 and September 2025`.
 *
 * @param months Each month on the website's calendar, written `2025-06`.
 * @param timeZoneId The website's zone, which its months are read in.
 * @param format The reader's own formatter.
 */
export function monthsInFull(
  months: readonly string[],
  timeZoneId: string,
  format: Formatter,
): string {
  return monthsListed(months, { timeZone: timeZoneId, month: 'long', year: 'numeric' }, format);
}

/**
 * Months named within the year they belong to, which says it already: `June and September`.
 *
 * @param months Each month on the website's calendar, written `2025-06`.
 * @param timeZoneId The website's zone, which its months are read in.
 * @param format The reader's own formatter.
 */
export function monthsOfItsYear(
  months: readonly string[],
  timeZoneId: string,
  format: Formatter,
): string {
  return monthsListed(months, { timeZone: timeZoneId, month: 'long' }, format);
}

/** Months each written as asked and joined into one list. */
function monthsListed(
  months: readonly string[],
  written: DateTimeFormatOptions & { readonly timeZone: string },
  format: Formatter,
): string {
  return format.list(
    months.map((month) =>
      format.dateTime(
        spanInstants({ first: `${month}-01`, last: `${month}-01` }, written.timeZone).first,
        written,
      ),
    ),
  );
}

/** The zone a date is written in, and its year where the period runs across one. */
function dated({ timeZoneId, manyYears }: BucketWriting): DateTimeFormatOptions {
  return manyYears ? { timeZone: timeZoneId, year: 'numeric' } : { timeZone: timeZoneId };
}

/** A day with its weekday and its month in full. */
function dayInFull(writing: BucketWriting): DateTimeFormatOptions {
  return { ...dated(writing), weekday: 'long', day: 'numeric', month: 'long' };
}

/** A bucket the period reaches for one day, written as that day. */
function oneDay(bucket: BucketDays, writing: BucketWriting, format: Formatter): string {
  return format.dateTime(spanInstants(bucket, writing.timeZoneId).first, dayInFull(writing));
}

/** The run of days a bucket covers, from its first to its last. */
function run(
  bucket: BucketDays,
  writing: BucketWriting,
  format: Formatter,
  month: 'short' | 'long',
): string {
  const { first, last } = spanInstants(bucket, writing.timeZoneId);

  return format.dateTimeRange(first, last, { ...dated(writing), day: 'numeric', month });
}

/** The run of months a bucket covers, from the month of its first day to the month of its last. */
function monthRun(bucket: BucketDays, writing: BucketWriting, format: Formatter): string {
  const { first, last } = spanInstants(bucket, writing.timeZoneId);

  return format.dateTimeRange(first, last, { ...dated(writing), month: 'short' });
}

/** The calendar month a day falls in, written `2025-06`. */
function calendarMonthOf(day: string): string {
  return day.slice(0, 7);
}

/** The month a whole bucket is. */
function monthName(
  bucket: BucketDays,
  writing: BucketWriting,
  format: Formatter,
  month: 'short' | 'long',
): string {
  return format.dateTime(spanInstants(bucket, writing.timeZoneId).first, {
    ...dated(writing),
    month,
  });
}

/** The year a whole bucket is, written as the reader writes a year on its own. */
function yearName(bucket: BucketDays, writing: BucketWriting, format: Formatter): string {
  return format.dateTime(spanInstants(bucket, writing.timeZoneId).first, {
    timeZone: writing.timeZoneId,
    year: 'numeric',
  });
}
