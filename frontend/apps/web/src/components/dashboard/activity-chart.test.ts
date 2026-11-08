import { describe, expect, it } from 'vitest';
import {
  type ActivityChart,
  activityOption,
  type Measure,
} from '@/components/dashboard/activity-chart';
import type { Drawing } from '@/lib/charts/drawing';
import { PALETTE } from '@/test/drawing';

/** A series as the drawing is handed it, with the parts these tests read. */
interface Drawn {
  readonly name?: string;
  readonly type: string;
  readonly data: readonly unknown[];
  readonly showSymbol?: boolean;
  readonly showAllSymbol?: boolean;
  readonly silent?: boolean;
  readonly tooltip?: { readonly show?: boolean };
  readonly itemStyle?: { readonly color?: string };
  readonly markArea?: unknown;
}

/** Fifteen months, more than a line carries a dot on every one of. */
const FIFTEEN = Array.from({ length: 15 }, (_, at) => `Month ${at + 1}`);

/**
 * Fifteen months of views: the first read on its own before a month never counted, then a run of
 * counted months, then another month on its own between two never counted, and the last on its own
 * after one.
 */
const BROKEN: readonly (number | null)[] = [
  120,
  null,
  95,
  88,
  102,
  76,
  81,
  64,
  null,
  59,
  null,
  70,
  92,
  null,
  40,
];

/** The series a measure of these figures is drawn as. */
function drawn(
  values: readonly (number | null)[],
  drawing: Drawing = 'area',
  second?: Measure,
): readonly Drawn[] {
  const views: Measure = { name: 'Views', values };
  const chart: ActivityChart = {
    labels: FIFTEEN.slice(0, values.length),
    titles: FIFTEEN.slice(0, values.length),
    granularity: 'month',
    measures: second === undefined ? [views] : [views, second],
    figure: (value) => String(value),
    drawing,
  };

  return activityOption(chart, PALETTE).series as readonly Drawn[];
}

/** The dots drawn for the figures a line or an area would leave undrawn. */
function dots(series: readonly Drawn[]): readonly Drawn[] {
  return series.filter(
    (one) => one.type === 'line' && one.silent === true && one.markArea === undefined,
  );
}

describe('a measure drawn as a line or an area', () => {
  it('leaves the dots off a run of more buckets than it carries a dot on every one of', () => {
    const [views] = drawn(BROKEN);

    expect(views?.name).toBe('Views');
    expect(views?.showSymbol).toBe(false);
  });

  /**
   * A line runs between two figures, so a month with none either side of it has nothing to run to,
   * and would read as a month nobody counted while the table and the hover give it a figure.
   */
  it('draws a month with no figure on either side as a dot', () => {
    const [dotted, ...more] = dots(drawn(BROKEN));

    expect(more).toStrictEqual([]);
    expect(dotted?.data).toStrictEqual([
      120,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      59,
      null,
      null,
      null,
      null,
      40,
    ]);
    expect(dotted?.showSymbol).toBe(true);
    // However tightly the months sit, rather than only on those the axis names.
    expect(dotted?.showAllSymbol).toBe(true);
    expect(dotted?.itemStyle?.color).toBe(PALETTE.series[0]);
  });

  /** The card a hover opens already writes the month's figure, once. */
  it('keeps the dots out of the card a hover opens and out of reach of the pointer', () => {
    const [dotted] = dots(drawn(BROKEN, 'line'));

    expect(dotted?.tooltip?.show).toBe(false);
    expect(dotted?.silent).toBe(true);
  });

  it('adds no dots to a run every figure of which has a neighbour', () => {
    expect(dots(drawn(BROKEN.map((value) => value ?? 50)))).toStrictEqual([]);
    expect(
      dots(drawn([120, 95, null, 88, 102, 76, 81, 64, 59, null, null, 70, 92, 85, 40])),
    ).toStrictEqual([]);
  });

  /** Where every bucket carries a dot, a month on its own is drawn as its own dot already. */
  it('adds no dots where every bucket already carries one', () => {
    const series = drawn(BROKEN.slice(0, 14));

    expect(series[0]?.showSymbol).toBe(true);
    expect(dots(series)).toStrictEqual([]);
  });

  it('adds no dots to columns, which draw every figure as a column of its own', () => {
    expect(dots(drawn(BROKEN, 'columns'))).toStrictEqual([]);
  });

  /** Two measures share their months, and each is dotted in its own colour, over both areas. */
  it('draws the dots of each measure in its own colour, over both measures', () => {
    const series = drawn(BROKEN, 'area', {
      name: 'Daily visitors',
      values: BROKEN.map((value) => (value === null ? null : Math.round(value / 2))),
    });

    expect(series.map((one) => one.name)).toStrictEqual([
      'Views',
      'Daily visitors',
      undefined,
      undefined,
    ]);
    expect(dots(series).map((one) => one.itemStyle?.color)).toStrictEqual([
      PALETTE.series[0],
      PALETTE.series[1],
    ]);
  });
});
