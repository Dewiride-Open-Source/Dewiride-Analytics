import { afterEach, describe, expect, it, vi } from 'vitest';
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
});
