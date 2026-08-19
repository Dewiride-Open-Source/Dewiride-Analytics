import { describe, expect, it } from 'vitest';
import { byWeight, CATEGORY_TONES, reasonKey, reasonValues } from '@/lib/analytics/verdicts';
import type { SignalDirection, VisitReason } from '@/lib/api/schemas';

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
