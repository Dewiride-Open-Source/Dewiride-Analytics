import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { MEASURES } from '@/lib/analytics/all-time';
import { CHART_BYS } from '@/lib/analytics/chart-by';
import { GRANULARITIES, hasEarlier, KEPT_MONTHS, PRESETS } from '@/lib/analytics/period';
import { LONGEST_SITEMAP_ADDRESS, SITEMAP_REFUSED } from '@/lib/analytics/sitemap';
import { reasonKey } from '@/lib/analytics/verdicts';
import {
  captureSurfaceSchema,
  deviceKindSchema,
  evidenceStrengthSchema,
  signalDirectionSchema,
  sitemapProblemSchema,
  sitemapStateSchema,
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
 * The dashboard's own closed sets are guarded the same way, for the same reason. The periods it
 * offers are a fixed list, and each of them needs both a name and a way of saying what it is
 * measured against.
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

const DECLARED_IDENTITY = path.join(
  REPOSITORY,
  'backend/src/Dewiride.Analytics.Classification/Detectors/DeclaredIdentityDetector.cs',
);

const ARRIVAL = path.join(
  REPOSITORY,
  'backend/src/Dewiride.Analytics.Classification/Detectors/ArrivalDetector.cs',
);

/**
 * The tables a period's figures are counted from: the reports of what was read, the verdicts on
 * who read it, and the names confirmed for the crawlers among them.
 */
const COUNTED_FROM = [
  'backend/migrations/clickhouse/0001_events.sql',
  'backend/migrations/clickhouse/0003_session_classifications.sql',
  'backend/migrations/clickhouse/0010_name_confirmations.sql',
].map((file) => path.join(REPOSITORY, file));

/**
 * Where the engine states, in its own code, how many months it keeps those reports — the figure it
 * works out which months it can still count from, and where the days it holds in detail begin.
 */
const RETENTION = path.join(
  REPOSITORY,
  'backend/src/Dewiride.Analytics.Application/Telemetry/Retention.cs',
);

/** Where the engine states how long an address it will read a website's posts from may be. */
const SITE = path.join(REPOSITORY, 'backend/src/Dewiride.Analytics.Domain/Sites/Site.cs');

/** Where the engine names the refusals a change to a website's settings can come back with. */
const SITE_SETTINGS_ENDPOINTS = path.join(
  REPOSITORY,
  'backend/src/Dewiride.Analytics.Api/Endpoints/SiteSettingsEndpoints.cs',
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
const purposes = declared(DECLARED_IDENTITY, /\[CrawlerPurpose\.\w+\] = "([^"]+)"/g);
const senders = declared(ARRIVAL, /\[SourceChannel\.\w+\] = "([^"]+)"/g);
const sitemapStates = declared(REPORTED_NAMES, /\[SitemapState\.\w+\] = "([^"]+)"/g);
const sitemapProblems = declared(REPORTED_NAMES, /\[SitemapProblem\.\w+\] = "([^"]+)"/g);

/** The sentence key the dashboard picks for another site of one kind having sent a visit. */
function sentBy(kind: string): string {
  return reasonKey({
    code: 'arrival.from_another_site',
    direction: 'toward-human',
    weight: 0,
    values: { kind },
  });
}

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

  // What a crawler is for is the question a publisher opens the page to ask, and it reaches the
  // screen as a whole sentence chosen by the value rather than as a noun dropped into a shared
  // one. A purpose the engine can report and this catalogue cannot write is a visit explained in
  // the wrong words, so the two lists are held against each other in both directions.
  it.each(purposes)('says in words what a crawler for %s does', (purpose) => {
    const key = reasonKey({
      code: 'identity.declared_crawler',
      direction: 'toward-automation',
      weight: 0,
      values: { purpose },
    });

    expect(isWritten(wordsAt(`reasons.${key}`))).toBe(true);
  });

  it('writes no sentence for a crawler purpose the engine cannot report', () => {
    // As with the fallback above, 'unstated' is not one of the engine's purposes: it is what a
    // purpose added in a later release reads as until somebody writes it a sentence of its own.
    const written = sentencesUnder('reasons.identity.declared_crawler')
      .filter((sentence) => sentence !== 'unstated')
      .sort();

    const reachable = purposes
      .map((purpose) =>
        reasonKey({
          code: 'identity.declared_crawler',
          direction: 'toward-automation',
          weight: 0,
          values: { purpose },
        }).replace('identity.declared_crawler.', ''),
      )
      .sort();

    expect(written).toEqual(reachable);
  });

  // Which kind of site sent a visit is written as a whole sentence for each kind, for the same
  // reason as a crawler's purpose, and the two lists are held against each other both ways.
  it.each(senders)('says in words that a site of the kind %s sent the visit', (kind) => {
    expect(sentBy(kind)).toBe(`arrival.from_another_site.${kind}`);
    expect(isWritten(wordsAt(`reasons.${sentBy(kind)}`))).toBe(true);
  });

  it('writes no sentence for a kind of sending site the engine cannot report', () => {
    const written = [...sentencesUnder('reasons.arrival.from_another_site')].sort();
    const reachable = senders
      .map((kind) => sentBy(kind).replace('arrival.from_another_site.', ''))
      .sort();

    expect(written).toEqual(reachable);
  });

  it('is accepted whole by the shapes the answers are checked against', () => {
    expect([...trafficCategorySchema.options].sort()).toEqual([...categories].sort());
    expect([...evidenceStrengthSchema.options].sort()).toEqual([...strengths].sort());
    expect([...captureSurfaceSchema.options].sort()).toEqual([...surfaces].sort());
    expect([...signalDirectionSchema.options].sort()).toEqual([...directions].sort());
    expect([...deviceKindSchema.options].sort()).toEqual([...devices].sort());
    expect([...sitemapStateSchema.options].sort()).toEqual([...sitemapStates].sort());
    expect([...sitemapProblemSchema.options].sort()).toEqual([...sitemapProblems].sort());
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

/**
 * A reading of a website's sitemap is reported as a word for how it stands and a word for what got
 * in its way, and neither word is ever shown. A word without a sentence would reach a customer as
 * the name of a key in a catalogue, on the one line telling them what to go and put right.
 */
describe('what the engine can say about reading a sitemap', () => {
  it.each(sitemapStates)('says in words what it means for a reading to stand at %s', (state) => {
    expect(isWritten(wordsAt(`siteSettings.sitemap.state.${state}`))).toBe(true);
  });

  it.each(sitemapProblems)('says in words what to do about %s', (problem) => {
    expect(isWritten(wordsAt(`siteSettings.sitemap.problem.${problem}`))).toBe(true);
  });

  it.each(sitemapProblems)(
    'says in words what to do about %s in a file the sitemap lists',
    (problem) => {
      expect(isWritten(wordsAt(`siteSettings.sitemap.partlyProblem.${problem}`))).toBe(true);
    },
  );

  it('writes no sentence for anything the engine cannot report', () => {
    expect([...sentencesUnder('siteSettings.sitemap.state')].sort()).toEqual(
      [...sitemapStates].sort(),
    );
    expect([...sentencesUnder('siteSettings.sitemap.problem')].sort()).toEqual(
      [...sitemapProblems].sort(),
    );
    expect([...sentencesUnder('siteSettings.sitemap.partlyProblem')].sort()).toEqual(
      [...sitemapProblems].sort(),
    );
  });

  /** An address the box lets through is one the engine has room for, and the reverse. */
  it('stops an address in the box exactly where the engine stops keeping one', () => {
    const [longest] = declared(SITE, /public const int MaxSitemapAddressLength = (\d+);/g);

    expect(LONGEST_SITEMAP_ADDRESS).toBe(Number(longest));
  });

  /**
   * The box says this under itself by the code, so a code the engine has stopped using would turn an
   * address it refuses into a general failure, and a code with no words would show its own name.
   */
  it('knows the refusal of an address by the engine’s word for it, and has words for it', () => {
    const [refusal] = declared(SITE_SETTINGS_ENDPOINTS, /SitemapRejectedCode = "([^"]+)";/g);

    expect(SITEMAP_REFUSED).toBe(refusal);
    expect(isWritten(wordsAt(`problems.${refusal}`))).toBe(true);
  });
});

describe('the periods this dashboard offers', () => {
  it.each(PRESETS)('has a name for %s', (preset) => {
    expect(isWritten(wordsAt(`dashboard.period.presets.${preset}`))).toBe(true);
  });

  it.each(PRESETS.filter(hasEarlier))('says in words what %s is measured against', (preset) => {
    expect(isWritten(wordsAt(`dashboard.metrics.against.${preset}`))).toBe(true);
  });

  /** All time has nothing before it, so a sentence naming what it is measured against says nothing true. */
  it('names no comparison for a period with nothing before it', () => {
    const measured = [...PRESETS.filter(hasEarlier), 'chosen'].sort();

    expect([...sentencesUnder('dashboard.metrics.against')].sort()).toEqual(measured);
    expect(PRESETS.filter((preset) => !hasEarlier(preset))).toStrictEqual(['all-time']);
  });

  it('names no period it cannot offer', () => {
    expect([...sentencesUnder('dashboard.period.presets')].sort()).toEqual([...PRESETS].sort());
  });
});

/**
 * Every way a picture can be cut needs a name in the list somebody picks it from, a heading for the
 * first column of its table, and a sentence under each picture saying where its buckets are cut.
 */
describe('the ways this dashboard cuts a picture', () => {
  /** Whatever the list of ways holds besides the ways themselves. */
  const AROUND_THE_WAYS = ['label', 'moves', 'moved'];

  it.each(CHART_BYS)('has a name for cutting a picture by the %s', (cut) => {
    expect(isWritten(wordsAt(`dashboard.chart.by.${cut}`))).toBe(true);
  });

  it('names no way of cutting a picture it cannot offer', () => {
    const named = sentencesUnder('dashboard.chart.by').filter(
      (sentence) => !AROUND_THE_WAYS.includes(sentence),
    );

    expect([...named].sort()).toEqual([...CHART_BYS].sort());
  });

  it.each(GRANULARITIES)('heads the first column of a picture cut by the %s', (granularity) => {
    expect(isWritten(wordsAt(`dashboard.chart.column.${granularity}`))).toBe(true);
  });

  it.each(GRANULARITIES)(
    'says where a picture cut by the %s is cut, in every picture',
    (granularity) => {
      for (const picture of ['activity', 'who', 'people', 'people.activity']) {
        expect(isWritten(wordsAt(`dashboard.chart.${picture}.cut.${granularity}`))).toBe(true);
      }
    },
  );

  it('writes nothing for a way of cutting a picture that does not exist', () => {
    for (const group of [
      'column',
      'activity.cut',
      'who.cut',
      'people.cut',
      'people.activity.cut',
    ]) {
      expect([...sentencesUnder(`dashboard.chart.${group}`)].sort()).toEqual(
        [...GRANULARITIES].sort(),
      );
    }
  });
});

/**
 * Every way a month of the record can be read needs a name in the switch somebody picks it from, a
 * caption for the table it is read in, and a heading for the figure each year ends on.
 */
describe('the ways this dashboard reads a month', () => {
  /** Whatever the switch holds besides the ways themselves. */
  const AROUND_THE_WAYS = ['label'];

  it.each(MEASURES)('has a name for reading a month as %s', (measure) => {
    expect(isWritten(wordsAt(`insights.months.measure.${measure}`))).toBe(true);
  });

  it.each(MEASURES)('captions a table read as %s, and heads what its years end on', (measure) => {
    expect(isWritten(wordsAt(`insights.months.caption.${measure}`))).toBe(true);
    expect(isWritten(wordsAt(`insights.months.${measure}`))).toBe(true);
  });

  it('names no way of reading a month it cannot offer', () => {
    const named = sentencesUnder('insights.months.measure').filter(
      (sentence) => !AROUND_THE_WAYS.includes(sentence),
    );

    expect([...named].sort()).toEqual([...MEASURES].sort());
    expect([...sentencesUnder('insights.months.caption')].sort()).toEqual([...MEASURES].sort());
  });
});

/**
 * The dashboard never reaches back past the days a website still holds, and works out which those
 * are from how long the engine keeps what a period is counted from. The two are one fact kept in
 * two languages, so the engine's own schema is read rather than a copy of the number.
 */
describe('how long the engine keeps what a period is counted from', () => {
  it.each(COUNTED_FROM)('keeps %s exactly as long as the dashboard believes', (file) => {
    const [kept] = declared(file, /^TTL toDateTime\(\w+\) \+ INTERVAL (\d+) MONTH/gm);

    expect(Number(kept)).toBe(KEPT_MONTHS);
  });

  /**
   * The engine works out for itself which months it can still count and where the days it holds in
   * detail begin, from its own figure for how long reports are kept. That figure is the dashboard's
   * too, or a month the screen offers to open would be a month the engine no longer holds.
   */
  it('works out what it still holds from the same number of months as the dashboard', () => {
    const [kept] = declared(RETENTION, /public const int ReportMonths = (\d+);/g);

    expect(Number(kept)).toBe(KEPT_MONTHS);
  });
});
