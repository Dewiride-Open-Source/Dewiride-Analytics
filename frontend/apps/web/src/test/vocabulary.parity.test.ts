import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  captureSurfaceSchema,
  deviceKindSchema,
  evidenceStrengthSchema,
  signalDirectionSchema,
  trafficCategorySchema,
} from '@/lib/api/schemas';
import messages from '../../messages/en.json';

/**
 * Guards the join between what the engine can say and what this dashboard can render.
 *
 * The engine answers in a fixed vocabulary of codes, none of which is ever shown to anybody: each
 * one is the key of a sentence written here. That makes the two halves a single contract kept in
 * two languages, and the only way a missing sentence otherwise announces itself is as its own key
 * appearing on a customer's screen.
 *
 * The engine's own source is read rather than a copy of it, because a copy is the thing that goes
 * stale. This is the one place in the dashboard that looks at the backend, and it looks at it as
 * a published contract rather than as code to depend on.
 */

const REPOSITORY = path.join(import.meta.dirname, '..', '..', '..', '..', '..');

const SIGNAL_CODES = path.join(
  REPOSITORY,
  'backend/src/Dewiride.Analytics.Classification/SignalCodes.cs',
);

const REPORTED_NAMES = path.join(
  REPOSITORY,
  'backend/src/Dewiride.Analytics.Api/Analytics/ReportedNames.cs',
);

/** Every value a declaration in the engine's source assigns. */
function declared(file: string, pattern: RegExp): readonly string[] {
  const found = [...readFileSync(file, 'utf8').matchAll(pattern)].map((match) => match[1] ?? '');

  expect(found.length).toBeGreaterThan(0);

  return found;
}

const codes = declared(SIGNAL_CODES, /public const string \w+ = "([^"]+)";/g);
const categories = declared(REPORTED_NAMES, /\[TrafficCategory\.\w+\] = "([^"]+)"/g);
const strengths = declared(REPORTED_NAMES, /\[EvidenceStrength\.\w+\] = "([^"]+)"/g);
const surfaces = declared(REPORTED_NAMES, /\[IngestSurface\.\w+\] = "([^"]+)"/g);
const directions = declared(REPORTED_NAMES, /\[SignalDirection\.\w+\] = "([^"]+)"/g);
const devices = declared(REPORTED_NAMES, /\[DeviceClass\.\w+\] = "([^"]+)"/g);

/** What the catalogue holds at a dotted path, whether that is a sentence or a group of them. */
function wordsAt(dotted: string): unknown {
  return dotted
    .split('.')
    .reduce<unknown>(
      (found, step) =>
        typeof found === 'object' && found !== null
          ? (found as Record<string, unknown>)[step]
          : undefined,
      messages,
    );
}

/** Whether a path leads to a sentence, or to a group whose every member is one. */
function isWritten(found: unknown): boolean {
  if (typeof found === 'string') {
    return found.trim().length > 0;
  }

  if (typeof found !== 'object' || found === null) {
    return false;
  }

  const members = Object.values(found);

  return members.length > 0 && members.every(isWritten);
}

/** Every path in the catalogue that leads to a sentence, beneath one namespace. */
function sentencesUnder(namespace: string): readonly string[] {
  const walk = (value: unknown, prefix: string): readonly string[] =>
    typeof value === 'object' && value !== null
      ? Object.entries(value).flatMap(([key, nested]) =>
          walk(nested, prefix ? `${prefix}.${key}` : key),
        )
      : [prefix];

  return walk(wordsAt(namespace), '');
}

describe('the engine vocabulary', () => {
  it.each(codes)('has words for the observation %s', (code) => {
    expect(isWritten(wordsAt(`reasons.${code}`))).toBe(true);
  });

  it.each(categories)('has a name for the category %s', (category) => {
    expect(isWritten(wordsAt(`verdicts.category.${category}`))).toBe(true);
  });

  it.each(strengths)('has words for the evidence strength %s', (strength) => {
    expect(isWritten(wordsAt(`verdicts.strength.${strength}`))).toBe(true);
  });

  it.each(surfaces)('has a name for the capture surface %s', (surface) => {
    expect(isWritten(wordsAt(`verdicts.surface.${surface}`))).toBe(true);
  });

  it.each(devices)('has a name for the kind of device %s', (device) => {
    expect(isWritten(wordsAt(`dashboard.devices.kind.${device}`))).toBe(true);
  });

  it('is accepted whole by the shapes the answers are checked against', () => {
    expect([...trafficCategorySchema.options].sort()).toEqual([...categories].sort());
    expect([...evidenceStrengthSchema.options].sort()).toEqual([...strengths].sort());
    expect([...captureSurfaceSchema.options].sort()).toEqual([...surfaces].sort());
    expect([...signalDirectionSchema.options].sort()).toEqual([...directions].sort());
    expect([...deviceKindSchema.options].sort()).toEqual([...devices].sort());
  });

  it('carries no sentence for an observation the engine cannot report', () => {
    // The fallback is deliberately not one of the engine's codes: it is what an observation added
    // in a later release reads as until somebody writes it a sentence of its own.
    const orphaned = sentencesUnder('reasons')
      .filter((sentence) => sentence !== 'other')
      .filter((sentence) => !codes.some((code) => sentence.startsWith(code)));

    expect(orphaned).toEqual([]);
  });

  it('names no category, strength, surface or device the engine cannot report', () => {
    expect([...sentencesUnder('verdicts.category')].sort()).toEqual([...categories].sort());
    expect([...sentencesUnder('verdicts.strength')].sort()).toEqual([...strengths].sort());
    expect([...sentencesUnder('verdicts.surface')].sort()).toEqual([...surfaces].sort());
    expect([...sentencesUnder('dashboard.devices.kind')].sort()).toEqual([...devices].sort());
  });
});
