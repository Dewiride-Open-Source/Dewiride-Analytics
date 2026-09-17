import { describe, expect, it } from 'vitest';
import { bandsIn, peopleIn, stillJudgingFrom, totalsIn } from '@/lib/analytics/traffic-series';
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
