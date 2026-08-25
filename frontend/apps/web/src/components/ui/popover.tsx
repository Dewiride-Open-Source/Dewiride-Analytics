'use client';

import * as Radix from '@radix-ui/react-popover';
import type { ReactNode } from 'react';
import { cn } from '@/lib/styling';

interface PopoverProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  /** The control it hangs from, which is given the state and the wiring it needs. */
  readonly trigger: ReactNode;
  /** What the panel is called, so it is announced as itself rather than as a nameless dialog. */
  readonly label: string;
  readonly children: ReactNode;
}

/**
 * A panel that hangs off a control and closes when the reader is done with it.
 *
 * The only file in the product that reaches for the primitive behind it, so what a popover does
 * is decided once. Almost none of that behaviour is decoration: the panel has to stay on screen
 * when the control is near an edge, take the cursor when it opens and give it back to the control
 * when it closes, shut on Escape and on a press outside, and be announced as a panel rather than
 * as a stray group of checkboxes. A hand-rolled version that misses any one of them is unusable
 * with a keyboard, which is the same verdict this product already reached about overlays.
 *
 * It is deliberately not the focal overlay: a filter is picked while looking at the list it
 * narrows, so the page behind stays live and readable rather than being made inert.
 */
export function Popover({ open, onOpenChange, trigger, label, children }: PopoverProps) {
  return (
    <Radix.Root open={open} onOpenChange={onOpenChange}>
      <Radix.Trigger asChild>{trigger}</Radix.Trigger>
      <Radix.Portal>
        <Radix.Content
          align="start"
          sideOffset={8}
          collisionPadding={12}
          aria-label={label}
          className={cn(
            // Never wider than the screen it is drawn on, so a panel opened from the last button
            // in the row on a phone is not half off the side of it.
            'glow-modal popover-panel z-50 w-[min(20rem,calc(100vw-1.5rem))] overflow-hidden',
            'rounded-lg border border-border bg-surface text-foreground',
          )}
        >
          {children}
        </Radix.Content>
      </Radix.Portal>
    </Radix.Root>
  );
}
