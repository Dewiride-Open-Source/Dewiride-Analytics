'use client';

import { ChevronDown } from 'lucide-react';
import type { ComponentProps, KeyboardEvent } from 'react';
import { cn } from '@/lib/styling';

/**
 * Keys that open the list, or move through it, with or without a modifier held: on one platform or
 * another each opens a closed list with one — Alt, Shift or ⌘ with the up and down arrows, Shift
 * with Space, Shift with Enter, Shift or ⌘ with F4.
 */
const OPENING_KEYS: ReadonlySet<string> = new Set(['ArrowUp', 'ArrowDown', ' ', 'Enter', 'F4']);

/**
 * Keys that move through the list, counted only when pressed on their own. With a modifier held they
 * are mostly the browser's — Alt or ⌘ with the left and right arrows go back and forward, Alt with
 * Home goes home, Ctrl with Page Up or Page Down changes tab — though some browsers still move a
 * closed list with Shift, or with Ctrl or ⌘ on the arrows, Home and End. A press of that kind that
 * does change the choice asks for what the new choice needs itself, so leaving it uncounted only
 * means it is asked for a moment later.
 */
const MOVING_KEYS: ReadonlySet<string> = new Set([
  'ArrowLeft',
  'ArrowRight',
  'Home',
  'End',
  'PageUp',
  'PageDown',
]);

/**
 * Whether a key pressed on the list works it, rather than moving focus away from it or calling on
 * the browser: a key that opens the list, whatever is held with it; a key that moves through it,
 * pressed on its own; or a single character typed with no Ctrl, Alt or ⌘ held, which picks the entry
 * whose name begins with it. Tab, F6, Escape, Ctrl+L, Alt+D, Ctrl+F and a modifier on its own work
 * nothing in it, whatever they are on their way to; a moving key with a modifier held is left
 * uncounted too, for the reason the moving keys give.
 *
 * @param event The key pressed.
 */
function works(event: KeyboardEvent<HTMLSelectElement>): boolean {
  const { key, altKey, ctrlKey, metaKey, shiftKey } = event;

  if (OPENING_KEYS.has(key)) {
    return true;
  }

  if (MOVING_KEYS.has(key)) {
    return !altKey && !ctrlKey && !metaKey && !shiftKey;
  }

  return key.length === 1 && !ctrlKey && !altKey && !metaKey;
}

interface NativeSelectProps extends Omit<ComponentProps<'select'>, 'onPointerDown' | 'onKeyDown'> {
  /**
   * Told when somebody reaches for the list — presses it, or works it from the keyboard — so a
   * screen can ask for what the next choice will need before it is made, rather than asking on
   * every screen nobody touches. Never told when the list is merely focused or passed through: a
   * screen hands focus to a list itself, and somebody tabbing on past it is going somewhere else.
   */
  readonly onReach?: () => void;
}

/**
 * One of the dashboard's lists of choices — the period, what it is compared with, how the picture
 * is cut: the browser's own list, worn as a control of the same height as the buttons beside it,
 * with the mark that says it opens.
 *
 * The browser's own rather than one built here: it opens correctly on a phone, and it is reachable
 * by keyboard and by voice without anything being wired up. A caller sets its width.
 */
export function NativeSelect({ className, children, onReach, ...rest }: NativeSelectProps) {
  return (
    <span className="relative flex items-center">
      <select
        {...rest}
        onPointerDown={onReach}
        onKeyDown={(event) => {
          if (works(event)) {
            onReach?.();
          }
        }}
        className={cn(
          'select-trigger h-9 cursor-pointer appearance-none truncate rounded-md border border-border bg-surface pr-8 pl-3 text-sm font-medium text-foreground',
          className,
        )}
      >
        {children}
      </select>
      <ChevronDown
        aria-hidden
        className="pointer-events-none absolute right-2.5 size-4 text-foreground-muted"
      />
    </span>
  );
}
