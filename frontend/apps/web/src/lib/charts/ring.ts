import { color, type EChartsCoreOption } from 'echarts/core';
import type { ChartPalette } from '@/lib/charts/palette';

/**
 * One part of a whole, ready to be drawn.
 *
 * The colour is a finished colour rather than a token, because the palette in force is only known
 * at the moment of drawing and is different in each theme.
 */
export interface RingSlice {
  /** What the part is called, in the reader's own language. */
  readonly name: string;
  /** How many it holds. */
  readonly value: number;
  /** The colour it is drawn in. */
  readonly colour: string;
}

/**
 * How one part of a whole is coloured, on the page and on the ring.
 *
 * Stated twice on purpose: the styling engine cannot see a class name worked out while the page is
 * running, and the charting engine cannot read a class name at all. Keeping the pair together is
 * what stops the dot beside a name drifting from the slice it stands for.
 */
export interface SlicePaint {
  /** The dot beside the name in the list, as a class the styling engine can see. */
  readonly fill: string;
  /** The slice on the ring, taken from the palette in force. */
  readonly colour: (palette: ChartPalette) => string;
}

/**
 * How wide the band is, as a share of the box it is drawn in.
 *
 * Open enough in the middle that the shape reads as a ring rather than as a pie. A pie asks the
 * eye to compare wedges by their angle at the centre, which is the comparison people are worst
 * at; a ring is compared by arc length, which they are far better at.
 */
const RADIUS = ['62%', '88%'] as const;

/** The gap held between parts, so two of a similar colour still read as two. */
const SEPARATION = 2;

/** What an empty ring is drawn as, so that it is still a ring. */
const NOTHING = 1;

/**
 * A whole divided into its parts.
 *
 * No axes, no labels on the drawing and nothing to hover over: every figure the ring stands for is
 * written out in the list beside it, which is what a screen reader is handed and what anybody
 * comparing two of them reads anyway. The ring is the shape of the answer; the list is the answer.
 *
 * Used only where the parts genuinely are the whole. A ranked slice of a long tail is not, and a
 * ring drawn over one would claim that the rows on screen are everything there was.
 */
export function ringOption(slices: readonly RingSlice[], palette: ChartPalette): EChartsCoreOption {
  const held = slices.filter((slice) => slice.value > 0);

  return {
    series: [
      {
        type: 'pie',
        radius: [...RADIUS],
        label: { show: false },
        labelLine: { show: false },
        silent: true,
        itemStyle: { borderColor: palette.surface, borderWidth: SEPARATION },
        data: held.length === 0 ? [closed(palette)] : held.map(drawPart),
      },
    ],
  };
}

/**
 * A second weight of the same colour.
 *
 * A closed set can hold more parts than the palette has colours for, and two weights of one hue
 * separate more surely than two hues close together — they differ in lightness as well, so the
 * ring still divides for a reader who cannot tell the hues apart.
 */
export function softened(colour: string): string {
  return color.modifyAlpha(colour, 0.5);
}

/** One part, drawn. */
function drawPart(slice: RingSlice): Record<string, unknown> {
  return { name: slice.name, value: slice.value, itemStyle: { color: slice.colour } };
}

/**
 * The ring a whole that came to nothing is drawn as.
 *
 * An unbroken band the colour of a rule, so a card whose parts all came to nought still looks
 * like the card it is instead of like a drawing that failed to arrive.
 */
function closed(palette: ChartPalette): Record<string, unknown> {
  return { name: '', value: NOTHING, itemStyle: { color: palette.line } };
}
