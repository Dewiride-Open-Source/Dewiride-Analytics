import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';

const APPLICATION = path.join(import.meta.dirname, '..', '..');
const REPOSITORY = path.join(APPLICATION, '..', '..', '..');

/** What one file says a setting is, found by the words around it. */
function stated(file: string, pattern: RegExp): number {
  const found = pattern.exec(readFileSync(file, 'utf8'))?.[1];

  expect(found, `${path.basename(file)} states it`).toBeDefined();

  return Number(found?.replaceAll('_', ''));
}

describe('how long the dashboard waits for the engine', () => {
  // The dashboard forwards a reader's request and waits for the engine, and the engine waits for
  // its store. If the dashboard gave up first, a slow statement would be answered as a fault with
  // nothing in the engine's log to say which statement it was, and asking again could not help.
  it('waits longer than the engine waits for one statement to its store', () => {
    const forwarded = stated(path.join(APPLICATION, 'next.config.ts'), /proxyTimeout:\s*([\d_]+)/);
    const statement = stated(
      path.join(
        REPOSITORY,
        'backend/src/Dewiride.Analytics.Infrastructure.ClickHouse/ClickHouseRegistration.cs',
      ),
      /LongestStatement = TimeSpan\.FromSeconds\((\d+)\)/,
    );

    expect(forwarded).toBeGreaterThan(statement * 1000);
  });
});
