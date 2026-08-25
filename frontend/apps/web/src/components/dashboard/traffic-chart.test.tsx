import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { TrafficChart, type TrafficPoint } from '@/components/dashboard/traffic-chart';
import type { Granularity } from '@/lib/analytics/period';
import type { ChartPalette } from '@/lib/charts/palette';
import { renderScreen } from '@/test/harness';

const PALETTE: ChartPalette = {
  series: ['rgba(110, 76, 232, 1)', 'rgba(56, 168, 184, 1)'],
  label: 'rgba(116, 113, 128, 1)',
  line: 'rgba(224, 222, 232, 1)',
  surface: 'rgba(255, 255, 255, 1)',
  border: 'rgba(205, 201, 216, 1)',
  text: 'rgba(41, 38, 51, 1)',
};

/**
 * Stands in for the drawing surface and runs the builder it is handed, so that what the chart
 * would be told to draw can be read as an object instead of as pixels on a canvas.
 */
const drawn = vi.hoisted(() => ({ option: undefined as Record<string, unknown> | undefined }));

vi.mock('@/components/charts/chart', () => ({
  Chart: ({
    option,
    label,
  }: {
    readonly option: (palette: ChartPalette) => unknown;
    readonly label: string;
  }) => {
    drawn.option = option(PALETTE) as Record<string, unknown>;

    return <div role="img" aria-label={label} />;
  },
}));

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

interface Drawing {
  series: { name: string; data: number[]; areaStyle?: { color?: { colorStops: unknown[] } } }[];
  xAxis: { data: string[] };
}

function show(
  points: readonly TrafficPoint[] = DAYS,
  granularity: Granularity = 'day',
  manyDays = true,
): Drawing {
  renderScreen(
    <TrafficChart
      points={points}
      siteName="My Blog"
      timeZoneId="Asia/Kolkata"
      zone="Kolkata"
      granularity={granularity}
      manyDays={manyDays}
    />,
  );

  return drawn.option as unknown as Drawing;
}

describe('the traffic chart', () => {
  it('draws both measures across the same buckets', () => {
    const option = show();

    expect(option.series.map((one) => one.name)).toStrictEqual(['Page views', 'Daily visitors']);
    expect(option.series[0]?.data).toStrictEqual([40, 55, 30]);
    expect(option.series[1]?.data).toStrictEqual([12, 18, 9]);
    expect(option.xAxis.data).toHaveLength(3);
  });

  /**
   * A flat wash has to be faint enough not to muddy the dark theme, which leaves it invisible in
   * the light one. Fading it out means one setting works in both.
   */
  it('washes the headline measure out towards the axis, and leaves the second bare', () => {
    const option = show();

    expect(option.series[0]?.areaStyle?.color?.colorStops).toHaveLength(2);
    expect(option.series[1]?.areaStyle).toBeUndefined();
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
});

describe('a period drawn an hour at a time', () => {
  it('writes each bucket as a time, with no date to repeat', () => {
    const option = show(HOURS, 'hour', false);

    expect(option.xAxis.data).toStrictEqual(['12 AM', '1 AM', '2 AM']);
  });

  it('keeps the date beside the time when the period covers more than one day', () => {
    const option = show(HOURS, 'hour', true);

    expect(option.xAxis.data[0]).toBe('Aug 18, 12 AM');
  });

  /**
   * Counted in an hour, distinct visitors are the people who were there in that hour. Calling that
   * figure the day's would be a claim about numbers that were never added up that way.
   */
  it('does not call an hour of visitors a day of them', () => {
    const option = show(HOURS, 'hour', false);

    expect(option.series.map((one) => one.name)).toStrictEqual(['Page views', 'Visitors']);
    expect(screen.getByText('Times are the clock in Kolkata.')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Time' })).toBeInTheDocument();
  });
});
