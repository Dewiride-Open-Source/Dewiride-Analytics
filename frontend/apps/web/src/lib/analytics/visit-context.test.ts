import { describe, expect, it } from 'vitest';
import { placeOf, readOn } from '@/lib/analytics/visit-context';
import type { VisitContext } from '@/lib/api/schemas';

function context(established: Partial<VisitContext> = {}): VisitContext {
  return {
    source: '',
    kind: 'direct',
    countryCode: '',
    town: '',
    network: '',
    device: 'unknown',
    browser: '',
    system: '',
    ...established,
  };
}

describe('roughly where a visit was', () => {
  it('writes a town with its country beside it', () => {
    expect(placeOf(context({ town: 'Springfield' }), 'United States')).toBe(
      'Springfield, United States',
    );
  });

  it('writes the country alone when nothing narrowed it to a town', () => {
    expect(placeOf(context(), 'India')).toBe('India');
  });

  /**
   * A town with no country is not a shorter answer, it is a different one: a great many town names
   * belong to more than one country, so it is withheld rather than shown on its own.
   */
  it('writes nothing at all when nothing placed the visit', () => {
    expect(placeOf(context({ town: 'Springfield' }), null)).toBeNull();
  });
});

describe('what a visit was read on', () => {
  it('names the browser and the system together where both are known', () => {
    expect(readOn(context({ browser: 'Chrome', system: 'Android' }))).toStrictEqual({
      observed: 'browser-and-system',
      browser: 'Chrome',
      system: 'Android',
    });
  });

  it('names whichever of the two was observed where only one was', () => {
    expect(readOn(context({ browser: 'Safari' }))).toStrictEqual({
      observed: 'software',
      name: 'Safari',
    });
    expect(readOn(context({ system: 'Windows' }))).toStrictEqual({
      observed: 'software',
      name: 'Windows',
    });
  });

  /**
   * The kind of device is the fallback rather than an addition. A row reading "Chrome on Android"
   * with "A phone" underneath would spend a line saying twice what the line above already said.
   */
  it('falls back to the kind of device when nothing named the software', () => {
    expect(
      readOn(context({ device: 'phone', browser: 'Chrome', system: 'Android' })),
    ).toStrictEqual({ observed: 'browser-and-system', browser: 'Chrome', system: 'Android' });
    expect(readOn(context({ device: 'phone' }))).toStrictEqual({
      observed: 'device',
      device: 'phone',
    });
  });

  it('says nothing where nothing named the software or the device', () => {
    expect(readOn(context())).toBeNull();
  });
});
