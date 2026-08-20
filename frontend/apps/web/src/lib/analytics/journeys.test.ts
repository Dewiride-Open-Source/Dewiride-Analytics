import { describe, expect, it } from 'vitest';
import {
  EVERY_JOURNEY,
  isNarrowed,
  STRENGTH_FLOORS,
  tallyCategories,
  toggleCategory,
} from '@/lib/analytics/journeys';
import type { TrafficGroup } from '@/lib/api/schemas';

const GROUPS: TrafficGroup[] = [
  { category: 'likely-human', strength: 'moderate', sessions: 6, pageViews: 18 },
  { category: 'likely-human', strength: 'strong', sessions: 4, pageViews: 12 },
  { category: 'security-scanner', strength: 'strong', sessions: 9, pageViews: 90 },
];

describe('which conclusions a period reached', () => {
  /**
   * The engine reports a category and the weight behind it together, because those are two
   * different statements. A control that narrows by category wants them added back up.
   */
  it('adds a conclusion up across the weights it was reached with', () => {
    expect(tallyCategories(GROUPS)).toContainEqual({ category: 'likely-human', journeys: 10 });
  });

  it('offers the busiest first', () => {
    expect(tallyCategories(GROUPS).map((tally) => tally.category)).toEqual([
      'likely-human',
      'security-scanner',
    ]);
  });

  it('offers only what actually happened', () => {
    expect(tallyCategories(GROUPS)).toHaveLength(2);
    expect(tallyCategories([])).toEqual([]);
  });

  it('puts two conclusions of the same size in a settled order', () => {
    const tied: TrafficGroup[] = [
      { category: 'unknown', strength: 'none', sessions: 3, pageViews: 3 },
      { category: 'content-scraper', strength: 'weak', sessions: 3, pageViews: 3 },
    ];

    expect(tallyCategories(tied).map((tally) => tally.category)).toEqual([
      'content-scraper',
      'unknown',
    ]);
  });
});

describe('narrowing a list of journeys', () => {
  it('starts with nothing narrowed', () => {
    expect(isNarrowed(EVERY_JOURNEY)).toBe(false);
  });

  it('counts any one of the three as narrowed', () => {
    expect(isNarrowed({ ...EVERY_JOURNEY, categories: ['likely-human'] })).toBe(true);
    expect(isNarrowed({ ...EVERY_JOURNEY, leastStrength: 'moderate' })).toBe(true);
    expect(isNarrowed({ ...EVERY_JOURNEY, leastPages: 1 })).toBe(true);
  });

  it('adds a conclusion and takes it off again', () => {
    const one = toggleCategory(EVERY_JOURNEY, 'likely-human');
    const two = toggleCategory(one, 'known-ai-crawler');

    expect(two.categories).toEqual(['likely-human', 'known-ai-crawler']);
    expect(toggleCategory(two, 'likely-human').categories).toEqual(['known-ai-crawler']);
  });

  it('leaves everything else alone', () => {
    const narrowed = { ...EVERY_JOURNEY, leastStrength: 'strong', leastPages: 2 } as const;

    expect(toggleCategory(narrowed, 'unknown')).toMatchObject({
      leastStrength: 'strong',
      leastPages: 2,
    });
  });

  /**
   * Confirmed identity is reserved for an operator's own published addresses, which nothing yet
   * reaches, and "nothing to go on" is every visit. Either would be a choice that can only come
   * back empty or change nothing.
   */
  it('offers no floor that could never do anything', () => {
    expect(STRENGTH_FLOORS).not.toContain('verified');
    expect(STRENGTH_FLOORS).not.toContain('none');
  });
});
