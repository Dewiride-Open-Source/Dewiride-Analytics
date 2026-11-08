import { describe, expect, it } from 'vitest';
import { busiestDay, type WeekRow, weekMove, weeksOf } from '@/lib/analytics/recent-weeks';

const A_DAY = 86_400_000;

/** The day so many days after another, on the website's calendar. */
function after(day: string, days: number): string {
  return new Date(Date.parse(`${day}T00:00:00Z`) + days * A_DAY).toISOString().slice(0, 10);
}

/** Days as the engine answers them, one figure to each, from the first on. */
function daysFrom(first: string, figures: readonly (number | null)[]) {
  return figures.map((pageViews, at) => ({ day: after(first, at), pageViews }));
}

/** Seven of the same figure: a whole week read alike every day. */
function week(figure: number | null): readonly (number | null)[] {
  return Array.from({ length: 7 }, () => figure);
}

/** Two whole weeks from Monday 19 October 2026, and this one up to today, Wednesday 4 November. */
const THREE_WEEKS = daysFrom('2026-10-19', [...week(1), ...week(2), 4, 2, 9]);

describe('a post’s recent weeks', () => {
  it('runs from the week running now to the oldest, Monday to Sunday', () => {
    const rows = weeksOf(THREE_WEEKS, '2026-10-19');

    expect(rows.map((row) => [row.first, row.last])).toEqual([
      ['2026-11-02', '2026-11-08'],
      ['2026-10-26', '2026-11-01'],
      ['2026-10-19', '2026-10-25'],
    ]);
    expect(rows.map((row) => row.days.length)).toEqual([7, 7, 7]);
    expect(rows.map((row) => row.running)).toEqual([true, false, false]);
  });

  it('fills the days after today as still to come', () => {
    const [now] = weeksOf(THREE_WEEKS, '2026-10-19');

    expect(now?.days.map((cell) => cell.state)).toEqual([
      'counted',
      'counted',
      'today',
      'future',
      'future',
      'future',
      'future',
    ]);
    expect(now?.days.slice(3).map((cell) => cell.pageViews)).toEqual([null, null, null, null]);
    expect(now?.days[6]?.day).toBe('2026-11-08');
  });

  it('counts today in the week running now', () => {
    expect(weeksOf(THREE_WEEKS, '2026-10-19')[0]?.total).toBe(15);
  });

  /** A day still running set against whole ones would read as a fall every morning. */
  it('averages the week running now over the days that have finished', () => {
    expect(weeksOf(THREE_WEEKS, '2026-10-19')[0]?.perDay).toBe(3);
  });

  it('sets nothing against the week running now before one of its days has finished', () => {
    const [now, before] = weeksOf(daysFrom('2026-10-26', [...week(2), 5]), '2026-10-26');

    expect(now?.total).toBe(5);
    expect(now?.perDay).toBeNull();
    expect(now?.change).toBeNull();
    expect(before?.perDay).toBe(2);
  });

  it('sets each week’s unrounded average against the week before’s', () => {
    const [, later] = weeksOf(
      daysFrom('2026-10-19', [...week(1), 1, 1, 1, 1, 2, 2, 2, 0]),
      '2026-10-19',
    );

    expect(later?.perDay).toBe(10 / 7);
    expect(later?.change).toEqual({ direction: 'up', fraction: 0.43 });
  });

  it('calls a rise from a week of nought up from none', () => {
    const [, risen] = weeksOf(daysFrom('2026-10-19', [...week(0), ...week(1), 0]), '2026-10-19');

    expect(risen?.change).toEqual({ direction: 'up', fraction: null });
  });

  /** Nought set against nought is no change at all, rather than nothing to compare with. */
  it('calls a week of nought after a week of nought level', () => {
    const [, second] = weeksOf(daysFrom('2026-10-19', [...week(0), ...week(0), 0]), '2026-10-19');

    expect(second?.change).toEqual({ direction: 'level', fraction: 0 });
  });

  it('sets nothing against the oldest week', () => {
    expect(weeksOf(THREE_WEEKS, '2026-10-19').at(-1)?.change).toBeNull();
  });

  /** A post published last week is not shown a quarter of a year of noughts. */
  it('leaves out every week before the post’s first day in which nobody read it', () => {
    const rows = weeksOf(
      daysFrom('2026-10-12', [...week(0), ...week(0), 0, 0, 3, 2, 1, 0, 0, 4, 1]),
      '2026-10-28',
    );

    expect(rows.map((row) => row.first)).toEqual(['2026-11-02', '2026-10-26']);
    // Not told the day it was published, the days of its first week before then are kept, as nought.
    expect(rows[1]?.days.map((cell) => cell.pageViews)).toEqual([0, 0, 3, 2, 1, 0, 0]);
  });

  /** A week somebody read the post in is shown wherever it falls, and so is every week after it. */
  it('keeps a week before the post’s first day in which somebody read it, and every week after', () => {
    const rows = weeksOf(
      daysFrom('2026-10-12', [...week(0), 0, 0, 1, 0, 0, 0, 0, ...week(0), 0, 4]),
      '2026-10-28',
    );

    expect(rows.map((row) => row.first)).toEqual(['2026-11-02', '2026-10-26', '2026-10-19']);
    expect(rows[2]?.days.map((cell) => cell.pageViews)).toEqual([0, 0, 1, 0, 0, 0, 0]);
    // A week wholly before the post's first day has no day since it, so it is averaged over all seven.
    expect(rows[2]?.perDay).toBe(1 / 7);
  });

  /** The days before the post was published are not days on which nobody read it. */
  it('averages the week a post was published in over the days since it was published', () => {
    const [now, published] = weeksOf(
      daysFrom('2026-10-26', [0, 0, 3, 2, 1, 0, 0, 2, 1, 3]),
      '2026-10-28',
    );

    expect(published?.total).toBe(6);
    expect(published?.perDay).toBe(6 / 5);
    expect(now?.perDay).toBe(1.5);
    expect(now?.change).toEqual({ direction: 'up', fraction: 0.25 });
  });

  /** Mon and Tue went by before the post existed; Wednesday, the day it was published, is today. */
  it('sets nothing for the week a post was published in until one of its days since has finished', () => {
    const [published] = weeksOf(daysFrom('2026-10-26', [0, 0, 4]), '2026-10-28');

    expect(published?.total).toBe(4);
    expect(published?.perDay).toBeNull();
    expect(published?.change).toBeNull();
  });

  it('lines a first day that is not a Monday up under its own weekday', () => {
    const [oldest] = weeksOf(
      daysFrom('2026-10-21', [1, 1, 1, 1, 1, 1, 1]),
      '2026-10-21',
    ).toReversed();

    expect(oldest?.first).toBe('2026-10-19');
    expect(oldest?.days.map((cell) => cell.state)).toEqual([
      'before',
      'before',
      'counted',
      'counted',
      'counted',
      'counted',
      'counted',
    ]);
    expect(oldest?.days[2]?.day).toBe('2026-10-21');
  });

  /** Nought on a day nobody read the post is a different fact from a day nothing was measured on. */
  it('tells a day before the website was first measured from a day nobody read the post on', () => {
    const [now] = weeksOf(daysFrom('2026-11-02', [null, null, 0, 2]), '2026-11-04');

    expect(now?.days.slice(0, 4).map((cell) => [cell.state, cell.pageViews])).toEqual([
      ['before', null],
      ['before', null],
      ['counted', 0],
      ['today', 2],
    ]);
    expect(now?.perDay).toBe(0);
  });

  /** A day before the post was published is not a day nobody read it. */
  it('says the days of the week a post was published in before it was are before it, with no figure', () => {
    const [now, published] = weeksOf(
      daysFrom('2026-10-26', [0, 0, 3, 2, 1, 0, 0, 2, 1, 3]),
      '2026-10-28',
      '2026-10-28',
    );

    expect(published?.days.map((cell) => [cell.state, cell.pageViews])).toEqual([
      ['unpublished', null],
      ['unpublished', null],
      ['counted', 3],
      ['counted', 2],
      ['counted', 1],
      ['counted', 0],
      ['counted', 0],
    ]);
    expect(published?.total).toBe(6);
    expect(published?.perDay).toBe(6 / 5);
    expect(now?.days.map((cell) => cell.state)).not.toContain('unpublished');
    expect(now?.change).toEqual({ direction: 'up', fraction: 0.25 });
  });

  /** Nobody can have read the post before it was published, so no figure is lost by saying so. */
  it('comes to the same weeks whether or not it is told the day the post was published', () => {
    const days = daysFrom('2026-10-12', [...week(0), ...week(0), 0, 0, 3, 2, 1, 0, 0, 4, 1]);
    const told = weeksOf(days, '2026-10-28', '2026-10-28');
    const untold = weeksOf(days, '2026-10-28');
    const figures = (rows: readonly WeekRow[]) =>
      rows.map(({ first, total, perDay, change }) => ({ first, total, perDay, change }));

    expect(figures(told)).toEqual(figures(untold));
    expect(figures(told).map((row) => row.first)).toEqual(['2026-11-02', '2026-10-26']);
    expect(busiestDay(told)).toBe(busiestDay(untold));
  });

  /** Nothing was measured on such a day, which says nothing about the post either way. */
  it('still says a day before the website was measured is that, in the week a post was published in', () => {
    const [now] = weeksOf(daysFrom('2026-11-02', [null, 0, 4, 2]), '2026-11-04', '2026-11-04');

    expect(now?.days.slice(0, 4).map((cell) => [cell.state, cell.pageViews])).toEqual([
      ['before', null],
      ['unpublished', null],
      ['counted', 4],
      ['today', 2],
    ]);
    expect(now?.total).toBe(6);
    expect(now?.perDay).toBe(4);
  });

  it('shows every day with a figure as it was counted where it is not told the day the post was published', () => {
    const days = daysFrom('2026-11-02', [null, 0, 4, 2]);
    const [now] = weeksOf(days, '2026-11-04');

    expect(weeksOf(days, '2026-11-04', null)).toEqual(weeksOf(days, '2026-11-04'));
    expect(now?.days.slice(0, 2).map((cell) => [cell.state, cell.pageViews])).toEqual([
      ['before', null],
      ['counted', 0],
    ]);
  });

  it('averages a week over the days it was measured on', () => {
    const [, first] = weeksOf(
      daysFrom('2026-10-26', [null, null, null, 3, 3, 3, 3, 1]),
      '2026-10-29',
    );

    expect(first?.total).toBe(12);
    expect(first?.perDay).toBe(3);
  });

  it('lays out nothing for no days', () => {
    expect(weeksOf([], null)).toEqual([]);
  });

  it('lays out nothing where nothing of the website is held', () => {
    expect(weeksOf(daysFrom('2026-10-19', [...week(null), null, null]), null)).toEqual([]);
  });
});

describe('the busiest day of a post’s recent weeks', () => {
  it('is the most any day was read, which every day is shaded against', () => {
    expect(busiestDay(weeksOf(THREE_WEEKS, '2026-10-19'))).toBe(9);
  });

  it('is nought where nothing was read', () => {
    expect(busiestDay([])).toBe(0);
  });
});

describe('which way a week is said to have moved', () => {
  it.each([
    [{ direction: 'up', fraction: 0.5 }, 'up'],
    [{ direction: 'down', fraction: -0.25 }, 'down'],
    [{ direction: 'level', fraction: 0 }, 'level'],
    [{ direction: 'up', fraction: null }, 'none'],
  ] as const)('says %j moved %s', (change, move) => {
    expect(weekMove(change)).toBe(move);
  });
});
