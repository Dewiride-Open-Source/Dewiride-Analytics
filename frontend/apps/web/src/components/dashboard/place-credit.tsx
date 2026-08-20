'use client';

import { useTranslations } from 'next-intl';
import type { ReactNode } from 'react';

interface PlaceCreditProps {
  /** A caveat worth saying beside the credit, where one applies to what is on screen. */
  readonly note?: string;
}

/**
 * The link back the place data's licence requires wherever its results are shown.
 *
 * Required rather than courteous: the data behind every country and town this product shows is
 * published under a licence whose one condition is a link back from anywhere its results appear.
 * So this goes wherever a place goes — a ranked list of them, or a single one beside one visit —
 * and it is one component rather than one line repeated, because a condition satisfied by copying
 * a link into each new screen is a condition that will eventually be missed on one.
 */
export function PlaceCredit({ note }: PlaceCreditProps) {
  const t = useTranslations('dashboard.locations');

  return (
    <p className="text-xs text-foreground-subtle">
      {t.rich('attribution', { source: creditLink })}
      {note === undefined ? null : ` ${note}`}
    </p>
  );
}

function creditLink(label: ReactNode) {
  return (
    <a
      href="https://db-ip.com"
      target="_blank"
      rel="noreferrer"
      className="underline underline-offset-2 hover:text-foreground-muted"
    >
      {label}
    </a>
  );
}
