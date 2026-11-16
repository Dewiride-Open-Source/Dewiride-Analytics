import { describe, expect, it } from 'vitest';
import {
  CHOSEN_EARLIER_DAYS,
  COMPARED_WITH,
  type ComparedWith,
  comparisonOf,
} from '@/lib/analytics/compared-with';

/** What a period may be set beside, as the screen is handed it: which, and nothing else here. */
function offering(...against: readonly ComparedWith[]) {
  return against.map((one) => ({ against: one }));
}

describe('the earlier days a period is set beside', () => {
  it('is the stretch somebody chose where the period offers it', () => {
    expect(comparisonOf(offering('before', 'day-before'), 'day-before')).toStrictEqual({
      against: 'day-before',
    });
  });

  it("is the period's own where nothing is chosen", () => {
    expect(comparisonOf(offering('before', 'day-before'), null)).toStrictEqual({
      against: 'before',
    });
  });

  /** A choice the period cannot honour is set aside, and the period's own stands in for it. */
  it("is the period's own where the one chosen is not offered", () => {
    expect(comparisonOf(offering('before'), '4-weeks-before')).toStrictEqual({ against: 'before' });
  });

  /**
   * A day whose own stretch reaches back past the days still kept may still be offered the day
   * before, and is set beside that rather than beside nothing.
   */
  it('is the first stretch offered where the period is not offered its own', () => {
    expect(comparisonOf(offering('day-before'), null)).toStrictEqual({ against: 'day-before' });
  });

  it('is nothing where the period is offered nothing', () => {
    expect(comparisonOf(offering(), 'day-before')).toBeNull();
    expect(comparisonOf(offering(), null)).toBeNull();
  });
});

describe('what an address can name', () => {
  /** An address that says nothing already means the period's own, so it never says that. */
  it("is every stretch but the period's own", () => {
    expect(CHOSEN_EARLIER_DAYS).toStrictEqual(COMPARED_WITH.filter((one) => one !== 'before'));
  });
});
