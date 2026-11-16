import type { ReactNode } from 'react';

interface DaysBeneathProps {
  /** Where the days are printed, which the control above them names as what describes it. */
  readonly id: string;
  /** The days, as printed — or nothing, where there are none to print yet. */
  readonly days: ReactNode;
  /** The control the days belong to, and anything that opens from it. */
  readonly children: ReactNode;
}

/**
 * A choice of days with the dates it works out to printed beneath it: the one thing a list of
 * names cannot show, and the difference between "Last 30 days" and knowing whether today is in it.
 *
 * Read out with the control as well as printed under it, since a name and its dates are one answer.
 * The control takes the whole width the pair is given — the full width of a phone, and from a small
 * screen up the width of whatever it is stacked with — so two choices stacked one under the other
 * line up on both edges wherever the row they sit in wraps. The dates are right-aligned under it from
 * a small screen up. The line keeps its height when it has nothing to print, so nothing beside or
 * below it moves when the dates arrive.
 */
export function DaysBeneath({ id, days, children }: DaysBeneathProps) {
  return (
    <div className="flex w-full min-w-0 flex-col gap-1 sm:w-auto">
      {children}
      <p id={id} className="min-h-4 truncate text-xs text-foreground-subtle sm:text-right">
        {days}
      </p>
    </div>
  );
}
