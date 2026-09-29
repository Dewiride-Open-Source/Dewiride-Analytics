import { describe, expect, it } from 'vitest';
import { chartFrame, LAYERS } from '@/lib/charts/frame';
import { PALETTE } from '@/test/drawing';

/** The depth the charting engine draws a column at, where the series says nothing of its own. */
const COLUMNS_DEPTH = 2;

/**
 * An hour of the day the clocks go back, which is written the same both times it comes round.
 *
 * The two full names are plainly made up, so that which of them comes back says which bucket was
 * looked up rather than anything about how a real one is written.
 */
const TWICE = ['1 AM', '1 AM'] as const;
const TITLES = ['1 AM, the first time', '1 AM, the second time'] as const;

/**
 * What the card a hover opens is headed with over the bucket at a place, asked for as the engine
 * asks for it: with the name the axis gives that bucket, and one series drawn there.
 */
function headingAt(frame: ReturnType<typeof chartFrame>, at: number): string {
  return frame.tooltip.axisPointer.label.formatter({
    value: frame.xAxis.data[at] ?? '',
    seriesData: [{ dataIndex: at }],
  });
}

describe('the card a hover opens', () => {
  it('is headed with the full name of the bucket under the pointer, found by where it sits', () => {
    const frame = chartFrame(PALETTE, TWICE, false, TITLES);

    expect(headingAt(frame, 0)).toBe('1 AM, the first time');
    expect(headingAt(frame, 1)).toBe('1 AM, the second time');
  });

  it('is headed with the name on the axis where no fuller one is given', () => {
    expect(headingAt(chartFrame(PALETTE, ['3:07 PM'], true), 0)).toBe('3:07 PM');
  });

  it('is headed with the name on the axis where the bucket has no fuller name of its own', () => {
    expect(headingAt(chartFrame(PALETTE, TWICE, false, TITLES.slice(0, 1)), 1)).toBe('1 AM');
  });

  /**
   * The engine asks for the same heading when it places the pointer on the axis, and there it may
   * have no series in hand to say where the bucket sits.
   */
  it('falls back to the name on the axis where no series is under the pointer', () => {
    const { tooltip } = chartFrame(PALETTE, TWICE, false, TITLES);

    expect(tooltip.axisPointer.label.formatter({ value: '1 AM', seriesData: [] })).toBe('1 AM');
  });

  /**
   * The heading is taken from a label the pointer could also carry on the axis. Left unasked for,
   * the engine keeps it off, and the axis goes on reading the short names.
   */
  it('leaves the axis to its short names', () => {
    const { tooltip, xAxis } = chartFrame(PALETTE, TWICE, true, TITLES);

    expect(tooltip.axisPointer.label).not.toHaveProperty('show');
    expect(xAxis.data).toStrictEqual(['1 AM', '1 AM']);
  });

  /**
   * Left to itself, the card jumps to the far side of the pointer when it would run off the right
   * of the picture, which on a picture a phone wide is often off the left of the screen.
   */
  it('stays inside the picture', () => {
    expect(chartFrame(PALETTE, TWICE, false).tooltip.confine).toBe(true);
    expect(chartFrame(PALETTE, TWICE, true).tooltip.confine).toBe(true);
  });

  it('keeps the pointer each drawing is read with', () => {
    expect(chartFrame(PALETTE, TWICE, true).tooltip.axisPointer.type).toBe('shadow');
    expect(chartFrame(PALETTE, TWICE, false).tooltip.axisPointer.type).toBe('line');
  });

  /**
   * Laid over the columns, the shade fades the bucket it picks out until it looks like one still
   * being judged.
   */
  it('lays its shade beneath the columns rather than over them', () => {
    expect(chartFrame(PALETTE, TWICE, true).tooltip.axisPointer).toMatchObject({
      type: 'shadow',
      z: LAYERS.pointerShade,
    });
    expect(LAYERS.pointerShade).toBeLessThan(COLUMNS_DEPTH);
  });

  /**
   * The engine keeps a depth once given until another replaces it, so a line that left its depth
   * unsaid would be drawn at the shade's after the picture was switched from columns, beneath the
   * bands it points through.
   */
  it('draws its line over everything else, whatever was drawn before', () => {
    const { z } = chartFrame(PALETTE, TWICE, false).tooltip.axisPointer;

    expect(z).toBe(LAYERS.pointerLine);
    expect(Math.max(...Object.values(LAYERS))).toBe(LAYERS.pointerLine);
  });
});
