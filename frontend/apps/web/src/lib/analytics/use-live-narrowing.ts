'use client';

import { parseAsStringLiteral, useQueryStates } from 'nuqs';
import { useCallback } from 'react';
import { NARROWING_NAMES } from '@/lib/analytics/journeys';
import { type LiveNarrowing, RECOGNISABLE_NOW } from '@/lib/analytics/live';
import { type SourceKind, sourceKindSchema, type TrafficCategory } from '@/lib/api/schemas';

/**
 * What the list of who is here is kept to, carried in the address.
 *
 * Written over the entry the reader is on rather than as a new one: a chip is pressed and let go in
 * the same place, and the way back should leave the screen rather than replay the presses. Never
 * remembered either — a list kept to one kind of visitor answers a question about this moment, not
 * a habit to open the screen on next time.
 */

/**
 * Each of the two choices, read only where it is one this screen can make.
 *
 * A link asking for a kind of visitor this screen never recognises — a person, or nothing that
 * could be told — is read as everybody, rather than as a list kept to something no row here will
 * ever be.
 */
const KEPT_TO = {
  category: parseAsStringLiteral(RECOGNISABLE_NOW),
  sourceKind: parseAsStringLiteral(sourceKindSchema.options),
};

/**
 * Spelled exactly as the User journey spells the same two narrowings, so a link naming a kind of
 * visitor means one thing on either screen.
 */
const URL_KEYS = {
  category: NARROWING_NAMES.categories,
  sourceKind: NARROWING_NAMES.sourceKinds,
};

export interface KeptList {
  /** What the list is kept to, which is everybody until somebody says otherwise. */
  readonly narrowing: LiveNarrowing;
  readonly narrow: (narrowing: LiveNarrowing) => void;
}

/**
 * Reads what the list is kept to out of the address, and writes another choice back into it.
 *
 * A link naming both a kind of visitor and a way of arriving is kept to the kind of visitor alone:
 * one chip at a time is the rule, and an address cannot press two.
 */
export function useLiveNarrowing(): KeptList {
  const [{ category, sourceKind }, write] = useQueryStates(KEPT_TO, {
    urlKeys: URL_KEYS,
    history: 'replace',
  });

  const narrow = useCallback(
    (narrowing: LiveNarrowing) => {
      void write({
        category: narrowing?.by === 'category' ? narrowing.category : null,
        sourceKind: narrowing?.by === 'sourceKind' ? narrowing.kind : null,
      });
    },
    [write],
  );

  return { narrowing: narrowingOf(category, sourceKind), narrow };
}

/** The one choice an address makes, where it makes one. */
function narrowingOf(
  category: TrafficCategory | null,
  sourceKind: SourceKind | null,
): LiveNarrowing {
  if (category !== null) {
    return { by: 'category', category };
  }

  return sourceKind === null ? null : { by: 'sourceKind', kind: sourceKind };
}
