import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DEFAULT_PERIOD, type Period, readPeriod, writePeriod } from '@/lib/analytics/period';
import { oneOf, remembered } from '@/lib/preferences/remembered';

/** A choice between two words, opening on the first. */
const PICK = remembered<'a' | 'b'>('test.pick', oneOf(['a', 'b'] as const), 'a');

/** A choice that is an object rather than a word, so that it has to be written and read back. */
const WHEN = remembered<Period>('test.when', readPeriod, DEFAULT_PERIOD, writePeriod);

/** A choice that may be nothing at all, which is what it opens on. */
const MAYBE = remembered<'x' | null>('test.maybe', oneOf(['x'] as const), null);

const YESTERDAY: Period = { kind: 'preset', preset: 'yesterday' };

// The browser's storage outlives a test, so each one starts with nothing remembered.
beforeEach(() => {
  window.localStorage.clear();
});

describe('a choice the browser remembers', () => {
  it('assumes the fallback where the browser holds nothing', () => {
    expect(PICK.read()).toBe('a');
  });

  it('reads back what was written', () => {
    PICK.write('b');

    expect(window.localStorage.getItem('test.pick')).toBe('b');
    expect(PICK.read()).toBe('b');
  });

  /** A record is written by an earlier version of this product as readily as by this one. */
  it('falls back where what was written cannot be read', () => {
    window.localStorage.setItem('test.pick', 'z');

    expect(PICK.read()).toBe('a');
  });

  /**
   * A store is read on every render, and a value built afresh each time would never be equal to
   * the one before it — which is a store that never settles.
   */
  it('hands back the same reading until something changes', () => {
    WHEN.write(YESTERDAY);

    const first = WHEN.read();

    expect(first).toEqual(YESTERDAY);
    expect(WHEN.read()).toBe(first);
  });

  it('forgets a choice written as nothing', () => {
    MAYBE.write('x');
    MAYBE.write(null);

    expect(window.localStorage.getItem('test.maybe')).toBeNull();
    expect(MAYBE.read()).toBeNull();
  });

  it('tells every subscriber about a write', () => {
    const listener = vi.fn();
    const stop = PICK.subscribe(listener);

    PICK.write('b');

    expect(listener).toHaveBeenCalledTimes(1);

    stop();
    PICK.write('a');

    expect(listener).toHaveBeenCalledTimes(1);
  });

  /**
   * Nothing is known about the browser's storage while a screen is being rendered on the server,
   * so the server assumes the fallback whatever the browser holds, and both sides draw the same
   * thing first.
   */
  it('assumes the same thing on the server every time', () => {
    PICK.write('b');

    expect(PICK.assume()).toBe('a');
    expect(WHEN.assume()).toBe(DEFAULT_PERIOD);
    expect(WHEN.assume()).toBe(WHEN.assume());
  });
});
