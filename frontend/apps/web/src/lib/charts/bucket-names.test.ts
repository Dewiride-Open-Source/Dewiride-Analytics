import { createFormatter } from 'next-intl';
import { describe, expect, it } from 'vitest';
import { bucketDays } from '@/lib/analytics/buckets';
import type { AnalyticsWindow, Granularity } from '@/lib/analytics/period';
import {
  type BucketNames,
  bucketNames,
  monthsInFull,
  monthsOfItsYear,
} from '@/lib/charts/bucket-names';

/** Writes as a reader in English does. */
const FORMAT = createFormatter({ locale: 'en' });

const ZONE = 'Asia/Kolkata';

interface Picture {
  readonly window: AnalyticsWindow;
  readonly starts: readonly string[];
  readonly granularity: Granularity;
  readonly manyDays?: boolean;
  readonly manyYears?: boolean;
}

/**
 * Every bucket of a picture, written out, with the spaces a run of dates is written with read as
 * ordinary ones — as anybody reading them does.
 */
function names({ window, starts, granularity, manyDays = true, manyYears = false }: Picture) {
  const written: BucketNames = bucketNames(
    bucketDays(starts, window, granularity, ZONE),
    { timeZoneId: ZONE, granularity, manyDays, manyYears },
    FORMAT,
  );
  const plain = (all: readonly string[]) => all.map((one) => one.replace(/\s+/g, ' '));

  return {
    labels: plain(written.labels),
    rows: plain(written.rows),
    titles: plain(written.titles),
  };
}

/** The first to the thirtieth of October in Kolkata, cut into the weeks it reaches into. */
const OCTOBER_BY_WEEK: Picture = {
  window: { from: '2026-09-30T18:30:00.000Z', to: '2026-10-30T10:00:00.000Z' },
  starts: [
    '2026-09-27T18:30:00+00:00',
    '2026-10-04T18:30:00+00:00',
    '2026-10-11T18:30:00+00:00',
    '2026-10-18T18:30:00+00:00',
    '2026-10-25T18:30:00+00:00',
  ],
  granularity: 'week',
};

/** The second of August to the thirtieth of October in Kolkata, cut into its months. */
const QUARTER_BY_MONTH: Picture = {
  window: { from: '2026-08-01T18:30:00.000Z', to: '2026-10-30T10:00:00.000Z' },
  starts: ['2026-07-31T18:30:00+00:00', '2026-08-31T18:30:00+00:00', '2026-09-30T18:30:00+00:00'],
  granularity: 'month',
};

describe('a week, written out', () => {
  it('is the run of days it covers', () => {
    expect(names(OCTOBER_BY_WEEK).labels).toStrictEqual([
      'Oct 1 – 4',
      'Oct 5 – 11',
      'Oct 12 – 18',
      'Oct 19 – 25',
      'Oct 26 – 30',
    ]);
  });

  it('is its dates in full over the card a hover opens', () => {
    expect(names(OCTOBER_BY_WEEK).titles).toStrictEqual([
      'October 1 – 4',
      'October 5 – 11',
      'October 12 – 18',
      'October 19 – 25',
      'October 26 – 30',
    ]);
  });

  it('names both months when it runs across the end of one', () => {
    const { labels, titles } = names({
      window: { from: '2026-09-27T18:30:00.000Z', to: '2026-10-04T18:30:00.000Z' },
      starts: ['2026-09-27T18:30:00+00:00'],
      granularity: 'week',
    });

    expect(labels).toStrictEqual(['Sep 28 – Oct 4']);
    expect(titles).toStrictEqual(['September 28 – October 4']);
  });

  it('carries the year once the period runs across one', () => {
    const { labels, titles } = names({
      window: { from: '2025-12-28T18:30:00.000Z', to: '2026-01-04T18:30:00.000Z' },
      starts: ['2025-12-28T18:30:00+00:00'],
      granularity: 'week',
      manyYears: true,
    });

    expect(labels).toStrictEqual(['Dec 29, 2025 – Jan 4, 2026']);
    expect(titles).toStrictEqual(['December 29, 2025 – January 4, 2026']);
  });

  /** A period beginning on a Sunday reaches one day of its first week, and that day is what it is. */
  it('is written as a day where the period reaches only one day of it', () => {
    const { labels, titles } = names({
      window: { from: '2026-10-03T18:30:00.000Z', to: '2026-10-11T18:30:00.000Z' },
      starts: ['2026-09-27T18:30:00+00:00', '2026-10-04T18:30:00+00:00'],
      granularity: 'week',
    });

    expect(labels).toStrictEqual(['Oct 4', 'Oct 5 – 11']);
    expect(titles).toStrictEqual(['Sunday, October 4', 'October 5 – 11']);
  });
});

describe('a month, written out', () => {
  it('is its name where the period holds the whole of it, and its days where it does not', () => {
    expect(names(QUARTER_BY_MONTH).labels).toStrictEqual(['Aug 2 – 31', 'Sep', 'Oct 1 – 30']);
  });

  it('is its name or its days in full over the card a hover opens', () => {
    expect(names(QUARTER_BY_MONTH).titles).toStrictEqual([
      'August 2 – 31',
      'September',
      'October 1 – 30',
    ]);
  });

  it('carries the year once the period runs across one', () => {
    const { labels, titles } = names({
      window: { from: '2025-11-30T18:30:00.000Z', to: '2026-01-31T18:30:00.000Z' },
      starts: ['2025-11-30T18:30:00+00:00', '2025-12-31T18:30:00+00:00'],
      granularity: 'month',
      manyYears: true,
    });

    expect(labels).toStrictEqual(['Dec 2025', 'Jan 2026']);
    expect(titles).toStrictEqual(['December 2025', 'January 2026']);
  });
});

describe('a year, written out', () => {
  /** All time from the nineteenth of August 2024 to the thirtieth of October 2026, by the year. */
  const RECORD_BY_YEAR: Picture = {
    window: { from: '2024-08-18T18:30:00.000Z', to: '2026-10-30T10:00:00.000Z' },
    starts: ['2023-12-31T18:30:00+00:00', '2024-12-31T18:30:00+00:00', '2025-12-31T18:30:00+00:00'],
    granularity: 'year',
    manyYears: true,
  };

  /**
   * The first year was measured from the nineteenth of August and this one runs to today, and
   * naming either as the whole year would claim days nobody counted. Under the axis each is the run
   * of months it covers, which is short enough for every year to be named beside the others on a
   * phone.
   */
  it('is its number where the whole of it is counted, and the months it covers where it is not', () => {
    expect(names(RECORD_BY_YEAR).labels).toStrictEqual([
      'Aug – Dec 2024',
      '2025',
      'Jan – Oct 2026',
    ]);
  });

  /** The table has a row to each year, and room there for the days a year cut short covers. */
  it('is its number or its days down the table', () => {
    expect(names(RECORD_BY_YEAR).rows).toStrictEqual([
      'Aug 19 – Dec 31, 2024',
      '2025',
      'Jan 1 – Oct 30, 2026',
    ]);
  });

  /** A run of one month would read as the whole of that month. */
  it('is its days under the axis too where the part of it counted lies inside one month', () => {
    const { labels, rows } = names({
      window: { from: '2025-03-18T18:30:00.000Z', to: '2026-01-15T10:00:00.000Z' },
      starts: ['2024-12-31T18:30:00+00:00', '2025-12-31T18:30:00+00:00'],
      granularity: 'year',
      manyYears: true,
    });

    expect(labels).toStrictEqual(['Mar – Dec 2025', 'Jan 1 – 15, 2026']);
    expect(rows).toStrictEqual(['Mar 19 – Dec 31, 2025', 'Jan 1 – 15, 2026']);
  });

  it('is its number or its days in full over the card a hover opens', () => {
    expect(names(RECORD_BY_YEAR).titles).toStrictEqual([
      'August 19 – December 31, 2024',
      '2025',
      'January 1 – October 30, 2026',
    ]);
  });
});

describe('a day and an hour, written out', () => {
  const TWO_DAYS: AnalyticsWindow = {
    from: '2026-10-28T18:30:00.000Z',
    to: '2026-10-30T10:00:00.000Z',
  };

  it('is a day by its date, and by its weekday as well over the card', () => {
    const { labels, titles } = names({
      window: TWO_DAYS,
      starts: ['2026-10-28T18:30:00+00:00', '2026-10-29T18:30:00+00:00'],
      granularity: 'day',
    });

    expect(labels).toStrictEqual(['Oct 29', 'Oct 30']);
    expect(titles).toStrictEqual(['Thursday, October 29', 'Friday, October 30']);
  });

  it('is an hour by its time, with its date where the period covers more than one day', () => {
    const hours: Picture = {
      window: TWO_DAYS,
      starts: ['2026-10-29T17:30:00+00:00', '2026-10-29T18:30:00+00:00'],
      granularity: 'hour',
    };

    expect(names(hours).labels).toStrictEqual(['Oct 29, 11 PM', 'Oct 30, 12 AM']);
    expect(names({ ...hours, manyDays: false }).labels).toStrictEqual(['11 PM', '12 AM']);
    expect(names(hours).titles).toStrictEqual([
      'Thursday, October 29 at 11 PM',
      'Friday, October 30 at 12 AM',
    ]);
  });
});

/**
 * A month that was never counted is named in the sentence that says the figures leave it out, and
 * beside the year it falls in, so a reader can tell which gap it is.
 */
describe('a month never counted, written out', () => {
  it('is its name and its year, joined to any others as the reader joins a list', () => {
    expect(monthsInFull(['2025-06'], ZONE, FORMAT)).toBe('June 2025');
    expect(monthsInFull(['2025-06', '2025-09'], ZONE, FORMAT)).toBe('June 2025 and September 2025');
  });

  it('is its name alone beside the year it falls in', () => {
    expect(monthsOfItsYear(['2025-06', '2025-09', '2025-11'], ZONE, FORMAT)).toBe(
      'June, September, and November',
    );
  });

  /** Read in the website's zone, the first of the month is still that month wherever the reader is. */
  it('is the month it is on the website’s calendar, not on the reader’s', () => {
    expect(monthsInFull(['2026-01'], 'Pacific/Kiritimati', FORMAT)).toBe('January 2026');
    expect(monthsInFull(['2026-01'], 'Etc/GMT+12', FORMAT)).toBe('January 2026');
  });
});
