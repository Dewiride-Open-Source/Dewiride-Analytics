import { judgedNoneOf, stillJudgingFrom } from '@/lib/analytics/traffic-series';
import type { AllTimeTraffic, TrafficPicture } from '@/lib/api/schemas';

/**
 * Which of three different facts it is when a screen kept to people has none to show.
 *
 * "Nobody was a person", "nobody has been looked at yet" and "nobody so far, with more still to look
 * at" are told apart on the overview and on Insights, and each says them in its own words. A website's
 * whole record runs up to the present, which is always still being judged, so Insights never has the
 * first to say.
 */

/** What a stretch held of each kind of visit judged, as it is being asked for. */
export interface Breakdown {
  readonly data: { readonly groups: readonly unknown[] } | undefined;
  readonly error: Error | null;
}

/** Where a picture says judging has reached. */
export interface JudgingReach {
  /** The first bucket still being judged, or nothing where every one has settled. */
  readonly stillJudging: number | null;
  /** Whether not one bucket of the period has settled, as `judgedNoneOf` says. */
  readonly none: boolean;
}

/**
 * Where a picture of who came says judging has reached.
 *
 * @param who Who came, bucket by bucket.
 */
export function reachedBy(who: TrafficPicture): JudgingReach {
  const stillJudging = stillJudgingFrom(who);

  return { stillJudging, none: judgedNoneOf(who, who.buckets.length, stillJudging) };
}

/**
 * All time's answer about who came, as the one thing the absent state asks of it: which kinds of
 * visit were judged in any month at all. A month never counted, or one that held none of a kind,
 * says nothing of it.
 *
 * @param answer Who came across all time, as it is being asked for.
 */
export function everJudged(answer: {
  readonly data: AllTimeTraffic | undefined;
  readonly error: Error | null;
}): Breakdown {
  return {
    data: answer.data && {
      groups: answer.data.groups.filter((group) =>
        group.sessions.some((sessions) => sessions !== null && sessions > 0),
      ),
    },
    error: answer.error,
  };
}

/**
 * Which of the absent states a stretch is, and the sentence that explains it.
 *
 * Settled first: a stretch judged to the end has its answer whether or not anything in it was
 * judged — a visit that began before the stretch and was judged there leaves its own breakdown
 * empty — and only a stretch still being judged waits.
 *
 * @param judged Whether anything in the stretch has been judged.
 * @param reached Where the picture says judging has reached.
 * @returns The state's name and the key of its sentence.
 */
export function absence(
  judged: boolean,
  { stillJudging, none }: JudgingReach,
): {
  readonly reason: 'unjudged' | 'nobody' | 'nobodyYet';
  readonly body: 'unjudged.body' | 'unjudged.bodyAll' | 'nobody.body' | 'nobodyYet.body';
} {
  if (stillJudging === null) {
    return { reason: 'nobody', body: 'nobody.body' };
  }

  if (judged) {
    return { reason: 'nobodyYet', body: 'nobodyYet.body' };
  }

  return { reason: 'unjudged', body: none ? 'unjudged.bodyAll' : 'unjudged.body' };
}
