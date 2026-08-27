import { describe, expect, it } from 'vitest';
import { ringOption, type RingSlice, softened } from '@/lib/charts/ring';
import { PALETTE } from '@/test/drawing';

const READERS: readonly RingSlice[] = [
  { name: 'Computers', value: 24, colour: 'rgba(110, 76, 232, 1)' },
  { name: 'Phones', value: 12, colour: 'rgba(56, 168, 184, 1)' },
  { name: 'Not known', value: 4, colour: 'rgba(136, 136, 146, 1)' },
];

/** The one series a ring draws, as the charting engine would receive it. */
function band(slices: readonly RingSlice[]): Record<string, unknown> {
  const series = ringOption(slices, PALETTE).series as readonly Record<string, unknown>[];

  return series[0] as Record<string, unknown>;
}

function parts(slices: readonly RingSlice[]): readonly Record<string, unknown>[] {
  return band(slices).data as readonly Record<string, unknown>[];
}

describe('a whole divided into its parts', () => {
  it('draws one part per named part, in the order it was handed them', () => {
    expect(parts(READERS).map((part) => part.name)).toStrictEqual([
      'Computers',
      'Phones',
      'Not known',
    ]);
  });

  it('draws each part in the colour its own name is marked with', () => {
    expect(parts(READERS)[1]).toStrictEqual({
      name: 'Phones',
      value: 12,
      itemStyle: { color: 'rgba(56, 168, 184, 1)' },
    });
  });

  /**
   * A part with nobody in it has no arc, and drawing it anyway leaves a hairline of border where
   * the reader has every reason to think something was counted.
   */
  it('leaves out a part that came to nothing', () => {
    const drawn = parts([...READERS, { name: 'Tablets', value: 0, colour: 'rgba(0, 0, 0, 1)' }]);

    expect(drawn).toHaveLength(3);
    expect(drawn.map((part) => part.name)).not.toContain('Tablets');
  });

  /**
   * A designed state rather than an absence: a card whose parts all came to nought still looks
   * like the card it is instead of like a drawing that never arrived.
   */
  it('draws an unbroken band when every part came to nothing', () => {
    const drawn = parts([{ name: 'Computers', value: 0, colour: 'rgba(0, 0, 0, 1)' }]);

    expect(drawn).toHaveLength(1);
    expect(drawn[0]).toStrictEqual({ name: '', value: 1, itemStyle: { color: PALETTE.line } });
  });

  /** Two parts of a similar colour still have to read as two. */
  it('holds the parts apart in the colour of the card behind them', () => {
    expect(band(READERS).itemStyle).toStrictEqual({
      borderColor: PALETTE.surface,
      borderWidth: 2,
    });
  });

  /**
   * The words are in the list beside the ring, which is what a screen reader is handed and what
   * anybody comparing two figures reads anyway.
   */
  it('writes nothing on the drawing and offers nothing to hover over', () => {
    const drawn = band(READERS);

    expect(drawn.label).toStrictEqual({ show: false });
    expect(drawn.labelLine).toStrictEqual({ show: false });
    expect(drawn.silent).toBe(true);
  });

  it('leaves the middle open, so the shape reads as a ring rather than as a pie', () => {
    expect(band(READERS).radius).toStrictEqual(['62%', '88%']);
  });
});

describe('a second weight of one colour', () => {
  it('keeps the hue and gives up half the strength', () => {
    expect(softened('rgba(110, 76, 232, 1)')).toBe('rgba(110,76,232,0.5)');
  });
});
