/**
 * How strongly a figure is shaded, from how it stands against the largest beside it.
 *
 * One violet, from close to the page to far from it: light to dark in the light theme, dark to light
 * in the dark one. So a table of months reads at a glance as where the reading was.
 * The classes are written out whole because the stylesheet is generated from the words it finds in
 * the source, and a class put together while the screen runs is a class nothing ever generated.
 */

/** How strongly a figure is shaded, from nothing at all to the most there is. */
export type HeatStep = 0 | 1 | 2 | 3 | 4 | 5;

/** The steps a figure above nought can fall on, quietest first. */
export const HEAT_STEPS = [1, 2, 3, 4, 5] as const;

/** Each step's shade, and the ink a figure is written in on it. */
export const HEAT_CELLS: Readonly<Record<HeatStep, string>> = {
  0: 'bg-heat-0 text-heat-0-ink',
  1: 'bg-heat-1 text-heat-1-ink',
  2: 'bg-heat-2 text-heat-2-ink',
  3: 'bg-heat-3 text-heat-3-ink',
  4: 'bg-heat-4 text-heat-4-ink',
  5: 'bg-heat-5 text-heat-5-ink',
};

/**
 * What a stretch with no figure wears: a hatching rather than any place on the scale, because nothing
 * says how much it held.
 */
export const LEFT_OUT_CELL =
  'bg-[repeating-linear-gradient(135deg,var(--surface-muted)_0_4px,transparent_4px_8px)] text-foreground-muted shadow-[inset_0_0_0_1px_var(--border)]';

/**
 * The step a figure falls on.
 *
 * Nought has a step of its own: a month nobody read is a different thing from a quiet one. Anything
 * above it stands on its square root against the largest, because one good month would otherwise
 * leave a growing website's early months all on one shade, and they still have something to say.
 *
 * @param value The figure.
 * @param largest The largest figure it is shaded beside.
 */
export function heatStep(value: number, largest: number): HeatStep {
  if (value <= 0 || largest <= 0) {
    return 0;
  }

  return HEAT_STEPS[Math.ceil(HEAT_STEPS.length * Math.sqrt(value / largest)) - 1] ?? 5;
}
