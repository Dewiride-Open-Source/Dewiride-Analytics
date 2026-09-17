import type { ChartPalette } from '@/lib/charts/palette';

/**
 * The drawing surface, stood in for.
 *
 * A chart is pixels on a canvas, which a test can neither read nor render — this document has no
 * canvas at all. So the builder a card hands the surface is run here instead, and what the chart
 * would have been told to draw is kept where a test can read it as an object.
 *
 * Written as a module rather than as a factory inside each test so that the palette is stated
 * once: a colour added to it would otherwise have to be added to every test that draws.
 */

/** A palette in the shape a real one has, with a different colour for every part of it. */
export const PALETTE: ChartPalette = {
  series: ['rgba(110, 76, 232, 1)', 'rgba(56, 168, 184, 1)'],
  tones: {
    people: 'rgba(31, 124, 75, 1)',
    automation: 'rgba(116, 86, 224, 1)',
    unwanted: 'rgba(188, 39, 41, 1)',
    unclear: 'rgba(136, 136, 146, 1)',
  },
  subtle: 'rgba(136, 136, 146, 1)',
  label: 'rgba(116, 113, 128, 1)',
  line: 'rgba(224, 222, 232, 1)',
  surface: 'rgba(255, 255, 255, 1)',
  border: 'rgba(205, 201, 216, 1)',
  text: 'rgba(41, 38, 51, 1)',
};

/** What the last chart to render would have drawn, and the way into each drawing a press was offered. */
export const drawn: {
  option: Record<string, unknown> | undefined;
  /** What the last surface would call with the category pressed, or nothing where no press means anything. */
  pick: ((index: number) => void) | undefined;
  /** The same for every drawing on the screen, by what each announces, since a screen draws several. */
  readonly picks: Map<string, ((index: number) => void) | undefined>;
} = { option: undefined, pick: undefined, picks: new Map() };

interface StubChartProps {
  readonly option: (palette: ChartPalette) => unknown;
  readonly label: string;
  readonly onPick?: (index: number) => void;
}

/**
 * Keeps what a render would have drawn, and what a press on it would do, where a test can reach
 * them.
 *
 * Called from the stand-in rather than written inside it: a component must not change anything
 * outside itself while it renders, and the rule that says so cannot tell a real one from a spy.
 */
function record(
  option: (palette: ChartPalette) => unknown,
  label: string,
  onPick: ((index: number) => void) | undefined,
): void {
  drawn.option = option(PALETTE) as Record<string, unknown>;
  drawn.pick = onPick;
  drawn.picks.set(label, onPick);
}

export function Chart({ option, label, onPick }: StubChartProps) {
  record(option, label, onPick);

  return <div role="img" aria-label={label} />;
}

/** The parts of the one series a ring draws, as the charting engine would have received them. */
export function ringParts(): readonly Record<string, unknown>[] {
  const series = drawn.option?.series as readonly Record<string, unknown>[] | undefined;
  const parts = series?.[0]?.data as readonly Record<string, unknown>[] | undefined;

  return parts ?? [];
}
