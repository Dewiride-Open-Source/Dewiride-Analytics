import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { HEAT_CELLS, type HeatStep, heatStep } from '@/lib/analytics/heat';

/** Every step there is, nought included. */
const EVERY_STEP: readonly HeatStep[] = [0, 1, 2, 3, 4, 5];

const STYLES = readFileSync(
  path.join(import.meta.dirname, '..', '..', 'app', 'globals.css'),
  'utf8',
);

/** The least contrast WCAG allows between text this small and what it is written on. */
const SMALL_TEXT_CONTRAST = 4.5;

/** What one block of the stylesheet says, found by the words that open it. */
function blockOf(opening: string): string {
  const begins = STYLES.indexOf(`${opening} {`);

  expect(begins).toBeGreaterThanOrEqual(0);

  return STYLES.slice(begins, STYLES.indexOf('\n}', begins));
}

/** What a block of the stylesheet sets one of its colours to, read through any it refers to. */
function colourIn(block: string, name: string): readonly [number, number, number] {
  const set = new RegExp(`${name}:\\s*([^;]+);`).exec(block)?.[1]?.trim();
  const referred = /^var\((--[\w-]+)\)$/.exec(set ?? '')?.[1];

  if (referred !== undefined) {
    return colourIn(block, referred);
  }

  const [lightness, chroma, hue] = (/^oklch\(([\d.]+) ([\d.]+) ([\d.]+)\)$/.exec(set ?? '') ?? [])
    .slice(1)
    .map(Number);

  if (lightness === undefined || chroma === undefined || hue === undefined) {
    throw new Error(`${name} is not a colour this can read: ${set}`);
  }

  return [lightness, chroma, hue];
}

/** How much light a colour gives off on a screen, as WCAG measures it, from its oklch coordinates. */
function luminanceOf([lightness, chroma, hue]: readonly [number, number, number]): number {
  const a = chroma * Math.cos((hue * Math.PI) / 180);
  const b = chroma * Math.sin((hue * Math.PI) / 180);
  const l = (lightness + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m = (lightness - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s = (lightness - 0.0894841775 * a - 1.291485548 * b) ** 3;
  const inGamut = (channel: number) => Math.min(1, Math.max(0, channel));

  return (
    0.2126 * inGamut(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s) +
    0.7152 * inGamut(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s) +
    0.0722 * inGamut(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s)
  );
}

/** The WCAG contrast between two colours of a block. */
function contrastIn(block: string, one: string, other: string): number {
  const [lighter, darker] = [
    luminanceOf(colourIn(block, one)),
    luminanceOf(colourIn(block, other)),
  ].sort((x, y) => y - x);

  return ((lighter ?? 0) + 0.05) / ((darker ?? 0) + 0.05);
}

describe('how strongly a figure is shaded', () => {
  it('is nothing for nought', () => {
    expect(heatStep(0, 100)).toBe(0);
    expect(heatStep(0, 0)).toBe(0);
  });

  it('is the strongest step for the busiest', () => {
    expect(heatStep(100, 100)).toBe(5);
  });

  /** A month with one page view in it is not a month nobody read, however busy the best one was. */
  it('never falls below the first step above nought', () => {
    expect(heatStep(1, 1_000_000)).toBe(1);
  });

  /**
   * One good month would otherwise leave a growing website's early months all on one shade, when a
   * month ten times another is something the table should still show.
   */
  it('spreads the quieter figures over the quieter steps by their square root', () => {
    expect(heatStep(4, 100)).toBe(1);
    expect(heatStep(5, 100)).toBe(2);
    expect(heatStep(25, 100)).toBe(3);
    expect(heatStep(49, 100)).toBe(4);
    expect(heatStep(81, 100)).toBe(5);
  });

  it('goes no further than the strongest step, whatever a figure is shaded against', () => {
    expect(heatStep(200, 100)).toBe(5);
  });
});

/**
 * The shades are read from the stylesheet by name, and a name it does not define is a cell with no
 * background at all and white ink on a white card, in one theme and not the other.
 */
describe('the shades themselves', () => {
  it.each(EVERY_STEP)('writes step %i with the classes it defines', (step) => {
    expect(HEAT_CELLS[step]).toBe(`bg-heat-${step} text-heat-${step}-ink`);
  });

  it.each(EVERY_STEP)('is defined for both themes at step %i', (step) => {
    for (const theme of [':root', '.dark']) {
      expect(blockOf(theme)).toContain(`--heat-${step}:`);
      expect(blockOf(theme)).toContain(`--heat-${step}-ink:`);
    }

    expect(blockOf('@theme inline')).toContain(`--color-heat-${step}: var(--heat-${step});`);
    expect(blockOf('@theme inline')).toContain(
      `--color-heat-${step}-ink: var(--heat-${step}-ink);`,
    );
  });

  /**
   * The figures on these shades are 13 px in the table and 11 px on a phone's tiles, which is small
   * text however it is measured. Computed from the stylesheet itself, so a shade cannot be moved
   * without its ink being checked against it.
   */
  it.each(EVERY_STEP)(
    'keeps its ink readable against its shade at step %i, in both themes',
    (step) => {
      for (const theme of [':root', '.dark']) {
        expect(
          contrastIn(blockOf(theme), `--heat-${step}`, `--heat-${step}-ink`),
        ).toBeGreaterThanOrEqual(SMALL_TEXT_CONTRAST);
      }
    },
  );
});
