'use client';

import { useTranslations } from 'next-intl';
import type { ReactNode } from 'react';

interface PlaceCreditProps {
  /** A caveat worth saying beside the credit, where one applies to what is on screen. */
  readonly note?: string;
}

/**
 * The links back the licences require wherever the data behind a place or a network is shown.
 *
 * Required rather than courteous: where a visitor was and whose network they arrived over come
 * from two separately published sets of data, and each is published under a licence whose one
 * condition is a link back from anywhere its results appear. So these go wherever those results
 * go — a ranked list of them, a single one beside one visit, or a filter offering the ones a
 * period held — and they are components rather than lines repeated, because a condition satisfied
 * by copying a link into each new screen is a condition that will eventually be missed on one.
 *
 * Two components rather than one, because the two sets of data answer different questions and a
 * screen showing only one of them must not credit the other.
 */
export function PlaceCredit({ note }: PlaceCreditProps) {
  const t = useTranslations('dashboard.locations');

  return (
    <p className="text-xs text-foreground-subtle">
      {t.rich('attribution', { source: creditLink('https://db-ip.com') })}
      {note === undefined ? null : ` ${note}`}
    </p>
  );
}

/** The link back the routing data's licence requires wherever a network is named. */
export function RoutingCredit() {
  const t = useTranslations('dashboard.locations');

  return (
    <p className="text-xs text-foreground-subtle">
      {t.rich('attributionNetworks', { source: creditLink('https://iptoasn.com') })}
    </p>
  );
}

interface ListCreditsProps {
  /** Whether any row on the list was placed somewhere. */
  readonly placed: boolean;
  /** Whether any row on the list names the network it came over. */
  readonly networked: boolean;
}

/**
 * The credits a list of visitors ends in, each only where its own results are on the list.
 *
 * A list that places some of its rows and names the networks of some of them owes both links;
 * one that does neither owes nothing and shows nothing, not even the rule it would sit under.
 * One component rather than a block repeated at the foot of every list, for the reason above.
 */
export function ListCredits({ placed, networked }: ListCreditsProps) {
  if (!placed && !networked) {
    return null;
  }

  return (
    <div className="flex flex-col gap-1 border-t border-border pt-3">
      {placed ? <PlaceCredit /> : null}
      {networked ? <RoutingCredit /> : null}
    </div>
  );
}

function creditLink(address: string) {
  return (label: ReactNode) => (
    <a
      href={address}
      target="_blank"
      rel="noreferrer"
      className="underline underline-offset-2 hover:text-foreground-muted"
    >
      {label}
    </a>
  );
}
