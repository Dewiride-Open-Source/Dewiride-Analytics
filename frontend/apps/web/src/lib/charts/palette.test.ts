import { afterEach, describe, expect, it, vi } from 'vitest';
import { TONE_ORDER } from '@/lib/analytics/verdicts';
import { readChartPalette } from '@/lib/charts/palette';

/**
 * A canvas that behaves the way a browser's does: it refuses a colour it cannot parse by leaving
 * the previous value in place, and it reports back what it actually painted.
 */
/**
 * The tokens the stylesheet would define. This document has no stylesheet at all, so without them
 * every colour is empty and the conversion is never reached.
 */
function styledDocument() {
  vi.spyOn(window, 'getComputedStyle').mockReturnValue({
    getPropertyValue: (token: string) => `oklch(0.58 0.195 288) /* ${token} */`,
  } as unknown as CSSStyleDeclaration);
}

function canvasPainting(pixel: readonly number[], refuse = false) {
  styledDocument();

  const context = {
    fillStyle: '',
    clearRect: () => {},
    fillRect: () => {},
    getImageData: () => ({ data: Uint8ClampedArray.from(pixel) }),
  };

  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
    () =>
      ({
        ...context,
        set fillStyle(value: string) {
          if (!refuse || value.startsWith('#')) {
            context.fillStyle = value;
          }
        },
        get fillStyle() {
          return context.fillStyle;
        },
      }) as unknown as CanvasRenderingContext2D,
  );
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('the colours a chart is drawn in', () => {
  /**
   * Every colour in the product is written in oklch, and the charting engine cannot read it. What
   * the canvas hands back has to be four plain numbers, because the notation that preserves a
   * colour outside the sRGB gamut is one the engine silently ignores.
   */
  it('reports whatever was actually painted, as plain numbers', () => {
    canvasPainting([110, 76, 232, 255]);

    expect(readChartPalette().series[0]).toBe('rgba(110, 76, 232, 1)');
  });

  it('keeps drawing in something sensible when a colour cannot be painted at all', () => {
    canvasPainting([0, 0, 0, 0], true);

    expect(readChartPalette().series[0]).toBe('rgba(110, 76, 232, 1)');
  });

  it('keeps drawing in something sensible where there is no canvas', () => {
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null);

    const palette = readChartPalette();

    expect(palette.series).toStrictEqual(['rgba(110, 76, 232, 1)', 'rgba(56, 168, 184, 1)']);
    expect(palette.text).toBe('rgba(41, 38, 51, 1)');
  });

  /**
   * A band on a chart has to be the colour the pill beside it already is, or the drawing and the
   * words under it are two different answers.
   */
  it('carries a colour for every meaning a verdict can be shown in', () => {
    canvasPainting([110, 76, 232, 255]);

    const palette = readChartPalette();

    for (const tone of TONE_ORDER) {
      expect(palette.tones[tone]).toBe('rgba(110, 76, 232, 1)');
    }
  });

  it('has one to fall back on for each of them as well', () => {
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null);

    const palette = readChartPalette();

    expect(new Set(TONE_ORDER.map((tone) => palette.tones[tone])).size).toBe(TONE_ORDER.length);
  });
});

describe('the colour of a part nothing could be established about', () => {
  /**
   * A device nobody could name is not a verdict about anything, so a ring of devices has its own
   * quiet grey rather than reaching into the vocabulary of traffic for one.
   */
  it('is read from the document like every other colour', () => {
    canvasPainting([136, 136, 146, 255]);

    expect(readChartPalette().subtle).toBe('rgba(136, 136, 146, 1)');
  });

  it('has one to fall back on where there is no canvas', () => {
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null);

    expect(readChartPalette().subtle).toBe('rgba(136, 136, 146, 1)');
  });
});
