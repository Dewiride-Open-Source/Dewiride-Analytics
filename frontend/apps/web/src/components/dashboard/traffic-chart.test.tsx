import { fireEvent, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TrafficChart, type TrafficPoint } from '@/components/dashboard/traffic-chart';
import type { ChartView } from '@/lib/analytics/chart-view';
import type { Granularity } from '@/lib/analytics/period';
import type { TrafficSeries } from '@/lib/api/schemas';
import { drawn } from '@/test/drawing';
import { renderScreen } from '@/test/harness';

/**
 * Stands in for the drawing surface and runs the builder it is handed, so that what the chart
 * would be told to draw can be read as an object instead of as pixels on a canvas.
 */
vi.mock('@/components/charts/chart', async () => ({ ...(await import('@/test/drawing')) }));

/** Three whole days, each cut at midnight in Kolkata. */
const DAYS: readonly TrafficPoint[] = [
  { start: '2026-08-10T18:30:00+00:00', pageViews: 40, visitors: 12 },
  { start: '2026-08-11T18:30:00+00:00', pageViews: 55, visitors: 18 },
  { start: '2026-08-12T18:30:00+00:00', pageViews: 30, visitors: 9 },
];

/** Three hours of one day, cut on the same clock. */
const HOURS: readonly TrafficPoint[] = [
  { start: '2026-08-17T18:30:00+00:00', pageViews: 4, visitors: 2 },
  { start: '2026-08-17T19:30:00+00:00', pageViews: 9, visitors: 5 },
  { start: '2026-08-17T20:30:00+00:00', pageViews: 6, visitors: 3 },
];

/**
 * The same three days, judged.
 *
 * Two crawler categories that share a colour and never share a name, so that what the chart does
 * with them can be told apart from what the words do. Nothing red, so that a website nobody has
 * scraped can be seen not to carry a band for it.
 */
const JUDGED: TrafficSeries = {
  from: '2026-08-10T18:30:00+00:00',
  to: '2026-08-13T18:30:00+00:00',
  granularity: 'day',
  completeTo: '2026-08-13T18:30:00+00:00',
  buckets: DAYS.map((day) => day.start),
  groups: [
    { category: 'likely-human', sessions: [6, 9, 4], pageViews: [18, 24, 11] },
    { category: 'known-search-crawler', sessions: [2, 1, 3], pageViews: [2, 1, 3] },
    { category: 'known-ai-crawler', sessions: [1, 4, 0], pageViews: [1, 6, 0] },
    { category: 'unknown', sessions: [0, 1, 1], pageViews: [0, 1, 1] },
  ],
};

/**
 * The three days before those, so that the two runs are the same length.
 *
 * Every figure differs from the same day of the period being read, so a line drawn from the wrong
 * one cannot pass for the right one.
 */
const EARLIER_DAYS: readonly TrafficPoint[] = [
  { start: '2026-08-07T18:30:00+00:00', pageViews: 30, visitors: 10 },
  { start: '2026-08-08T18:30:00+00:00', pageViews: 44, visitors: 15 },
  { start: '2026-08-09T18:30:00+00:00', pageViews: 28, visitors: 8 },
];

/** The same three days, judged: six, eight and five visits. */
const EARLIER_JUDGED: TrafficSeries = {
  from: '2026-08-07T18:30:00+00:00',
  to: '2026-08-10T18:30:00+00:00',
  granularity: 'day',
  completeTo: '2026-08-10T18:30:00+00:00',
  buckets: EARLIER_DAYS.map((day) => day.start),
  groups: [
    { category: 'likely-human', sessions: [5, 7, 3], pageViews: [15, 21, 9] },
    { category: 'known-search-crawler', sessions: [1, 1, 2], pageViews: [1, 1, 2] },
  ],
};

/** What those days are called on screen. */
const EARLIER_DATES = '7–9 Aug 2026';

interface Drawing {
  series: {
    name?: string;
    type: string;
    stack?: string;
    data: (number | null | { value: number; itemStyle: { opacity: number } })[];
    itemStyle?: { color?: string };
    areaStyle?: { color?: unknown };
    lineStyle?: { type?: string; width?: number };
    smooth?: number | boolean;
    z?: number;
    markArea?: { z?: number; data: { xAxis: string }[][] };
  }[];
  xAxis: { data: string[]; boundaryGap: boolean };
}

interface Shown {
  readonly view?: ChartView;
  /** Whether every figure is kept to the visits judged to be people. */
  readonly peopleOnly?: boolean;
  readonly points?: readonly TrafficPoint[];
  /** The first bucket of activity still being judged, or nothing where every figure is settled. */
  readonly stillJudging?: number | null;
  readonly series?: TrafficSeries;
  readonly granularity?: Granularity;
  readonly manyDays?: boolean;
  readonly manyYears?: boolean;
  readonly problem?: unknown;
  readonly style?: string;
  /** Whether the period before is being drawn behind the one being read. */
  readonly against?: boolean;
  readonly earlierPoints?: readonly TrafficPoint[];
  readonly earlierSeries?: TrafficSeries;
  /** Told the day a bucket was pressed into, where the period can still be narrowed to one. */
  readonly onPickDay?: (day: string) => void;
}

const chose = vi.fn();
const compared = vi.fn();

function show({
  view = 'activity',
  peopleOnly = false,
  points = DAYS,
  stillJudging = null,
  series = JUDGED,
  granularity = 'day',
  manyDays = true,
  manyYears = false,
  problem = null,
  style,
  against = false,
  earlierPoints = EARLIER_DAYS,
  earlierSeries = EARLIER_JUDGED,
  onPickDay,
}: Shown = {}): Drawing {
  renderScreen(
    <TrafficChart
      view={view}
      onView={chose}
      peopleOnly={peopleOnly}
      activity={{ points, stillJudging }}
      who={series}
      problem={problem}
      comparison={{
        on: against,
        onChange: compared,
        activity: earlierPoints,
        who: earlierSeries,
        days: EARLIER_DATES,
      }}
      siteName="My Blog"
      timeZoneId="Asia/Kolkata"
      zone="Kolkata"
      granularity={granularity}
      manyDays={manyDays}
      manyYears={manyYears}
      onPickDay={onPickDay}
    />,
  );

  if (style) {
    fireEvent.click(screen.getByRole('radio', { name: style }));
  }

  return drawn.option as unknown as Drawing;
}

beforeEach(() => {
  window.localStorage.clear();
  drawn.option = undefined;
  drawn.pick = undefined;
  drawn.picks.clear();
  chose.mockClear();
  compared.mockClear();
});

describe('how much traffic there was', () => {
  it('draws both measures across the same buckets', () => {
    const option = show();

    expect(option.series.map((one) => one.name)).toStrictEqual(['Page views', 'Daily visitors']);
    expect(option.series[0]?.data).toStrictEqual([40, 55, 30]);
    expect(option.series[1]?.data).toStrictEqual([12, 18, 9]);
    expect(option.xAxis.data).toHaveLength(3);
  });

  it('publishes the same figures as a table, since a drawing is not readable to everyone', () => {
    show();

    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(DAYS.length + 1);
    expect(
      screen.getByRole('img', { name: /Page views and visitors for My Blog/ }),
    ).toBeInTheDocument();
  });

  it('names the place a day is counted in rather than its identifier', () => {
    show();

    expect(screen.getByText('Days run midnight to midnight in Kolkata.')).toBeInTheDocument();
  });

  /**
   * A day beginning at half past six the evening before is the next day where the site is, and the
   * day before that anywhere west of it. Written without naming the zone, every label on the chart
   * is off by one for most of the people reading it.
   */
  it("writes a bucket in the site's own day rather than the reader's", () => {
    const option = show();

    expect(option.xAxis.data).toStrictEqual(['Aug 11', 'Aug 12', 'Aug 13']);
  });

  /** A day written as `3 Jan` reads unambiguously within a year and ambiguously across one. */
  it('writes the year beside the day once the period runs across one', () => {
    const option = show({ manyYears: true });

    expect(option.xAxis.data[0]).toBe('Aug 11, 2026');
  });
});

describe('a period drawn an hour at a time', () => {
  it('writes each bucket as a time, with no date to repeat', () => {
    const option = show({ points: HOURS, granularity: 'hour', manyDays: false });

    expect(option.xAxis.data).toStrictEqual(['12 AM', '1 AM', '2 AM']);
  });

  it('keeps the date beside the time when the period covers more than one day', () => {
    const option = show({ points: HOURS, granularity: 'hour' });

    expect(option.xAxis.data[0]).toBe('Aug 18, 12 AM');
  });

  /**
   * Counted in an hour, distinct visitors are the people who were there in that hour. Calling that
   * figure the day's would be a claim about numbers that were never added up that way.
   */
  it('does not call an hour of visitors a day of them', () => {
    const option = show({ points: HOURS, granularity: 'hour', manyDays: false });

    expect(option.series.map((one) => one.name)).toStrictEqual(['Page views', 'Visitors']);
    expect(screen.getByText('Times are the clock in Kolkata.')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Time' })).toBeInTheDocument();
  });
});

describe('who the traffic was', () => {
  /**
   * Fourteen categories reach the screen and four colours leave it, because the colour answers
   * what somebody glances at a chart to ask. A crawler this product has confirmed and one that
   * merely says it is that crawler share this band and are never named by it.
   */
  it('gathers the categories into the four things a colour can mean', () => {
    const option = show({ view: 'who' });

    expect(option.series.map((one) => one.name)).toStrictEqual([
      'People',
      'Machinery',
      "Can't say",
    ]);
    expect(option.series[1]?.data).toStrictEqual([3, 5, 3]);
  });

  it('leaves out a kind of traffic the period never held', () => {
    show({ view: 'who' });

    expect(screen.queryByText('Unwanted')).not.toBeInTheDocument();
  });

  it('stacks the bands so the whole column is the period’s judged visits', () => {
    const option = show({ view: 'who' });

    expect(new Set(option.series.map((one) => one.stack))).toStrictEqual(new Set(['who']));
  });

  it('publishes every band and its total as a table', () => {
    show({ view: 'who' });

    expect(screen.getByRole('columnheader', { name: 'All visits' })).toBeInTheDocument();
    expect(screen.getByRole('row', { name: 'Aug 12 15 9 5 1' })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /Who and what visited My Blog/ })).toBeInTheDocument();
  });

  it('says which visits it counted, and where its days are cut', () => {
    show({ view: 'who' });

    expect(screen.getByText('Visits that have finished, by day in Kolkata.')).toBeInTheDocument();
  });

  /**
   * A website whose verdicts are still coming has usually had plenty of traffic, so the state
   * carries the one thing worth doing from it rather than being a sentence in an empty box.
   */
  it('has something to say about a period nothing has been judged in, and somewhere to go', async () => {
    show({ view: 'who', series: { ...JUDGED, groups: [] } });

    expect(screen.getByText('No visits have been judged in this period yet.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'See how much was read' }));

    expect(chose).toHaveBeenCalledWith('activity');
  });
});

describe('the buckets that have not finished being judged', () => {
  /** A visit is judged once it ends, so the last bucket of a live period is always still filling. */
  const FILLING: TrafficSeries = { ...JUDGED, completeTo: '2026-08-12T18:30:00+00:00' };

  /**
   * A shape drawn through points draws everything to the right of the last settled point from a
   * figure that is still filling, so the wash starts there rather than at the first unsettled
   * point — where, on the ordinary live period, it would start at the end of the run and have no
   * width at all.
   */
  it('washes over them where the period is drawn as one continuous shape', () => {
    const option = show({ view: 'who', series: FILLING });

    expect(option.series.at(-1)?.markArea?.data).toStrictEqual([
      [{ xAxis: 'Aug 12' }, { xAxis: 'Aug 13' }],
    ]);
  });

  it('washes over the whole run where none of it has finished being judged', () => {
    const option = show({ view: 'who', series: { ...JUDGED, completeTo: JUDGED.from } });

    expect(option.series.at(-1)?.markArea?.data).toStrictEqual([
      [{ xAxis: 'Aug 11' }, { xAxis: 'Aug 13' }],
    ]);
  });

  /**
   * A wash is placed against the points the labels sit at, and columns straddle those points
   * rather than starting at them — so a wash would cut the first and last column of the run down
   * the middle. Fading each column instead lands exactly on the buckets it belongs to.
   */
  it('fades them one at a time where the period is drawn as columns', () => {
    const option = show({ view: 'who', series: FILLING, style: 'Columns' });

    expect(option.series.some((one) => one.markArea !== undefined)).toBe(false);
    expect(option.series[0]?.data).toStrictEqual([
      6,
      9,
      { value: 4, itemStyle: { opacity: 0.35 } },
    ]);
  });

  it('says so in the table too, since a wash on a canvas is not readable to everyone', () => {
    show({ view: 'who', series: FILLING });

    expect(screen.getAllByText('still being judged')).toHaveLength(1);
    expect(
      screen.getByText(
        'Visits that have finished, by day in Kolkata. The newest are still being judged.',
      ),
    ).toBeInTheDocument();
  });

  it('says nothing at all once the whole period has settled', () => {
    const option = show({ view: 'who' });

    expect(option.series.some((one) => one.markArea !== undefined)).toBe(false);
    expect(screen.queryByText('The newest are still being judged.')).not.toBeInTheDocument();
  });
});

describe('choosing what to look at and how', () => {
  it('offers both questions and reports which one was asked for', async () => {
    show();

    await userEvent.click(screen.getByRole('radio', { name: 'Who came' }));

    expect(chose).toHaveBeenCalledWith('who');
  });

  it('redraws the same numbers in the style somebody picked', async () => {
    show();

    await userEvent.click(screen.getByRole('radio', { name: 'Columns' }));

    const option = drawn.option as unknown as Drawing;

    expect(option.series.map((one) => one.type)).toStrictEqual(['bar', 'bar']);
    expect(option.xAxis.boundaryGap).toBe(true);
  });

  it('remembers the style for the next time somebody opens a chart', async () => {
    show();

    await userEvent.click(screen.getByRole('radio', { name: 'Line' }));

    expect(window.localStorage.getItem('dewiride.chart-drawing')).toBe('line');
  });

  /**
   * Each band on a stack is read by its thickness, and a line drawn along the top of one is read
   * as that band's own figure when it is really the total of everything underneath it.
   */
  it('does not offer a line for the stacked view, and falls back to one that reads honestly', () => {
    window.localStorage.setItem('dewiride.chart-drawing', 'line');

    const option = show({ view: 'who' });

    expect(screen.queryByRole('radio', { name: 'Line' })).not.toBeInTheDocument();
    expect(option.series[0]?.type).toBe('line');
    expect(option.series[0]?.areaStyle).toBeDefined();
  });
});

describe('a picture that could not be drawn', () => {
  it('says what went wrong instead of holding an empty frame for ever', () => {
    show({ problem: new Error('nope') });

    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Who came' })).toBeInTheDocument();
  });
});

describe('the period before, set behind the one being read', () => {
  it('is not drawn until somebody asks for it', () => {
    const option = show({ view: 'who' });

    expect(option.series.some((one) => one.lineStyle?.type === 'dashed')).toBe(false);
    expect(screen.queryByText(/dashed/)).not.toBeInTheDocument();
  });

  it('is asked for by the control beside the rest', async () => {
    show({ view: 'who' });

    await userEvent.click(screen.getByRole('button', { name: 'Compare with the period before' }));

    expect(compared).toHaveBeenCalledWith(true);
  });

  /**
   * One line rather than four. Four faded bands behind four solid ones is unreadable, and it is
   * not what a comparison is for: the bands already answer what this period was made of.
   */
  it('is one dashed total on the stacked view, drawn over the stack rather than under it', () => {
    const option = show({ view: 'who', against: true });
    const earlier = option.series.at(-1);

    expect(earlier?.name).toBe('All visits before');
    expect(earlier?.data).toStrictEqual([6, 8, 5]);
    expect(earlier?.lineStyle?.type).toBe('dashed');
    expect(earlier?.stack).toBeUndefined();
  });

  it('is both measures dashed on the view that counts how much was read', () => {
    const option = show({ view: 'activity', against: true });
    const earlier = option.series.filter((one) => one.lineStyle?.type === 'dashed');

    expect(earlier.map((one) => one.name)).toStrictEqual([
      'Page views before',
      'Daily visitors before',
    ]);
    expect(earlier[0]?.data).toStrictEqual([30, 44, 28]);
    expect(earlier[1]?.data).toStrictEqual([10, 15, 8]);
  });

  /**
   * The dashed line is drawn against this period's buckets, because reading two periods off the
   * same place on the axis is the whole point of it. Which days it actually covers therefore has
   * to be written down, or the comparison is a shape with no dates on it.
   */
  it('says which days it covers', () => {
    show({ view: 'who', against: true });

    expect(
      screen.getByText(
        'Visits that have finished, by day in Kolkata. The dashed line is 7–9 Aug 2026.',
      ),
    ).toBeInTheDocument();
  });

  it('publishes its figures in the table beside this period’s', () => {
    show({ view: 'who', against: true });

    expect(screen.getByRole('columnheader', { name: 'All visits before' })).toBeInTheDocument();
    expect(
      screen
        .getAllByRole('cell')
        .slice(0, 2)
        .map((cell) => cell.textContent),
    ).toStrictEqual(['9', '6']);
  });

  /**
   * A month set beside a shorter one has no thirty-first day. A line that stops where the earlier
   * period ran out is the truthful drawing of a day that did not exist.
   */
  it('stops where the earlier period was shorter than this one', () => {
    const option = show({
      view: 'activity',
      against: true,
      earlierPoints: EARLIER_DAYS.slice(0, 2),
    });
    const earlier = option.series.filter((one) => one.lineStyle?.type === 'dashed');

    expect(earlier[0]?.data).toStrictEqual([30, 44, null]);
  });
});

describe('an earlier period drawn across a period that is still being judged', () => {
  /** Three days of which the last is still filling, so the wash covers the end of the run. */
  const FILLING: TrafficSeries = { ...JUDGED, completeTo: '2026-08-12T18:30:00+00:00' };

  /**
   * The wash marks the buckets of this period that have not finished being judged. The earlier
   * period has been judged in full, so it is not one of the things being quietened — and a
   * comparison somebody asked for that fades out at exactly the end they are looking at is worse
   * than no comparison at all.
   */
  it('is drawn over the wash rather than under it', () => {
    const option = show({ view: 'who', series: FILLING, against: true });
    const wash = option.series.find((one) => one.markArea !== undefined);
    const earlier = option.series.at(-1);

    expect(wash?.markArea?.z).toBeDefined();
    expect(earlier?.z ?? 0).toBeGreaterThan(wash?.markArea?.z ?? 0);
  });

  /**
   * A straight line beside a curved one reads as a difference in the traffic rather than as a
   * difference in how the two were drawn.
   */
  it('is curved exactly as much as the measure it is set against', () => {
    const option = show({ view: 'activity', against: true, style: 'Line' });
    const earlier = option.series.filter((one) => one.lineStyle?.type === 'dashed');

    expect(earlier[0]?.smooth).toBe(option.series[0]?.smooth);
  });
});

describe('the picture kept to the people a website is for', () => {
  /** The same three days, judged to hold machinery and nothing else. */
  const NOBODY: TrafficSeries = {
    ...JUDGED,
    groups: JUDGED.groups.filter((group) => group.category !== 'likely-human'),
  };

  it('draws only the people where somebody asked for them alone', () => {
    const option = show({ view: 'who', peopleOnly: true });

    expect(option.series.map((one) => one.name)).toStrictEqual(['People']);
    expect(option.series[0]?.data).toStrictEqual([6, 9, 4]);
    expect(screen.queryByText('Unwanted')).not.toBeInTheDocument();
    expect(screen.queryByText('Machinery')).not.toBeInTheDocument();

    fireEvent.click(screen.getByText('Show these figures as a table'));

    expect(screen.queryByRole('columnheader', { name: 'All visits' })).not.toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'People' })).toBeInTheDocument();
  });

  /**
   * The figures are the cards' own arithmetic over the people's reports, so they are the same
   * shape as everybody's and differ only in whose they are — which their names say.
   */
  it('names the measures for the people when the figures are theirs', () => {
    const option = show({ view: 'activity', peopleOnly: true });

    expect(option.series.map((one) => one.name)).toStrictEqual([
      'Page views by people',
      'Daily visitors judged to be people',
    ]);
    expect(option.series[0]?.data).toStrictEqual([40, 55, 30]);
    expect(option.series[1]?.data).toStrictEqual([12, 18, 9]);
  });

  it('does not call an hour of people a day of them', () => {
    const option = show({ view: 'activity', peopleOnly: true, points: HOURS, granularity: 'hour' });

    expect(option.series[1]?.name).toBe('Visitors judged to be people');
  });

  it('says once whose figures they are, and where its days are cut', () => {
    show({ view: 'activity', peopleOnly: true });

    expect(screen.getByText('Days run midnight to midnight in Kolkata.')).toBeInTheDocument();
    expect(
      screen.getByRole('img', { name: /Page views and visitors among people on My Blog/ }),
    ).toBeInTheDocument();
  });

  /**
   * A visit is judged once it ends, so the people's figures have a tail that is still filling —
   * and it is washed exactly as the picture of who came washes its own.
   */
  it('washes the buckets the engine has not finished judging', () => {
    const area = show({ view: 'activity', peopleOnly: true, stillJudging: 2, style: 'Area' });

    expect(area.series.at(-1)?.markArea?.data).toStrictEqual([
      [{ xAxis: 'Aug 12' }, { xAxis: 'Aug 13' }],
    ]);
    expect(
      screen.getByText(
        'Days run midnight to midnight in Kolkata. The newest are still being judged.',
      ),
    ).toBeInTheDocument();
    expect(screen.getAllByText('still being judged')).toHaveLength(1);

    fireEvent.click(screen.getByRole('radio', { name: 'Columns' }));

    const columns = drawn.option as unknown as Drawing;

    expect(columns.series.some((one) => one.markArea !== undefined)).toBe(false);
    expect(columns.series[0]?.data[2]).toStrictEqual({ value: 30, itemStyle: { opacity: 0.35 } });
  });

  it('washes nothing where every figure is settled', () => {
    const option = show({ view: 'activity', peopleOnly: true, style: 'Area' });

    expect(option.series.some((one) => one.markArea !== undefined)).toBe(false);
    expect(screen.queryByText(/The newest are still being judged/)).not.toBeInTheDocument();
  });

  it("names the earlier period's measures for the people too", () => {
    const activity = show({ view: 'activity', peopleOnly: true, against: true });
    const dashed = activity.series.filter((one) => one.lineStyle?.type === 'dashed');

    expect(dashed.map((one) => one.name)).toStrictEqual([
      'Page views by people before',
      'Daily visitors judged to be people before',
    ]);
    expect(dashed[0]?.data).toStrictEqual(EARLIER_DAYS.map((point) => point.pageViews));
    expect(dashed[1]?.data).toStrictEqual(EARLIER_DAYS.map((point) => point.visitors));
  });

  /**
   * The earlier period's people rather than the whole of it. A dashed line for everybody behind
   * a stack kept to people would tower over it and say nothing about whether more of them came.
   */
  it('is one dashed line of people behind the stack kept to them', () => {
    const option = show({ view: 'who', peopleOnly: true, against: true });
    const earlier = option.series.at(-1);

    expect(earlier?.name).toBe('People before');
    expect(earlier?.data).toStrictEqual([5, 7, 3]);
    expect(earlier?.lineStyle?.type).toBe('dashed');
    expect(screen.getByRole('columnheader', { name: 'People before' })).toBeInTheDocument();
  });

  /**
   * A period judged to hold no people is an answer rather than an empty box. No way out is
   * offered on the card, because the control that kept the screen to people sits above it and is
   * the way out of it.
   */
  it('has something to say when nobody in the period was judged to be a person', () => {
    show({ view: 'who', peopleOnly: true, series: NOBODY });

    expect(
      screen.getByText('No visits in this period were judged to be people.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Show everyone' })).not.toBeInTheDocument();
  });

  /**
   * The one way out of a period nothing has been judged in is the picture that waits on no
   * verdict, which kept to people is the people's how much — counted from the same reports the
   * cards above are.
   */
  it('leads from a stack kept to people, with nothing judged, to how much was read', async () => {
    show({ view: 'who', peopleOnly: true, series: { ...JUDGED, groups: [] } });

    expect(screen.getByText('No visits have been judged in this period yet.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'See how much was read' }));

    expect(chose).toHaveBeenCalledWith('activity');
  });

  /** The population is the screen's to choose, above the card, which offers no second control for it. */
  it('offers no control of its own for keeping the picture to people', () => {
    show({ peopleOnly: true });

    expect(screen.queryByRole('button', { name: /People only/ })).not.toBeInTheDocument();
  });

  /**
   * Kept to people, the one band is the whole, so it takes the whole's place at the front and the
   * period before follows it — the order every other table on the card reads in.
   */
  it('tables the people first and the people before them second', () => {
    show({ view: 'who', peopleOnly: true, against: true });

    const headers = screen.getAllByRole('columnheader').map((header) => header.textContent);

    expect(headers).toStrictEqual(['Day', 'People', 'People before']);
  });
});

describe('a day pressed on the picture', () => {
  /** The same three days, the last of them still being judged. */
  const FILLING: TrafficSeries = { ...JUDGED, completeTo: '2026-08-12T18:30:00+00:00' };

  /**
   * A bucket beginning at half past six in the evening, London time, is the next day where the
   * website is — and that is the day it hands back, whoever is reading.
   */
  it("hands the picture the day a bucket falls in, in the website's own zone", () => {
    const picked = vi.fn();

    show({ onPickDay: picked });
    drawn.pick?.(1);

    expect(picked).toHaveBeenCalledWith('2026-08-12');
  });

  /**
   * Under a comparison the earlier period is drawn behind this one, bucket for bucket, so the
   * first column is two days at once. The one pressed is this period's.
   */
  it("lands on this period's day rather than the earlier one's, with the period before drawn behind it", () => {
    const picked = vi.fn();

    show({ against: true, onPickDay: picked });
    drawn.pick?.(0);

    expect(picked).toHaveBeenCalledWith('2026-08-11');
    expect(picked).not.toHaveBeenCalledWith('2026-08-08');
  });

  /** A canvas is not a control, so the table the same figures are published in carries the way in. */
  it('offers the same day from the row of the table, for anybody without a pointer', async () => {
    const picked = vi.fn();

    show({ onPickDay: picked });

    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(screen.getByRole('button', { name: 'Aug 12, look at this day' }));

    expect(picked).toHaveBeenCalledWith('2026-08-12');
  });

  it('names the day an hour falls in, on a period cut by the hour', async () => {
    const picked = vi.fn();

    show({ points: HOURS, granularity: 'hour', onPickDay: picked });

    await userEvent.click(screen.getByRole('button', { name: 'Aug 18, 12 AM, look at this day' }));

    expect(picked).toHaveBeenCalledWith('2026-08-18');
  });

  it('takes the picture of who came into a day too', () => {
    const picked = vi.fn();

    show({ view: 'who', onPickDay: picked });
    drawn.pick?.(2);

    expect(picked).toHaveBeenCalledWith('2026-08-13');
    expect(screen.getByRole('button', { name: 'Aug 13, look at this day' })).toBeInTheDocument();
  });

  /**
   * A day still being judged is still a day. The row that says so beneath its bucket offers the
   * same way in as every other row; that the wash on the canvas does not swallow a press is a
   * fact about the surface, held by looking at it in a browser.
   */
  it('offers a bucket still being judged the same way in', () => {
    const picked = vi.fn();

    show({ view: 'who', series: FILLING, onPickDay: picked });
    drawn.pick?.(2);

    expect(picked).toHaveBeenCalledWith('2026-08-13');
    expect(screen.getByRole('button', { name: 'Aug 13, look at this day' })).toBeInTheDocument();
    expect(screen.getAllByText('still being judged')).toHaveLength(1);
  });

  /** The far edge of the last column rounds to one more bucket than there are, which is no day. */
  it('names no day for a press the surface places past the last bucket', () => {
    const picked = vi.fn();

    show({ onPickDay: picked });

    expect(() => drawn.pick?.(DAYS.length)).not.toThrow();
    expect(picked).not.toHaveBeenCalled();
  });

  /** On a period that is already one day there is nothing narrower, and the table reads as it did. */
  it('offers no way into a day when none is offered, and the table reads as it did', () => {
    show();

    expect(drawn.pick).toBeUndefined();
    expect(screen.queryByRole('button', { name: /look at this day/ })).not.toBeInTheDocument();
    expect(screen.getAllByRole('rowheader').map((row) => row.textContent)).toStrictEqual([
      'Aug 11',
      'Aug 12',
      'Aug 13',
    ]);
  });
});
