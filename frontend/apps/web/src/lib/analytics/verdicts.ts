import type { TrafficCategory, VisitReason } from '@/lib/api/schemas';

/**
 * How a verdict reaches the screen.
 *
 * The engine answers in a fixed vocabulary of codes, and none of them is ever shown. This turns
 * each one into the key of a sentence in the reader's own language, and decides how a category
 * should read as a colour. Kept apart from the components so the rules can be checked without
 * rendering anything.
 */

/** What a category means for the person whose website it is. */
export type VerdictTone = 'people' | 'automation' | 'unwanted' | 'unclear';

/**
 * The tone each category is shown in.
 *
 * Four tones rather than fourteen colours, because the colour answers the question somebody
 * glances at a chart to ask — is this my audience, is it machinery, is it something I would
 * rather not have, or can it not be said — while the row beside it still names the category
 * exactly. Nothing here merges two categories: a crawler that says it is an AI one and a
 * confirmed AI one share a colour and never share a label.
 */
export const CATEGORY_TONES: Readonly<Record<TrafficCategory, VerdictTone>> = {
  'likely-human': 'people',
  'known-search-crawler': 'automation',
  'known-ai-crawler': 'automation',
  'suspected-ai-crawler': 'automation',
  'known-automated-service': 'automation',
  'browser-automation': 'automation',
  'generic-web-crawler': 'automation',
  'monitoring-or-synthetic': 'automation',
  'content-scraper': 'unwanted',
  'security-scanner': 'unwanted',
  'suspicious-automation': 'unwanted',
  'likely-analytics-spam': 'unwanted',
  'insufficient-evidence': 'unclear',
  unknown: 'unclear',
};

/**
 * The order the tones are read in.
 *
 * The people a website is for come first, so that in a stack they sit on the axis with a straight
 * edge to measure against and everything else piles above them.
 */
export const TONE_ORDER = [
  'people',
  'automation',
  'unwanted',
  'unclear',
] as const satisfies readonly VerdictTone[];

/**
 * The design token each tone is drawn in.
 *
 * The single statement of which colour a tone carries. `TONE_FILLS` says the same thing again as
 * class names, because the styling engine cannot see a name worked out while the page is running,
 * and a test holds the two side by side so the second copy cannot drift from this one.
 */
export const TONE_TOKENS: Readonly<Record<VerdictTone, string>> = {
  people: '--positive',
  automation: '--accent',
  unwanted: '--danger',
  unclear: '--foreground-subtle',
};

/** One tone and the visits a period judged into it. */
export interface TonePortion {
  readonly tone: VerdictTone;
  readonly sessions: number;
}

/**
 * The least that has to be known about something for it to be counted into a tone.
 *
 * A category and a count, at whatever size the caller holds them: a period's worth of visits
 * folded by category, or one judged thing standing for itself. Everything else a caller has — how
 * strong the evidence was, how many pages were read — has no bearing on which colour the answer is
 * drawn in, so asking for it would only tie the rule to one shape of caller.
 */
export interface Judged {
  readonly category: TrafficCategory;
  /** How many visits it stands for, which is one where it is one visitor. */
  readonly sessions: number;
}

/**
 * How a set of judged things divides between the four tones, in reading order.
 *
 * The summary above a list that names every category exactly — the same fold the chart on the
 * overview makes, over a period counted once rather than bucket by bucket. A tone nothing was
 * judged into is left out rather than carried as a nought, so a website nobody has scraped shows
 * neither a slice for it nor a name.
 */
export function tonesIn(judged: readonly Judged[]): readonly TonePortion[] {
  const counted = new Map<VerdictTone, number>();

  for (const one of judged) {
    const tone = CATEGORY_TONES[one.category];

    counted.set(tone, (counted.get(tone) ?? 0) + one.sessions);
  }

  return TONE_ORDER.flatMap((tone) => {
    const sessions = counted.get(tone);

    return sessions === undefined ? [] : [{ tone, sessions }];
  });
}

/**
 * The two observations whose sentence depends on a value inside them.
 *
 * A crawler that fetches pages to train a model and one that fetches them to build search results
 * are the same observation about two different things, and a language that inflects would not let
 * one sentence cover both by substituting a noun. Each reading therefore gets a whole sentence of
 * its own, and this is what picks between them.
 */
const DECLARED_CRAWLER = 'identity.declared_crawler';
const DECLARED_TOOL = 'identity.declared_tool';
const READ_TIME = 'engagement.read_time';

/**
 * The observation that the address belongs to a company's own crawlers, which is what lets a row
 * name it.
 */
const CONFIRMED_CRAWLER = 'identity.confirmed_crawler';

/**
 * The point at which a length of time is better said in minutes.
 *
 * Nobody describes their own afternoon as two hundred seconds. Below this the seconds are the
 * natural reading and rounding them would throw away most of the answer; above it the minutes are,
 * and the sentence says "about" because it has rounded.
 */
const LONG_READ_SECONDS = 120;

const PURPOSES: Readonly<Record<string, string>> = {
  'ai-training': 'aiTraining',
  'ai-assistant': 'aiAssistant',
  'ai-search': 'aiSearch',
  'search-index': 'searchIndex',
  advertising: 'advertising',
  'site-tooling': 'siteTooling',
  'social-preview': 'socialPreview',
  monitoring: 'monitoring',
  archival: 'archival',
  'seo-audit': 'seoAudit',
  unstated: 'unstated',
};

const TOOL_KINDS: Readonly<Record<string, string>> = {
  'headless-browser': 'headlessBrowser',
  script: 'script',
  'scraping-framework': 'scrapingFramework',
  'command-line': 'commandLine',
};

/**
 * The values that are numbers.
 *
 * They arrive as text so that a verdict reads the same wherever it was reached, and a sentence
 * that says "3 pages" rather than "3 page" has to have them back as numbers to count with.
 */
const COUNTED: ReadonlySet<string> = new Set([
  'seconds',
  'percent',
  'pageCount',
  'missingCount',
  'attemptCount',
  'perMinute',
]);

/**
 * The sentence an observation is written as.
 *
 * @param reason The observation, as the engine reported it.
 * @returns The key of its sentence in the catalogue.
 */
export function reasonKey(reason: VisitReason): string {
  if (reason.code === DECLARED_CRAWLER) {
    return `${DECLARED_CRAWLER}.${PURPOSES[reason.values.purpose ?? ''] ?? 'unstated'}`;
  }

  if (reason.code === DECLARED_TOOL) {
    return `${DECLARED_TOOL}.${TOOL_KINDS[reason.values.kind ?? ''] ?? 'other'}`;
  }

  if (reason.code === READ_TIME) {
    return `${READ_TIME}.${secondsIn(reason) >= LONG_READ_SECONDS ? 'minutes' : 'seconds'}`;
  }

  return reason.code;
}

/**
 * The values that sentence substitutes, with the counted ones back as numbers.
 *
 * A length of time long enough to be worth saying in minutes is offered in minutes as well, so a
 * sentence can be written in whichever of the two a reader would actually use. A shorter one is
 * not: rounding forty seconds to a minute would throw away most of what was measured.
 */
export function reasonValues(reason: VisitReason): Record<string, string | number> {
  const given = Object.fromEntries(
    Object.entries(reason.values).map(([name, value]) => [
      name,
      COUNTED.has(name) && /^-?\d+$/.test(value) ? Number(value) : value,
    ]),
  );

  const seconds = secondsIn(reason);

  return seconds >= LONG_READ_SECONDS ? { ...given, minutes: Math.round(seconds / 60) } : given;
}

/** How long something took, or nothing when it was not measured. */
function secondsIn(reason: VisitReason): number {
  const seconds = Number(reason.values.seconds ?? '');

  return Number.isFinite(seconds) ? seconds : 0;
}

/**
 * Observations in the order they are worth reading.
 *
 * How much an observation counted is never shown — it is a weight out of a hundred, and a number
 * out of a hundred beside a verdict reads as a probability, which is exactly what this product
 * refuses to imply. It decides the order and nothing else.
 */
export function byWeight(reasons: readonly VisitReason[]): readonly VisitReason[] {
  return [...reasons].sort((first, second) => second.weight - first.weight);
}

/**
 * The name a confirmed crawler is called on a row, or nothing where the visitor is not one.
 *
 * Only a confirmed identity is named. The token a crawler declared is what people know it by — a
 * name from this product's own catalogue, never the words the request arrived with — but it is
 * taken onto the row only once the company behind it has vouched for the address, and the operator
 * stands in where a confirmed crawler declared no token this product recognises. A name any visitor
 * can claim is never lifted onto the row, however loudly it was claimed.
 *
 * Takes the observations rather than a visit, so a row that is still being watched can be named
 * the same way as one that has finished.
 */
export function namedAs(supporting: readonly VisitReason[]): string | null {
  const confirmed = supporting.find((reason) => reason.code === CONFIRMED_CRAWLER);

  if (confirmed === undefined) {
    return null;
  }

  const declared = supporting.find((reason) => reason.code === DECLARED_CRAWLER);

  return declared?.values.token || confirmed.values.operator || null;
}
