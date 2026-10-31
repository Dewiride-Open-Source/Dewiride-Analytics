import { describe, expect, it } from 'vitest';
import {
  bandsIn,
  judgedNoneOf,
  peopleIn,
  stillJudgingFrom,
  totalsIn,
} from '@/lib/analytics/traffic-series';
import type { TrafficPicture, TrafficSeries } from '@/lib/api/schemas';

const BUCKETS = [
  '2026-08-11T00:00:00+00:00',
  '2026-08-12T00:00:00+00:00',
  '2026-08-13T00:00:00+00:00',
];

function judged(groups: TrafficSeries['groups'], completeTo = '2026-08-14T00:00:00+00:00') {
  return {
    from: BUCKETS[0] ?? '',
    to: '2026-08-14T00:00:00+00:00',
    granularity: 'day',
    completeTo,
    buckets: BUCKETS,
    groups,
  } satisfies TrafficSeries;
}

describe('gathering categories into the four things a colour can mean', () => {
  /**
   * A crawler this product has confirmed and one that merely says it is that crawler are separate
   * categories everywhere they are named, and share one colour. Adding their counts together is
   * what the chart does; it never puts either name on the result.
   */
  it('adds up every category that shares a tone', () => {
    const bands = bandsIn(
      judged([
        { category: 'known-ai-crawler', sessions: [1, 2, 3], pageViews: [1, 2, 3] },
        { category: 'suspected-ai-crawler', sessions: [4, 0, 1], pageViews: [4, 0, 1] },
        { category: 'known-search-crawler', sessions: [0, 5, 0], pageViews: [0, 5, 0] },
      ]),
    );

    expect(bands).toStrictEqual([{ tone: 'automation', visits: [5, 7, 4] }]);
  });

  it('leaves out a tone the period never held rather than drawing it flat along the axis', () => {
    const bands = bandsIn(
      judged([{ category: 'likely-human', sessions: [3, 1, 2], pageViews: [9, 1, 4] }]),
    );

    expect(bands.map((band) => band.tone)).toStrictEqual(['people']);
  });

  /** The people a website is for sit on the axis, so their band has a straight edge to read. */
  it('puts the bands in reading order however the engine happened to order its categories', () => {
    const bands = bandsIn(
      judged([
        { category: 'unknown', sessions: [1, 1, 1], pageViews: [1, 1, 1] },
        { category: 'content-scraper', sessions: [2, 2, 2], pageViews: [2, 2, 2] },
        { category: 'likely-human', sessions: [3, 3, 3], pageViews: [3, 3, 3] },
        { category: 'generic-web-crawler', sessions: [4, 4, 4], pageViews: [4, 4, 4] },
      ]),
    );

    expect(bands.map((band) => band.tone)).toStrictEqual([
      'people',
      'automation',
      'unwanted',
      'unclear',
    ]);
  });

  it('has no bands at all for a period nothing has been judged in', () => {
    expect(bandsIn(judged([]))).toStrictEqual([]);
  });
});

describe('what a bucket held altogether', () => {
  it('counts every visit in it, whatever generated it', () => {
    const series = judged([
      { category: 'likely-human', sessions: [3, 1, 2], pageViews: [9, 1, 4] },
      { category: 'known-ai-crawler', sessions: [1, 4, 0], pageViews: [1, 4, 0] },
    ]);

    expect(totalsIn(series)).toStrictEqual([4, 5, 2]);
  });

  /** A period nothing has been judged in still has its days, and each of them held nothing. */
  it('is nought in every bucket of a period nothing has been judged in', () => {
    expect(totalsIn(judged([]))).toStrictEqual([0, 0, 0]);
  });
});

/**
 * A month of all time that was never counted is nothing in every category, and stays nothing in
 * everything worked out from it: a month nobody counted is not a month nobody came in.
 */
describe('a month never counted', () => {
  const RECORD: TrafficPicture = {
    from: BUCKETS[0] ?? '',
    to: '2026-08-14T00:00:00+00:00',
    granularity: 'month',
    completeTo: '2026-08-14T00:00:00+00:00',
    buckets: BUCKETS,
    missing: ['2026-08'],
    unjudged: [],
    groups: [
      { category: 'likely-human', sessions: [3, null, 2], pageViews: [9, null, 4] },
      { category: 'known-ai-crawler', sessions: [1, null, 0], pageViews: [1, null, 0] },
    ],
  };

  it('is nothing in every band', () => {
    expect(bandsIn(RECORD)).toStrictEqual([
      { tone: 'people', visits: [3, null, 2] },
      { tone: 'automation', visits: [1, null, 0] },
    ]);
  });

  it('is nothing in the whole, and among the people', () => {
    expect(totalsIn(RECORD)).toStrictEqual([4, null, 2]);
    expect(peopleIn(RECORD)).toStrictEqual([3, null, 2]);
  });
});

describe('the visits judged to be people', () => {
  it('counts only the visits judged to be people', () => {
    const series = judged([
      { category: 'likely-human', sessions: [3, 1, 2], pageViews: [9, 1, 4] },
      { category: 'known-ai-crawler', sessions: [1, 4, 0], pageViews: [1, 6, 0] },
    ]);

    expect(peopleIn(series)).toStrictEqual([3, 1, 2]);
  });

  /**
   * A run of noughts rather than nothing at all. Set behind a period as the one before, a run
   * that is missing draws as a line that never started, and a period nobody came to is a fact
   * about that period rather than a gap in the record.
   */
  it('is nought in every bucket where nobody was judged to be a person', () => {
    const series = judged([
      { category: 'known-search-crawler', sessions: [2, 1, 3], pageViews: [2, 1, 3] },
      { category: 'content-scraper', sessions: [0, 1, 0], pageViews: [0, 4, 0] },
    ]);

    expect(peopleIn(series)).toStrictEqual([0, 0, 0]);
  });

  it('is nought across a period nothing has been judged in', () => {
    expect(peopleIn(judged([]))).toStrictEqual([0, 0, 0]);
  });
});

describe('the buckets that have not finished being judged', () => {
  const SOME: TrafficSeries['groups'] = [
    { category: 'likely-human', sessions: [1, 1, 1], pageViews: [1, 1, 1] },
  ];

  it('finds none where judging has caught up with the end of the period', () => {
    expect(stillJudgingFrom(judged(SOME))).toBeNull();
  });

  /**
   * A bucket counts as still filling the moment any part of it is beyond where judging has got —
   * not when it begins there. Judging that stopped mid-afternoon leaves the whole of that day
   * short, and drawn as though it were complete the day reads as a collapse.
   */
  it('marks a bucket whose end is beyond where judging has got', () => {
    expect(stillJudgingFrom(judged(SOME, '2026-08-13T09:00:00+00:00'))).toBe(2);
  });

  it('marks every bucket where judging has not started on any of them', () => {
    expect(stillJudgingFrom(judged(SOME, '2026-08-10T00:00:00+00:00'))).toBe(0);
  });

  /**
   * The edge is found on any run of buckets that says how far it is complete, so an answer
   * counted from activity kept to people is washed on exactly the terms the judged one is.
   */
  it('finds where the judging stops on any run of buckets', () => {
    const bucketed = {
      to: '2026-08-14T00:00:00+00:00',
      completeTo: '2026-08-13T09:00:00+00:00',
      buckets: BUCKETS,
    };

    expect(stillJudgingFrom(bucketed)).toBe(2);
  });
});

/**
 * Judging that has reached none of a period is a different fact from a period whose newest bucket
 * is still filling: a period with one bucket under way has had everything judged but its last half
 * hour.
 */
describe('a period not one bucket of which has settled', () => {
  /** Where the picture is three days long, how far judging has reached into it, and what it says. */
  const settledIn = (completeTo: string) => {
    const answer = judged([], completeTo);

    return judgedNoneOf(answer, answer.buckets.length, stillJudgingFrom(answer));
  };

  it('is one whose answer is complete to no later than where it opens', () => {
    expect(settledIn(BUCKETS[0] ?? '')).toBe(true);
    expect(settledIn('2026-08-10T23:30:00+00:00')).toBe(true);
  });

  /**
   * Judging stalled six hours into the first of three days has settled none of them, which the
   * line under the picture says of the whole period rather than of "the newest" (0054).
   */
  it('is one whose judging stopped inside its first bucket', () => {
    expect(settledIn('2026-08-11T06:00:00+00:00')).toBe(true);
  });

  it('is not one whose first bucket has settled', () => {
    expect(settledIn('2026-08-12T00:00:00+00:00')).toBe(false);
    expect(settledIn('2026-08-13T06:00:00+00:00')).toBe(false);
  });

  /** All time's first month: one bucket, still running, judged to half an hour ago. */
  it('is not a period whose only bucket is still running and judged most of the way', () => {
    const month = {
      from: '2026-10-11T18:30:00.000Z',
      to: '2026-10-30T10:00:00.000Z',
      completeTo: '2026-10-30T09:30:00.000Z',
      buckets: ['2026-09-30T18:30:00.000Z'],
    };

    expect(stillJudgingFrom(month)).toBe(0);
    expect(judgedNoneOf(month, 1, 0)).toBe(false);
  });

  it('is a period of one bucket that judging has reached none of', () => {
    const month = {
      from: '2026-10-11T18:30:00.000Z',
      completeTo: '2026-10-11T18:30:00.000Z',
    };

    expect(judgedNoneOf(month, 1, 0)).toBe(true);
  });
});
