import { describe, expect, it } from 'vitest';
import { CHART_VIEWS } from '@/lib/analytics/chart-view';
import { DEFAULT_DRAWING, DRAWINGS, drawingFor, drawingsFor } from '@/lib/charts/drawing';

describe('the styles a chart may be drawn in', () => {
  it('offers every one of them where the numbers stand side by side', () => {
    expect(drawingsFor('activity')).toStrictEqual(DRAWINGS);
  });

  /**
   * Each band on a stack is read by its thickness, and a line drawn along the top of one is read
   * as that band's own figure when it is really the total of everything underneath it.
   */
  it('offers no line where the numbers are stacked on each other', () => {
    expect(drawingsFor('who')).not.toContain('line');
  });

  it('draws in the style somebody last asked for', () => {
    expect(drawingFor('activity', 'columns')).toBe('columns');
  });

  it('falls back where the view cannot honour what somebody last asked for', () => {
    expect(drawingFor('who', 'line')).toBe(DEFAULT_DRAWING);
  });

  /** Which is only a fallback at all because every view can draw it. */
  it('keeps the fallback something every view offers', () => {
    for (const view of CHART_VIEWS) {
      expect(drawingsFor(view)).toContain(DEFAULT_DRAWING);
    }
  });
});
