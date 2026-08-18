'use client';

import { Card } from '@/components/ui/card';

interface MetricCardProps {
  readonly label: string;
  /** The number to show, or nothing when the period gives no answer. */
  readonly value: string | null;
  /**
   * The one clause a number cannot honestly be shown without.
   *
   * Reserved for exactly that. A note on every card turns a dashboard into a manual, and a note
   * on none of them lets a count of daily visitors be read as a count of people.
   */
  readonly note?: string;
}

/** Stands in for a number that a period with no traffic cannot produce. */
const ABSENT = '—';

/** One headline number. */
export function MetricCard({ label, value, note }: MetricCardProps) {
  return (
    <Card className="flex flex-col gap-1 p-5">
      <h3 className="text-sm font-medium text-foreground-muted">{label}</h3>
      <p
        className={
          value === null
            ? 'text-3xl font-semibold text-foreground-subtle sm:text-4xl'
            : 'text-3xl font-semibold tracking-tight tabular-nums text-foreground sm:text-4xl'
        }
      >
        {value ?? ABSENT}
      </p>
      {note ? <p className="mt-1 text-xs text-foreground-subtle">{note}</p> : null}
    </Card>
  );
}

/** The shape of a metric card while its number is still being fetched. */
export function MetricCardSkeleton() {
  return (
    <Card className="flex flex-col gap-2 p-5">
      <div className="h-4 w-24 animate-pulse rounded-sm bg-surface-muted" />
      <div className="h-9 w-20 animate-pulse rounded-sm bg-surface-muted" />
    </Card>
  );
}
