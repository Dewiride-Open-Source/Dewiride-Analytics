import { describe, expect, it } from 'vitest';
import {
  calendarMonths,
  heldWithin,
  movedTo,
  postingDaysNewestFirst,
  postingStep,
  postingTotals,
} from '@/lib/analytics/posting-calendar';

/** Twelve months to a Saturday, as the engine answers on 7 November 2026. */
const RANGE = { from: '2025-12-01', to: '2026-11-07' };

describe('the months of a posting calendar', () => {
  it('runs a block to each month from the first day to today, oldest first', () => {
    const months = calendarMonths(RANGE, []).map(({ month }) => month);

    expect(months).toStrictEqual([
      '2025-12',
      '2026-01',
      '2026-02',
      '2026-03',
      '2026-04',
      '2026-05',
      '2026-06',
      '2026-07',
      '2026-08',
      '2026-09',
      '2026-10',
      '2026-11',
    ]);
  });

  /** December 2025 began on a Monday and ran to a Wednesday, five weeks on. */
  it('lays each month out a week to a column, Monday first, with nothing outside the month', () => {
    const [december] = calendarMonths(RANGE, []);

    expect(december?.weeks.map(({ monday }) => monday)).toStrictEqual([
      '2025-12-01',
      '2025-12-08',
      '2025-12-15',
      '2025-12-22',
      '2025-12-29',
    ]);
    expect(december?.weeks[4]?.days.map((day) => day?.day ?? null)).toStrictEqual([
      '2025-12-29',
      '2025-12-30',
      '2025-12-31',
      null,
      null,
      null,
      null,
    ]);
  });

  /** November 2026 began on a Sunday, the last place of a week that began in October. */
  it('begins a month part-way through its first week, and stops at today', () => {
    const november = calendarMonths(RANGE, []).at(-1);

    expect(november?.weeks.map(({ monday }) => monday)).toStrictEqual(['2026-10-26', '2026-11-02']);
    expect(november?.weeks[0]?.days.map((day) => day?.day ?? null)).toStrictEqual([
      null,
      null,
      null,
      null,
      null,
      null,
      '2026-11-01',
    ]);
    expect(november?.weeks[1]?.days.at(-1)).toBeNull();
    expect(november?.weeks[1]?.days.at(-2)?.day).toBe('2026-11-07');
  });

  it('puts each day’s posts on it, and nought on every other day', () => {
    const months = calendarMonths(RANGE, [{ day: '2026-11-03', posts: 2 }]);
    const days = months.flatMap(({ weeks }) => weeks.flatMap((week) => week.days));

    expect(days.find((day) => day?.day === '2026-11-03')?.posts).toBe(2);
    expect(days.find((day) => day?.day === '2026-11-04')?.posts).toBe(0);
    expect(days.filter((day) => day !== null)).toHaveLength(342);
  });
});

describe('how strongly a day is shaded', () => {
  it.each([
    [0, 0],
    [1, 1],
    [2, 2],
    [4, 4],
    [5, 5],
    [12, 5],
  ])('shades a day of %i posts at step %i', (posts, step) => {
    expect(postingStep(posts)).toBe(step);
  });
});

describe('what a calendar adds up to', () => {
  const POSTED = [
    { day: '2026-03-31', posts: 2 },
    { day: '2026-11-03', posts: 1 },
    { day: '2026-01-15', posts: 4 },
  ];

  it('counts the posts and the days that saw one', () => {
    expect(postingTotals(POSTED)).toStrictEqual({ posts: 7, days: 3 });
    expect(postingTotals([])).toStrictEqual({ posts: 0, days: 0 });
  });

  it('lists the days newest first', () => {
    expect(postingDaysNewestFirst(POSTED).map(({ day }) => day)).toStrictEqual([
      '2026-11-03',
      '2026-03-31',
      '2026-01-15',
    ]);
  });
});

describe('moving through a calendar with the keyboard', () => {
  it.each([
    ['ArrowUp', '2026-06-15', '2026-06-14'],
    ['ArrowDown', '2026-06-15', '2026-06-16'],
    ['ArrowLeft', '2026-06-15', '2026-06-08'],
    ['ArrowRight', '2026-06-15', '2026-06-22'],
    ['PageUp', '2026-06-15', '2026-05-15'],
    ['PageDown', '2026-06-15', '2026-07-15'],
    ['Home', '2026-06-15', '2025-12-01'],
    ['End', '2026-06-15', '2026-11-07'],
  ])('moves %s from %s to %s', (key, from, to) => {
    expect(movedTo(from, key, RANGE)).toBe(to);
  });

  it('pages to the last day of a shorter month', () => {
    expect(movedTo('2026-03-31', 'PageUp', RANGE)).toBe('2026-02-28');
    expect(movedTo('2026-01-31', 'PageDown', RANGE)).toBe('2026-02-28');
  });

  it.each([
    ['ArrowUp', '2025-12-01'],
    ['ArrowLeft', '2025-12-03'],
    ['PageUp', '2025-12-20'],
  ])('never moves %s past the first day', (key, from) => {
    expect(movedTo(from, key, RANGE)).toBe('2025-12-01');
  });

  it.each([
    ['ArrowDown', '2026-11-07'],
    ['ArrowRight', '2026-11-02'],
    ['PageDown', '2026-10-20'],
  ])('never moves %s past today', (key, from) => {
    expect(movedTo(from, key, RANGE)).toBe('2026-11-07');
  });

  it('takes no notice of a key that does not move, however it is spelt', () => {
    expect(movedTo('2026-06-15', 'Enter', RANGE)).toBeNull();
    expect(movedTo('2026-06-15', 'constructor', RANGE)).toBeNull();
  });

  it('holds a day left outside the range at its nearer end', () => {
    expect(heldWithin('2025-11-30', RANGE)).toBe('2025-12-01');
    expect(heldWithin('2026-11-08', RANGE)).toBe('2026-11-07');
    expect(heldWithin('2026-06-15', RANGE)).toBe('2026-06-15');
  });
});
