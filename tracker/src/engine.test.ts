// @vitest-environment node
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * What the detection engine assumes about this beacon.
 *
 * The engine reads a report of how a page is going that arrives sooner than this beacon ever sends
 * one as a page whose wait was cut short, and calls the visit automation. And it reads a page still
 * open at midnight as one that may have gone on reporting under the next day's key, for as long as
 * the longest gap this beacon leaves between those reports. Both are promises made to the engine:
 * a beacon that reported sooner would make every honest browser look like one whose wait was cut
 * short, and one that reported less often would make readers at midnight look like pages left open.
 *
 * Both sides are read as text rather than copied, because a copy is the thing that goes stale.
 */

const REPOSITORY = path.join(import.meta.dirname, '..', '..');

const BEACON = path.join(REPOSITORY, 'tracker/src/beacon.ts');

const TIMING_DETECTOR = path.join(
  REPOSITORY,
  'backend/src/Dewiride.Analytics.Classification/Detectors/TimingDetector.cs',
);

const SESSION_STATEMENT = path.join(
  REPOSITORY,
  'backend/src/Dewiride.Analytics.Infrastructure.ClickHouse/Sessions/SessionSqlCompiler.cs',
);

const CATALOGUE = path.join(REPOSITORY, 'frontend/apps/web/messages/en.json');

/** The one number a declaration in a source file assigns. */
function declared(file: string, pattern: RegExp): number {
  const found = pattern.exec(readFileSync(file, 'utf8'));

  expect(found, `${path.basename(file)} declares ${pattern.source}`).not.toBeNull();

  return Number(found?.[1]);
}

describe('the reports of how a page is going', () => {
  it('wait as long before the first as the detection engine reads a wait cut short against', () => {
    const beacon = declared(BEACON, /const FIRST_PROGRESS = (\d+);/);
    const engine = declared(TIMING_DETECTOR, /const int FirstReportAfterMs = (\d+);/);

    expect(beacon).toBe(engine);
  });

  it('never leave a longer gap than the engine allows a page still open at midnight', () => {
    const slowest = declared(BEACON, /const SLOWEST_PROGRESS = (\d+);/);
    const allowed = declared(
      SESSION_STATEMENT,
      /ProgressInterval = TimeSpan\.FromSeconds\((\d+)\);/,
    );

    expect(slowest).toBeLessThan(allowed * 1000);
  });

  // The sentence a site owner reads about a wait cut short names the wait, as every figure in the
  // catalogue is named, so it is one more copy of the same number.
  it('wait as long before the first as the dashboard tells a site owner they do', () => {
    const beacon = declared(BEACON, /const FIRST_PROGRESS = (\d+);/);
    const sentence: unknown = JSON.parse(readFileSync(CATALOGUE, 'utf8')).reasons.automation
      .wait_cut_short;

    expect(sentence).toContain(`waits ${beacon / 1000} seconds`);
  });
});
