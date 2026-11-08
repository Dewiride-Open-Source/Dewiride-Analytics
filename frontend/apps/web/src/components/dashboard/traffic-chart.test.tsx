import { cleanup, fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  type Activity,
  TrafficChart,
  type TrafficPoint,
} from '@/components/dashboard/traffic-chart';
import type { ChartBy, ChartCutting } from '@/lib/analytics/chart-by';
import type { ChartView } from '@/lib/analytics/chart-view';
import type { Granularity } from '@/lib/analytics/period';
import type { TrafficPicture, TrafficSeries } from '@/lib/api/schemas';
import { UNSETTLED } from '@/lib/charts/frame';
import { drawn, PALETTE } from '@/test/drawing';
import { renderScreen } from '@/test/harness';

/**
 * Stands in for the drawing surface and runs the builder it is handed, so that what the chart
 * would be told to draw can be read as an object instead of as pixels on a canvas.
 */
vi.mock('@/components/charts/chart', async () => ({ ...(await import('@/test/drawing')) }));

/** How much was read over a window, before anything about the judging is known. */
type Counted = Omit<Activity, 'stillJudging' | 'completeTo'>;

/** Three whole days, each cut at midnight in Kolkata, and the window they fill. */
const DAYS: Counted = {
  from: '2026-08-10T18:30:00+00:00',
  to: '2026-08-13T18:30:00+00:00',
  points: [
    { start: '2026-08-10T18:30:00+00:00', pageViews: 40, visitors: 12 },
    { start: '2026-08-11T18:30:00+00:00', pageViews: 55, visitors: 18 },
    { start: '2026-08-12T18:30:00+00:00', pageViews: 30, visitors: 9 },
  ],
};

/** Three hours of one day, cut on the same clock. */
const HOURS: Counted = {
  from: '2026-08-17T18:30:00+00:00',
  to: '2026-08-17T21:30:00+00:00',
  points: [
    { start: '2026-08-17T18:30:00+00:00', pageViews: 4, visitors: 2 },
    { start: '2026-08-17T19:30:00+00:00', pageViews: 9, visitors: 5 },
    { start: '2026-08-17T20:30:00+00:00', pageViews: 6, visitors: 3 },
  ],
};

/**
 * The first to the thirtieth of October in Kolkata, a week at a time. Each week is named by the
 * moment its Monday begins there; the first began on the twenty-eighth of September, before the
 * period did, and the last is still running.
 */
const WEEKS: Counted = {
  from: '2026-09-30T18:30:00+00:00',
  to: '2026-10-30T10:00:00+00:00',
  points: [
    { start: '2026-09-27T18:30:00+00:00', pageViews: 120, visitors: 40 },
    { start: '2026-10-04T18:30:00+00:00', pageViews: 310, visitors: 98 },
    { start: '2026-10-11T18:30:00+00:00', pageViews: 280, visitors: 91 },
    { start: '2026-10-18T18:30:00+00:00', pageViews: 295, visitors: 95 },
    { start: '2026-10-25T18:30:00+00:00', pageViews: 190, visitors: 66 },
  ],
};

/** Three whole weeks in Kolkata, the fifth to the twenty-fifth of October, and nothing either side. */
const WHOLE_WEEKS: Counted = {
  from: '2026-10-04T18:30:00+00:00',
  to: '2026-10-25T18:30:00+00:00',
  points: [
    { start: '2026-10-04T18:30:00+00:00', pageViews: 310, visitors: 98 },
    { start: '2026-10-11T18:30:00+00:00', pageViews: 280, visitors: 91 },
    { start: '2026-10-18T18:30:00+00:00', pageViews: 295, visitors: 95 },
  ],
};

/**
 * The second of August to the thirtieth of October in Kolkata, a month at a time. The first month
 * is cut by the period, and the last is still running.
 */
const MONTHS: Counted = {
  from: '2026-08-01T18:30:00+00:00',
  to: '2026-10-30T10:00:00+00:00',
  points: [
    { start: '2026-07-31T18:30:00+00:00', pageViews: 1200, visitors: 400 },
    { start: '2026-08-31T18:30:00+00:00', pageViews: 1350, visitors: 450 },
    { start: '2026-09-30T18:30:00+00:00', pageViews: 1100, visitors: 380 },
  ],
};

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
  buckets: DAYS.points.map((day) => day.start),
  groups: [
    { category: 'likely-human', sessions: [6, 9, 4], pageViews: [18, 24, 11] },
    { category: 'known-search-crawler', sessions: [2, 1, 3], pageViews: [2, 1, 3] },
    { category: 'known-ai-crawler', sessions: [1, 4, 0], pageViews: [1, 6, 0] },
    { category: 'unknown', sessions: [0, 1, 1], pageViews: [0, 1, 1] },
  ],
};

/** The same weeks of October, judged. */
const JUDGED_WEEKS: TrafficSeries = {
  from: WEEKS.from,
  to: WEEKS.to,
  granularity: 'week',
  completeTo: WEEKS.to,
  buckets: WEEKS.points.map((week) => week.start),
  groups: [
    {
      category: 'likely-human',
      sessions: [30, 80, 70, 75, 50],
      pageViews: [90, 240, 210, 225, 150],
    },
    {
      category: 'known-search-crawler',
      sessions: [5, 12, 10, 11, 8],
      pageViews: [5, 12, 10, 11, 8],
    },
  ],
};

/**
 * All time from the nineteenth of August 2025, the record's first day, to the fifteenth of January
 * 2026, a month at a time. September was never counted.
 */
const RECORD_BY_MONTH: Counted = {
  from: '2025-08-18T18:30:00+00:00',
  to: '2026-01-15T10:00:00+00:00',
  points: [
    { start: '2025-07-31T18:30:00+00:00', pageViews: 400, visitors: 120 },
    { start: '2025-08-31T18:30:00+00:00', pageViews: null, visitors: null },
    { start: '2025-09-30T18:30:00+00:00', pageViews: 700, visitors: 210 },
    { start: '2025-10-31T18:30:00+00:00', pageViews: 650, visitors: 200 },
    { start: '2025-11-30T18:30:00+00:00', pageViews: 900, visitors: 270 },
    { start: '2025-12-31T18:30:00+00:00', pageViews: 300, visitors: 90 },
  ],
};

/** The same record, judged, with September never counted in any category. */
const RECORD_JUDGED: TrafficPicture = {
  from: RECORD_BY_MONTH.from,
  to: RECORD_BY_MONTH.to,
  granularity: 'month',
  completeTo: RECORD_BY_MONTH.to,
  buckets: RECORD_BY_MONTH.points.map((month) => month.start),
  missing: ['2025-09'],
  unjudged: [],
  groups: [
    {
      category: 'likely-human',
      sessions: [90, null, 160, 150, 200, 70],
      pageViews: [270, null, 480, 450, 600, 210],
    },
    {
      category: 'known-search-crawler',
      sessions: [10, null, 20, 25, 30, 5],
      pageViews: [10, null, 20, 25, 30, 5],
    },
  ],
};

/**
 * The same record judged, with November kept before its visits were all judged as well: counted,
 * but with no settled verdicts in any category.
 */
const RECORD_UNSETTLED: TrafficPicture = {
  ...RECORD_JUDGED,
  missing: ['2025-09'],
  unjudged: ['2025-11'],
  groups: RECORD_JUDGED.groups.map((group) => ({
    ...group,
    sessions: group.sessions.map((visits, at) => (at === 3 ? null : visits)),
    pageViews: group.pageViews.map((read, at) => (at === 3 ? null : read)),
  })),
};

/** The same record a year at a time: the first year from the nineteenth of August, then this one. */
const RECORD_BY_YEAR: Counted = {
  from: RECORD_BY_MONTH.from,
  to: RECORD_BY_MONTH.to,
  points: [
    { start: '2024-12-31T18:30:00+00:00', pageViews: 2650, visitors: 800 },
    { start: '2025-12-31T18:30:00+00:00', pageViews: 300, visitors: 90 },
  ],
};

/** The same months, judged. */
const JUDGED_MONTHS: TrafficSeries = {
  from: MONTHS.from,
  to: MONTHS.to,
  granularity: 'month',
  completeTo: MONTHS.to,
  buckets: MONTHS.points.map((month) => month.start),
  groups: [{ category: 'likely-human', sessions: [300, 340, 290], pageViews: [900, 1020, 870] }],
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

/**
 * Thirty-five days before the weeks of October, judged — the same weekdays, five weeks back, from
 * a Thursday to a Friday.
 */
const EARLIER_JUDGED_WEEKS: TrafficSeries = {
  from: '2026-08-26T18:30:00+00:00',
  to: '2026-09-25T10:00:00+00:00',
  granularity: 'week',
  completeTo: '2026-09-25T10:00:00+00:00',
  buckets: [
    '2026-08-23T18:30:00+00:00',
    '2026-08-30T18:30:00+00:00',
    '2026-09-06T18:30:00+00:00',
    '2026-09-13T18:30:00+00:00',
    '2026-09-20T18:30:00+00:00',
  ],
  groups: [
    {
      category: 'likely-human',
      sessions: [25, 70, 66, 72, 41],
      pageViews: [75, 210, 198, 216, 123],
    },
  ],
};

/** What those days are called on screen. */
const EARLIER_DATES = '7–9 Aug 2026';

/** Every way of cutting a period, offered by one long enough for all of them. */
const EVERY_WAY: readonly ChartBy[] = ['day', 'week', 'month'];

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
    tooltip?: { valueFormatter?: (value: unknown, at: number) => string };
  }[];
  xAxis: {
    data: string[];
    boundaryGap: boolean;
    axisLabel: { interval: unknown; alignMaxLabel: string };
  };
  tooltip: {
    axisPointer: {
      label: {
        formatter: (pointed: { value: string; seriesData: { dataIndex: number }[] }) => string;
      };
    };
  };
}

/**
 * What the card a hover opens over a bucket is headed with, asked for as the engine asks for it:
 * with the name the axis gives the bucket, and a series drawn where it sits.
 */
function cardHeading(option: Drawing, at: number): string {
  return option.tooltip.axisPointer.label.formatter({
    value: option.xAxis.data[at] ?? '',
    seriesData: [{ dataIndex: at }],
  });
}

/**
 * Words as anybody reads them: the narrow spaces a run of dates is written with are spaces too,
 * and are read as ordinary ones, as the queries that find text on a screen already do.
 */
function plain(words: string): string {
  return words.replace(/\s+/g, ' ');
}

/** The name each row of the table is headed with. */
function rowNames(): readonly string[] {
  return screen.getAllByRole('rowheader').map((row) => plain(row.textContent ?? ''));
}

/** The names of the measures drawn, leaving out the wash laid over the quietened ends. */
function named(option: Drawing): readonly (string | undefined)[] {
  return option.series.filter((one) => one.markArea === undefined).map((one) => one.name);
}

/** Each stretch the wash is laid over, as the names of the buckets it runs between. */
function washed(option: Drawing): readonly (readonly string[])[] | undefined {
  return option.series
    .find((one) => one.markArea !== undefined)
    ?.markArea?.data.map((stretch) => stretch.map((end) => plain(end.xAxis)));
}

interface Shown {
  readonly view?: ChartView;
  /** Whether every figure is kept to the visits judged to be people. */
  readonly peopleOnly?: boolean;
  readonly activity?: Counted;
  /** The first bucket of activity still being judged, or nothing where every figure is settled. */
  readonly stillJudging?: number | null;
  readonly series?: TrafficPicture;
  readonly granularity?: Granularity;
  readonly manyDays?: boolean;
  readonly manyYears?: boolean;
  readonly problem?: unknown;
  readonly style?: string;
  /** Whether there is a period before to offer, which there is not where it is no longer kept. */
  readonly comparing?: boolean;
  /** Whether the period before is being drawn behind the one being read. */
  readonly against?: boolean;
  readonly earlierPoints?: readonly TrafficPoint[];
  readonly earlierSeries?: TrafficSeries;
  readonly earlierDates?: string;
  /** The ways the period can be cut, where it is long enough to offer one. */
  readonly cutting?: ChartCutting;
  /** Told the days a bucket was pressed into, where the period can still be narrowed. */
  readonly onPick?: (days: { first: string; last: string }) => void;
  /** The first day a bucket may begin on and still be pressed, where not every bucket can be. */
  readonly pressableFrom?: string;
  /** The months the figures leave out because they were never counted, written `2025-06`. */
  readonly missing?: readonly string[];
  /** The months they leave out because their visits were not all judged before they were kept. */
  readonly unsettled?: readonly string[];
  /**
   * How far the judging of how much was read has reached, where that is not the start of its first
   * bucket still being judged — or the end of the window, where every figure is settled.
   */
  readonly judgedTo?: string;
}

const chose = vi.fn();
const compared = vi.fn();

/**
 * Where the judging of how much was read has reached: the start of the first bucket still being
 * judged, or the end of the window where every figure is settled.
 */
function reachedTo(activity: Counted, stillJudging: number | null): string {
  return stillJudging === null
    ? activity.to
    : (activity.points[stillJudging]?.start ?? activity.to);
}

function show({
  view = 'activity',
  peopleOnly = false,
  activity = DAYS,
  stillJudging = null,
  series = JUDGED,
  granularity = 'day',
  manyDays = true,
  manyYears = false,
  problem = null,
  style,
  comparing = true,
  against = false,
  earlierPoints = EARLIER_DAYS,
  earlierSeries = EARLIER_JUDGED,
  earlierDates = EARLIER_DATES,
  cutting,
  onPick,
  pressableFrom,
  missing,
  unsettled,
  judgedTo,
}: Shown = {}): Drawing {
  renderScreen(
    <TrafficChart
      view={view}
      onView={chose}
      peopleOnly={peopleOnly}
      activity={{
        ...activity,
        stillJudging,
        completeTo: judgedTo ?? reachedTo(activity, stillJudging),
      }}
      who={series}
      problem={problem}
      comparison={
        comparing
          ? {
              on: against,
              onChange: compared,
              activity: earlierPoints,
              who: earlierSeries,
              days: earlierDates,
            }
          : undefined
      }
      siteName="My Blog"
      timeZoneId="Asia/Kolkata"
      zone="Kolkata"
      granularity={granularity}
      manyDays={manyDays}
      manyYears={manyYears}
      cutting={cutting}
      onPick={onPick}
      pressableFrom={pressableFrom}
      missing={missing}
      unsettled={unsettled}
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
  drawn.options.clear();
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
    expect(screen.getAllByRole('row')).toHaveLength(DAYS.points.length + 1);
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
    const option = show({ activity: HOURS, granularity: 'hour', manyDays: false });

    expect(option.xAxis.data).toStrictEqual(['12 AM', '1 AM', '2 AM']);
  });

  it('keeps the date beside the time when the period covers more than one day', () => {
    const option = show({ activity: HOURS, granularity: 'hour' });

    expect(option.xAxis.data[0]).toBe('Aug 18, 12 AM');
  });

  /**
   * Counted in an hour, distinct visitors are the people who were there in that hour. Calling that
   * figure the day's would be a claim about numbers that were never added up that way.
   */
  it('does not call an hour of visitors a day of them', () => {
    const option = show({ activity: HOURS, granularity: 'hour', manyDays: false });

    expect(option.series.map((one) => one.name)).toStrictEqual(['Page views', 'Visitors']);
    expect(screen.getByText('Times are the clock in Kolkata.')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Time' })).toBeInTheDocument();
  });
});

describe('a period drawn a week at a time', () => {
  it('writes each week as the days it covers', () => {
    const option = show({ activity: WEEKS, granularity: 'week' });

    expect(option.xAxis.data.map(plain)).toStrictEqual([
      'Oct 1 – 4',
      'Oct 5 – 11',
      'Oct 12 – 18',
      'Oct 19 – 25',
      'Oct 26 – 30',
    ]);
  });

  /**
   * The first week began on the twenty-eighth of September, and only the four days of it from the
   * first of October were counted. Named by the whole week, it would claim three days it never held.
   */
  it('writes a week cut by the period as only its days', () => {
    const option = show({ activity: WEEKS, granularity: 'week' });

    expect(plain(option.xAxis.data[0] ?? '')).toBe('Oct 1 – 4');
    expect(rowNames()[0]).toBe('Oct 1 – 4');
  });

  it("heads the card with the week's dates in full", () => {
    const option = show({ activity: WEEKS, granularity: 'week' });

    expect(plain(cardHeading(option, 1))).toBe('October 5 – 11');
    expect(plain(cardHeading(option, 4))).toBe('October 26 – 30');
  });

  /** A week's visitors are counted the way the cards count them, and keep the cards' name. */
  it("keeps calling a week's visitors daily visitors", () => {
    const option = show({ activity: WEEKS, granularity: 'week' });

    expect(named(option)).toStrictEqual(['Page views', 'Daily visitors']);
    expect(option.series[1]?.data).toStrictEqual([40, 98, 91, 95, 66]);
  });

  it('says weeks run Monday to Sunday, and that somebody who comes back counts again', () => {
    show({ activity: WEEKS, granularity: 'week' });

    expect(
      screen.getByText(
        'Weeks run Monday to Sunday in Kolkata, and somebody who comes back on another day counts again. Weeks the period cuts short are drawn fainter.',
      ),
    ).toBeInTheDocument();
  });

  it('says the same of the visits that have finished, and of the people among them', () => {
    show({ view: 'who', series: JUDGED_WEEKS, granularity: 'week' });

    expect(
      screen.getByText(
        'Visits that have finished, by week in Kolkata, Monday to Sunday. Weeks the period cuts short are drawn fainter.',
      ),
    ).toBeInTheDocument();
  });

  it('says it of the people too, when the figures are kept to them', () => {
    show({ view: 'who', series: JUDGED_WEEKS, granularity: 'week', peopleOnly: true });

    expect(
      screen.getByText(
        'Finished visits judged to be people, by week in Kolkata, Monday to Sunday. Weeks the period cuts short are drawn fainter.',
      ),
    ).toBeInTheDocument();
  });

  /**
   * The first week of the period holds four days of the twenty-eighth of September's week and the
   * last holds five of the twenty-sixth of October's. Drawn at full strength, either reads as a fall
   * in traffic rather than as part of a week.
   */
  it('draws the weeks the period cuts short fainter, at either end', () => {
    const option = show({ activity: WEEKS, granularity: 'week', style: 'Area' });

    expect(washed(option)).toStrictEqual([
      ['Oct 1 – 4', 'Oct 5 – 11'],
      ['Oct 19 – 25', 'Oct 26 – 30'],
    ]);
  });

  it('fades a column the period cuts short on its own, and leaves every whole week as it is', () => {
    const option = show({ activity: WEEKS, granularity: 'week', style: 'Columns' });

    expect(option.series[0]?.data).toStrictEqual([
      { value: 120, itemStyle: { opacity: UNSETTLED } },
      310,
      280,
      295,
      { value: 190, itemStyle: { opacity: UNSETTLED } },
    ]);
  });

  it('says nothing about weeks cut short where the period holds only whole ones', () => {
    const option = show({ activity: WHOLE_WEEKS, granularity: 'week', style: 'Area' });

    expect(option.series.some((one) => one.markArea !== undefined)).toBe(false);
    expect(
      screen.getByText(
        'Weeks run Monday to Sunday in Kolkata, and somebody who comes back on another day counts again.',
      ),
    ).toBeInTheDocument();
  });

  it('heads the first column Week', () => {
    show({ activity: WEEKS, granularity: 'week' });

    expect(screen.getByRole('columnheader', { name: 'Week' })).toBeInTheDocument();
  });

  it('narrows to the days of a pressed week', () => {
    const picked = vi.fn();

    show({ activity: WEEKS, granularity: 'week', onPick: picked });
    drawn.pick?.(1);

    expect(picked).toHaveBeenCalledWith({ first: '2026-10-05', last: '2026-10-11' });
  });

  /** A canvas is not a control, so the table the same figures are published in carries the way in. */
  it('offers the same week from the row of the table', async () => {
    const picked = vi.fn();

    show({ activity: WEEKS, granularity: 'week', onPick: picked });

    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(
      screen.getByRole('button', { name: /^Oct 26\s–\s30, look at these days$/ }),
    );

    expect(picked).toHaveBeenCalledWith({ first: '2026-10-26', last: '2026-10-30' });
  });

  it('takes the picture of who came into the days of a week as well', () => {
    const picked = vi.fn();

    const option = show({ view: 'who', series: JUDGED_WEEKS, granularity: 'week', onPick: picked });
    drawn.pick?.(0);

    expect(option.xAxis.data.map(plain)[0]).toBe('Oct 1 – 4');
    expect(picked).toHaveBeenCalledWith({ first: '2026-10-01', last: '2026-10-04' });
  });
});

describe('a period drawn a month at a time', () => {
  /**
   * The second of August to the thirtieth of October holds the whole of September, and only parts
   * of the months either side of it.
   */
  it('writes each month by its name where the period holds the whole of it', () => {
    const option = show({ activity: MONTHS, granularity: 'month' });

    expect(option.xAxis.data.map(plain)).toStrictEqual(['Aug 2 – 31', 'Sep', 'Oct 1 – 30']);
  });

  it('writes a month cut by the period as only its days', () => {
    show({ activity: MONTHS, granularity: 'month' });

    expect(rowNames()).toStrictEqual(['Aug 2 – 31', 'Sep', 'Oct 1 – 30']);
  });

  it('heads the card with the month in full', () => {
    const option = show({ activity: MONTHS, granularity: 'month' });

    expect(plain(cardHeading(option, 0))).toBe('August 2 – 31');
    expect(plain(cardHeading(option, 1))).toBe('September');
  });

  it("keeps calling a month's visitors daily visitors", () => {
    const option = show({ activity: MONTHS, granularity: 'month' });

    expect(named(option)).toStrictEqual(['Page views', 'Daily visitors']);
  });

  it('says months follow the calendar where the website is, and that somebody who comes back counts again', () => {
    show({ activity: MONTHS, granularity: 'month' });

    expect(
      screen.getByText(
        'Months follow the calendar in Kolkata, and somebody who comes back on another day counts again.',
      ),
    ).toBeInTheDocument();
  });

  it('says how the visits that have finished are cut', () => {
    show({ view: 'who', series: JUDGED_MONTHS, granularity: 'month' });

    expect(screen.getByText('Visits that have finished, by month in Kolkata.')).toBeInTheDocument();
  });

  /**
   * August and October are cut short and September is whole. An area is drawn between the months'
   * points, so washing both cut ends would wash September's stretches too and leave nothing at full
   * strength — and a picture faded all over reads as nothing faded at all.
   */
  it('leaves three months cut short at both ends at full strength as an area, and says nothing of them', () => {
    const option = show({ activity: MONTHS, granularity: 'month', style: 'Area' });

    expect(washed(option)).toBeUndefined();
    expect(screen.queryByText(/Months the period cuts short/)).not.toBeInTheDocument();
  });

  it('fades the months the period cuts short as columns, and says so', () => {
    const option = show({ activity: MONTHS, granularity: 'month', style: 'Columns' });

    expect(option.series[0]?.data).toStrictEqual([
      { value: 1200, itemStyle: { opacity: UNSETTLED } },
      1350,
      { value: 1100, itemStyle: { opacity: UNSETTLED } },
    ]);
    expect(
      screen.getByText(
        'Months follow the calendar in Kolkata, and somebody who comes back on another day counts again. Months the period cuts short are drawn fainter.',
      ),
    ).toBeInTheDocument();
  });

  it('heads the first column Month', () => {
    show({ activity: MONTHS, granularity: 'month' });

    expect(screen.getByRole('columnheader', { name: 'Month' })).toBeInTheDocument();
  });

  it('narrows to the days of a pressed month', () => {
    const picked = vi.fn();

    show({ activity: MONTHS, granularity: 'month', onPick: picked });
    drawn.pick?.(1);

    expect(picked).toHaveBeenCalledWith({ first: '2026-09-01', last: '2026-09-30' });
  });

  it('offers the same month from the row of the table', async () => {
    const picked = vi.fn();

    show({ activity: MONTHS, granularity: 'month', onPick: picked });

    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(screen.getByRole('button', { name: 'Sep, look at these days' }));

    expect(picked).toHaveBeenCalledWith({ first: '2026-09-01', last: '2026-09-30' });
  });
});

/**
 * All time is the website's whole record, from its first month kept, or still held, to now. The
 * months of it older than the days still held one by one are held as their totals alone, and a
 * month nobody counted in time cannot be counted afterwards.
 */
describe('all time, a month at a time', () => {
  /** All time as the overview draws it: from the first day, a month at a time, across a year. */
  function record(extra: Shown = {}): Drawing {
    return show({
      activity: RECORD_BY_MONTH,
      series: RECORD_JUDGED,
      granularity: 'month',
      manyYears: true,
      comparing: false,
      ...extra,
    });
  }

  it('cuts the first month at the record’s first day', () => {
    record();

    expect(rowNames()).toStrictEqual([
      'Aug 19 – 31, 2025',
      'Sep 2025',
      'Oct 2025',
      'Nov 2025',
      'Dec 2025',
      'Jan 1 – 15, 2026',
    ]);
  });

  /** A month nobody counted is not a month nobody came in. */
  it('leaves a month never counted out of the drawing rather than drawing it as nought', () => {
    const line = record();

    expect(line.series[0]?.data[1]).toBeNull();
    expect(line.series[1]?.data[1]).toBeNull();
  });

  it('draws no column for a month never counted', () => {
    const columns = record({ style: 'Columns' });

    expect(columns.series[0]?.data[1]).toBeNull();
    expect(columns.series[1]?.data[1]).toBeNull();
  });

  it('says in the table that a month was not counted', () => {
    record();

    expect(
      screen.getByRole('row', { name: 'Sep 2025 Not counted Not counted' }),
    ).toBeInTheDocument();
  });

  it('leaves a month never counted out of who came, in every band and the whole', () => {
    const option = record({ view: 'who' });

    expect(
      option.series.filter((one) => one.markArea === undefined).map((one) => one.data[1]),
    ).toStrictEqual([null, null]);
    expect(
      screen.getByRole('row', { name: 'Sep 2025 Not counted Not counted Not counted' }),
    ).toBeInTheDocument();
  });

  /** A gap in the drawing and a month nobody came in can look alike, so the month is named. */
  it('names the months the figures leave out', () => {
    record({ missing: ['2025-09'] });

    expect(
      screen.getByText(/September 2025 wasn.t counted and is left out of these figures\.$/),
    ).toBeInTheDocument();
  });

  it('names every month the figures leave out, as one list', () => {
    record({ missing: ['2025-09', '2025-11'] });

    expect(
      screen.getByText(
        /September 2025 and November 2025 weren.t counted and are left out of these figures\.$/,
      ),
    ).toBeInTheDocument();
  });

  it('says nothing about months left out where none were', () => {
    record();

    expect(screen.queryByText(/left out of these figures/)).not.toBeInTheDocument();
  });

  /** A month kept before its visits were all judged was counted, so it is never called uncounted. */
  it('names a month not fully judged apart from a month never counted', () => {
    record({ view: 'who', series: RECORD_UNSETTLED, missing: ['2025-09'], unsettled: ['2025-11'] });

    expect(
      screen.getByText(
        /September 2025 wasn.t counted and is left out of these figures\. November 2025 wasn.t fully judged and is left out of these figures\.$/,
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('row', { name: 'Sep 2025 Not counted Not counted Not counted' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('row', {
        name: 'Nov 2025 Not fully judged Not fully judged Not fully judged',
      }),
    ).toBeInTheDocument();
  });

  it('says only that a month was not fully judged where every month was counted', () => {
    record({ unsettled: ['2025-11'] });

    expect(
      screen.getByText(/November 2025 wasn.t fully judged and is left out of these figures\.$/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/wasn.t counted/)).not.toBeInTheDocument();
  });

  it('calls a month not fully judged that in the card a hover opens, as the table does', () => {
    const measures = record({
      view: 'who',
      series: RECORD_UNSETTLED,
      missing: ['2025-09'],
      unsettled: ['2025-11'],
    }).series.filter((one) => one.markArea === undefined);

    expect(measures.length).toBeGreaterThan(0);

    for (const measure of measures) {
      expect(measure.tooltip?.valueFormatter?.(null, 3)).toBe('Not fully judged');
      expect(measure.tooltip?.valueFormatter?.(null, 1)).toBe('Not counted');
    }
  });

  /** No people among the months judged says nothing about a month that was not. */
  it('names the months left out where none of the visits judged was a person', () => {
    record({
      view: 'who',
      peopleOnly: true,
      series: { ...RECORD_UNSETTLED, groups: RECORD_UNSETTLED.groups.slice(1) },
      missing: ['2025-09'],
      unsettled: ['2025-11'],
    });

    expect(
      screen.getByText(
        "No visits in this period were judged to be people. September 2025 wasn't counted and is left out of these figures. November 2025 wasn't fully judged and is left out of these figures.",
      ),
    ).toBeInTheDocument();
  });

  it('names the months left out where nothing was judged', () => {
    record({
      view: 'who',
      series: { ...RECORD_UNSETTLED, groups: [] },
      unsettled: ['2025-11'],
    });

    expect(
      screen.getByText(
        "No visits have been judged in this period yet. November 2025 wasn't fully judged and is left out of these figures.",
      ),
    ).toBeInTheDocument();
  });

  /** The engine's own dash for a figure it was handed nothing for reads as a figure of its own. */
  it('calls a month never counted not counted in the card a hover opens, as the table does', () => {
    for (const view of ['activity', 'who'] as const) {
      const measures = record({ view }).series.filter((one) => one.markArea === undefined);

      expect(measures.length).toBeGreaterThan(0);

      for (const measure of measures) {
        expect(measure.tooltip?.valueFormatter?.(undefined, 1)).toBe('Not counted');
        expect(measure.tooltip?.valueFormatter?.(1350, 2)).toBe('1,350');
      }
    }
  });

  /** The period before is padded past its own end with nothing, which is not a month left out. */
  it('leaves the period before to write its own figures', () => {
    const option = show({ against: true });
    const dashed = option.series.filter((one) => one.lineStyle?.type === 'dashed');

    expect(dashed).toHaveLength(2);
    expect(dashed.every((one) => one.tooltip?.valueFormatter === undefined)).toBe(true);
  });

  /** A month held as its totals alone has no days to narrow the screen to. */
  it('offers only the months still held day by day to be pressed', async () => {
    const picked = vi.fn();

    record({ onPick: picked, pressableFrom: '2025-10-01' });
    await userEvent.click(screen.getByText('Show these figures as a table'));

    expect(
      screen
        .getAllByRole('button', { name: /look at these days$/ })
        .map((row) => plain(row.textContent ?? '')),
    ).toStrictEqual(['Oct 2025', 'Nov 2025', 'Dec 2025', 'Jan 1 – 15, 2026']);
    expect(screen.queryByRole('button', { name: /^Aug 19/ })).not.toBeInTheDocument();
  });

  it('does nothing when a month held as its totals alone is pressed on the picture', () => {
    const picked = vi.fn();

    record({ onPick: picked, pressableFrom: '2025-10-01' });
    drawn.pick?.(0);

    expect(picked).not.toHaveBeenCalled();

    drawn.pick?.(2);

    expect(picked).toHaveBeenCalledWith({ first: '2025-10-01', last: '2025-10-31' });
  });
});

describe('all time, a year at a time', () => {
  /** All time from the nineteenth of November 2024 to the thirtieth of October 2026, by the year. */
  const THREE_YEARS: Counted = {
    from: '2024-11-18T18:30:00+00:00',
    to: '2026-10-30T10:00:00+00:00',
    points: [
      { start: '2023-12-31T18:30:00+00:00', pageViews: 2100, visitors: 640 },
      { start: '2024-12-31T18:30:00+00:00', pageViews: 31200, visitors: 9800 },
      { start: '2025-12-31T18:30:00+00:00', pageViews: 35400, visitors: 11050 },
    ],
  };

  /** The two years of the record judged, the first settled and the one under way still filling. */
  const JUDGED_BY_YEAR: TrafficPicture = {
    from: RECORD_BY_YEAR.from,
    to: RECORD_BY_YEAR.to,
    granularity: 'year',
    completeTo: '2026-01-15T09:30:00+00:00',
    buckets: RECORD_BY_YEAR.points.map((year) => year.start),
    missing: [],
    unjudged: [],
    groups: [{ category: 'likely-human', sessions: [820, 95], pageViews: [2400, 280] }],
  };

  /** All time as the overview draws it by the year. */
  function years(extra: Shown = {}): Drawing {
    return show({
      activity: RECORD_BY_YEAR,
      granularity: 'year',
      manyYears: true,
      comparing: false,
      ...extra,
    });
  }

  /**
   * Under the axis a year cut short is the months it covers — short enough for every year to be
   * named beside the others on a phone — or its days, where they lie inside one month. The table
   * has room for its days.
   */
  it('writes a year by its number, and a year only partly counted by what of it was counted', () => {
    const option = years();

    expect(option.xAxis.data.map(plain)).toStrictEqual(['Aug – Dec 2025', 'Jan 1 – 15, 2026']);
    expect(rowNames()).toStrictEqual(['Aug 19 – Dec 31, 2025', 'Jan 1 – 15, 2026']);
    expect(screen.getByRole('columnheader', { name: 'Year' })).toBeInTheDocument();
  });

  it('heads the card over a year cut short with its days in full', () => {
    const option = years();

    expect(plain(cardHeading(option, 0))).toBe('August 19 – December 31, 2025');
  });

  /** A handful of buckets, each a year of a website's history: none is left unnamed to make room. */
  it('names every year along the axis', () => {
    const option = years({ activity: THREE_YEARS, style: 'Area' });
    const named = option.xAxis.axisLabel.interval as (at: number, label: string) => boolean;

    expect(option.xAxis.data.map(plain)).toStrictEqual([
      'Nov – Dec 2024',
      '2025',
      'Jan – Oct 2026',
    ]);
    expect(option.xAxis.data.map((label, at) => named(at, label))).toStrictEqual([
      true,
      true,
      true,
    ]);
    expect(option.xAxis.axisLabel.alignMaxLabel).toBe('right');
  });

  /** Ending at the middle of its column, the newest year's name would reach back over the one before. */
  it('centres the newest year’s name on its column', () => {
    const option = years({ activity: THREE_YEARS, style: 'Columns' });

    expect(option.xAxis.axisLabel.alignMaxLabel).toBe('center');
  });

  /**
   * The six weeks a website was first measured in and the ten months of this year, drawn at full
   * strength beside a whole year, read as traffic that climbed from almost nothing.
   */
  it('fades the years the period cuts short as columns where a whole one lies between, and says so', () => {
    const option = years({ activity: THREE_YEARS, style: 'Columns' });

    expect(option.series[0]?.data).toStrictEqual([
      { value: 2100, itemStyle: { opacity: UNSETTLED } },
      31200,
      { value: 35400, itemStyle: { opacity: UNSETTLED } },
    ]);
    expect(
      screen.getByText(/Years the period cuts short are drawn fainter\.$/),
    ).toBeInTheDocument();
  });

  /** A line through three years washed at both ends would be washed from end to end. */
  it('leaves three years cut short at both ends at full strength as an area', () => {
    const option = years({ activity: THREE_YEARS, style: 'Area' });

    expect(washed(option)).toBeUndefined();
    expect(screen.queryByText(/drawn fainter/)).not.toBeInTheDocument();
  });

  /** Two years both cut short have nothing between them to be read against, faded or not. */
  it('leaves two years both cut short at full strength', () => {
    const option = years();

    expect(option.series[0]?.data).toStrictEqual([2650, 300]);
    expect(screen.queryByText(/drawn fainter/)).not.toBeInTheDocument();
  });

  /**
   * Two years are one slope as a line or an area, and washed from the settled first year to the one
   * still being judged the whole of it would read as unsettled. As columns only the year under way
   * is faded, as the caption and the table say.
   */
  it('fades only the year still being judged where two years are drawn', () => {
    const option = years({ view: 'who', series: JUDGED_BY_YEAR });

    expect(option.series.map((one) => one.type)).toStrictEqual(['bar']);
    expect(option.series.some((one) => one.markArea !== undefined)).toBe(false);
    expect(option.series[0]?.data).toStrictEqual([
      820,
      { value: 95, itemStyle: { opacity: UNSETTLED } },
    ]);
    expect(
      screen.getByText(
        'Visits that have finished, by year in Kolkata. The newest are still being judged.',
      ),
    ).toBeInTheDocument();
    expect(screen.getAllByText('still being judged')).toHaveLength(1);
  });

  it('washes nothing over the people’s two years still being judged', () => {
    const option = years({
      peopleOnly: true,
      stillJudging: 1,
      judgedTo: '2026-01-15T09:30:00+00:00',
    });

    expect(option.series.map((one) => one.type)).toStrictEqual(['bar', 'bar']);
    expect(option.series.some((one) => one.markArea !== undefined)).toBe(false);
    expect(option.series[0]?.data).toStrictEqual([
      2650,
      { value: 300, itemStyle: { opacity: UNSETTLED } },
    ]);
  });

  /**
   * A year adds up the months of it that were counted, so its figure stands — and its row says
   * which months it leaves out, which the caption names in full.
   */
  it('marks the year that leaves out a month never counted', () => {
    years({ missing: ['2025-09'] });

    expect(rowNames()).toStrictEqual([
      'Aug 19 – Dec 31, 2025leaves out September',
      'Jan 1 – 15, 2026',
    ]);
    expect(
      screen.getByText(/September 2025 wasn.t counted and is left out of these figures\.$/),
    ).toBeInTheDocument();
  });

  /**
   * A year whose only months are one kept before its visits were all judged and one never counted
   * was counted in part, so it is not called uncounted: its row and the caption name both months.
   */
  it('calls a year not fully judged where a month of it was counted, beside one never counted', () => {
    const LEFT_OUT: TrafficPicture = {
      from: '2025-11-09T18:30:00+00:00',
      to: '2026-01-15T10:00:00+00:00',
      granularity: 'year',
      completeTo: '2026-01-15T10:00:00+00:00',
      buckets: ['2024-12-31T18:30:00+00:00', '2025-12-31T18:30:00+00:00'],
      missing: ['2025-12'],
      unjudged: ['2025-11'],
      groups: [{ category: 'likely-human', sessions: [null, 95], pageViews: [null, 280] }],
    };

    const measures = years({
      view: 'who',
      activity: { ...RECORD_BY_YEAR, from: LEFT_OUT.from },
      series: LEFT_OUT,
      missing: ['2025-12'],
      unsettled: ['2025-11'],
    }).series.filter((one) => one.markArea === undefined);

    const first = screen.getAllByRole('rowheader')[0];

    expect(plain(first?.textContent ?? '')).toBe(
      'Nov 10 – Dec 31, 2025leaves out November and December',
    );
    expect(
      within(first?.closest('tr') ?? document.body)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toStrictEqual(['Not fully judged', 'Not fully judged']);
    expect(measures.length).toBeGreaterThan(0);

    for (const measure of measures) {
      expect(measure.tooltip?.valueFormatter?.(null, 0)).toBe('Not fully judged');
    }

    expect(
      screen.getByText(
        /December 2025 wasn.t counted and is left out of these figures\. November 2025 wasn.t fully judged and is left out of these figures\.$/,
      ),
    ).toBeInTheDocument();
  });

  it('says years follow the calendar where the website is', () => {
    years();

    expect(
      screen.getByText(
        'Years follow the calendar in Kolkata, and somebody who comes back on another day counts again.',
      ),
    ).toBeInTheDocument();
  });
});

/**
 * One bucket is a single point, which a line or an area draws as nothing at all, and two are one
 * slope read as a trend. A website's all time is one month for the whole of its first month.
 */
describe('a picture with too few buckets for a shape', () => {
  /** All time for a website first measured on the twelfth of October, read on the thirtieth. */
  const FIRST_MONTH: Counted = {
    from: '2026-10-11T18:30:00+00:00',
    to: '2026-10-30T10:00:00+00:00',
    points: [{ start: '2026-09-30T18:30:00+00:00', pageViews: 820, visitors: 260 }],
  };

  /** The same month judged up to half an hour ago, as the engine always leaves the month under way. */
  const FIRST_MONTH_JUDGED: TrafficPicture = {
    from: FIRST_MONTH.from,
    to: FIRST_MONTH.to,
    granularity: 'month',
    completeTo: '2026-10-30T09:30:00+00:00',
    buckets: FIRST_MONTH.points.map((month) => month.start),
    missing: [],
    unjudged: [],
    groups: [
      { category: 'likely-human', sessions: [190], pageViews: [520] },
      { category: 'known-search-crawler', sessions: [40], pageViews: [40] },
    ],
  };

  /** The same website a month on: September from the twelfth, and October to the thirtieth. */
  const FIRST_TWO_MONTHS: Counted = {
    from: '2026-09-11T18:30:00+00:00',
    to: '2026-10-30T10:00:00+00:00',
    points: [
      { start: '2026-08-31T18:30:00+00:00', pageViews: 500, visitors: 160 },
      { start: '2026-09-30T18:30:00+00:00', pageViews: 820, visitors: 260 },
    ],
  };

  /** The first month as the overview draws it, with an area remembered from another picture. */
  function firstMonth(extra: Shown = {}): Drawing {
    window.localStorage.setItem('dewiride.chart-drawing', 'area');

    return show({
      view: 'who',
      activity: FIRST_MONTH,
      series: FIRST_MONTH_JUDGED,
      granularity: 'month',
      comparing: false,
      ...extra,
    });
  }

  it('draws a single month as columns, whatever was asked for', () => {
    expect(firstMonth().series.map((one) => one.type)).toStrictEqual(['bar', 'bar']);
  });

  it('draws a single month of how much was read as columns too', () => {
    expect(firstMonth({ view: 'activity' }).series.map((one) => one.type)).toStrictEqual([
      'bar',
      'bar',
    ]);
  });

  /** A switch that can change nothing is not a choice. */
  it('offers nothing else to draw it as', () => {
    firstMonth();

    expect(
      screen.queryByRole('radiogroup', { name: 'How the chart is drawn' }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('radiogroup', { name: 'What the chart shows' })).toBeInTheDocument();
  });

  /** Everything up to the last half hour has been judged; only the month under way is still filling. */
  it('says the newest are still being judged where its one month is still under way', () => {
    firstMonth();

    expect(
      screen.getByText(
        'Visits that have finished, by month in Kolkata. The newest are still being judged.',
      ),
    ).toBeInTheDocument();
    expect(rowNames()).toStrictEqual(['Oct 12 – 30still being judged']);
    expect(screen.queryByText(/drawn fainter/)).not.toBeInTheDocument();
  });

  it('says the whole period is still being judged where judging has reached none of it', () => {
    firstMonth({ series: { ...FIRST_MONTH_JUDGED, completeTo: FIRST_MONTH.from } });

    expect(
      screen.getByText(
        'Visits that have finished, by month in Kolkata. This whole period is still being judged.',
      ),
    ).toBeInTheDocument();
  });

  it('says the newest of the people’s figures are still being judged while its month is under way', () => {
    firstMonth({
      view: 'activity',
      peopleOnly: true,
      stillJudging: 0,
      judgedTo: '2026-10-30T09:30:00+00:00',
    });

    expect(
      screen.getByText(
        'Months follow the calendar in Kolkata, and somebody who comes back on another day counts again. The newest are still being judged.',
      ),
    ).toBeInTheDocument();
  });

  /** Neither month is whole, and there is no whole month between them to read them against. */
  it('draws two months as columns, and fades neither for being cut short', () => {
    window.localStorage.setItem('dewiride.chart-drawing', 'line');

    const option = show({ activity: FIRST_TWO_MONTHS, granularity: 'month', comparing: false });

    expect(option.series.map((one) => one.type)).toStrictEqual(['bar', 'bar']);
    expect(option.series[0]?.data).toStrictEqual([500, 820]);
    expect(screen.queryByText(/drawn fainter/)).not.toBeInTheDocument();
  });

  it('draws three buckets as asked, and offers every way to draw them', () => {
    window.localStorage.setItem('dewiride.chart-drawing', 'line');

    const option = show();

    expect(option.series.map((one) => one.type)).toStrictEqual(['line', 'line']);
    expect(screen.getByRole('radio', { name: 'Line' })).toHaveAttribute('aria-checked', 'true');
  });
});

/**
 * Whether there is a choice of how to draw a picture is settled by how many buckets it has, and a
 * switch shown over a picture on its way and then taken back when it arrives is a control somebody
 * reaches for and finds gone.
 */
describe('a picture on its way', () => {
  /** The chart with neither answer in yet, due the number of buckets given. */
  function waiting(view: 'activity' | 'who', bucketsDue: number | undefined) {
    renderScreen(
      <TrafficChart
        view={view}
        onView={chose}
        peopleOnly={false}
        activity={undefined}
        who={undefined}
        problem={null}
        comparison={undefined}
        siteName="My Blog"
        timeZoneId="Asia/Kolkata"
        zone="Kolkata"
        granularity="month"
        manyDays
        manyYears={false}
        bucketsDue={bucketsDue}
      />,
    );
  }

  it('offers no way to draw it while it is due too few buckets for a shape', () => {
    for (const view of ['activity', 'who'] as const) {
      waiting(view, 1);

      expect(
        screen.queryByRole('radiogroup', { name: 'How the chart is drawn' }),
      ).not.toBeInTheDocument();

      cleanup();
    }
  });

  it('offers every way to draw it while it is due buckets enough for a shape', () => {
    waiting('activity', 3);

    const group = screen.getByRole('radiogroup', { name: 'How the chart is drawn' });

    for (const style of ['Line', 'Columns', 'Area']) {
      expect(within(group).getByRole('radio', { name: style })).toBeInTheDocument();
    }
  });

  /**
   * Neither offered nor missing: its place is held, out of sight and out of reach, so a header that
   * goes on to offer it does not move to make room.
   */
  it('holds the place of the ways to draw it, offering none, while how many buckets it is due is not known', () => {
    waiting('who', undefined);

    expect(
      screen.queryByRole('radiogroup', { name: 'How the chart is drawn' }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('radiogroup', { name: 'What the chart shows' })).toBeInTheDocument();

    const place = screen.getByRole('radiogroup', { name: 'How the chart is drawn', hidden: true });
    const list = screen.getByRole('combobox', { name: 'Show the chart by', hidden: true });

    expect(place.closest('[inert]')).not.toBeNull();
    expect(place.closest('[aria-hidden="true"]')).not.toBeNull();
    expect(list.closest('[inert]')).not.toBeNull();
    expect(screen.queryByRole('combobox', { name: 'Show the chart by' })).not.toBeInTheDocument();
  });

  it('holds no place where it is due too few buckets for a shape', () => {
    waiting('who', 2);

    expect(
      screen.queryByRole('radiogroup', { name: 'How the chart is drawn', hidden: true }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('combobox', { name: 'Show the chart by', hidden: true }),
    ).not.toBeInTheDocument();
  });
});

describe('choosing how finely the chart is cut', () => {
  /** Told the way somebody chose. */
  const cut = vi.fn();

  /** The list of ways, as it is reached without seeing the card. */
  function ways() {
    return screen.getByRole('combobox', { name: 'Show the chart by' });
  }

  /**
   * A way this period is too short for is still listed, with the period it moves to — so somebody
   * looking at a month finds months from there rather than from a note they need a pointer to read.
   */
  it('offers every way, naming the period a way this one cannot show moves to', () => {
    show({ cutting: { offered: ['day', 'week'], listed: EVERY_WAY, onChoose: cut } });

    expect(
      within(ways())
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toStrictEqual(['By day', 'By week', 'By month · Last 90 days']);
  });

  it('names a short period’s way to weeks as well as to months', () => {
    show({ cutting: { offered: ['day'], listed: EVERY_WAY, onChoose: cut } });

    expect(
      within(ways())
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toStrictEqual(['By day', 'By week · Last 30 days', 'By month · Last 90 days']);
  });

  it('lists the ways alone where the period offers every one', () => {
    show({ cutting: { offered: EVERY_WAY, listed: EVERY_WAY, onChoose: cut } });

    expect(
      within(ways())
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toStrictEqual(['By day', 'By week', 'By month']);
  });

  it('reads as the way the picture is being drawn', () => {
    show({
      activity: WEEKS,
      granularity: 'week',
      cutting: { offered: ['day', 'week'], listed: EVERY_WAY, onChoose: cut },
    });

    expect(ways()).toHaveValue('week');
  });

  it('reports the way somebody chose, whether or not this period offers it', async () => {
    show({ cutting: { offered: ['day', 'week'], listed: EVERY_WAY, onChoose: cut } });

    await userEvent.selectOptions(ways(), 'week');
    await userEvent.selectOptions(ways(), 'month');

    expect(cut).toHaveBeenNthCalledWith(1, 'week');
    expect(cut).toHaveBeenNthCalledWith(2, 'month');
  });

  /** Years are all time's alone, and listed only where all time would have more than one to show. */
  it('lists years with the period they move to where all time would show them', () => {
    show({ cutting: { offered: EVERY_WAY, listed: [...EVERY_WAY, 'year'], onChoose: cut } });

    expect(
      within(ways())
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toStrictEqual(['By day', 'By week', 'By month', 'By year · All time']);
  });

  /** All time is every period there is, so there is nothing longer to list a way of moving to. */
  it('lists only the months and years of all time', () => {
    show({
      activity: RECORD_BY_MONTH,
      granularity: 'month',
      cutting: { offered: ['month', 'year'], listed: ['month', 'year'], onChoose: cut },
    });

    expect(
      within(ways())
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toStrictEqual(['By month', 'By year']);
    expect(ways()).toHaveValue('month');
  });

  it('offers no choice on a period drawn an hour at a time', () => {
    show({ activity: HOURS, granularity: 'hour' });

    expect(screen.queryByRole('combobox', { name: 'Show the chart by' })).not.toBeInTheDocument();
  });
});

describe('the card a hover opens', () => {
  /**
   * The second bucket begins at half past six on the evening of the eleventh, a Tuesday where these
   * tests run and a Wednesday where the website is. The card names the website's weekday, and the
   * axis and the table keep the short names that fit them on a phone.
   */
  it("names the weekday over a day in the website's own zone, and nowhere else", () => {
    const option = show();

    expect(cardHeading(option, 1)).toBe('Wednesday, August 12');
    expect(option.xAxis.data).toStrictEqual(['Aug 11', 'Aug 12', 'Aug 13']);
    expect(rowNames()).toStrictEqual(['Aug 11', 'Aug 12', 'Aug 13']);
  });

  it('writes the year there too once the period runs across one', () => {
    const option = show({ manyYears: true });

    expect(cardHeading(option, 0)).toBe('Tuesday, August 11, 2026');
  });

  /**
   * The axis of a single day needs no date beside each hour, because every hour on it is from the
   * same day. The card is read on its own, away from anything that would say which day that is.
   */
  it('gives an hour its weekday and its date, even on a period of one day', () => {
    const option = show({ activity: HOURS, granularity: 'hour', manyDays: false });

    expect(cardHeading(option, 0)).toBe('Tuesday, August 18 at 12 AM');
    expect(cardHeading(option, 2)).toBe('Tuesday, August 18 at 2 AM');
    expect(option.xAxis.data).toStrictEqual(['12 AM', '1 AM', '2 AM']);
  });

  it('heads the picture of who came the same way', () => {
    const option = show({ view: 'who' });

    expect(cardHeading(option, 2)).toBe('Thursday, August 13');
    expect(screen.getByRole('row', { name: 'Aug 13 8 4 3 1' })).toBeInTheDocument();
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

  /**
   * The card a hover opens marks each figure with its series' own colour, and a band that did not
   * state one would be marked in a colour of the engine's choosing — a card that disagrees with the
   * picture beneath it and the legend beside it.
   */
  it('marks each band in the card with the colour it is drawn in', () => {
    const option = show({ view: 'who' });

    expect(option.series.map((one) => one.itemStyle?.color)).toStrictEqual([
      PALETTE.tones.people,
      PALETTE.tones.automation,
      PALETTE.tones.unclear,
    ]);
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

  /**
   * While the engine is stopped, or still working through a backlog, not one bucket may have
   * settled. Calling that "the newest" would read as though the rest were final.
   */
  it('says the whole period is still being judged where not one bucket has settled', () => {
    show({ view: 'who', series: { ...JUDGED, completeTo: JUDGED.from } });

    expect(screen.getAllByText('still being judged')).toHaveLength(3);
    expect(
      screen.getByText(
        'Visits that have finished, by day in Kolkata. This whole period is still being judged.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/The newest are still being judged/)).not.toBeInTheDocument();
  });

  /**
   * Judging stalled six hours into the first of three days has settled none of them, however far
   * into the period it reached: the line says so of the whole period, not of "the newest" (0054).
   */
  it('says the whole period is still being judged where judging stopped inside its first bucket', () => {
    show({ view: 'who', series: { ...JUDGED, completeTo: '2026-08-11T00:30:00+00:00' } });

    expect(screen.getAllByText('still being judged')).toHaveLength(3);
    expect(
      screen.getByText(
        'Visits that have finished, by day in Kolkata. This whole period is still being judged.',
      ),
    ).toBeInTheDocument();
  });

  it('says nothing at all once the whole period has settled', () => {
    const option = show({ view: 'who' });

    expect(option.series.some((one) => one.markArea !== undefined)).toBe(false);
    expect(screen.queryByText('The newest are still being judged.')).not.toBeInTheDocument();
  });

  /** Three whole weeks, so whatever is washed is washed for the judging and nothing else. */
  it('washes a week still being judged like a day, when the screen is kept to people', () => {
    const option = show({
      activity: WHOLE_WEEKS,
      granularity: 'week',
      peopleOnly: true,
      stillJudging: 2,
      style: 'Area',
    });

    expect(washed(option)).toStrictEqual([['Oct 12 – 18', 'Oct 19 – 25']]);
    expect(screen.getAllByText('still being judged')).toHaveLength(1);
  });

  /**
   * The judging reaches back to the third week, past the last week the period cuts short. Columns
   * fade both weeks cut short and every week still being judged. An area washes from the last week
   * at full strength to the end, once, and leaves the first week alone: washed as well, it would
   * leave nothing at full strength — so the caption says nothing of the weeks cut short there.
   */
  it('quietens weeks cut short and weeks still being judged without washing a stretch twice', () => {
    const columns = show({
      activity: WEEKS,
      granularity: 'week',
      peopleOnly: true,
      stillJudging: 2,
      style: 'Columns',
    });

    expect(columns.series[0]?.data).toStrictEqual([
      { value: 120, itemStyle: { opacity: UNSETTLED } },
      310,
      { value: 280, itemStyle: { opacity: UNSETTLED } },
      { value: 295, itemStyle: { opacity: UNSETTLED } },
      { value: 190, itemStyle: { opacity: UNSETTLED } },
    ]);
    expect(screen.getByText(/Weeks the period cuts short are drawn fainter\./)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('radio', { name: 'Area' }));

    expect(washed(drawn.option as unknown as Drawing)).toStrictEqual([
      ['Oct 5 – 11', 'Oct 26 – 30'],
    ]);
    expect(screen.queryByText(/Weeks the period cuts short/)).not.toBeInTheDocument();
    expect(screen.getByText(/The newest are still being judged\./)).toBeInTheDocument();
  });

  it('washes the whole run once where none of it is settled, weeks cut short and all', () => {
    const option = show({
      activity: WEEKS,
      granularity: 'week',
      peopleOnly: true,
      stillJudging: 0,
      style: 'Area',
    });

    expect(washed(option)).toStrictEqual([['Oct 1 – 4', 'Oct 26 – 30']]);
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
   * A line drawn from days whose reports have gone is a line drawn from nothing, so where there is
   * no period before to draw, nothing on the card offers one.
   */
  it('is not offered where there is no period before still kept to draw', () => {
    const option = show({ view: 'who', comparing: false });

    expect(
      screen.queryByRole('button', { name: 'Compare with the period before' }),
    ).not.toBeInTheDocument();
    expect(option.series.some((one) => one.lineStyle?.type === 'dashed')).toBe(false);
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

  /**
   * Each earlier week falls on the same weekdays as the week it is drawn beside, so its days are
   * not the days the cards are measured against — and the caption says which they are.
   */
  it('sets the dashed lines week beside week, and says which days they are', () => {
    const option = show({
      view: 'who',
      series: JUDGED_WEEKS,
      granularity: 'week',
      against: true,
      earlierSeries: EARLIER_JUDGED_WEEKS,
      earlierDates: 'Aug 27 – Sep 25, 2026',
    });

    expect(option.series.at(-1)?.data).toStrictEqual([25, 70, 66, 72, 41]);
    expect(
      screen.getByText(
        'Visits that have finished, by week in Kolkata, Monday to Sunday. Weeks the period cuts short are drawn fainter. The dashed line is Aug 27 – Sep 25, 2026, lined up week for week.',
      ),
    ).toBeInTheDocument();
  });

  it('sets the dashed lines of how much was read month beside month, and says so', () => {
    show({
      activity: MONTHS,
      granularity: 'month',
      against: true,
      earlierPoints: MONTHS.points,
      earlierDates: 'May 2 – Jul 30, 2026',
    });

    expect(
      screen.getByText(
        'Months follow the calendar in Kolkata, and somebody who comes back on another day counts again. The dashed lines are May 2 – Jul 30, 2026, lined up month for month.',
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
    const option = show({
      view: 'activity',
      peopleOnly: true,
      activity: HOURS,
      granularity: 'hour',
    });

    expect(option.series[1]?.name).toBe('Visitors judged to be people');
  });

  it('keeps calling a week of people daily visitors judged to be people', () => {
    const option = show({
      view: 'activity',
      peopleOnly: true,
      activity: WEEKS,
      granularity: 'week',
    });

    expect(option.series[1]?.name).toBe('Daily visitors judged to be people');
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

  it('says the whole period of the people’s figures is still being judged where judging stopped inside its first day', () => {
    show({
      view: 'activity',
      peopleOnly: true,
      stillJudging: 0,
      judgedTo: '2026-08-11T00:30:00+00:00',
    });

    expect(
      screen.getByText(
        'Days run midnight to midnight in Kolkata. This whole period is still being judged.',
      ),
    ).toBeInTheDocument();
  });

  it('says the whole period is still being judged where not one of its figures has settled', () => {
    show({ view: 'activity', peopleOnly: true, stillJudging: 0 });

    expect(
      screen.getByText(
        'Days run midnight to midnight in Kolkata. This whole period is still being judged.',
      ),
    ).toBeInTheDocument();
    expect(screen.getAllByText('still being judged')).toHaveLength(3);
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
   * Over buckets still being judged the people may yet come, so the answer says so rather than
   * reading as final.
   */
  it('says the people may yet come where the period is still being judged', () => {
    show({
      view: 'who',
      peopleOnly: true,
      series: { ...NOBODY, completeTo: '2026-08-12T18:30:00+00:00' },
    });

    expect(
      screen.getByText(
        'No visits in this period were judged to be people. The newest are still being judged.',
      ),
    ).toBeInTheDocument();
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

  /** One day, as a press hands it back. */
  function day(date: string) {
    return { first: date, last: date };
  }

  /**
   * A bucket beginning at half past six in the evening, London time, is the next day where the
   * website is — and that is the day it hands back, whoever is reading.
   */
  it("hands the picture the day a bucket falls in, in the website's own zone", () => {
    const picked = vi.fn();

    show({ onPick: picked });
    drawn.pick?.(1);

    expect(picked).toHaveBeenCalledWith(day('2026-08-12'));
  });

  /**
   * Under a comparison the earlier period is drawn behind this one, bucket for bucket, so the
   * first column is two days at once. The one pressed is this period's.
   */
  it("lands on this period's day rather than the earlier one's, with the period before drawn behind it", () => {
    const picked = vi.fn();

    show({ against: true, onPick: picked });
    drawn.pick?.(0);

    expect(picked).toHaveBeenCalledWith(day('2026-08-11'));
    expect(picked).not.toHaveBeenCalledWith(day('2026-08-08'));
  });

  /** A canvas is not a control, so the table the same figures are published in carries the way in. */
  it('offers the same day from the row of the table, for anybody without a pointer', async () => {
    const picked = vi.fn();

    show({ onPick: picked });

    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(screen.getByRole('button', { name: 'Aug 12, look at this day' }));

    expect(picked).toHaveBeenCalledWith(day('2026-08-12'));
  });

  it('names the day an hour falls in, on a period cut by the hour', async () => {
    const picked = vi.fn();

    show({ activity: HOURS, granularity: 'hour', onPick: picked });

    await userEvent.click(screen.getByRole('button', { name: 'Aug 18, 12 AM, look at this day' }));

    expect(picked).toHaveBeenCalledWith(day('2026-08-18'));
  });

  it('takes the picture of who came into a day too', () => {
    const picked = vi.fn();

    show({ view: 'who', onPick: picked });
    drawn.pick?.(2);

    expect(picked).toHaveBeenCalledWith(day('2026-08-13'));
    expect(screen.getByRole('button', { name: 'Aug 13, look at this day' })).toBeInTheDocument();
  });

  /**
   * A day still being judged is still a day. The row that says so beneath its bucket offers the
   * same way in as every other row; that the wash on the canvas does not swallow a press is a
   * fact about the surface, held by looking at it in a browser.
   */
  it('offers a bucket still being judged the same way in', () => {
    const picked = vi.fn();

    show({ view: 'who', series: FILLING, onPick: picked });
    drawn.pick?.(2);

    expect(picked).toHaveBeenCalledWith(day('2026-08-13'));
    expect(screen.getByRole('button', { name: 'Aug 13, look at this day' })).toBeInTheDocument();
    expect(screen.getAllByText('still being judged')).toHaveLength(1);
  });

  /** The far edge of the last column rounds to one more bucket than there are, which is no day. */
  it('names no day for a press the surface places past the last bucket', () => {
    const picked = vi.fn();

    show({ onPick: picked });

    expect(() => drawn.pick?.(DAYS.points.length)).not.toThrow();
    expect(picked).not.toHaveBeenCalled();
  });

  /** On a period that is already one day there is nothing narrower, and the table reads as it did. */
  it('offers no way into a day when none is offered, and the table reads as it did', () => {
    show();

    expect(drawn.pick).toBeUndefined();
    expect(screen.queryByRole('button', { name: /look at th/ })).not.toBeInTheDocument();
    expect(rowNames()).toStrictEqual(['Aug 11', 'Aug 12', 'Aug 13']);
  });
});
