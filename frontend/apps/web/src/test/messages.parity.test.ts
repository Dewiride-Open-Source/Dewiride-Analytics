import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { routing } from '@/i18n/routing';

/**
 * Guards the promise that adding a language is a translation job and nothing else.
 *
 * Only English is filled in today. This suite is what makes that a decision rather than a state
 * of affairs: the moment a second catalogue appears it must cover exactly the same ground, and a
 * key that exists in one language and not another fails here instead of appearing on somebody's
 * screen as its own name.
 */

const CATALOGUES = path.join(import.meta.dirname, '..', '..', 'messages');

function load(locale: string): Record<string, unknown> {
  return JSON.parse(readFileSync(path.join(CATALOGUES, `${locale}.json`), 'utf8'));
}

function flatten(value: unknown, prefix = ''): Map<string, string> {
  const found = new Map<string, string>();

  if (typeof value === 'string') {
    found.set(prefix, value);

    return found;
  }

  if (typeof value === 'object' && value !== null) {
    for (const [key, nested] of Object.entries(value)) {
      const name = prefix ? `${prefix}.${key}` : key;

      for (const [flatKey, flatValue] of flatten(nested, name)) {
        found.set(flatKey, flatValue);
      }
    }
  }

  return found;
}

describe('message catalogues', () => {
  const published = readdirSync(CATALOGUES)
    .filter((file) => file.endsWith('.json'))
    .map((file) => path.basename(file, '.json'))
    .sort();

  it('has a catalogue for every language the routing publishes, and no others', () => {
    expect(published).toEqual([...routing.locales].sort());
  });

  it('carries the default language', () => {
    expect(published).toContain(routing.defaultLocale);
  });

  const reference = flatten(load(routing.defaultLocale));

  it('defines at least one message', () => {
    expect(reference.size).toBeGreaterThan(0);
  });

  it.each(published)('%s covers exactly the same keys as the default', (locale) => {
    const keys = [...flatten(load(locale)).keys()].sort();

    expect(keys).toEqual([...reference.keys()].sort());
  });

  it.each(published)('%s leaves no message empty', (locale) => {
    const blank = [...flatten(load(locale))]
      .filter(([, message]) => message.trim().length === 0)
      .map(([key]) => key);

    expect(blank).toEqual([]);
  });
});
