import { describe, expect, it } from 'vitest';
import { CHART_VIEWS } from '@/lib/analytics/chart-view';
import {
  DEFAULT_DRAWING,
  DRAWINGS,
  drawingFor,
  drawingsFor,
  SHAPED_FROM,
} from '@/lib/charts/drawing';

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

/**
 * One bucket is a point, which a line or an area draws as nothing at all, and two are a slope read
 * as a trend. Side by side as columns, each reads as the figure it is.
 */
describe('a picture with too few buckets for a shape', () => {
  it('is drawn as columns whatever somebody last asked for', () => {
    for (const view of CHART_VIEWS) {
      for (const buckets of [1, 2]) {
        expect(drawingFor(view, 'area', buckets)).toBe('columns');
        expect(drawingFor(view, 'line', buckets)).toBe('columns');
      }
    }
  });

  it('offers nothing else to draw it as', () => {
    for (const view of CHART_VIEWS) {
      expect(drawingsFor(view, SHAPED_FROM - 1)).toStrictEqual(['columns']);
    }
  });

  it('is a shape again from three buckets', () => {
    expect(SHAPED_FROM).toBe(3);
    expect(drawingFor('who', 'area', SHAPED_FROM)).toBe('area');
    expect(drawingsFor('activity', SHAPED_FROM)).toStrictEqual(DRAWINGS);
  });

  it('is drawn as asked while how many buckets it has is not known', () => {
    expect(drawingFor('activity', 'line')).toBe('line');
    expect(drawingsFor('who')).toStrictEqual(['columns', 'area']);
  });
});
