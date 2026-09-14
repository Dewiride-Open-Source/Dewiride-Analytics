'use client';

import { type Options, type SingleParserBuilder, useQueryState } from 'nuqs';
import { useCallback, useEffect, useRef } from 'react';
import { type Remembered, useRemembered } from '@/lib/preferences/remembered';

/**
 * A choice about what is being looked at, carried in the address and remembered by the browser.
 *
 * The address is where such a choice belongs: a link somebody sends opens on what they were
 * looking at, and the browser's own way back undoes a change to it. But an address that says
 * nothing is the ordinary address of every screen, and somebody who chose a stretch of days
 * yesterday should not have to choose it again this morning. So the browser remembers the last
 * deliberate choice, and an address that says nothing is given it — written in quietly, so that
 * the link in the bar still says what is on the screen, and without an entry in the history,
 * because nothing anybody pressed put it there.
 *
 * Given it once, on arrival. An address that falls silent afterwards is one the reader has come
 * back to — the way back from a choice made on the plain address of the screen — and it means
 * what it meant before the choice, which is the fallback. Written into again, it would swallow the
 * entry they went back to, and the button they reached for would appear to do nothing.
 *
 * The address always wins over what was remembered. A link names what its sender was looking at,
 * and the reader's own habit gives way to it for as long as the link is being followed.
 */

/** Overwrites the entry somebody is already on rather than adding one: nothing they pressed put the value there. */
const SEEDED: Options = { history: 'replace' };

/** Whether two readings are the same, with absence only ever equal to absence. */
function same<T>(written: SingleParserBuilder<T>, one: T | null, other: T | null): boolean {
  return one === null || other === null ? one === other : written.eq(one, other);
}

/**
 * Reads a choice out of the address, or out of what the browser remembers where the address says
 * nothing, and writes a new one to both.
 *
 * @param key What the choice is called in an address.
 * @param written How it is written there and read back, carrying its own history option.
 * @param store Where the browser remembers it, whose fallback is what an address that says
 * nothing means.
 * @param seeding Whether a remembered choice is written into an address that says nothing. Asked
 * for by the screens the choice is the subject of, and by nothing else: a screen that merely reads
 * the choice must not write it into its own address.
 */
export function useRememberedAddress<T extends object | string, R extends T | null = T>(
  key: string,
  written: SingleParserBuilder<T>,
  store: Remembered<R>,
  seeding = true,
): readonly [T | R, (value: R) => void] {
  // No default on the parser, so that an address saying nothing can be told from one saying the
  // fallback: only the former is given what was remembered.
  const [inAddress, write] = useQueryState(key, written);
  const [recalled, remember] = useRemembered(store);
  const value: T | R = inAddress ?? recalled;

  // What the address said the last time it was looked at, or nothing at all before it ever was.
  // Arrival is the one look with nothing before it, and an address that has fallen silent is one
  // that said something the look before.
  const lastSaid = useRef<T | null | undefined>(undefined);

  useEffect(() => {
    if (!seeding) {
      return;
    }

    const before = lastSaid.current;

    lastSaid.current = inAddress;

    if (before === undefined) {
      // Reads the store itself rather than what was rendered: on a screen the server drew first,
      // the render before this runs still carries the fallback the server assumed.
      const held = store.read();

      if (inAddress === null && !same(written, held, store.fallback)) {
        void write(held, SEEDED);
      }

      return;
    }

    // Recorded rather than merely shown, so that a screen which only reads the choice — the bar
    // across the top, whose links carry the period — stays in step with the one it is the subject
    // of. The store is what a silent address means to both of them.
    if (inAddress === null && before !== null) {
      remember(store.fallback);
    }
  }, [seeding, inAddress, store, written, write, remember]);

  const choose = useCallback(
    (next: R) => {
      remember(next);
      // The fallback is left out of the address, so the plain address of a screen stays plain.
      void write(same(written, next, store.fallback) ? null : next);
    },
    [remember, write, written, store],
  );

  return [value, choose];
}
