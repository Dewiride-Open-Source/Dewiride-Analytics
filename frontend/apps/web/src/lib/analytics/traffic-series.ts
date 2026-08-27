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
  return series.buckets.map((_, bucket) =>
    series.groups.reduce((running, group) => running + (group.sessions[bucket] ?? 0), 0),
  );
}
