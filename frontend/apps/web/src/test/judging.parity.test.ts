import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { JUDGING_BEHIND_AFTER } from '@/lib/analytics/all-time';

const REPOSITORY = path.join(import.meta.dirname, '..', '..', '..', '..', '..');
const SETTINGS = path.join(
  REPOSITORY,
  'backend/src/Dewiride.Analytics.Application/Sessions/ClassificationOptions.cs',
);

const SECOND = 1000;
const MINUTE = 60 * SECOND;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/**
 * The longest the engine lets one of its settings be, in milliseconds, found by the setting's name:
 * the upper end of the range written above it, as .NET writes a length of time, `12:00:00` or
 * `7.00:00:00`.
 */
function longest(setting: string): number {
  const stated = new RegExp(
    String.raw`\[Range\(typeof\(TimeSpan\), "[^"]+", "((?:\d+\.)?\d\d:\d\d:\d\d)"\)\]\s+public TimeSpan ${setting}\b`,
  ).exec(readFileSync(SETTINGS, 'utf8'))?.[1];

  expect(stated, `ClassificationOptions.cs states how long ${setting} may be`).toBeDefined();

  const [clock = '', days = '0'] = (stated ?? '').split('.').reverse();
  const [hours = 0, minutes = 0, seconds = 0] = clock.split(':').map(Number);

  return Number(days) * DAY + hours * HOUR + minutes * MINUTE + seconds * SECOND;
}

describe('how far behind judging is before the dashboard says so', () => {
  // The people's figures always stop short of the present: a visit is held open until it has been
  // quiet for the idle timeout, and is judged at the next run after that. If the dashboard's line
  // sat within what those two settings can come to, an installation set to wait that long would be
  // told the newest are still being judged on every reading, with nothing waiting.
  it('is beyond the longest a visit may stay quiet and the longest wait between runs put together', () => {
    expect(JUDGING_BEHIND_AFTER).toBeGreaterThan(longest('IdleTimeout') + longest('Interval'));
  });
});
