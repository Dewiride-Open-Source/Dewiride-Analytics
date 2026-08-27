import { describe, expect, it } from 'vitest';
import { changeBetween } from '@/lib/analytics/change';

describe('how a figure moved against the period before', () => {
  it('reports a rise as a share of what there was', () => {
    expect(changeBetween(112, 100)).toStrictEqual({ direction: 'up', fraction: 0.12 });
  });

  it('reports a fall the same way', () => {
    expect(changeBetween(88, 100)).toStrictEqual({ direction: 'down', fraction: -0.12 });
  });

  it('reports a period that held exactly as much as the one before it', () => {
    expect(changeBetween(100, 100)).toStrictEqual({ direction: 'level', fraction: 0 });
  });

  /**
   * The screen shows whole percentages, so the direction has to be read off the figure as it will
   * appear. Four in a thousand shown as nought per cent beside an arrow pointing up reads as a
   * mistake in the arithmetic rather than as a quiet week.
   */
  it('calls a move too small to show as no change at all', () => {
    expect(changeBetween(1004, 1000)).toStrictEqual({ direction: 'level', fraction: 0 });
    expect(changeBetween(996, 1000)).toStrictEqual({ direction: 'level', fraction: 0 });
  });

  /**
   * Forty visits after none is neither four thousand per cent nor infinitely many. It is a rise
   * with no size to it, and a percentage taken against nothing would look like a measurement.
   */
  it('has no figure for a rise out of a period that held nothing', () => {
    expect(changeBetween(40, 0)).toStrictEqual({ direction: 'up', fraction: null });
  });

  it('has nothing at all to report where neither period held any of it', () => {
    expect(changeBetween(0, 0)).toBeNull();
  });

  it('reports a fall to nothing as the whole of what there was', () => {
    expect(changeBetween(0, 40)).toStrictEqual({ direction: 'down', fraction: -1 });
  });

  /** Pages per visitor is a figure of the same kind, and moves by the same rule. */
  it('works the same way on a figure that is not a count', () => {
    expect(changeBetween(3.5, 3.2)).toStrictEqual({ direction: 'up', fraction: 0.09 });
  });
});
