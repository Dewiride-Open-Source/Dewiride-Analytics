import { cleanup } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Chart } from '@/components/charts/chart';
import { renderScreen } from '@/test/harness';

const setOption = vi.fn();
const resize = vi.fn();
const dispose = vi.fn();
const init = vi.fn(() => ({ setOption, resize, dispose }));

/**
 * Registration happens as the module is first read, which is before anything declared here has
 * been initialised — so the list has to live on the mock rather than beside it.
 */
const echarts = vi.hoisted(() => ({ registered: [] as unknown[] }));

vi.mock('echarts/core', () => ({
  init: (...args: unknown[]) => init(...(args as [])),
  use: (parts: unknown[]) => echarts.registered.push(...parts),
  color: { modifyAlpha: (colour: string) => colour },
}));

vi.mock('echarts/charts', () => ({ LineChart: 'line' }));
vi.mock('echarts/components', () => ({ GridComponent: 'grid', TooltipComponent: 'tooltip' }));
vi.mock('echarts/renderers', () => ({ CanvasRenderer: 'canvas' }));
vi.mock('next-themes', () => ({ useTheme: () => ({ resolvedTheme: 'light' }) }));

/** Nothing in this document observes anything, so the chart's watcher has to be supplied. */
class StubObserver {
  observe() {}
  disconnect() {}
}

beforeEach(() => {
  vi.stubGlobal('ResizeObserver', StubObserver);
  setOption.mockClear();
  resize.mockClear();
  dispose.mockClear();
});

/** A chart with nothing in it: this is about the surface around one, not about a drawing. */
const EMPTY = () => ({ series: [] });

describe('the charting surface', () => {
  it('registers only the pieces the product draws with', () => {
    expect(echarts.registered).toStrictEqual(['line', 'grid', 'tooltip', 'canvas']);
  });

  it('announces what it shows, since a drawing tells a screen reader nothing', () => {
    const { getByRole } = renderScreen(<Chart option={EMPTY} label="Traffic over a week" />);

    expect(getByRole('img', { name: 'Traffic over a week' })).toBeInTheDocument();
  });

  it('builds the chart from the palette in force', () => {
    renderScreen(<Chart option={EMPTY} label="Anything" />);

    expect(init).toHaveBeenCalled();
    expect(setOption).toHaveBeenCalledWith(expect.objectContaining({ series: [] }));
  });

  /** The engine writes its own spoken description; ours is written for the chart it is on. */
  it('turns off the description it would otherwise generate for itself', () => {
    renderScreen(<Chart option={EMPTY} label="Anything" />);

    expect(setOption).toHaveBeenCalledWith(expect.objectContaining({ aria: { enabled: false } }));
  });

  it('holds still for somebody who has asked for less movement', () => {
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      configurable: true,
      value: (query: string) => ({ matches: true, media: query }) as MediaQueryList,
    });

    renderScreen(<Chart option={EMPTY} label="Anything" />);

    expect(setOption).toHaveBeenCalledWith(expect.objectContaining({ animation: false }));
  });

  it('takes the chart down with the screen rather than leaving it behind', () => {
    renderScreen(<Chart option={EMPTY} label="Anything" />);

    cleanup();

    expect(dispose).toHaveBeenCalledOnce();
  });
});
