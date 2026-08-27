import { describe, expect, it } from 'vitest';
import { bandsIn, stillJudgingFrom, totalsIn } from '@/lib/analytics/traffic-series';
import type { TrafficSeries } from '@/lib/api/schemas';

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
});
