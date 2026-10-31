import { CATEGORY_TONES, TONE_ORDER, type VerdictTone } from '@/lib/analytics/verdicts';
import type { TrafficPicture } from '@/lib/api/schemas';

/**
 * Turning what the engine counted into what a chart draws.
 *
 * The engine answers in its own fourteen categories and always will, because a category is what a
 * verdict actually is and two of them that share a colour never share a name. A chart is glanced
 * at rather than read, so it draws the four tones those categories fall into — and the list under
 * the chart below still names every category exactly.
 *
 * A month of all time left out — never counted, or kept before its visits were all judged — is
 * nothing rather than nought in every category, and stays nothing in every figure worked out from
 * it: a gap in the drawing, not a month nobody came.
 */

/** One tone's visits across every bucket of a period, in the order the buckets are named. */
export interface TrafficBand {
  readonly tone: VerdictTone;
  /** What it counted in each bucket, or nothing for a month left out. */
  readonly visits: readonly (number | null)[];
}

/**
 * The bands a period divides into, in reading order.
 *
 * A tone the period never held is left out rather than drawn flat along the axis, so a website
 * nobody has ever scraped carries no red band and no mention of one.
 */
export function bandsIn(series: TrafficPicture): readonly TrafficBand[] {
  const counted = new Map<VerdictTone, (number | null)[]>();

  for (const group of series.groups) {
    const tone = CATEGORY_TONES[group.category];
    const running = counted.get(tone) ?? series.buckets.map((): number | null => 0);

    group.sessions.forEach((visits, at) => {
      running[at] = added(figureAt(running, at), visits);
    });

    counted.set(tone, running);
  }

  return TONE_ORDER.flatMap((tone) => {
    const visits = counted.get(tone);

    return visits ? [{ tone, visits }] : [];
  });
}

/** What any answer cut into buckets says, for the edge of the judging to be found on it. */
export interface Bucketed {
  /** The exclusive end of the period. */
  readonly to: string;
  /** The instant the answer is complete up to. */
  readonly completeTo: string;
  /** The start of each bucket, oldest first. */
  readonly buckets: readonly string[];
}

/**
 * The first bucket that has not finished being judged, or nothing when every one of them has.
 *
 * A visit is judged once it has ended, so the buckets at the end of a period are still filling.
 * Drawn as though they were complete they read as a collapse in traffic, which is why they are
 * marked rather than either trusted or cut off — cutting them would give the two views different
 * axes, and switching between them would look like the period had changed. The same edge is found
 * on an answer counted from activity kept to people, which is complete to the same instant.
 */
export function stillJudgingFrom(series: Bucketed): number | null {
  const settled = Date.parse(series.completeTo);

  for (let bucket = 0; bucket < series.buckets.length; bucket += 1) {
    const ends = Date.parse(series.buckets[bucket + 1] ?? series.to);

    if (ends > settled) {
      return bucket;
    }
  }

  return null;
}

/** What any answer says of where its period opens and how far it is complete. */
export interface Reached {
  /** Where the period opens. */
  readonly from: string;
  /** The instant the answer is complete up to. */
  readonly completeTo: string;
}

/**
 * Whether not one bucket of a period has settled, which is when the line under its picture says the
 * whole period is still being judged: its first bucket is still being judged.
 *
 * A period of one bucket is the exception. Its bucket runs to now, so it is never settled while the
 * period is under way — a website's all time through its first month has had everything judged up
 * to the last half hour, and calling all of it still being judged would say none of it could be
 * read yet. It counts as judged none of only where judging has not reached where it opens, which is
 * where an engine stopped or still working through a backlog leaves it.
 *
 * @param answer Where the period opens and how far its answer is complete.
 * @param buckets How many buckets its picture has.
 * @param stillJudging The first bucket still being judged, or nothing where every one has settled.
 */
export function judgedNoneOf(
  answer: Reached,
  buckets: number,
  stillJudging: number | null,
): boolean {
  return (
    stillJudging === 0 && (buckets > 1 || Date.parse(answer.completeTo) <= Date.parse(answer.from))
  );
}

/**
 * How many visits each bucket held, whatever generated them.
 *
 * Counted straight off the answer rather than off the bands, because the whole of a period is
 * wanted as a run of figures in its own right: it is what the table states beside each bucket,
 * and what an earlier period is drawn as when one is set behind this one.
 */
export function totalsIn(series: TrafficPicture): readonly (number | null)[] {
  return summed(series.buckets, series.groups, (group) => group.sessions);
}

/**
 * The visits judged to be people, bucket by bucket.
 *
 * Nought in a bucket nobody was judged a person in, so an earlier period with no people draws as
 * none rather than as missing.
 */
export function peopleIn(series: TrafficPicture): readonly (number | null)[] {
  return summed(
    series.buckets,
    series.groups.filter((group) => CATEGORY_TONES[group.category] === 'people'),
    (group) => group.sessions,
  );
}

/** One of the groups the engine answers with: a category and what it counted, bucket by bucket. */
type CategoryGroup = TrafficPicture['groups'][number];

/** One figure per bucket, added up across the groups from whichever run each of them carries. */
function summed(
  buckets: readonly string[],
  groups: readonly CategoryGroup[],
  counted: (group: CategoryGroup) => readonly (number | null)[],
): readonly (number | null)[] {
  return buckets.map((_, bucket) =>
    groups.reduce<number | null>(
      (running, group) => added(running, figureAt(counted(group), bucket)),
      0,
    ),
  );
}

/** Two figures for one bucket added together, or nothing where either is left out. */
function added(one: number | null, other: number | null): number | null {
  return one === null || other === null ? null : one + other;
}

/**
 * The figure a run holds for one bucket: nought where the run does not reach that far, and nothing
 * where the bucket is left out.
 */
function figureAt(run: readonly (number | null)[], bucket: number): number | null {
  const figure = run[bucket];

  return figure === undefined ? 0 : figure;
}
