import { describe, expect, it } from 'vitest';
import { splitDuration } from '@/lib/analytics/duration';

describe('writing a length of time out', () => {
  it('gives seconds alone for anything under a minute', () => {
    expect(splitDuration(42_000)).toStrictEqual({ minutes: 0, seconds: 42 });
  });

  it('gives whole minutes and the seconds left over', () => {
    expect(splitDuration(72_000)).toStrictEqual({ minutes: 1, seconds: 12 });
  });

  it('rounds to the nearest second rather than dropping the remainder', () => {
    expect(splitDuration(1600)).toStrictEqual({ minutes: 0, seconds: 2 });
  });

  it('carries a rounded-up minute rather than reporting sixty seconds', () => {
    expect(splitDuration(119_600)).toStrictEqual({ minutes: 2, seconds: 0 });
  });

  it('says nothing rather than nought when there is nothing to say', () => {
    expect(splitDuration(0)).toStrictEqual({ minutes: 0, seconds: 0 });
  });

  /**
   * Attention is measured by a clock in somebody else's browser, and a page restored from the
   * browser's own store can hand back a reading taken before the one it is compared against.
   */
  it('never reports a length of time that runs backwards', () => {
    expect(splitDuration(-5000)).toStrictEqual({ minutes: 0, seconds: 0 });
  });
});
