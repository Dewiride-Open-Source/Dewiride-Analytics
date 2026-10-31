import { LAYERS, UNSETTLED } from '@/lib/charts/frame';
import type { ChartPalette } from '@/lib/charts/palette';

/**
 * How the buckets that cannot be read at full strength are quietened on a drawing.
 *
 * Two kinds of bucket are quietened, for two different reasons. From the first bucket the engine
 * has not finished judging to the end of the run, because those figures may yet grow — which is a
 * fact about the figures, and is always shown. At either end, a week, a month or a year the period
 * cuts short, because the first week of "Last 90 days" may hold a single Sunday, and drawn at full
 * strength its figure reads as traffic collapsing rather than as a seventh of a week — which is an
 * aid to reading the picture, and is given up wherever nothing between the ends would be left at
 * full strength to read them against.
 *
 * By different means in each style, because a column and an area offer different things to
 * quieten. A column has a fill of its own and is faded one column at a time, which lands exactly
 * on the buckets it belongs to. An area has one fill for the whole run and none per bucket, so a
 * wash is laid over stretches of it instead — and that wash cannot be used on columns, because it
 * is placed against the points the labels sit at and would cut the first and last column of the
 * run down the middle.
 */

/** Which buckets of a run are drawn quietly, and why. */
export interface Quiet {
  /** Whether the first bucket is a week, a month or a year the period cuts short. */
  readonly cutAtStart: boolean;
  /** Whether the last bucket is a week, a month or a year the period cuts short. */
  readonly cutAtEnd: boolean;
  /** The first bucket still being judged, or nothing where every one is settled. */
  readonly judging: number | null;
}

/** A run with nothing quietened. */
export const UNQUIETENED: Quiet = { cutAtStart: false, cutAtEnd: false, judging: null };

/**
 * One figure as a column draws it: plain at full strength, faded where it is quietened, and nothing
 * where it has no figure — which draws no column at all.
 */
export type Quietened =
  | number
  | null
  | { readonly value: number; readonly itemStyle: { readonly opacity: number } };

/** A stretch of a run between two buckets' points, each counted from the first bucket. */
type Stretch = readonly [number, number];

/** Each figure faded where its bucket is quietened, for a drawing with a fill a bucket. */
export function quietened(values: readonly (number | null)[], quiet: Quiet): Quietened[] {
  const last = values.length - 1;
  const ends = fullColumnBetween(quiet, last);

  return values.map((value, bucket) =>
    value !== null && isQuiet(bucket, last, quiet, ends)
      ? { value, itemStyle: { opacity: UNSETTLED } }
      : value,
  );
}

/**
 * Whether a drawing quietens the weeks, months or years the period cuts short.
 *
 * Only where something between its quietened ends is still drawn at full strength to read them
 * against: a column, where the run is drawn as columns, and a stretch between two points, where it
 * is drawn as a line or an area. Two months both cut short have no column between them, and three
 * have nothing between their quietened points but the point of the middle month — quietened anyway,
 * either run would be faded from end to end, which reads as none of it faded at all.
 *
 * @param quiet Which buckets of the run are quietened.
 * @param buckets How many buckets the run has.
 * @param columns Whether the run is drawn as columns.
 */
export function quietensCutShort(quiet: Quiet, buckets: number, columns: boolean): boolean {
  if (!quiet.cutAtStart && !quiet.cutAtEnd) {
    return false;
  }

  return columns
    ? fullColumnBetween(quiet, buckets - 1)
    : cutShortStretches(quiet, buckets - 1).length > 0;
}

/**
 * The wash over the quietened stretches of a run, or nothing when nothing is quietened.
 *
 * A drawing placed at points draws each stretch between two points from both of them, so a wash
 * covers every stretch that touches a quietened point. Over the buckets still being judged it runs
 * from the last point drawn at full strength to the end of the run; begun at the first unsettled
 * point instead, it would have no width at all on the ordinary live period, whose only unsettled
 * bucket is the newest. Over a week, a month or a year the period cuts short it runs from that
 * bucket's point to its neighbour's, and only where something between the quietened ends is left at
 * full strength, as the clause under the picture is told.
 *
 * Stretches that meet or overlap are laid as one, so no part of the run is washed twice and drawn
 * fainter than the rest of what is quietened.
 *
 * Carried by a series of its own, at a depth of its own, so that it sits over the bands it is
 * quietening rather than behind them. It is the card's own background at part strength, so the
 * stretches read as faded rather than as a different colour that might mean something.
 */
export function washOver(
  labels: readonly string[],
  quiet: Quiet,
  palette: ChartPalette,
): Record<string, unknown> | undefined {
  const last = labels.length - 1;
  const stretches = laidAsOne([...cutShortStretches(quiet, last), ...judgingStretch(quiet, last)])
    .map(([from, to]) => [labels[from], labels[to]] as const)
    .filter((ends): ends is readonly [string, string] => ends.every((end) => end !== undefined));

  if (stretches.length === 0) {
    return undefined;
  }

  return {
    type: 'line',
    data: [],
    silent: true,
    markArea: {
      silent: true,
      itemStyle: { color: palette.surface, opacity: 0.62 },
      z: LAYERS.unsettled,
      data: stretches.map(([from, to]) => [{ xAxis: from }, { xAxis: to }]),
    },
  };
}

/**
 * Whether one bucket of a run drawn as columns is quietened.
 *
 * @param ends Whether the ends cut short are quietened at all, which they are only where a column
 * between them stays at full strength.
 */
function isQuiet(bucket: number, last: number, quiet: Quiet, ends: boolean): boolean {
  const cutShort = (quiet.cutAtStart && bucket === 0) || (quiet.cutAtEnd && bucket === last);

  return (ends && cutShort) || (quiet.judging !== null && bucket >= quiet.judging);
}

/**
 * Whether a column between the ends a period cuts short stays at full strength: one neither cut
 * short nor still being judged.
 */
function fullColumnBetween(quiet: Quiet, last: number): boolean {
  const from = quiet.cutAtStart ? 1 : 0;
  const settled = quiet.judging === null ? last : quiet.judging - 1;

  return from <= Math.min(quiet.cutAtEnd ? last - 1 : last, settled);
}

/**
 * The stretches a line or an area washes over the weeks, months or years the period cuts short — or
 * none, where washing them would leave nothing between the quietened ends at full strength.
 */
function cutShortStretches(quiet: Quiet, last: number): readonly Stretch[] {
  const fullFrom = quiet.cutAtStart ? 1 : 0;
  const fullTo = Math.min(quiet.cutAtEnd ? last - 1 : last, settledTo(quiet, last));

  if (fullFrom >= fullTo) {
    return [];
  }

  const leading: readonly Stretch[] = quiet.cutAtStart ? [[0, 1]] : [];
  const trailing: readonly Stretch[] = quiet.cutAtEnd ? [[last - 1, last]] : [];

  return [...leading, ...trailing];
}

/** The stretch washed over the buckets still being judged, or none where every one is settled. */
function judgingStretch(quiet: Quiet, last: number): readonly Stretch[] {
  return quiet.judging === null || last < 0 ? [] : [[settledTo(quiet, last), last]];
}

/** The last point drawn at full strength before the buckets still being judged. */
function settledTo(quiet: Quiet, last: number): number {
  return quiet.judging === null ? last : Math.max(quiet.judging - 1, 0);
}

/** Stretches that meet or overlap laid as one, in order along the run. */
function laidAsOne(stretches: readonly Stretch[]): readonly Stretch[] {
  const laid: Stretch[] = [];

  for (const [from, to] of [...stretches].sort((one, other) => one[0] - other[0])) {
    const previous = laid.at(-1);

    if (previous !== undefined && from <= previous[1]) {
      laid.pop();
      laid.push([previous[0], Math.max(previous[1], to)]);
    } else {
      laid.push([from, to]);
    }
  }

  return laid;
}
