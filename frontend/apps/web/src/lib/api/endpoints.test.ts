import { afterEach, describe, expect, it, vi } from 'vitest';
import { EVERY_JOURNEY } from '@/lib/analytics/journeys';
import { readFacets, readVisits } from '@/lib/api/endpoints';
import { engineAnswering } from '@/test/engine';

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

/** Where a question was sent, without the question itself. */
function addressOf(sent: string): string {
  return sent.split('?')[0] ?? sent;
}

/** What was actually asked, read back the way the engine reads it. */
function askedIn(sent: string): URLSearchParams {
  return new URLSearchParams(sent.slice(sent.indexOf('?') + 1));
}

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
