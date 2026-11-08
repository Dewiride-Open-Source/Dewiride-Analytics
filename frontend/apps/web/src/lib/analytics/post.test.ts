import { describe, expect, it } from 'vitest';
import {
  beforeOfPost,
  leftOutOf,
  postBeginsOn,
  postCalled,
  postHighlights,
  postOpensOn,
  postScreen,
  postShown,
  postViewsSince,
  readPostPath,
  recordPicture,
} from '@/lib/analytics/post';
import { ApiError } from '@/lib/api/problem';
import type { PostInsights } from '@/lib/api/schemas';

const TO = '2026-11-07T09:30:00+00:00';

/** One month as the engine answers it. */
function month(written: string, pageViews: number | null, daysMeasured: number) {
  return { month: written, pageViews, daysMeasured };
}

/** A post's record: published in March, read in no month unless a test says so. */
function record(overrides: Partial<PostInsights> = {}): PostInsights {
  return {
    post: {
      path: '/hello-world/',
      address: 'https://example.com/hello-world/',
      title: 'Hello, world',
      published: '2026-03-12',
      pageViews: 0,
    },
    viewsFrom: '2025-11',
    firstDay: '2025-11-19',
    from: '2026-03-12',
    to: TO,
    completeTo: TO,
    months: [month('2026-03', 0, 20), month('2026-04', 0, 30)],
    days: [],
    ...overrides,
  };
}

/** The same post, read as often as a test says. */
function read(pageViews: number, overrides: Partial<PostInsights> = {}): PostInsights {
  const answer = record(overrides);

  return { ...answer, post: { ...answer.post, pageViews } };
}

describe('the address of one post’s screen', () => {
  it('names the post, and nothing else under everybody', () => {
    expect(postScreen('/hello-world/', 'everybody')).toBe(
      '/app/insights/post?path=%2Fhello-world%2F',
    );
  });

  it('names the people beside the post', () => {
    expect(postScreen('/hello-world/', 'people')).toBe(
      '/app/insights/post?path=%2Fhello-world%2F&only=people',
    );
  });

  /** The post's screen leads back to the page of the list it was opened from. */
  it('names the page of the list of every post it is opened from, as the list writes it', () => {
    const address = postScreen('/hello-world/', 'people', 'search=a+b%26c&order=newest&page=3');
    const asked = new URLSearchParams(address.slice(address.indexOf('?') + 1));

    expect(address.split('?')).toHaveLength(2);
    expect([...asked.entries()]).toStrictEqual([
      ['path', '/hello-world/'],
      ['list', 'search=a+b%26c&order=newest&page=3'],
      ['only', 'people'],
    ]);
  });

  it('names the first page of the list as it opens, as nothing written', () => {
    expect(postScreen('/hello-world/', 'everybody', '')).toBe(
      '/app/insights/post?path=%2Fhello-world%2F&list=',
    );
  });

  /** A path is written by whoever wrote the website, and may hold anything an address can. */
  it.each(['/a?b=c&d=e/', '/caf%C3%A9/', '/one+two/', '/hash#fragment/', '/?only=people'])(
    'writes one question mark however the path is written, and reads %j back exactly',
    (path) => {
      const address = postScreen(path, 'people');
      const asked = new URLSearchParams(address.slice(address.indexOf('?') + 1));

      expect(address.split('?')).toHaveLength(2);
      expect(asked.get('path')).toBe(path);
      expect(asked.getAll('only')).toEqual(['people']);
    },
  );
});

describe('a post read back out of an address', () => {
  /** Every path a post is listed under begins at the root of its website. */
  it.each([null, '', 'hello-world', 'https://example.com/hello-world/', '%2Fhello-world%2F'])(
    'is nothing where the address names no post, as with %j',
    (written) => {
      expect(readPostPath(written)).toBeNull();
    },
  );

  it.each(['/hello-world/', '/caf%C3%A9/', '/', '/a b+c/'])('is %j as it was written', (path) => {
    expect(readPostPath(path)).toBe(path);
  });
});

describe('what a post is called', () => {
  it('is its title', () => {
    expect(postCalled({ title: 'Hello, world', path: '/hello-world/' })).toBe('Hello, world');
  });

  it('is the address it is read at where its page gave no title, written to be read', () => {
    expect(postCalled({ title: null, path: '/caf%C3%A9/' })).toBe('/café/');
  });
});

describe('the months a post’s figures leave out', () => {
  it('names every month never counted for it, and none as kept before it was judged', () => {
    expect(
      leftOutOf(
        record({
          months: [month('2026-01', null, 0), month('2026-02', 4, 28), month('2026-03', null, 0)],
        }),
      ),
    ).toEqual({ missing: ['2026-01', '2026-03'], unjudged: [] });
  });
});

describe('what stands on the screen about one post', () => {
  const REFUSED = new Error('refused');
  const NOT_THERE = new ApiError(404, { title: 'Not Found', status: 404 }, 'Not Found');
  const nothingYet = { data: undefined, error: null };
  const refused = { data: undefined, error: REFUSED };
  const notThere = { data: undefined, error: NOT_THERE };
  const views = read(12);
  const nought = record();
  const leavingOut = record({ months: [month('2026-03', null, 0), month('2026-04', 0, 30)] });

  /** Nothing is asked about an address that names no post, so nothing can be waited for. */
  it('says the post cannot be found where the address names none, whatever the answers are doing', () => {
    expect(postShown(null, nothingYet, nothingYet)).toEqual({ state: 'missing' });
    expect(postShown(null, refused, { data: views, error: null })).toEqual({ state: 'missing' });
  });

  it('says the post cannot be found where either answer says so', () => {
    expect(postShown('/hello-world/', nothingYet, notThere)).toEqual({ state: 'missing' });
    expect(postShown('/hello-world/', notThere, { data: nought, error: null })).toEqual({
      state: 'missing',
    });
    expect(postShown('/hello-world/', nothingYet, { data: views, error: NOT_THERE })).toEqual({
      state: 'missing',
    });
  });

  it('waits for the screen’s own answer', () => {
    expect(postShown('/hello-world/', nothingYet, nothingYet)).toEqual({ state: 'waiting' });
    expect(postShown('/hello-world/', { data: views, error: null }, nothingYet)).toEqual({
      state: 'waiting',
    });
  });

  /** A refused answer is not on its way, so nothing goes on standing in for it. */
  it('says its own answer was refused, rather than waiting for one that is not coming', () => {
    expect(postShown('/hello-world/', nothingYet, refused)).toEqual({
      state: 'refused',
      error: REFUSED,
    });
  });

  it('shows the record as soon as it has one, whatever everybody’s answer is doing', () => {
    expect(postShown('/hello-world/', nothingYet, { data: views, error: null })).toEqual({
      state: 'ready',
      answer: views,
    });
    expect(postShown('/hello-world/', refused, { data: views, error: null })).toEqual({
      state: 'ready',
      answer: views,
    });
  });

  it('keeps an answer it has read on the screen when asking again fails', () => {
    expect(postShown('/hello-world/', nothingYet, { data: views, error: REFUSED })).toEqual({
      state: 'ready',
      answer: views,
    });
  });

  /** Nought is only what was counted there, and the record says which months were not. */
  it('shows a record that comes to nought with months left out, rather than calling it unread', () => {
    expect(postShown('/hello-world/', nothingYet, { data: leavingOut, error: null })).toEqual({
      state: 'ready',
      answer: leavingOut,
    });
  });

  it('settles nothing about nought until everybody’s answer says who read the post', () => {
    expect(postShown('/hello-world/', nothingYet, { data: nought, error: null })).toEqual({
      state: 'waiting',
    });
  });

  it('says it was refused where nought cannot be told apart without everybody’s answer', () => {
    expect(postShown('/hello-world/', refused, { data: nought, error: null })).toEqual({
      state: 'refused',
      error: REFUSED,
    });
  });

  it('calls nought among people absent where somebody read the post', () => {
    expect(
      postShown('/hello-world/', { data: views, error: null }, { data: nought, error: null }),
    ).toEqual({ state: 'absent', answer: nought });
  });

  it('calls nought in everybody’s answer too a post nobody has read', () => {
    expect(
      postShown('/hello-world/', { data: nought, error: null }, { data: nought, error: null }),
    ).toEqual({ state: 'unread', answer: nought });
  });
});

describe('a post’s all-time highlights', () => {
  /** Months counted from the reports can add up to a little more than the post's own figure. */
  it('takes its views from the post, as Insights does', () => {
    const answer = read(100, { months: [month('2026-03', 60, 20), month('2026-04', 45, 30)] });

    expect(postHighlights(answer).views).toBe(100);
  });

  /** Σ views over Σ days, as a year of the table reads, leaving a month never counted out of both. */
  it('averages over the days its months with figures cover', () => {
    const answer = read(30, {
      months: [month('2026-02', 10, 5), month('2026-03', null, 0), month('2026-04', 20, 10)],
    });

    expect(postHighlights(answer).perDay).toBe(2);
  });

  it('has no average where no month has a figure', () => {
    const answer = record({ months: [month('2026-03', null, 0), month('2026-04', null, 0)] });

    expect(postHighlights(answer).perDay).toBeNull();
  });

  it('names the month read most, the earliest of a tie', () => {
    const answer = read(140, {
      months: [
        month('2026-01', 20, 31),
        month('2026-02', 50, 28),
        month('2026-03', 50, 31),
        month('2026-04', 20, 30),
      ],
    });

    expect(postHighlights(answer).bestMonth).toEqual({
      month: '2026-02',
      pageViews: 50,
      running: false,
    });
  });

  it('says the month read most is still running where it is the month running now', () => {
    const answer = read(70, { months: [month('2026-03', 20, 20), month('2026-04', 50, 7)] });

    expect(postHighlights(answer).bestMonth).toEqual({
      month: '2026-04',
      pageViews: 50,
      running: true,
    });
  });

  it('names no best month where no month was read', () => {
    expect(postHighlights(record()).bestMonth).toBeNull();
  });
});

describe('what the months before a post’s first are called', () => {
  it('calls them not yet published where it was published in its first month', () => {
    expect(beforeOfPost(record(), '2026-02')).toBe('unpublished');
  });

  /** Re-dated later than it was first read, its months begin before it says it was published. */
  it('calls them not yet published where it was read before it says it was published', () => {
    const answer = record({ months: [month('2026-02', 3, 28), month('2026-03', 0, 31)] });

    expect(beforeOfPost(answer, '2026-01')).toBe('unpublished');
  });

  it('calls them not measured where it was published before its views were counted', () => {
    const answer = record({
      post: { ...record().post, published: '2024-06-01' },
      months: [month('2025-11', 3, 12), month('2025-12', 0, 31)],
    });

    expect(beforeOfPost(answer, '2025-10')).toBe('unmeasured');
  });

  it('calls them months of no views where an undated post was first read after its views began to be counted', () => {
    const answer = record({ post: { ...record().post, published: null } });

    expect(beforeOfPost(answer, '2026-02')).toBe('unread');
  });

  /** Nothing was counted before its views were, so nothing says whether anybody read it then. */
  it('calls those before an undated post’s views were counted not measured, and those since months of no views', () => {
    const answer = record({ post: { ...record().post, published: null }, viewsFrom: '2026-02' });

    expect(beforeOfPost(answer, '2026-01')).toBe('unmeasured');
    expect(beforeOfPost(answer, '2026-02')).toBe('unread');
  });

  /** Nothing was counted before its first month, so nothing says nobody read it then. */
  it('calls them not measured where an undated post’s months begin with the first its views are counted in', () => {
    const answer = record({
      post: { ...record().post, published: null },
      months: [month('2025-11', 3, 12), month('2025-12', 0, 31)],
    });

    expect(beforeOfPost(answer, '2025-10')).toBe('unmeasured');
  });
});

describe('since when a post’s views are said to be counted', () => {
  it('names the month they are counted from where it was published before then', () => {
    expect(postViewsSince(record({ post: { ...record().post, published: '2024-06-01' } }))).toBe(
      '2025-11',
    );
  });

  it('names nothing where it was published once they were counted', () => {
    expect(postViewsSince(record())).toBeNull();
  });

  it('names the month they are counted from where an undated post’s website was measured before then', () => {
    const answer = record({ post: { ...record().post, published: null }, firstDay: '2025-06-03' });

    expect(postViewsSince(answer)).toBe('2025-11');
  });

  it.each([
    ['an undated post counted from its website’s first month', { firstDay: '2025-11-19' }],
    ['a record with no month to count in', { viewsFrom: null }],
  ])('names nothing for %s', (_, overrides) => {
    const answer = record({
      post: { ...record().post, published: null },
      viewsFrom: '2025-11',
      firstDay: '2025-11-03',
      ...overrides,
    });

    expect(postViewsSince(answer)).toBeNull();
  });
});

/**
 * All time's picture of a post is drawn from the months of its record, in Kolkata, which is five and
 * a half hours ahead of the engine's clock: each month and each year begins the evening before there.
 */
describe('all time’s picture of a post', () => {
  const KOLKATA = 'Asia/Kolkata';

  /** A post first counted on 12 March 2025, whose website was measured long before it. */
  const ACROSS_A_NEW_YEAR = record({
    firstDay: '2024-11-19',
    from: '2025-03-12',
    months: [
      month('2025-03', 120, 20),
      month('2025-04', null, 0),
      month('2025-05', 0, 31),
      month('2026-01', 40, 31),
      month('2026-02', null, 0),
    ],
  });

  it('lays a post’s months out as the buckets of all time', () => {
    expect(recordPicture(ACROSS_A_NEW_YEAR, 'month', KOLKATA)?.points).toStrictEqual([
      { bucketStart: '2025-02-28T18:30:00.000Z', value: 120 },
      { bucketStart: '2025-03-31T18:30:00.000Z', value: null },
      { bucketStart: '2025-04-30T18:30:00.000Z', value: 0 },
      { bucketStart: '2025-12-31T18:30:00.000Z', value: 40 },
      { bucketStart: '2026-01-31T18:30:00.000Z', value: null },
    ]);
  });

  /** A year adds up what its months were counted at, as the table of them does. */
  it('adds a year’s months up and leaves a year with no figure nothing', () => {
    const answer = record({
      from: '2024-12-02',
      months: [
        month('2024-12', null, 0),
        month('2025-01', 30, 31),
        month('2025-02', null, 0),
        month('2025-03', 12, 31),
      ],
    });

    expect(recordPicture(answer, 'year', KOLKATA)?.points).toStrictEqual([
      { bucketStart: '2023-12-31T18:30:00.000Z', value: null },
      { bucketStart: '2024-12-31T18:30:00.000Z', value: 42 },
    ]);
  });

  /** The website's first day is long before; the picture opens on the post's own. */
  it('opens at the midnight the post’s first day begins, and runs to where its record was read', () => {
    const picture = recordPicture(
      { ...ACROSS_A_NEW_YEAR, completeTo: '2026-11-07T09:00:00+00:00' },
      'year',
      KOLKATA,
    );

    expect(picture?.from).toBe('2025-03-11T18:30:00.000Z');
    expect(picture?.to).toBe(TO);
    expect(picture?.completeTo).toBe('2026-11-07T09:00:00+00:00');
  });

  it('is nothing where the record has no first day to open on', () => {
    expect(recordPicture(record({ from: null, months: [] }), 'month', KOLKATA)).toBeUndefined();
  });
});

/** A post published on Tuesday 3 November, its record beginning that day. */
const PUBLISHED_TUESDAY = record({
  post: { ...record().post, published: '2026-11-03' },
  from: '2026-11-03',
  months: [month('2026-11', 412, 5)],
  days: [
    { day: '2026-11-01', pageViews: 0 },
    { day: '2026-11-02', pageViews: 0 },
    { day: '2026-11-03', pageViews: 260 },
  ],
});

describe('the day a post began, where no day before it can have held one of its views', () => {
  it('is the day a dated post was published, where its record begins on it', () => {
    expect(postBeginsOn(PUBLISHED_TUESDAY)).toBe('2026-11-03');
  });

  /** A day before the website was measured holds nothing, and says nothing about the post. */
  it('is that day past days before its website was measured', () => {
    const measuredLate = {
      ...PUBLISHED_TUESDAY,
      days: [{ day: '2026-10-31', pageViews: null }, ...PUBLISHED_TUESDAY.days],
    };

    expect(postBeginsOn(measuredLate)).toBe('2026-11-03');
  });

  /** Nothing says when it began, so any day before its record begins may have held its views. */
  it('is nothing for a post that says no date', () => {
    const undated = { ...PUBLISHED_TUESDAY, post: { ...PUBLISHED_TUESDAY.post, published: null } };

    expect(postBeginsOn(undated)).toBeNull();
  });

  /** Published before its views were counted, it may have been read on any day before then. */
  it('is nothing for a post whose record begins later than it was published', () => {
    const countedLater = record({ post: { ...record().post, published: '2024-06-01' } });

    expect(postBeginsOn(countedLater)).toBeNull();
  });

  it('is nothing for a record with no day to begin on', () => {
    expect(postBeginsOn(record({ from: null, months: [] }))).toBeNull();
  });

  /** Dated later than it was first read: the date it gives never sets aside a view of it. */
  it('is nothing for a post read on a recent day before the day it gives', () => {
    const readSunday = {
      ...PUBLISHED_TUESDAY,
      days: [
        { day: '2026-11-01', pageViews: 4 },
        { day: '2026-11-02', pageViews: 0 },
        { day: '2026-11-03', pageViews: 260 },
      ],
    };

    expect(postBeginsOn(readSunday)).toBeNull();
  });
});

describe('where a picture of a post over a period opens', () => {
  const LAST_30_DAYS = { first: '2026-10-09', last: '2026-11-07' };

  /** A day before the post was published is not a day nobody read it. */
  it('opens on the day the post was published, where the period begins before it', () => {
    expect(postOpensOn(PUBLISHED_TUESDAY, LAST_30_DAYS)).toBe('2026-11-03');
  });

  /** The screen then says so, rather than asking about days nobody could have read it on. */
  it('names that day for a period that ends before it as well', () => {
    expect(postOpensOn(PUBLISHED_TUESDAY, { first: '2026-10-01', last: '2026-10-31' })).toBe(
      '2026-11-03',
    );
  });

  it.each([
    [
      'a period that begins on the day it was published',
      { first: '2026-11-03', last: '2026-11-07' },
    ],
    ['a period that begins after it', { first: '2026-11-07', last: '2026-11-07' }],
  ])('opens with the period for %s', (_, span) => {
    expect(postOpensOn(PUBLISHED_TUESDAY, span)).toBeNull();
  });

  /** Any day before its record begins may have held its views, and is drawn as it was counted. */
  it.each([
    [
      'a post that says no date',
      { post: { ...PUBLISHED_TUESDAY.post, published: null }, from: '2026-11-01' },
    ],
    [
      'a post published before its views were counted',
      { post: { ...PUBLISHED_TUESDAY.post, published: '2024-06-01' }, from: '2026-11-01' },
    ],
    ['a post with no months in its record', { from: null, months: [] }],
  ])('opens with the period for %s', (_, overrides) => {
    expect(postOpensOn({ ...PUBLISHED_TUESDAY, ...overrides }, LAST_30_DAYS)).toBeNull();
  });

  /** The date a post gives never keeps a view of it off the picture. */
  it('opens with the period for a post read before the day it gives', () => {
    const readEarlier = {
      ...PUBLISHED_TUESDAY,
      days: PUBLISHED_TUESDAY.days.map((day) =>
        day.day === '2026-11-01' ? { ...day, pageViews: 4 } : day,
      ),
    };

    expect(postOpensOn(readEarlier, LAST_30_DAYS)).toBeNull();
  });

  /** A day before the website was measured holds nothing, and says nothing about the post. */
  it('opens on the day it was published past days before its website was measured', () => {
    const measuredLate = {
      ...PUBLISHED_TUESDAY,
      days: [{ day: '2026-10-31', pageViews: null }, ...PUBLISHED_TUESDAY.days],
    };

    expect(postOpensOn(measuredLate, LAST_30_DAYS)).toBe('2026-11-03');
  });
});
