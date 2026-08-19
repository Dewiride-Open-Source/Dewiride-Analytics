'use client';

import { Loader2 } from 'lucide-react';
import type { ReactNode } from 'react';
import { useId } from 'react';
import { cn } from '@/lib/styling';

interface SwitchProps {
  /** What the switch governs, read out with its state.  */
  readonly label: string;
  /** The one thing somebody would otherwise get wrong, or nothing where there is none. */
  readonly hint?: ReactNode;
  readonly checked: boolean;
  /** Whether the change is still being saved. */
  readonly busy?: boolean;
  readonly disabled?: boolean;
  readonly onChange: (checked: boolean) => void;
}

/**
 * A setting that is either on or off.
 *
 * The whole row is the control, so the label is part of what somebody presses rather than
 * something beside it they have to aim past — which matters most on a phone, where the track
 * itself is about as wide as a fingertip.
 *
 * Announced as a switch rather than drawn as one: what it is and whether it is on both reach a
 * screen reader from the element, so the sliding track is decoration and is hidden.
 */
export function Switch({
  label,
  hint,
  checked,
  busy = false,
  disabled = false,
  onChange,
}: SwitchProps) {
  const described = useId();
  const unavailable = disabled || busy;

  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-describedby={hint ? described : undefined}
      aria-busy={busy}
      disabled={unavailable}
      onClick={() => onChange(!checked)}
      className={cn(
        'glow-control flex w-full items-start gap-4 rounded-lg border border-border bg-surface',
        'px-4 py-3.5 text-left disabled:pointer-events-none disabled:opacity-55',
      )}
    >
      <span className="flex min-w-0 flex-1 flex-col gap-1">
        <span className="text-sm font-medium text-foreground">{label}</span>
        {hint ? (
          <span id={described} className="text-sm text-foreground-muted">
            {hint}
          </span>
        ) : null}
      </span>

      <span
        aria-hidden
        className={cn(
          'mt-0.5 flex h-6 w-11 shrink-0 items-center rounded-full p-0.5',
          checked ? 'bg-accent' : 'bg-surface-muted ring-1 ring-border ring-inset',
        )}
      >
        <span
          className={cn(
            'flex size-5 items-center justify-center rounded-full bg-surface shadow-sm',
            checked ? 'translate-x-5' : 'translate-x-0',
          )}
        >
          {busy ? <Loader2 className="size-3 animate-spin text-foreground-muted" /> : null}
        </span>
      </span>
    </button>
  );
}
