'use client';

import {
  createMultiParser,
  parseAsNumberLiteral,
  parseAsStringLiteral,
  useQueryStates,
} from 'nuqs';
import {
  type JourneyFilters,
  MOST_VALUES,
  NARROWING_NAMES,
  PAGE_FLOORS,
  STRENGTH_FLOORS,
} from '@/lib/analytics/journeys';
import { deviceKindSchema, trafficCategorySchema, visitSourceKindSchema } from '@/lib/api/schemas';

/**
 * What a list of journeys is narrowed to, kept in the address itself.
 *
 * A reader who has worked a period down to the visits they were looking for has answered a
 * question, and the answer is worth keeping: the address is what they bookmark, what they send to
 * somebody, and what survives a reload. Held as state on the screen instead, all three of those
 * end the moment they close the tab.
 *
 * Every set is written as its own name repeated once per value — `?country=IN&country=FR` — which
 * is the shape the engine is asked in as well. It is also the only shape with nothing to escape:
 * a separated list has to decide what a value may not contain, and one of these sets holds the
 * pages a visitor asked the website for, which may contain anything at all.
 */

/**
 * A set of values, read out of an address and written back into one.
 *
 * An address is written by whoever sent the link, so nothing in it is taken on trust. A value the
 * reader cannot make sense of is left out rather than thrown over, and no more than the engine
 * accepts is carried through — a link naming a hundred countries opens on the first of them
 * instead of on a refusal, which is the right answer to a link somebody edited by hand.
 *
 * @param reads One value as it appears in the address, or nothing where that is not one.
 */
function chosenFrom<T extends string>(reads: (written: string) => T | null) {
  return createMultiParser<readonly T[]>({
    parse: (written) =>
      written
        .flatMap((one) => {
          const value = reads(one);

          return value === null ? [] : [value];
        })
        .slice(0, MOST_VALUES),
    serialize: (values) => [...values],
    // Compared by what they hold rather than as objects: read back out of an address they are
    // never the same array twice, and without this the address would never be free of a
    // narrowing nobody asked for.
    eq: (one, other) =>
      one.length === other.length && one.every((value, at) => value === other[at]),
  }).withDefault([]);
}

/**
 * Whatever the website itself recorded, which nobody can write down in advance.
 *
 * Nothing to check against, so nothing is: a browser, a town or a page is whatever the traffic
 * turned out to hold. The empty value is a value like any other and means the visits nothing
 * could be established about, which is a real question and a different one from asking for all
 * of them.
 */
const RECORDED = chosenFrom<string>((written) => written);

/** One of a vocabulary the engine names, and nothing else. */
function oneOf<T extends string>(words: readonly T[]) {
  return chosenFrom<T>((written) => words.find((word) => word === written) ?? null);
}

/**
 * Every narrowing, and how each is read out of an address.
 *
 * Named after the parts of the narrowing rather than after the address, and mapped onto the
 * address by the same list the engine's own question is written from, so the two cannot drift.
 */
const NARROWING = {
  categories: oneOf(trafficCategorySchema.options),
  devices: oneOf(deviceKindSchema.options),
  sourceKinds: oneOf(visitSourceKindSchema.options),
  browsers: RECORDED,
  systems: RECORDED,
  countries: RECORDED,
  towns: RECORDED,
  networks: RECORDED,
  sources: RECORDED,
  entryPages: RECORDED,
  leastStrength: parseAsStringLiteral(STRENGTH_FLOORS),
  leastPages: parseAsNumberLiteral(PAGE_FLOORS).withDefault(0),
};

export interface ChosenJourneys {
  /** What the list is narrowed to, which is everything until somebody says otherwise. */
  readonly filters: JourneyFilters;
  readonly narrow: (filters: JourneyFilters) => void;
}

/**
 * Reads the narrowing out of the address, and writes one back into it.
 *
 * Written over the entry the reader is already on rather than as a new one, unlike the period.
 * Narrowing is a handful of small presses rather than one decision, and a reader who ticked six
 * things should not have to press the browser's own way back six times to leave the screen —
 * especially when every one of those six is on screen as something a single press takes off.
 */
export function useJourneyFilters(): ChosenJourneys {
  const [filters, write] = useQueryStates(NARROWING, { urlKeys: NARROWING_NAMES });

  return {
    filters,
    narrow: (chosen) => {
      void write(chosen);
    },
  };
}
