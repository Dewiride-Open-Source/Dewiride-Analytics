import { CATEGORY_TONES, TONE_ORDER, type VerdictTone } from '@/lib/analytics/verdicts';
import type { TrafficSeries } from '@/lib/api/schemas';

/**
 * Turning what the engine counted into what a chart draws.
 *
 * The engine answers in its own fourteen categories and always will, because a category is what a
 * verdict actually is and two of them that share a colour never share a name. A chart is glanced
 * at rather than read, so it draws the four tones those categories fall into — and the list under
 * the chart below still names every category exactly.
 */

/** One tone's visits across every bucket of a period, in the order the buckets are named. */
export interface TrafficBand {
  readonly tone: VerdictTone;
  readonly visits: readonly number[];
}

/**
 * The bands a period divides into, in reading order.
 *
 * A tone the period never held is left out rather than drawn flat along the axis, so a website
 * nobody has ever scraped carries no red band and no mention of one.
 */
export function bandsIn(series: TrafficSeries): readonly TrafficBand[] {
  const counted = new Map<VerdictTone, number[]>();

  for (const group of series.groups) {
    const tone = CATEGORY_TONES[group.category];
    const running = counted.get(tone) ?? series.buckets.map(() => 0);

    group.sessions.forEach((visits, at) => {
      running[at] = (running[at] ?? 0) + visits;
    });

    counted.set(tone, running);
  }

  return TONE_ORDER.flatMap((tone) => {
    const visits = counted.get(tone);

    return visits ? [{ tone, visits }] : [];
  });
}

/**
 * The first bucket that has not finished being judged, or nothing when every one of them has.
 *
 * A visit is judged once it has ended, so the buckets at the end of a period are still filling.
 * Drawn as though they were complete they read as a collapse in traffic, which is why they are
 * marked rather than either trusted or cut off — cutting them would give the two views different
 * axes, and switching between them would look like the period had changed.
 */
export function stillJudgingFrom(series: TrafficSeries): number | null {
  const settled = Date.parse(series.completeTo);

  for (let bucket = 0; bucket < series.buckets.length; bucket += 1) {
    const ends = Date.parse(series.buckets[bucket + 1] ?? series.to);

    if (ends > settled) {
      return bucket;
    }
  }

  return null;
}

/**
 * How many visits each bucket held, whatever generated them.
 *
 * Counted straight off the answer rather than off the bands, because the whole of a period is
 * wanted as a run of figures in its own right: it is what the table states beside each bucket,
 * and what an earlier period is drawn as when one is set behind this one.
 */
export function totalsIn(series: TrafficSeries): readonly number[] {
  return summed(series.buckets, series.groups, (group) => group.sessions);
}

/**
 * What the people a website is for did across a period: the visits judged to be theirs and the
 * pages those visits read, bucket by bucket.
 *
 * Nought in a bucket nobody was judged a person in, so an earlier period with no people draws as
 * none rather than as missing.
 */
export interface PeopleSeries {
  readonly visits: readonly number[];
  readonly pageViews: readonly number[];
}

/** The visits judged to be people and the pages those visits read, bucket by bucket. */
export function peopleIn(series: TrafficSeries): PeopleSeries {
  const people = series.groups.filter((group) => CATEGORY_TONES[group.category] === 'people');

  return {
    visits: summed(series.buckets, people, (group) => group.sessions),
    pageViews: summed(series.buckets, people, (group) => group.pageViews),
  };
}

/** One of the groups the engine answers with: a category and what it counted, bucket by bucket. */
type CategoryGroup = TrafficSeries['groups'][number];

/** One figure per bucket, added up across the groups from whichever run each of them carries. */
function summed(
  buckets: readonly string[],
  groups: readonly CategoryGroup[],
  counted: (group: CategoryGroup) => readonly number[],
): readonly number[] {
  return buckets.map((_, bucket) =>
    groups.reduce((running, group) => running + (counted(group)[bucket] ?? 0), 0),
  );
}
