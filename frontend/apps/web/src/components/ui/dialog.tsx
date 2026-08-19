'use client';

import { X } from 'lucide-react';
import { type ReactNode, useEffect, useId, useRef } from 'react';
import { cn } from '@/lib/styling';

interface DialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
  /** Shown as the heading, and given to assistive technology as the dialog's own name. */
  readonly title: string;
  /** Accessible name for the button that closes it. */
  readonly closeLabel: string;
  readonly children: ReactNode;
  readonly className?: string;
}

/**
 * A focal overlay.
 *
 * Built on the browser's own dialog element rather than on a stack of divs, which is what supplies
 * the behaviour people expect without any of it being written here: the rest of the page is made
 * inert, focus is held inside, Escape closes it, and it is announced as a dialog. None of that is
 * decoration — a hand-rolled overlay that misses any one of them is unusable with a keyboard.
 *
 * It closes on Escape and on the close button, and deliberately not on a press outside it. What
 * these hold is meant to be read and selected, and a selection dragged past the edge of the panel
 * ends with a press on the backdrop — which would throw away the thing the reader was copying.
 */
export function Dialog({ open, onClose, title, closeLabel, children, className }: DialogProps) {
  const dialog = useRef<HTMLDialogElement>(null);

  /*
    Generated rather than written down. A screen keeps every panel it can open mounted at once, so
    a fixed identifier here would appear several times over in one document and every dialog on
    that screen would be announced under the name of whichever was rendered first.
  */
  const heading = useId();

  useEffect(() => {
    const element = dialog.current;

    if (!element) {
      return;
    }

    if (open && !element.open) {
      element.showModal();
    } else if (!open && element.open) {
      element.close();
    }
  }, [open]);

  return (
    <dialog
      ref={dialog}
      // Escape closes it without anything here running, so the state that opened it has to be
      // told rather than assumed.
      onClose={onClose}
      aria-labelledby={heading}
      className={cn(
        'glow-modal m-auto w-[calc(100vw-2rem)] max-w-2xl rounded-xl border border-border',
        'bg-surface p-0 text-foreground backdrop:bg-background-deep/70 backdrop:backdrop-blur-sm',
        className,
      )}
    >
      <div className="flex items-start justify-between gap-4 border-b border-border px-5 py-4 sm:px-6">
        <h2 id={heading} className="text-lg font-semibold tracking-tight">
          {title}
        </h2>
        <button
          type="button"
          onClick={onClose}
          aria-label={closeLabel}
          className={cn(
            'glow-control -mt-1 -mr-1 flex size-9 shrink-0 items-center justify-center rounded-md',
            'border border-transparent text-foreground-muted hover:bg-surface-muted',
            'hover:text-foreground',
          )}
        >
          <X aria-hidden className="size-4" />
        </button>
      </div>
      <div className="px-5 py-5 sm:px-6">{children}</div>
    </dialog>
  );
}
