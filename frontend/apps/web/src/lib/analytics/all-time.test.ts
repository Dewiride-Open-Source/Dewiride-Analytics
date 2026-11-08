import { createFormatter } from 'next-intl';
import { describe, expect, it } from 'vitest';
import {
  figureAcross,
  figureOf,
  largestOf,
  type MonthCell,
  recordShown,
  weekdayName,
  writableDay,
  writeFigure,
  writtenMonth,
  yearFigure,
  yearsOf,
} from '@/lib/analytics/all-time';
import type { Insights } from '@/lib/api/schemas';

const format = createFormatter({ locale: 'en' });

const TO = '2026-10-30T09:30:00+00:00';

/** A website's record: nothing read unless a test says so. */
function record(overrides: Partial<Insights> = {}): Insights {
  return {
    firstDay: '2025-11-19',
    to: TO,
    completeTo: TO,
    pageViews: 0,
    visitors: 0,
    bestDay: null,
    weekdays: [0, 0, 0, 0, 0, 0, 0],
    busiestWeekdays: [],
    hours: Array.from({ length: 24 }, () => 0),
    busiestHours: [],
    months: [],
    missing: [],
    unjudged: [],
    ...overrides,
  };
}

/** One month as the engine answers it. */
function month(written: string, pageViews: number | null, daysMeasured: number) {
  return { month: written, pageViews, daysMeasured };
}

/** The months of a record laid out, by the year and the month they belong to. */
function cellOf(rows: ReturnType<typeof yearsOf>, written: string): MonthCell {
  const cell = rows.find((row) => row.year === written.slice(0, 4))?.months[
    Number(written.slice(5)) - 1
  ];

  if (cell === undefined) {
    throw new Error(`The record does not lay out ${written}.`);
  }

  return cell;
}

describe('a record laid out as years', () => {
  it('runs from the newest year to the oldest, twelve months to each', () => {
    const rows = yearsOf(
      record({
        months: [month('2024-11', 10, 12), month('2025-01', 20, 31), month('2026-02', 30, 5)],
      }),
    );

    expect(rows.map((row) => row.year)).toEqual(['2026', '2025', '2024']);
    expect(rows.map((row) => row.months.length)).toEqual([12, 12, 12]);
    expect(rows[0]?.months[0]?.month).toBe('2026-01');
    expect(rows[0]?.months[11]?.month).toBe('2026-12');
  });

  it('lays out nothing for a record with no months', () => {
    expect(yearsOf(record())).toEqual([]);
  });

  it('tells a month before the record from one measured, the one running and one to come', () => {
    const rows = yearsOf(record({ months: [month('2026-03', 40, 20), month('2026-04', 50, 9)] }));

    expect(cellOf(rows, '2026-02')).toMatchObject({ state: 'before', pageViews: null, days: 0 });
    expect(cellOf(rows, '2026-03')).toMatchObject({ state: 'measured', pageViews: 40, days: 20 });
    expect(cellOf(rows, '2026-04')).toMatchObject({ state: 'running', pageViews: 50, days: 9 });
    expect(cellOf(rows, '2026-05')).toMatchObject({ state: 'future', pageViews: null, days: 0 });
  });

  /** Nought is a figure. A month nobody read is measured, and nothing says it was not. */
  it('keeps a month nobody read among the months measured', () => {
    const rows = yearsOf(record({ months: [month('2026-03', 0, 31), month('2026-04', 5, 9)] }));

    expect(cellOf(rows, '2026-03')).toMatchObject({ state: 'measured', pageViews: 0, days: 31 });
  });

  it('names a month left out by why it was, as the answer says', () => {
    const rows = yearsOf(
      record({
        months: [month('2026-01', null, 0), month('2026-02', null, 0), month('2026-03', 5, 9)],
        missing: ['2026-01'],
        unjudged: ['2026-02'],
      }),
    );

    expect(cellOf(rows, '2026-01')).toMatchObject({ state: 'notCounted', pageViews: null });
    expect(cellOf(rows, '2026-02')).toMatchObject({ state: 'notJudged', pageViews: null });
  });

  /** A hole in the answer is never read as a month nobody came in. */
  it('reads a month inside the record the answer does not name as never counted', () => {
    const rows = yearsOf(record({ months: [month('2026-01', 5, 31), month('2026-03', 5, 9)] }));

    expect(cellOf(rows, '2026-02')).toMatchObject({
      state: 'notCounted',
      pageViews: null,
      days: 0,
    });
  });
});

describe('what a month and a year show', () => {
  const rows = yearsOf(
    record({
      months: [
        month('2025-11', 120, 12),
        month('2025-12', 310, 31),
        month('2026-01', null, 0),
        month('2026-02', 280, 28),
        month('2026-03', 90, 9),
      ],
      missing: ['2026-01'],
    }),
  );
  const year2025 = rows.find((row) => row.year === '2025');
  const year2026 = rows.find((row) => row.year === '2026');

  it('reads a month as its page views', () => {
    expect(figureOf(cellOf(rows, '2025-11'), 'total')).toBe(120);
  });

  it('averages a month over the days measured, not the days it has', () => {
    expect(figureOf(cellOf(rows, '2025-11'), 'average')).toBe(10);
  });

  it('gives no figure to a month left out, however it is read', () => {
    expect(figureOf(cellOf(rows, '2026-01'), 'total')).toBeNull();
    expect(figureOf(cellOf(rows, '2026-01'), 'average')).toBeNull();
  });

  it('has nothing to divide by for a month with no days measured', () => {
    const none = { month: '2026-01', state: 'measured', pageViews: 0, days: 0 } as const;

    expect(figureOf(none, 'average')).toBeNull();
  });

  it('totals a year over the months with figures', () => {
    expect(year2025 && yearFigure(year2025, 'total')).toBe(430);
    expect(year2026 && yearFigure(year2026, 'total')).toBe(370);
  });

  /**
   * Σ views over Σ days, never the average of the months' averages, which would count a month of nine
   * days as much as a month of thirty. A month left out counts on neither side.
   */
  it('averages a year over the days its figures cover', () => {
    expect(year2025 && yearFigure(year2025, 'average')).toBe(430 / 43);
    expect(year2026 && yearFigure(year2026, 'average')).toBe(370 / 37);
  });

  /** Nought would say nobody came. Nothing says anything about it at all. */
  it('gives a year with no figures in it no figure of its own', () => {
    const left = yearsOf(
      record({
        months: [month('2025-11', null, 0), month('2025-12', null, 0), month('2026-01', 5, 2)],
        missing: ['2025-11', '2025-12'],
      }),
    ).find((row) => row.year === '2025');

    expect(left && yearFigure(left, 'total')).toBeNull();
    expect(left && yearFigure(left, 'average')).toBeNull();
  });

  /** A year's own figure would hold every month to the bottom of the scale. */
  it('shades against the largest month, never against a year', () => {
    expect(largestOf(rows, 'total')).toBe(310);
    expect(largestOf(rows, 'average')).toBe(10);
  });

  it('has nothing to shade against in an empty record', () => {
    expect(largestOf([], 'total')).toBe(0);
  });

  /** One post's months are read exactly as a year of the table is, wherever they fall. */
  it('adds the figures of any run of months up as a year does', () => {
    const months = rows.flatMap((row) => row.months);

    expect(figureAcross(months, 'total')).toBe(800);
    expect(figureAcross(months, 'average')).toBe(800 / 80);
    expect(figureAcross([cellOf(rows, '2026-01')], 'total')).toBeNull();
    expect(figureAcross([], 'average')).toBeNull();
  });
});

describe('a figure as a month, a week or a year shows it', () => {
  it('writes a count whole', () => {
    expect(writeFigure(5180, 'total', format)).toBe('5,180');
  });

  /** The difference between 1 and 1.4 a day is real, and between 41 and 41.4 it is not. */
  it('writes an average under ten with a decimal, and one too small to round as its first digit', () => {
    expect(writeFigure(9.66, 'average', format)).toBe('9.7');
    expect(writeFigure(41.4, 'average', format)).toBe('41');
    expect(writeFigure(0.032, 'average', format)).toBe('0.03');
    expect(writeFigure(0, 'average', format)).toBe('0');
  });

  it('marks a figure that is not there', () => {
    expect(writeFigure(null, 'total', format)).toBe('–');
  });
});

describe('a day on the calendar, to be written', () => {
  it('stays the day it is, in the zone it is written in', () => {
    expect(writableDay('2026-09-16').toISOString()).toBe('2026-09-16T00:00:00.000Z');
  });

  it('writes a month out in full, as the reader’s language writes one', () => {
    expect(writtenMonth('2026-08', format)).toBe('August 2026');
  });

  it.each([
    [0, 'long', 'Monday'],
    [6, 'long', 'Sunday'],
    [2, 'short', 'Wed'],
    [4, 'narrow', 'F'],
  ] as const)('names place %s in the week, written %s, as %s', (place, width, name) => {
    expect(weekdayName(place, width, format)).toBe(name);
  });
});

describe('what stands on the screen about a record', () => {
  const REFUSED = new Error('refused');
  const nothingYet = { data: undefined, error: null };
  const refused = { data: undefined, error: REFUSED };
  const figures = record({ pageViews: 12, visitors: 3 });
  const nought = record();
  const leavingOut = record({ missing: ['2026-01'] });

  /** A website nothing is held of has no people to be absent, whoever the screen is kept to. */
  it('says nothing of the website is held, without waiting for the screen to be kept to anybody', () => {
    const never = { data: record({ firstDay: null }), error: null };

    expect(recordShown(never, never)).toEqual({ state: 'nothing' });
    expect(recordShown(never, nothingYet)).toEqual({ state: 'nothing' });
    expect(recordShown(never, refused)).toEqual({ state: 'nothing' });
  });

  it('waits for the screen’s own answer', () => {
    expect(recordShown(nothingYet, nothingYet)).toEqual({ state: 'waiting' });
    expect(recordShown({ data: figures, error: null }, nothingYet)).toEqual({ state: 'waiting' });
  });

  /** A refused answer is not on its way, so nothing goes on standing in for it. */
  it('says its own answer was refused, rather than waiting for one that is not coming', () => {
    expect(recordShown(nothingYet, refused)).toEqual({ state: 'refused', error: REFUSED });
    expect(recordShown({ data: figures, error: null }, refused)).toEqual({
      state: 'refused',
      error: REFUSED,
    });
  });

  it('shows figures as soon as it has them, whatever everybody’s answer is doing', () => {
    expect(recordShown(nothingYet, { data: figures, error: null })).toEqual({
      state: 'ready',
      insights: figures,
    });
    expect(recordShown(refused, { data: figures, error: null })).toEqual({
      state: 'ready',
      insights: figures,
    });
  });

  it('keeps an answer it has read on the screen when asking again fails', () => {
    expect(recordShown(nothingYet, { data: figures, error: REFUSED })).toEqual({
      state: 'ready',
      insights: figures,
    });
  });

  /** Nought is only what was counted there, and the record says which months were not. */
  it('shows a record that comes to nought with months left out, rather than calling it empty', () => {
    expect(recordShown(nothingYet, { data: leavingOut, error: null })).toEqual({
      state: 'ready',
      insights: leavingOut,
    });
  });

  it('settles nothing about nought until everybody’s answer says who came', () => {
    expect(recordShown(nothingYet, { data: nought, error: null })).toEqual({ state: 'waiting' });
  });

  it('says it was refused where nought cannot be told apart without everybody’s answer', () => {
    expect(recordShown(refused, { data: nought, error: null })).toEqual({
      state: 'refused',
      error: REFUSED,
    });
  });

  it('calls nought among people absent where somebody came', () => {
    expect(recordShown({ data: figures, error: null }, { data: nought, error: null })).toEqual({
      state: 'absent',
    });
    expect(recordShown({ data: leavingOut, error: null }, { data: nought, error: null })).toEqual({
      state: 'absent',
    });
  });

  it('calls nought in everybody’s answer too a website with nothing to look back on', () => {
    expect(recordShown({ data: nought, error: null }, { data: nought, error: null })).toEqual({
      state: 'nothing',
    });
  });
});
