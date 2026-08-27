import { describe, expect, it } from 'vitest';
import { TONE_FILLS } from '@/components/dashboard/verdict-badge';
import {
  byWeight,
  CATEGORY_TONES,
  reasonKey,
  reasonValues,
  TONE_ORDER,
  TONE_TOKENS,
  tonesIn,
} from '@/lib/analytics/verdicts';
import type {
  EvidenceStrength,
  SignalDirection,
  TrafficCategory,
  VisitReason,
} from '@/lib/api/schemas';

describe('the colour a verdict is shown in', () => {
  /**
   * Which token a tone is stated once, and again as a class name because the styling engine
   * cannot see a name worked out while the page is running. This is what stops the second copy
   * drifting from the first and leaving a chart drawn in one colour beside a pill in another.
   */
  it('is the same colour on a chart as it is on a pill', () => {
    for (const tone of TONE_ORDER) {
      expect(TONE_FILLS[tone]).toBe(`bg-${TONE_TOKENS[tone].replace('--', '')}`);
    }
  });

  it('covers every meaning a category can carry, and nothing else', () => {
    expect(new Set(TONE_ORDER)).toStrictEqual(new Set(Object.values(CATEGORY_TONES)));
    expect(Object.keys(TONE_TOKENS).sort()).toStrictEqual([...TONE_ORDER].sort());
  });
});

function reason(
  code: string,
  values: Record<string, string> = {},
  weight = 50,
  direction: SignalDirection = 'toward-automation',
): VisitReason {
  return { code, direction, weight, values };
}

describe('choosing an observation a sentence', () => {
  it('picks the sentence written for what the crawler said it was for', () => {
    const found = reasonKey(
      reason('identity.declared_crawler', {
        operator: 'OpenAI',
        token: 'GPTBot',
        purpose: 'ai-training',
      }),
    );

    expect(found).toBe('identity.declared_crawler.aiTraining');
  });

  it('falls back to the plainest sentence when no purpose was stated', () => {
    expect(reasonKey(reason('identity.declared_crawler', { token: 'SomeBot' }))).toBe(
      'identity.declared_crawler.unstated',
    );
  });

  it('picks the sentence written for the kind of tool that named itself', () => {
    expect(reasonKey(reason('identity.declared_tool', { kind: 'headless-browser' }))).toBe(
      'identity.declared_tool.headlessBrowser',
    );
  });

  it('has somewhere to go when a later release names a kind this one has never heard of', () => {
    expect(reasonKey(reason('identity.declared_tool', { kind: 'quantum-fetcher' }))).toBe(
      'identity.declared_tool.other',
    );
  });

  it('says a short read in seconds, which is how anybody would say it', () => {
    expect(reasonKey(reason('engagement.read_time', { seconds: '40' }))).toBe(
      'engagement.read_time.seconds',
    );
  });

  it('says a long read in minutes rather than in two hundred seconds', () => {
    expect(reasonKey(reason('engagement.read_time', { seconds: '212' }))).toBe(
      'engagement.read_time.minutes',
    );
  });

  it('uses the observation itself for everything that reads the same however it happened', () => {
    expect(reasonKey(reason('engagement.pointer_used'))).toBe('engagement.pointer_used');
  });
});

describe('the values a sentence counts with', () => {
  it('hands back counted values as numbers, so a sentence can say one page or two', () => {
    const found = reasonValues(
      reason('retrieval.rate', { pageCount: '120', seconds: '30', perMinute: '240' }),
    );

    expect(found).toEqual({ pageCount: 120, seconds: 30, perMinute: 240 });
  });

  it('offers a length of time in minutes as well, so a sentence can use either', () => {
    expect(reasonValues(reason('engagement.read_time', { seconds: '212' }))).toEqual({
      seconds: 212,
      minutes: 4,
    });
  });

  it('offers no minutes for a stretch too short to be worth rounding', () => {
    expect(reasonValues(reason('engagement.read_time', { seconds: '40' }))).toEqual({
      seconds: 40,
    });
  });

  it('leaves everything else exactly as it arrived', () => {
    const found = reasonValues(
      reason('identity.declared_crawler', { operator: 'OpenAI', token: 'GPTBot' }),
    );

    expect(found).toEqual({ operator: 'OpenAI', token: 'GPTBot' });
  });

  it('leaves a counted value alone when it did not arrive as a whole number', () => {
    expect(reasonValues(reason('retrieval.rate', { pageCount: 'lots' }))).toEqual({
      pageCount: 'lots',
    });
  });
});

describe('the order observations are read in', () => {
  it('puts what counted most first', () => {
    const ordered = byWeight([reason('a', {}, 20), reason('b', {}, 85), reason('c', {}, 50)]);

    expect(ordered.map((found) => found.code)).toEqual(['b', 'c', 'a']);
  });

  it('leaves the list it was given untouched', () => {
    const given = [reason('a', {}, 20), reason('b', {}, 85)];

    byWeight(given);

    expect(given.map((found) => found.code)).toEqual(['a', 'b']);
  });
});

describe('the tone a category is shown in', () => {
  it('separates the people a website is for from everything else', () => {
    expect(CATEGORY_TONES['likely-human']).toBe('people');
    expect(CATEGORY_TONES['generic-web-crawler']).toBe('automation');
    expect(CATEGORY_TONES['security-scanner']).toBe('unwanted');
    expect(CATEGORY_TONES.unknown).toBe('unclear');
  });

  it('never lets a crawler that says it is an AI one be read as a person', () => {
    expect(CATEGORY_TONES['suspected-ai-crawler']).not.toBe('people');
    expect(CATEGORY_TONES['known-ai-crawler']).not.toBe('people');
  });
});

describe('how a period divides between the four tones', () => {
  const GROUPS = [
    group('likely-human', 'moderate', 6),
    group('known-search-crawler', 'strong', 3),
    group('known-ai-crawler', 'strong', 2),
    group('suspected-ai-crawler', 'weak', 1),
    group('unknown', 'weak', 4),
  ];

  it('adds up every category that shares a tone', () => {
    expect(tonesIn(GROUPS)).toStrictEqual([
      { tone: 'people', sessions: 6 },
      { tone: 'automation', sessions: 6 },
      { tone: 'unclear', sessions: 4 },
    ]);
  });

  /**
   * A website nobody has scraped shows no red anywhere: not a slice on the ring, and not a name
   * for one in the list beside it.
   */
  it('leaves out a tone the period never held', () => {
    expect(tonesIn(GROUPS).map((portion) => portion.tone)).not.toContain('unwanted');
  });

  /** The people a website is for come first, wherever they happen to fall in the answer. */
  it('reads in the same order everywhere else does', () => {
    const found = tonesIn([group('unknown', 'weak', 4), group('likely-human', 'strong', 1)]);

    expect(found.map((portion) => portion.tone)).toStrictEqual(['people', 'unclear']);
  });

  it('has nothing to divide when nothing has been judged', () => {
    expect(tonesIn([])).toStrictEqual([]);
  });
});

function group(category: TrafficCategory, strength: EvidenceStrength, sessions: number) {
  return { category, strength, sessions, pageViews: sessions * 3 };
}
