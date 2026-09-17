import { afterEach, describe, expect, it, vi } from 'vitest';
import { EVERY_JOURNEY } from '@/lib/analytics/journeys';
import type { Population } from '@/lib/analytics/people-only';
import {
  readActions,
  readDevices,
  readEngagement,
  readFacets,
  readLocations,
  readOverview,
  readPageEngagement,
  readPages,
  readSeries,
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
