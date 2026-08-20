'use client';

import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useId } from 'react';
import { SelectInput } from '@/components/ui/field';
import { offsetOf, pageCount, pageOf, pagesFor } from '@/lib/paging';
import { cn } from '@/lib/styling';

interface PaginationProps {
  /** What this steps through, for somebody who reaches it without seeing the list above. */
  readonly label: string;
  /** How many rows there are altogether, across every page. */
  readonly total: number;
  /** How many rows one page holds. */
  readonly perPage: number;
  /** How many rows were passed over to reach the page on screen. */
  readonly offset: number;
  /** How many rows are on screen, which is fewer than a page on the last one. */
  readonly shown: number;
  /** The page sizes on offer, smallest first. */
  readonly sizes: readonly number[];
  /** Whether the next page is still on its way, so the controls do not invite a second press. */
  readonly busy: boolean;
  readonly onMove: (offset: number) => void;
  readonly onResize: (perPage: number) => void;
}

/**
 * The way through a list too long to show at once, with every page a press away.
 *
 * A step at a time is enough for a list somebody skims and not enough for one they work through:
 * on a busy website a period holds hundreds of visits, and reaching the middle of them by pressing
 * Next twenty times is the same as not being able to reach them. So the numbers are here, both
 * ends are always offered, and how many rows a page holds is the reader's choice rather than a
 * decision taken for them.
 *
 * How many rows to show is offered even where there is only one page of them, because it is what
 * turns three pages into one — while the numbers themselves go away when there is nothing to move
 * between.
 */
export function Pagination({
  label,
  total,
  perPage,
  offset,
  shown,
  sizes,
  busy,
  onMove,
  onResize,
}: PaginationProps) {
  const t = useTranslations('dashboard.list');
  const sizeId = useId();
  const count = pageCount(total, perPage);
  const current = pageOf(offset, perPage);

  return (
    <nav
      aria-label={label}
      className="flex flex-col gap-4 border-t border-border pt-4 sm:flex-row sm:items-center sm:justify-between"
    >
      <p aria-live="polite" className="text-sm text-foreground-muted tabular-nums">
        {t('showing', { first: offset + 1, last: offset + shown, total })}
      </p>

      {count > 1 ? (
        <div className="flex items-center justify-center gap-1">
          <Step
            label={t('previous')}
            disabled={current <= 1 || busy}
            onClick={() => onMove(offsetOf(current - 1, perPage))}
          >
            <ChevronLeft aria-hidden className="size-4" />
          </Step>

          {pagesFor(current, count).map((step, index) =>
            step === 'gap' ? (
              // Keyed by where it sits, because two breaks in one row are the same character and
              // the row is rebuilt whenever somebody moves.
              // biome-ignore lint/suspicious/noArrayIndexKey: a break has nothing else to be named by
              <span key={`gap-${index}`} aria-hidden className="px-1 text-foreground-subtle">
                …
              </span>
            ) : (
              <button
                key={step}
                type="button"
                aria-label={t('page', { number: step })}
                aria-current={step === current ? 'page' : undefined}
                disabled={busy}
                onClick={() => onMove(offsetOf(step, perPage))}
                className={cn(
                  'h-9 min-w-9 rounded-md border px-2 text-sm font-medium tabular-nums transition-colors',
                  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong',
                  'disabled:pointer-events-none disabled:opacity-55',
                  step === current
                    ? 'border-accent/40 bg-accent-soft text-accent-strong'
                    : 'border-border bg-surface text-foreground-muted hover:bg-surface-muted hover:text-foreground',
                )}
              >
                {step}
              </button>
            ),
          )}

          <Step
            label={t('next')}
            disabled={current >= count || busy}
            onClick={() => onMove(offsetOf(current + 1, perPage))}
          >
            <ChevronRight aria-hidden className="size-4" />
          </Step>
        </div>
      ) : null}

      <div className="flex items-center justify-end gap-2">
        <label htmlFor={sizeId} className="shrink-0 text-sm text-foreground-muted">
          {t('perPage')}
        </label>
        <SelectInput
          id={sizeId}
          value={perPage}
          disabled={busy}
          onChange={(event) => onResize(Number(event.target.value))}
          className="h-9 w-24"
        >
          {sizes.map((size) => (
            <option key={size} value={size}>
              {size}
            </option>
          ))}
        </SelectInput>
      </div>
    </nav>
  );
}

interface StepProps {
  readonly label: string;
  readonly disabled: boolean;
  readonly onClick: () => void;
  readonly children: React.ReactNode;
}

/** One end of the row: the page before, or the page after. */
function Step({ label, disabled, onClick, children }: StepProps) {
  return (
    <button
      type="button"
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
      className={cn(
        'flex size-9 items-center justify-center rounded-md border border-border bg-surface',
        'text-foreground-muted transition-colors hover:bg-surface-muted hover:text-foreground',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong',
        'disabled:pointer-events-none disabled:opacity-55',
      )}
    >
      {children}
    </button>
  );
}
