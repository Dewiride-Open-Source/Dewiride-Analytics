import type { ReactNode } from 'react';
import { cn } from '@/lib/styling';

/** One cell of a figures table, spaced so the columns read apart and the last one sits flush. */
export const CELL = 'whitespace-nowrap py-1.5 pr-3 last:pr-0 sm:pr-4';

/** The same, for a cell holding a number. */
export const FIGURE = `${CELL} text-right tabular-nums`;

/**
 * The same figures as a table, for anybody who reads rather than looks.
 *
 * Every drawing in this product publishes the numbers behind it, because a canvas announces itself
 * as one sentence and a sentence is not the figures. Folded away rather than beside the drawing:
 * it is the same answer a second time, and a screen that shows both at once has said everything
 * twice to everybody.
 *
 * Unfolded they sit in a box of their own height, so a long table does not push the rest of the screen
 * away, unless the screen says the table is the one to read through whole. Folding is the browser's
 * own to keep, unless the screen has to keep it through something the table does not outlive.
 *
 * The line that unfolds them can stand before the figures are in, and already works: what stands in
 * for them is drawn where they will be and kept from anybody reading, since a table of figures on
 * their way is nothing to read.
 */
export function Figures({
  label,
  children,
  open,
  onToggle,
  uncapped = false,
  waiting = false,
}: {
  readonly label: string;
  readonly children: ReactNode;
  /** Whether the figures are unfolded, where the screen keeps that itself. */
  readonly open?: boolean;
  /** Told when the reader folds or unfolds them, where the screen keeps that itself. */
  readonly onToggle?: (open: boolean) => void;
  /** Whether every row shows when unfolded, with no box to scroll. */
  readonly uncapped?: boolean;
  /** Whether the figures are still on their way, and the table holds only what stands in for them. */
  readonly waiting?: boolean;
}) {
  return (
    <details
      open={open}
      onToggle={onToggle && ((event) => onToggle(event.currentTarget.open))}
      className="group border-t border-border pt-3"
    >
      <summary className="cursor-pointer text-sm font-medium text-foreground-muted marker:text-foreground-subtle hover:text-foreground">
        {label}
      </summary>
      <div
        aria-hidden={waiting || undefined}
        className={cn('mt-3 overflow-auto', !uncapped && 'max-h-72')}
      >
        <table className="w-full min-w-max text-left text-sm">{children}</table>
      </div>
    </details>
  );
}
