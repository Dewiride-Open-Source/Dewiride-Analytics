'use client';

import { useTranslations } from 'next-intl';
import { fromWords, readWords } from '@/components/dashboard/visit-trail';
import { type CameFrom, placeOf, readOn } from '@/lib/analytics/visit-context';
import type { VisitContext } from '@/lib/api/schemas';

/** The facts a row can say about its visitor. */
export type WhereaboutsFact = 'source' | 'place' | 'network' | 'readOn';

/** What a row says when nothing chose otherwise: roughly where, and on what. */
const USUAL: readonly WhereaboutsFact[] = ['place', 'readOn'];

interface WhereaboutsProps {
  readonly context: VisitContext;
  /** How the visitor arrived, as the row worked it out from what it was read from. */
  readonly from: CameFrom;
  /** Writes a country code out in the reader's language, built once for the whole list. */
  readonly countryName: (code: string) => string | null;
  /** Which facts to say, in this order. */
  readonly facts?: readonly WhereaboutsFact[];
}

/**
 * Who sent a visitor, roughly where they were, whose network they came over and what they were
 * reading on, on the row itself.
 *
 * Two rows wearing the same badge are the same row to a reader until one of them says Leeds and a
 * phone and the other says a hosting company in Amsterdam. That is the difference somebody scans
 * a list for, and asking them to open every row to find it is asking them to open every row.
 *
 * Says whichever of the four facts the list asks for, each left out where nothing established it
 * rather than written as an absence. Where the visit came from is said wherever a sender was named
 * or the arrival was seen, because an arrival seen to name nowhere came straight here, and that is
 * an answer rather than a gap; an unseen arrival that named no sender — a finished visit whose
 * first page is no longer on record — says nothing.
 *
 * The list of who is here now does not ask for where somebody came from here: it says it on its
 * own first line, because its half hour may have begun after the visitor arrived.
 */
export function Whereabouts({ context, from, countryName, facts = USUAL }: WhereaboutsProps) {
  const t = useTranslations('dashboard.journey.about');

  const said = facts.flatMap((fact) => {
    const words = wordsFor(fact, context, from, countryName, t);

    return words === null ? [] : [{ fact, words }];
  });

  if (said.length === 0) {
    return null;
  }

  return (
    <span className="flex flex-wrap items-center gap-x-1.5 text-xs text-foreground-subtle">
      {/*
        Each separator travels with the words it introduces rather than standing between two facts
        as a piece of its own, so that a narrow screen wrapping the line does not leave a stray
        middot hanging off the end of it — and sits on the first line of those words, so that a
        network name three lines long on a phone still reads as one fact rather than as three.

        A source nothing has a name for is a hostname, and a hostname has nowhere to break. It is
        allowed to break anywhere rather than run under the figures beside it.
      */}
      {said.map((one, at) => (
        <span key={one.fact} className="flex min-w-0 items-baseline gap-x-1.5">
          {at === 0 ? null : <span aria-hidden>·</span>}
          <bdi className="wrap-anywhere">{one.words}</bdi>
        </span>
      ))}
    </span>
  );
}

/**
 * The words one fact is written in, or nothing where the visit did not establish it.
 *
 * The source and the network are each said inside a phrase that says what kind of fact it is,
 * because both are company names much of the time and a bare "Google" on a row could be the site
 * that sent the visit or the network it came over. Roughly where and what it was read on need
 * nothing of the kind: a town and a browser cannot be mistaken for anything else.
 */
function wordsFor(
  fact: WhereaboutsFact,
  context: VisitContext,
  from: CameFrom,
  countryName: (code: string) => string | null,
  t: ReturnType<typeof useTranslations>,
): string | null {
  switch (fact) {
    case 'source':
      return from.arrived === 'unseen' ? null : fromWords(from, t);
    case 'place':
      return placeOf(context, countryName(context.countryCode));
    case 'network':
      return context.network === '' ? null : t('viaNetwork', { network: context.network });
    case 'readOn': {
      const read = readOn(context);

      return read === null ? null : readWords(read, t);
    }
  }
}
