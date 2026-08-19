/**
 * The bridge between the design tokens and the charting engine.
 *
 * Every colour in this product is stated in oklch, which keeps a hue steady while its lightness
 * moves and is what makes one accent work in both themes. The charting engine's colour parser
 * predates that notation and cannot read it, so a token handed straight to a chart is silently
 * dropped and the chart draws in its own default palette instead.
 *
 * The conversion is delegated to the browser rather than reimplemented: a canvas context parses
 * any colour the stylesheet could contain and hands back the same colour written the way the
 * chart understands. That keeps one palette, defined once in the stylesheet, with no second copy
 * here to drift out of step with it.
 */

/** The colours a chart needs, already in a notation the charting engine can read. */
export interface ChartPalette {
  /** First series — the headline measure. */
  readonly series: readonly [string, string];
  /** Axis labels and the tooltip's supporting text. */
  readonly label: string;
  /** Grid lines and axis rules. */
  readonly line: string;
  /** Tooltip background. */
  readonly surface: string;
  /** Tooltip border. */
  readonly border: string;
  /** Tooltip heading. */
  readonly text: string;
}

const TOKENS = {
  series: ['--chart-1', '--chart-2'],
  label: '--foreground-muted',
  line: '--border',
  surface: '--surface',
  border: '--border-strong',
  text: '--foreground',
} as const;

/**
 * A colour that appears in no palette, used to tell a refused colour from an accepted one.
 *
 * Assigning to a canvas context's fill leaves the previous value in place when the colour cannot
 * be parsed, so the only way to detect a refusal is to seed it with something recognisable first.
 */
const SENTINEL = '#010203';

/** Used when there is no canvas to convert with, as in a test document. */
const FALLBACKS = {
  series: ['rgba(110, 76, 232, 1)', 'rgba(56, 168, 184, 1)'],
  label: 'rgba(116, 113, 128, 1)',
  line: 'rgba(224, 222, 232, 1)',
  surface: 'rgba(255, 255, 255, 1)',
  border: 'rgba(205, 201, 216, 1)',
  text: 'rgba(41, 38, 51, 1)',
} as const;

/**
 * Reads the palette currently in force on the document.
 *
 * Read from the document rather than passed in, because the theme is a class on the root element
 * and the same call therefore answers correctly in either theme without being told which is on.
 */
export function readChartPalette(): ChartPalette {
  const styles = getComputedStyle(document.documentElement);
  const paint = (token: string, fallback: string) =>
    convert(styles.getPropertyValue(token)) ?? fallback;

  return {
    series: [
      paint(TOKENS.series[0], FALLBACKS.series[0]),
      paint(TOKENS.series[1], FALLBACKS.series[1]),
    ],
    label: paint(TOKENS.label, FALLBACKS.label),
    line: paint(TOKENS.line, FALLBACKS.line),
    surface: paint(TOKENS.surface, FALLBACKS.surface),
    border: paint(TOKENS.border, FALLBACKS.border),
    text: paint(TOKENS.text, FALLBACKS.text),
  };
}

/**
 * Rewrites one colour into the notation the charting engine reads, or gives up on it.
 *
 * The colour is painted onto a single pixel and read back rather than taken from the fill
 * property directly. Asking the property returns whatever notation preserves the colour, which
 * for anything outside the sRGB gamut is `color(srgb …)` — accepted everywhere the browser paints
 * and understood nowhere in the charting engine, so a series would draw correctly while the
 * gradient beneath it silently failed. A pixel is four numbers and cannot be anything else.
 */
function convert(value: string): string | undefined {
  const colour = value.trim();

  if (colour.length === 0) {
    return undefined;
  }

  const context = document.createElement('canvas').getContext('2d', { willReadFrequently: true });

  if (!context) {
    return undefined;
  }

  context.fillStyle = SENTINEL;
  context.fillStyle = colour;

  if (context.fillStyle === SENTINEL) {
    return undefined;
  }

  context.clearRect(0, 0, 1, 1);
  context.fillRect(0, 0, 1, 1);

  const [red, green, blue, alpha] = context.getImageData(0, 0, 1, 1).data;

  return `rgba(${red}, ${green}, ${blue}, ${(alpha ?? 0) / 255})`;
}
