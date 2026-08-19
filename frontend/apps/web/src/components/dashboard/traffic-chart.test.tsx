import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { TrafficChart } from '@/components/dashboard/traffic-chart';
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

const DAYS = [
  { start: '2026-08-11T00:00:00+00:00', pageViews: 40, visitors: 12 },
  { start: '2026-08-12T00:00:00+00:00', pageViews: 55, visitors: 18 },
  { start: '2026-08-13T00:00:00+00:00', pageViews: 30, visitors: 9 },
];

function show() {
  renderScreen(<TrafficChart days={DAYS} siteName="My Blog" zone="Kolkata" />);

  return drawn.option as {
    series: { name: string; data: number[]; areaStyle?: { color?: { colorStops: unknown[] } } }[];
    xAxis: { data: string[] };
  };
}

describe('the traffic chart', () => {
  it('draws both measures across the same days', () => {
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
      screen.getByRole('img', { name: /Daily page views and daily visitors for My Blog/ }),
    ).toBeInTheDocument();
  });

  it('names the place a day is counted in rather than its identifier', () => {
    show();

    expect(screen.getByText('Days run midnight to midnight in Kolkata.')).toBeInTheDocument();
  });
});
