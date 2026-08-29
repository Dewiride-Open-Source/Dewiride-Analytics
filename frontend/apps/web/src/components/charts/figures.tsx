import type { ReactNode } from 'react';

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
 */
export function Figures({
  label,
  children,
}: {
  readonly label: string;
  readonly children: ReactNode;
}) {
  return (
    <details className="group border-t border-border pt-3">
      <summary className="cursor-pointer text-sm font-medium text-foreground-muted marker:text-foreground-subtle hover:text-foreground">
        {label}
      </summary>
      <div className="mt-3 max-h-72 overflow-auto">
        <table className="w-full min-w-max text-left text-sm">{children}</table>
      </div>
    </details>
  );
}
