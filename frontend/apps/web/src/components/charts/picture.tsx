'use client';

import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { Chart } from '@/components/charts/chart';
import type { ChartPalette } from '@/lib/charts/palette';

/**
 * The box a drawing sits in, and the two things that stand in its place.
 *
 * All three keep one height. A box that shrank while its figures were on their way, or when there
 * was nothing to draw, would move everything below it up the page and then back down again as the
 * answer arrived — on the live screen, every few seconds.
 */
const BOX = 'h-56 w-full sm:h-72';

interface PictureProps {
  readonly option: (palette: ChartPalette) => Record<string, unknown>;
  readonly label: string;
}

/** A drawing, at the height every drawing in the product keeps. */
export function Picture({ option, label }: PictureProps) {
  return (
    <div className={BOX}>
      <Chart option={option} label={label} />
    </div>
  );
}

/** The shape of one, held while its figures are on their way. */
export function Settling() {
  return <div className={`${BOX} animate-pulse rounded-md bg-surface-muted`} />;
}

interface NothingDrawnProps {
  readonly icon: LucideIcon;
  /** Why there is nothing to draw, in the reader's terms. */
  readonly body: string;
  /**
   * The one thing worth doing from here, where there is one.
   *
   * Left out where the screen around the drawing already carries it, since the same way out
   * offered twice on one screen reads as two different ways out.
   */
  readonly action?: ReactNode;
}

/**
 * A drawing with nothing in it.
 *
 * A designed state rather than an empty grid. A run of columns all at nought is honest and reads
 * as a fault, and there is nothing in it for anybody to look at.
 */
export function NothingDrawn({ icon: Icon, body, action }: NothingDrawnProps) {
  return (
    <div className={`flex flex-col items-center justify-center gap-3 text-center ${BOX}`}>
      <span
        aria-hidden
        className="flex size-11 items-center justify-center rounded-full bg-accent-soft"
      >
        <Icon className="size-5 text-accent-strong" />
      </span>
      <p className="max-w-xs text-sm text-foreground-muted">{body}</p>
      {action}
    </div>
  );
}
