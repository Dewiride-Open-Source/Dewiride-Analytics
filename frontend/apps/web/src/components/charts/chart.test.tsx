import { cleanup, fireEvent } from '@testing-library/react';
import { useState } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Chart } from '@/components/charts/chart';
import { renderScreen } from '@/test/harness';

const setOption = vi.fn();
const resize = vi.fn();
const dispose = vi.fn();
const containPixel = vi.fn((_finder: unknown, _at: readonly number[]) => true);
const convertFromPixel = vi.fn((_finder: unknown, _x: number) => 2);
const init = vi.fn(() => ({ setOption, resize, dispose, containPixel, convertFromPixel }));

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

vi.mock('echarts/charts', () => ({ LineChart: 'line', BarChart: 'bar', PieChart: 'pie' }));
vi.mock('echarts/components', () => ({
  GridComponent: 'grid',
  MarkAreaComponent: 'markArea',
  TooltipComponent: 'tooltip',
}));
vi.mock('echarts/renderers', () => ({ CanvasRenderer: 'canvas' }));
vi.mock('next-themes', () => ({ useTheme: () => ({ resolvedTheme: 'light' }) }));

/** Nothing in this document observes anything, so the chart's watcher has to be supplied. */
class StubObserver {
  observe() {}
  disconnect() {}
}

beforeEach(() => {
  vi.stubGlobal('ResizeObserver', StubObserver);
  containPixel.mockReturnValue(true);
  convertFromPixel.mockReturnValue(2);
});

/** A chart with nothing in it: this is about the surface around one, not about a drawing. */
const EMPTY = () => ({ series: [] });

/** The same surface handed a second set of figures, the way the live screen hands it one. */
function Moving() {
  const [option, redraw] = useState<() => { series: unknown[] }>(() => EMPTY);

  return (
    <>
      <button type="button" onClick={() => redraw(() => () => ({ series: [{ id: 'minutes' }] }))}>
        Redraw
      </button>
      <Chart option={option} label="Anything" />
    </>
  );
}

interface RepointedProps {
  readonly first: (index: number) => void;
  readonly second: (index: number) => void;
}

/**
 * The same surface handed a second handler, the way a redrawn view hands it one, and then none
 * at all, the way a period narrowed to a single day withdraws it.
 */
function Repointed({ first, second }: RepointedProps) {
  const [picker, repoint] = useState<((index: number) => void) | undefined>(() => first);

  return (
    <>
      <button type="button" onClick={() => repoint(() => second)}>
        Repoint
      </button>
      <button type="button" onClick={() => repoint(undefined)}>
        Withdraw
      </button>
      <Chart option={EMPTY} label="Anything" onPick={picker} />
    </>
  );
}

/** The cursor over the drawing, as the surface writes it. */
const POINTER = '[&_canvas]:cursor-pointer';
const ARROW = '[&_canvas]:cursor-default';

describe('the charting surface', () => {
  it('registers only the pieces the product draws with', () => {
    expect(echarts.registered).toStrictEqual([
      'line',
      'bar',
      'pie',
      'grid',
      'markArea',
      'tooltip',
      'canvas',
    ]);
  });

  it('announces what it shows, since a drawing tells a screen reader nothing', () => {
    const { getByRole } = renderScreen(<Chart option={EMPTY} label="Traffic over a week" />);

    expect(getByRole('img', { name: 'Traffic over a week' })).toBeInTheDocument();
  });

  it('builds the chart from the palette in force', () => {
    renderScreen(<Chart option={EMPTY} label="Anything" />);

    expect(init).toHaveBeenCalled();
    expect(setOption).toHaveBeenCalledWith(
      expect.objectContaining({ series: [] }),
      expect.anything(),
    );
  });

  /** The engine writes its own spoken description; ours is written for the chart it is on. */
  it('turns off the description it would otherwise generate for itself', () => {
    renderScreen(<Chart option={EMPTY} label="Anything" />);

    expect(setOption).toHaveBeenCalledWith(
      expect.objectContaining({ aria: { enabled: false } }),
      expect.anything(),
    );
  });

  it('holds still for somebody who has asked for less movement', () => {
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      configurable: true,
      value: (query: string) => ({ matches: true, media: query }) as MediaQueryList,
    });

    renderScreen(<Chart option={EMPTY} label="Anything" />);

    expect(setOption).toHaveBeenCalledWith(
      expect.objectContaining({ animation: false }),
      expect.anything(),
    );
  });

  /**
   * The live screen redraws its chart every few seconds. A surface built afresh each time would
   * start every column at the axis and grow it out again, so a busy website would never be still.
   */
  it('redraws the chart it already has when the figures on it move', () => {
    const { getByRole } = renderScreen(<Moving />);

    fireEvent.click(getByRole('button'));

    expect(init).toHaveBeenCalledOnce();
    expect(dispose).not.toHaveBeenCalled();
    expect(setOption).toHaveBeenCalledTimes(2);
  });

  /** Anything the surface is not handed a second time is taken off it rather than left behind. */
  it('replaces what is drawn rather than matching it up with what was there before', () => {
    renderScreen(<Chart option={EMPTY} label="Anything" />);

    expect(setOption).toHaveBeenCalledWith(expect.anything(), { replaceMerge: ['series'] });
  });

  it('takes the chart down with the screen rather than leaving it behind', () => {
    renderScreen(<Chart option={EMPTY} label="Anything" />);

    cleanup();

    expect(dispose).toHaveBeenCalledOnce();
  });
});

describe('a chart that can be pressed', () => {
  it('tells its caller which category was pressed, by index', () => {
    const picked = vi.fn();
    const { getByRole } = renderScreen(<Chart option={EMPTY} label="Anything" onPick={picked} />);

    fireEvent.click(getByRole('img'), { clientX: 120, clientY: 40 });

    expect(containPixel).toHaveBeenCalledWith('grid', [120, 40]);
    expect(convertFromPixel).toHaveBeenCalledWith({ xAxisIndex: 0 }, 120);
    expect(picked).toHaveBeenCalledWith(2);
  });

  /** The axis labels and the margins belong to no category. */
  it('ignores a press outside the plot', () => {
    containPixel.mockReturnValue(false);
    const picked = vi.fn();
    const { getByRole } = renderScreen(<Chart option={EMPTY} label="Anything" onPick={picked} />);

    fireEvent.click(getByRole('img'), { clientX: 3, clientY: 40 });

    expect(convertFromPixel).not.toHaveBeenCalled();
    expect(picked).not.toHaveBeenCalled();
  });

  /** A surface with nothing drawn on it answers with no category at all. */
  it('ignores a press the engine cannot place on a category', () => {
    convertFromPixel.mockReturnValue(Number.NaN);
    const picked = vi.fn();
    const { getByRole } = renderScreen(<Chart option={EMPTY} label="Anything" onPick={picked} />);

    fireEvent.click(getByRole('img'), { clientX: 120, clientY: 40 });

    expect(picked).not.toHaveBeenCalled();
  });

  it('listens for nothing when nobody is interested', () => {
    const { getByRole } = renderScreen(<Chart option={EMPTY} label="Anything" />);

    fireEvent.click(getByRole('img'), { clientX: 120, clientY: 40 });

    expect(containPixel).not.toHaveBeenCalled();
  });

  /**
   * The charting engine offers a hand over every column and slice of its own accord. A hand that
   * leads nowhere is a promise the picture cannot keep, so the surface overrules it.
   */
  it('shows a pointer only where a press means something', () => {
    const { getByRole, unmount } = renderScreen(
      <Chart option={EMPTY} label="Anything" onPick={vi.fn()} />,
    );

    expect(getByRole('img')).toHaveClass(POINTER);
    expect(getByRole('img')).not.toHaveClass(ARROW);

    unmount();

    const idle = renderScreen(<Chart option={EMPTY} label="Anything" />);

    expect(idle.getByRole('img')).toHaveClass(ARROW);
    expect(idle.getByRole('img')).not.toHaveClass(POINTER);
  });

  /**
   * The charting engine counts pixels from the surface's own corner, wherever that corner is on
   * the page, so a press is measured from there rather than from the page's.
   */
  it('measures a press from its own corner rather than the page’s', () => {
    const picked = vi.fn();
    const { getByRole } = renderScreen(<Chart option={EMPTY} label="Anything" onPick={picked} />);

    vi.spyOn(getByRole('img'), 'getBoundingClientRect').mockReturnValue({
      left: 100,
      top: 30,
    } as DOMRect);
    fireEvent.click(getByRole('img'), { clientX: 220, clientY: 70 });

    expect(containPixel).toHaveBeenCalledWith('grid', [120, 40]);
    expect(convertFromPixel).toHaveBeenCalledWith({ xAxisIndex: 0 }, 120);
    expect(picked).toHaveBeenCalledWith(2);
  });

  /** Re-pointing is a listener swapped, not a chart rebuilt from the axis up. */
  it('keeps the chart it has when the handler changes, and presses the new one', () => {
    const first = vi.fn();
    const second = vi.fn();
    const { getByRole } = renderScreen(<Repointed first={first} second={second} />);

    fireEvent.click(getByRole('button', { name: 'Repoint' }));
    fireEvent.click(getByRole('img'), { clientX: 120, clientY: 40 });

    expect(init).toHaveBeenCalledOnce();
    expect(dispose).not.toHaveBeenCalled();
    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledWith(2);
  });

  it('stops listening once nobody is interested any more', () => {
    const first = vi.fn();
    const { getByRole } = renderScreen(<Repointed first={first} second={vi.fn()} />);

    fireEvent.click(getByRole('button', { name: 'Withdraw' }));
    fireEvent.click(getByRole('img'), { clientX: 120, clientY: 40 });

    expect(containPixel).not.toHaveBeenCalled();
    expect(first).not.toHaveBeenCalled();
    expect(getByRole('img')).toHaveClass(ARROW);
  });
});
