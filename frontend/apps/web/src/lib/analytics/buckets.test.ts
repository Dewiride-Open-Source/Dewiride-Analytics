import { describe, expect, it } from 'vitest';
import { bucketDays, heldInDetail } from '@/lib/analytics/buckets';

/**
 * The first to the thirtieth of October in Kolkata, asked for at three in the afternoon there on
 * the thirtieth: from midnight on the first to the top of the next hour.
 */
const OCTOBER = { from: '2026-09-30T18:30:00.000Z', to: '2026-10-30T10:00:00.000Z' };

/**
 * The weeks those days reach into, each named by the moment its Monday begins in Kolkata — which is
 * the evening before in UTC. The first began on the twenty-eighth of September, before the period.
 */
const WEEKS = [
  '2026-09-27T18:30:00+00:00',
  '2026-10-04T18:30:00+00:00',
  '2026-10-11T18:30:00+00:00',
  '2026-10-18T18:30:00+00:00',
  '2026-10-25T18:30:00+00:00',
];

/** The second of August to the thirtieth of October in Kolkata, asked for at the same moment. */
const QUARTER = { from: '2026-08-01T18:30:00.000Z', to: '2026-10-30T10:00:00.000Z' };

/** The months those days reach into, each named by the moment its first day begins in Kolkata. */
const MONTHS = [
  '2026-07-31T18:30:00+00:00',
  '2026-08-31T18:30:00+00:00',
  '2026-09-30T18:30:00+00:00',
];

/** The days each bucket covers, without the moment it is named by. */
function covered(
  window: { from: string; to: string },
  starts: readonly string[],
  cut: 'week' | 'month',
) {
  return bucketDays(starts, window, cut, 'Asia/Kolkata').map(({ first, last, whole }) => ({
    first,
    last,
    whole,
  }));
}

describe('the days a bucket covers', () => {
  /** The last thirty days began on a Thursday, and only the four days of that week from then on were counted. */
  it('cuts the first week to the period', () => {
    expect(covered(OCTOBER, WEEKS, 'week')[0]).toStrictEqual({
      first: '2026-10-01',
      last: '2026-10-04',
      whole: false,
    });
  });

  it('ends the running week today', () => {
    expect(covered(OCTOBER, WEEKS, 'week').at(-1)).toStrictEqual({
      first: '2026-10-26',
      last: '2026-10-30',
      whole: false,
    });
  });

  it('knows a whole week and a whole month', () => {
    expect(covered(OCTOBER, WEEKS, 'week').map((week) => week.whole)).toStrictEqual([
      false,
      true,
      true,
      true,
      false,
    ]);
    expect(covered(QUARTER, MONTHS, 'month')).toStrictEqual([
      { first: '2026-08-02', last: '2026-08-31', whole: false },
      { first: '2026-09-01', last: '2026-09-30', whole: true },
      { first: '2026-10-01', last: '2026-10-30', whole: false },
    ]);
  });

  /**
   * Each week begins at half past six on a Sunday evening in UTC, which is midnight on Monday in
   * Kolkata. Read in UTC, every week would be named by the day before it.
   */
  it('counts days where the site is', () => {
    expect(covered(OCTOBER, WEEKS, 'week').map((week) => week.first)).toStrictEqual([
      '2026-10-01',
      '2026-10-05',
      '2026-10-12',
      '2026-10-19',
      '2026-10-26',
    ]);
  });

  /** The engine may name a first bucket by its own week or by the start of the period; both read alike. */
  it('reads a first bucket the same whether it is named by its week or by the period', () => {
    const named = [OCTOBER.from, ...WEEKS.slice(1)];

    expect(covered(OCTOBER, named, 'week')).toStrictEqual(covered(OCTOBER, WEEKS, 'week'));
  });

  it('holds a whole month once the period reaches all of it', () => {
    const finished = { from: '2026-07-31T18:30:00.000Z', to: '2026-10-31T18:30:00.000Z' };

    expect(covered(finished, MONTHS, 'month').map((month) => month.whole)).toStrictEqual([
      true,
      true,
      true,
    ]);
  });

  it('keeps the moment each bucket is named by', () => {
    expect(
      bucketDays(WEEKS, OCTOBER, 'week', 'Asia/Kolkata').map((week) => week.start),
    ).toStrictEqual(WEEKS);
  });

  /** An hour belongs to the day it falls in, and the last hour before midnight is still that day. */
  it('gives an hour the one day it falls in', () => {
    const night = { from: '2026-10-28T18:30:00.000Z', to: '2026-10-29T19:30:00.000Z' };
    const hours = ['2026-10-29T17:30:00+00:00', '2026-10-29T18:30:00+00:00'];

    expect(bucketDays(hours, night, 'hour', 'Asia/Kolkata')).toStrictEqual([
      { start: hours[0], first: '2026-10-29', last: '2026-10-29', whole: true },
      { start: hours[1], first: '2026-10-30', last: '2026-10-30', whole: true },
    ]);
  });

  /**
   * A day is cut at midnight where the website is, so it is that one day however its neighbour is
   * named. Read up to the next bucket instead, a day named in another clock would run into the next.
   */
  it('never stretches an hour or a day into the day after it', () => {
    const days = ['2026-08-11T00:00:00+00:00', '2026-08-12T00:00:00+00:00'];
    const window = { from: '2026-08-11T00:00:00.000Z', to: '2026-08-13T00:00:00.000Z' };

    for (const granularity of ['hour', 'day'] as const) {
      expect(
        bucketDays(days, window, granularity, 'Asia/Kolkata').map(({ first, last }) => [
          first,
          last,
        ]),
      ).toStrictEqual([
        ['2026-08-11', '2026-08-11'],
        ['2026-08-12', '2026-08-12'],
      ]);
    }
  });

  it('gives a day itself', () => {
    const twoDays = { from: '2026-10-28T18:30:00.000Z', to: '2026-10-30T18:30:00.000Z' };
    const days = ['2026-10-28T18:30:00+00:00', '2026-10-29T18:30:00+00:00'];

    expect(bucketDays(days, twoDays, 'day', 'Asia/Kolkata')).toStrictEqual([
      { start: days[0], first: '2026-10-29', last: '2026-10-29', whole: true },
      { start: days[1], first: '2026-10-30', last: '2026-10-30', whole: true },
    ]);
  });

  /**
   * All time from the nineteenth of August 2024, the record's first day, to the thirtieth of
   * October 2026: the first year from that day, the whole of the next, and this year so far.
   */
  it('cuts the first year at the record’s first day and ends the running one today', () => {
    const record = { from: '2024-08-18T18:30:00.000Z', to: '2026-10-30T10:00:00.000Z' };
    const years = [
      '2023-12-31T18:30:00+00:00',
      '2024-12-31T18:30:00+00:00',
      '2025-12-31T18:30:00+00:00',
    ];

    expect(
      bucketDays(years, record, 'year', 'Asia/Kolkata').map(({ first, last, whole }) => ({
        first,
        last,
        whole,
      })),
    ).toStrictEqual([
      { first: '2024-08-19', last: '2024-12-31', whole: false },
      { first: '2025-01-01', last: '2025-12-31', whole: true },
      { first: '2026-01-01', last: '2026-10-30', whole: false },
    ]);
  });
});

describe('a bucket the screen can be narrowed to', () => {
  it('is one whose days are all still held one by one', () => {
    expect(heldInDetail({ first: '2025-11-01', last: '2025-11-30' }, '2025-10-31')).toBe(true);
    expect(heldInDetail({ first: '2025-10-31', last: '2025-10-31' }, '2025-10-31')).toBe(true);
  });

  /** A month that began before the first day still kept is held as its totals and nothing finer. */
  it('is never one that began before the first day still kept', () => {
    expect(heldInDetail({ first: '2025-10-01', last: '2025-10-31' }, '2025-10-02')).toBe(false);
  });
});
