'use client';

import { Bot, Info, type LucideIcon, UserRound } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { byWeight, reasonKey, reasonValues } from '@/lib/analytics/verdicts';
import type { SignalDirection, VisitReason } from '@/lib/api/schemas';
import { cn } from '@/lib/styling';

/** What each observation is marked with: somebody, something, or a qualifier on either. */
const MARKERS: Readonly<Record<SignalDirection, LucideIcon>> = {
  'toward-human': UserRound,
  'toward-automation': Bot,
  neutral: Info,
};

const TINTS: Readonly<Record<SignalDirection, string>> = {
  'toward-human': 'text-positive',
  'toward-automation': 'text-accent-strong',
  neutral: 'text-foreground-subtle',
};

interface VisitEvidenceProps {
  readonly title: string;
  readonly reasons: readonly VisitReason[];
}

/**
 * What a verdict was reached on, written out.
 *
 * A product whose whole proposition is that a conclusion can be explained has to explain one the
 * same way wherever it is shown. So the observations behind a verdict are one component rather
 * than a shape each screen redraws: a marker that separates what pointed at a person from what
 * pointed at machinery, in the order the observations counted, and never how much each counted by.
 */
export function VisitEvidence({ title, reasons }: VisitEvidenceProps) {
  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-xs font-medium tracking-wide text-foreground-muted uppercase">{title}</h3>
      <ul className="flex flex-col gap-1.5">
        {byWeight(reasons).map((reason) => (
          <Reason key={reason.code} reason={reason} />
        ))}
      </ul>
    </div>
  );
}

/**
 * One observation, written out.
 *
 * An observation this build has no sentence for is shown as a plain acknowledgement that
 * something else counted. Its code is a name for our own convenience and would mean nothing to
 * the person reading, and quietly dropping it would leave a verdict looking thinner than the case
 * that actually produced it.
 */
function Reason({ reason }: { readonly reason: VisitReason }) {
  const t = useTranslations('reasons');
  const key = reasonKey(reason);
  const Marker = MARKERS[reason.direction];

  return (
    <li className="flex gap-2 text-sm text-foreground-muted">
      <Marker aria-hidden className={cn('mt-0.5 size-3.5 shrink-0', TINTS[reason.direction])} />
      <span>{t.has(key) ? t(key, reasonValues(reason)) : t('other')}</span>
    </li>
  );
}
