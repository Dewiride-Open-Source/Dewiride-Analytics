import { afterEach, describe, expect, it, vi } from 'vitest';
import { EVERY_JOURNEY } from '@/lib/analytics/journeys';
import type { Population } from '@/lib/analytics/people-only';
import {
  readActions,
  readAllTime,
  readAllTimeSeries,
  readAllTimeTraffic,
  readDevices,
  readEngagement,
  readFacets,
  readInsights,
  readLocations,
  readOverview,
  readPageEngagement,
  readPages,
  readSeries,
  readSitemapStatus,
  readSoftware,
  readSources,
  readTraffic,
  readTrafficSeries,
  readVisitPages,
  readVisits,
  readVisitTotals,
} from '@/lib/api/endpoints';
import { type Engine, engineAnswering, engineDoing, respondWith } from '@/test/engine';

afterEach(() => {
  vi.unstubAllGlobals();
});

const SITE = '01a013fa-49d6-77be-b65d-20ec86e9df78';
const FROM = '2026-08-11T00:00:00.000Z';
const TO = '2026-08-18T00:00:00.000Z';
const PERIOD = { from: FROM, to: TO };

const NO_VISITS = { from: FROM, to: TO, totalVisits: 0, visits: [] };

const NOTHING_HELD = {
  from: FROM,
  to: TO,
  devices: [],
  sourceKinds: [],
  browsers: [],
  systems: [],
  countries: [],
  towns: [],
  networks: [],
  sources: [],
  entryPages: [],
};

/**
 * Honest empty answers to every question about a period's activity, by the address it was sent
 * to, so the question itself is what a test reads.
 */
const NOTHING_RECORDED: Readonly<Record<string, Readonly<Record<string, unknown>>>> = {
  overview: { pageViews: 0, visitors: 0, events: 0 },
  series: { metric: 'pageviews', granularity: 'day', completeTo: TO, points: [] },
  pages: { pageViews: 0, totalPaths: 0, mostPageViews: 0, pages: [] },
  locations: { grouping: 'country', visitors: 0, totalPlaces: 0, mostVisitors: 0, places: [] },
  sources: { grouping: 'site', visitors: 0, totalSources: 0, mostVisitors: 0, sources: [] },
  devices: { visitors: 0, devices: [] },
  software: { grouping: 'browser', visitors: 0, totalNames: 0, mostVisitors: 0, names: [] },
  actions: { grouping: 'control', presses: 0, totalControls: 0, mostPresses: 0, controls: [] },
  engagement: {
    readings: 0,
    measured: 0,
    medianEngagedMs: 0,
    interacted: 0,
    depths: { top: 0, quarter: 0, half: 0, whole: 0 },
  },
  'engagement/pages': { ranking: 'attention', totalPages: 0, longestMedianEngagedMs: 0, pages: [] },
  'visits/totals': { visits: 0, singlePageVisits: 0, pageViews: 0 },
  'visits/pages': { position: 'entry', totalVisits: 0, totalPaths: 0, mostVisits: 0, pages: [] },
  traffic: { sessions: 0, pageViews: 0, groups: [] },
  'traffic/series': { granularity: 'day', completeTo: TO, buckets: [], groups: [] },
  visits: { totalVisits: 0, visits: [] },
  'visits/facets': NOTHING_HELD,
};

/** An engine with nothing recorded, answering whichever question about a period it is asked. */
function quietEngine(): Engine {
  return engineDoing(async (path) => {
    const question = addressOf(path).slice(`/api/sites/${SITE}/`.length);

    return respondWith(200, { from: FROM, to: TO, ...NOTHING_RECORDED[question] });
  });
}

/** Every question about a period's activity, asked of one population. */
const ACTIVITY: readonly (readonly [string, (population: Population) => Promise<unknown>])[] = [
  ['the totals', (population) => readOverview(SITE, PERIOD, population)],
  [
    'a measure in buckets',
    (population) => readSeries(SITE, 'pageviews', PERIOD, population, 'day'),
  ],
  ['the pages', (population) => readPages(SITE, PERIOD, population, 10, 0)],
  ['the places', (population) => readLocations(SITE, PERIOD, population, 'country', 10, 0)],
  ['the sources', (population) => readSources(SITE, PERIOD, population, 'site', 10, 0)],
  ['the devices', (population) => readDevices(SITE, PERIOD, population)],
  ['the software', (population) => readSoftware(SITE, PERIOD, population, 'browser', 10, 0)],
  ['the presses', (population) => readActions(SITE, PERIOD, population, 'control', 10, 0)],
  ['the readings', (population) => readEngagement(SITE, PERIOD, population)],
  [
    'the pages by reading',
    (population) => readPageEngagement(SITE, PERIOD, population, 'attention', 10, 0),
  ],
  ['the visit totals', (population) => readVisitTotals(SITE, PERIOD, population)],
  ['the doorways', (population) => readVisitPages(SITE, PERIOD, population, 'entry', 10, 0)],
];

/** Where a question was sent, without the question itself. */
function addressOf(sent: string): string {
  return sent.split('?')[0] ?? sent;
}

/** What was actually asked, read back the way the engine reads it. */
function askedIn(sent: string): URLSearchParams {
  return new URLSearchParams(sent.slice(sent.indexOf('?') + 1));
}

describe('asking the engine about a period’s activity', () => {
  /**
   * Everybody is what an absent word means to the engine, so a question about everybody carries no
   * word for it, and the people are asked for by name.
   */
  it('asks about everybody by saying nothing, and about people by name', async () => {
    const everybody = quietEngine();

    await readOverview(SITE, PERIOD, 'everybody');

    expect([...askedIn(everybody.first().path).keys()]).toEqual(['from', 'to']);

    const people = quietEngine();

    await readOverview(SITE, PERIOD, 'people');

    expect(askedIn(people.first().path).get('only')).toBe('people');
  });

  it.each(ACTIVITY)('carries the population on every question about %s', async (_, ask) => {
    const everybody = quietEngine();

    await ask('everybody');

    expect(askedIn(everybody.first().path).has('only')).toBe(false);

    const people = quietEngine();

    await ask('people');

    expect(askedIn(people.first().path).get('only')).toBe('people');
  });

  /**
   * A verdict already says who a visit was, so the questions answered from verdicts carry every
   * conclusion for the screen to read by and are never asked of a population.
   */
  it('asks about verdicts of everybody, always', async () => {
    const engine = quietEngine();

    await readTraffic(SITE, PERIOD);
    await readTrafficSeries(SITE, PERIOD, 'day');
    await readVisits(SITE, PERIOD, 25, 0);
    await readFacets(SITE, PERIOD);

    expect(engine.all().every((sent) => !askedIn(sent.path).has('only'))).toBe(true);
  });
});

describe('asking the engine what generated a period’s traffic', () => {
  const NOTHING_JUDGED = {
    from: FROM,
    to: TO,
    granularity: 'day',
    completeTo: TO,
    buckets: [],
    groups: [],
  };

  /**
   * How finely to cut the period is part of the question rather than left to the engine, because
   * it is the screen that knows how many buckets will fit across the width somebody is reading on.
   */
  it('asks for the period cut as finely as the screen can draw it', async () => {
    const engine = engineAnswering(200, NOTHING_JUDGED);

    await readTrafficSeries(SITE, PERIOD, 'hour');

    const asked = askedIn(engine.first().path);

    expect(addressOf(engine.first().path)).toBe(`/api/sites/${SITE}/traffic/series`);
    expect(asked.get('granularity')).toBe('hour');
    expect(asked.get('from')).toBe(FROM);
    expect(asked.get('to')).toBe(TO);
  });

  /** The engine says back how it cut the period, and an answer by the week is read as one. */
  it('asks for the period cut by the week, and reads the weeks back', async () => {
    const engine = engineAnswering(200, { ...NOTHING_JUDGED, granularity: 'week' });

    await expect(readTrafficSeries(SITE, PERIOD, 'week')).resolves.toMatchObject({
      granularity: 'week',
    });
    expect(askedIn(engine.first().path).get('granularity')).toBe('week');
  });

  it('asks for the period cut by the month, and reads the months back', async () => {
    const engine = engineAnswering(200, { ...NOTHING_JUDGED, granularity: 'month' });

    await expect(readTrafficSeries(SITE, PERIOD, 'month')).resolves.toMatchObject({
      granularity: 'month',
    });
    expect(askedIn(engine.first().path).get('granularity')).toBe('month');
  });
});

describe('asking the engine for a measure in buckets', () => {
  const NOTHING_COUNTED = {
    from: FROM,
    to: TO,
    metric: 'pageviews',
    completeTo: TO,
    points: [],
  };

  it('asks for the period cut by the week', async () => {
    const engine = engineAnswering(200, { ...NOTHING_COUNTED, granularity: 'week' });

    await expect(readSeries(SITE, 'pageviews', PERIOD, 'everybody', 'week')).resolves.toMatchObject(
      { granularity: 'week' },
    );

    const asked = askedIn(engine.first().path);

    expect(addressOf(engine.first().path)).toBe(`/api/sites/${SITE}/series`);
    expect(asked.get('granularity')).toBe('week');
    expect(asked.get('metric')).toBe('pageviews');
  });

  it('asks for the period cut by the month, for the people alone where asked', async () => {
    const engine = engineAnswering(200, {
      ...NOTHING_COUNTED,
      metric: 'visitors',
      granularity: 'month',
    });

    await readSeries(SITE, 'visitors', PERIOD, 'people', 'month');

    const asked = askedIn(engine.first().path);

    expect(asked.get('granularity')).toBe('month');
    expect(asked.get('only')).toBe('people');
  });

  /** A way of cutting this dashboard never asks for is an answer it cannot draw, and is refused. */
  it('refuses an answer cut in a way the dashboard never asks for', async () => {
    engineAnswering(200, { ...NOTHING_COUNTED, granularity: 'fortnight' });

    await expect(readSeries(SITE, 'pageviews', PERIOD, 'everybody', 'week')).rejects.toThrow();
  });
});

/**
 * All time has no window: it is every month the engine holds for a website, and the engine says
 * which months those are.
 */
describe('asking the engine about all time', () => {
  const NEVER_MEASURED = {
    from: null,
    to: TO,
    firstDay: null,
    detailFrom: FROM,
    pageViews: 0,
    visitors: 0,
    completeTo: TO,
    missing: [],
    unjudged: [],
  };

  const MONTHS = {
    from: FROM,
    to: TO,
    metric: 'pageviews',
    granularity: 'month',
    completeTo: TO,
    points: [
      { bucketStart: '2026-06-30T18:30:00+00:00', value: 120 },
      { bucketStart: '2026-07-31T18:30:00+00:00', value: null },
    ],
    missing: ['2026-08'],
    unjudged: [],
  };

  /** What the whole record says, all of it from one answer, as the screen about it reads it. */
  describe('what the whole record says', () => {
    const RECORD = {
      firstDay: '2026-07-19',
      to: '2026-08-18T00:00:00+00:00',
      completeTo: '2026-08-18T00:00:00+00:00',
      pageViews: 120,
      visitors: 40,
      bestDay: { day: '2026-07-21', pageViews: 12 },
      weekdays: [1, 2, 3, 4, 5, 6, 7],
      busiestWeekdays: [6],
      hours: Array.from({ length: 24 }, (_, hour) => hour),
      busiestHours: [23],
      months: [
        { month: '2026-07', pageViews: null, daysMeasured: 0 },
        { month: '2026-08', pageViews: 120, daysMeasured: 18 },
      ],
      missing: ['2026-07'],
      unjudged: [],
    };

    it('asks about everybody by saying nothing, with no bare question mark', async () => {
      const engine = engineAnswering(200, RECORD);

      await readInsights(SITE, 'everybody');

      expect(engine.first().path).toBe(`/api/sites/${SITE}/insights`);
    });

    it('asks about the people by name, and never about a window', async () => {
      const engine = engineAnswering(200, RECORD);

      await readInsights(SITE, 'people');

      const asked = askedIn(engine.first().path);

      expect(addressOf(engine.first().path)).toBe(`/api/sites/${SITE}/insights`);
      expect([...asked.keys()]).toStrictEqual(['only']);
      expect(asked.get('only')).toBe('people');
    });

    it('reads a month with no figure as nothing rather than as nought', async () => {
      engineAnswering(200, RECORD);

      await expect(readInsights(SITE, 'everybody')).resolves.toMatchObject({
        months: [
          { month: '2026-07', pageViews: null, daysMeasured: 0 },
          { month: '2026-08', pageViews: 120, daysMeasured: 18 },
        ],
        missing: ['2026-07'],
      });
    });

    /** A week of six days or a day of twenty-three hours would misplace every share. */
    it('refuses an answer whose week or day is the wrong length', async () => {
      engineAnswering(200, { ...RECORD, weekdays: [1, 2, 3] });

      await expect(readInsights(SITE, 'everybody')).rejects.toThrow();

      engineAnswering(200, { ...RECORD, hours: [1, 2, 3] });

      await expect(readInsights(SITE, 'everybody')).rejects.toThrow();
    });
  });

  it('asks about everybody by saying nothing, with no bare question mark', async () => {
    const engine = engineAnswering(200, NEVER_MEASURED);

    await readAllTime(SITE, 'everybody');

    expect(engine.first().path).toBe(`/api/sites/${SITE}/all-time`);
  });

  it('asks about the people by name, and never about a window', async () => {
    const engine = engineAnswering(200, NEVER_MEASURED);

    await readAllTime(SITE, 'people');

    const asked = askedIn(engine.first().path);

    expect(addressOf(engine.first().path)).toBe(`/api/sites/${SITE}/all-time`);
    expect([...asked.keys()]).toStrictEqual(['only']);
    expect(asked.get('only')).toBe('people');
  });

  /** A month never counted is nothing, and reads back as nothing rather than as nought. */
  it('asks for a measure by the month, and reads a month never counted as nothing', async () => {
    const engine = engineAnswering(200, MONTHS);

    await expect(readAllTimeSeries(SITE, 'pageviews', 'month', 'people')).resolves.toMatchObject({
      points: [{ value: 120 }, { value: null }],
      missing: ['2026-08'],
      unjudged: [],
    });

    const asked = askedIn(engine.first().path);

    expect(addressOf(engine.first().path)).toBe(`/api/sites/${SITE}/all-time/series`);
    expect(asked.get('metric')).toBe('pageviews');
    expect(asked.get('granularity')).toBe('month');
    expect(asked.get('only')).toBe('people');
    expect(asked.has('from')).toBe(false);
  });

  it('asks who came by the year, of no population', async () => {
    const engine = engineAnswering(200, {
      from: FROM,
      to: TO,
      granularity: 'year',
      completeTo: TO,
      buckets: ['2025-12-31T18:30:00+00:00'],
      groups: [{ category: 'likely-human', sessions: [null], pageViews: [null] }],
      missing: [],
      unjudged: [],
    });

    await readAllTimeTraffic(SITE, 'year');

    const asked = askedIn(engine.first().path);

    expect(addressOf(engine.first().path)).toBe(`/api/sites/${SITE}/all-time/traffic/series`);
    expect([...asked.keys()]).toStrictEqual(['granularity']);
    expect(asked.get('granularity')).toBe('year');
  });

  /** All time is never cut finer than a month, so an answer that says it was cannot be drawn. */
  it('refuses an answer cut finer than a month', async () => {
    engineAnswering(200, { ...MONTHS, granularity: 'week' });

    await expect(readAllTimeSeries(SITE, 'pageviews', 'month', 'everybody')).rejects.toThrow();
  });

  it('refuses a month named by anything but its year and its number', async () => {
    engineAnswering(200, { ...NEVER_MEASURED, missing: ['2026-13'] });

    await expect(readAllTime(SITE, 'everybody')).rejects.toThrow();
  });
});

describe('asking the engine for judged visits', () => {
  it('asks for one slice of the period', async () => {
    const engine = engineAnswering(200, NO_VISITS);

    await readVisits(SITE, PERIOD, 25, 50);

    const asked = askedIn(engine.first().path);

    expect(addressOf(engine.first().path)).toBe(`/api/sites/${SITE}/visits`);
    expect(asked.get('limit')).toBe('25');
    expect(asked.get('offset')).toBe('50');
    expect(asked.get('from')).toBe(FROM);
    expect(asked.get('to')).toBe(TO);
  });

  it('asks for nothing beyond the slice while nothing is narrowed', async () => {
    const engine = engineAnswering(200, NO_VISITS);

    await readVisits(SITE, PERIOD, 25, 0);

    expect([...askedIn(engine.first().path).keys()]).toEqual(['limit', 'offset', 'from', 'to']);
  });

  /**
   * Narrowing is asked of the engine rather than applied to what comes back, so everything a
   * reader picked has to survive the journey into the address — including the values that are
   * whatever a website's own traffic happened to record.
   */
  it('carries everything the reader picked into the address', async () => {
    const engine = engineAnswering(200, NO_VISITS);

    await readVisits(SITE, PERIOD, 10, 0, {
      ...EVERY_JOURNEY,
      categories: ['likely-human'],
      devices: ['phone'],
      countries: ['IN', 'FR'],
      networks: ['Reliance Jio Infocomm Limited'],
      entryPages: ['/pricing'],
      leastStrength: 'moderate',
      leastPages: 2,
    });

    const asked = askedIn(engine.first().path);

    expect(asked.get('category')).toBe('likely-human');
    expect(asked.get('device')).toBe('phone');
    expect(asked.getAll('country')).toEqual(['FR', 'IN']);
    expect(asked.get('network')).toBe('Reliance Jio Infocomm Limited');
    expect(asked.get('entryPage')).toBe('/pricing');
    expect(asked.get('strength')).toBe('moderate');
    expect(asked.get('minPages')).toBe('2');
  });
});

describe('asking the engine what a period held', () => {
  it('asks about the whole period and narrows it by nothing', async () => {
    const engine = engineAnswering(200, NOTHING_HELD);

    await readFacets(SITE, PERIOD);

    expect(addressOf(engine.first().path)).toBe(`/api/sites/${SITE}/visits/facets`);
    expect([...askedIn(engine.first().path).keys()]).toEqual(['from', 'to']);
  });

  /**
   * An empty value is what the engine reports where nothing was ever established, and it has to
   * survive being read back: it is the difference between offering "the visits nobody could place"
   * and offering nothing at all.
   */
  it('reads back what each detail held, including what nothing was established about', async () => {
    engineAnswering(200, {
      ...NOTHING_HELD,
      countries: [
        { value: 'IN', visits: 3 },
        { value: '', visits: 1 },
      ],
      sourceKinds: [{ value: 'search', visits: 4 }],
    });

    await expect(readFacets(SITE, PERIOD)).resolves.toMatchObject({
      countries: [
        { value: 'IN', visits: 3 },
        { value: '', visits: 1 },
      ],
      sourceKinds: [{ value: 'search', visits: 4 }],
      towns: [],
    });
  });
});

describe('asking the engine how reading a sitemap is going', () => {
  const NOTHING_NAMED = {
    address: null,
    state: 'none',
    problem: null,
    readAt: null,
    succeededAt: null,
    posts: 0,
    dated: 0,
    reading: 0,
    turnedAway: 0,
  };

  it('asks about the website alone', async () => {
    const engine = engineAnswering(200, NOTHING_NAMED);

    await readSitemapStatus(SITE);

    expect(engine.first().path).toBe(`/api/sites/${SITE}/sitemap`);
  });

  it('reads a website nobody has named a sitemap for', async () => {
    engineAnswering(200, NOTHING_NAMED);

    await expect(readSitemapStatus(SITE)).resolves.toStrictEqual(NOTHING_NAMED);
  });

  /** PostgreSQL keeps six places of a second, and the engine writes them all. */
  it('reads a reading that got partway, to the fraction of a second', async () => {
    const partly = {
      address: 'https://example.com/sitemap.xml',
      state: 'partly-read',
      problem: 'too-slow',
      readAt: '2026-08-18T09:30:00.123456+00:00',
      succeededAt: '2026-08-17T09:30:00.654321+00:00',
      posts: 12,
      dated: 10,
      reading: 3,
      turnedAway: 2,
    };

    engineAnswering(200, partly);

    await expect(readSitemapStatus(SITE)).resolves.toStrictEqual(partly);
  });

  it('refuses a word it has no sentence for', async () => {
    engineAnswering(200, { ...NOTHING_NAMED, state: 'pondering' });

    await expect(readSitemapStatus(SITE)).rejects.toThrow();
  });
});
