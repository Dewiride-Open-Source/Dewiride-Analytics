'use client';

import { useSyncExternalStore } from 'react';

/**
 * A choice the browser remembers on somebody's behalf.
 *
 * Kept on the machine rather than on the account. Which website somebody was last looking at, how
 * they like a chart drawn, which stretch of days they keep coming back to — each is true of the
 * machine they are sitting at rather than of who they are, and keeping any of them centrally
 * would let one person's tab change what another sees.
 *
 * Read as an external store rather than copied into state, for two reasons. Nothing is known
 * about the browser's storage while a screen is being rendered on the server, and this is the one
 * shape that lets the server and the browser agree on a first paint and then let the browser
 * correct it. And a second tab that changes the choice tells the first, because the browser
 * announces the write.
 */
export interface Remembered<T> {
  /** Where the last choice is written. */
  readonly key: string;
  /** What is assumed until the browser has been asked, and what an unreadable record falls back to. */
  readonly fallback: T;
  /** What the browser has recorded, or the fallback where it holds nothing this reader wrote. */
  readonly read: () => T;
  /** What the server has to assume, so that both sides draw the same thing first. Always the fallback. */
  readonly assume: () => T;
  /** Records a choice, and tells every open tab about it. */
  readonly write: (value: T) => void;
  readonly subscribe: (listener: () => void) => () => void;
}

/**
 * One remembered choice.
 *
 * @param key Where it is written.
 * @param accept Reads a recorded value back, or refuses one that is not a value this store holds —
 * a record is written by an earlier version of this product as readily as by this one.
 * @param fallback What is assumed where nothing is recorded, or where what is recorded is refused.
 * @param serialize How a value is written; nothing forgets the choice altogether.
 */
export function remembered<T>(
  key: string,
  accept: (raw: string) => T | null,
  fallback: T,
  serialize: (value: T) => string | null = plainly,
): Remembered<T> {
  const listeners = new Set<() => void>();
  let lastRaw: string | null | undefined;
  let lastValue: T = fallback;

  // The reading is kept against the record it was made from, so the same record hands back the
  // same value. A store is read on every render, and a value built afresh each time would never
  // be equal to the one before it, which is a store that never settles.
  function read(): T {
    const raw = window.localStorage.getItem(key);

    if (raw !== lastRaw) {
      lastRaw = raw;
      lastValue = raw === null ? fallback : (accept(raw) ?? fallback);
    }

    return lastValue;
  }

  function write(value: T): void {
    const written = serialize(value);

    if (written === null) {
      window.localStorage.removeItem(key);
    } else {
      window.localStorage.setItem(key, written);
    }

    for (const listener of listeners) {
      listener();
    }
  }

  function subscribe(listener: () => void): () => void {
    listeners.add(listener);
    window.addEventListener('storage', listener);

    return () => {
      listeners.delete(listener);
      window.removeEventListener('storage', listener);
    };
  }

  return { key, fallback, read, assume: () => fallback, write, subscribe };
}

/** A value written as itself, which is what a word is; nothing at all forgets the choice. */
function plainly<T>(value: T): string | null {
  return value === null || value === undefined ? null : String(value);
}

/** Accepts one of a closed set of words, and refuses everything else. */
export function oneOf<T extends string>(words: readonly T[]): (raw: string) => T | null {
  return (raw) => words.find((one) => one === raw) ?? null;
}

/** Reads a remembered choice, and records a new one. */
export function useRemembered<T>(store: Remembered<T>): readonly [T, (value: T) => void] {
  const value = useSyncExternalStore(store.subscribe, store.read, store.assume);

  return [value, store.write];
}
