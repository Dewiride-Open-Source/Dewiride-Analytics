'use client';

import { Search } from 'lucide-react';
import { useFormatter } from 'next-intl';
import { useId, useState } from 'react';
import { cn } from '@/lib/styling';

/** One value on offer, written the way a reader sees it. */
export interface Option {
  /** What the engine is asked for, which is never shown. */
  readonly value: string;
  /** What a reader sees instead. */
  readonly label: string;
  /** How many visits held it, where that can honestly be said. */
  readonly count?: number;
}

interface OptionListProps {
  readonly options: readonly Option[];
  readonly chosen: readonly string[];
  /** Whether more than one may be asked for at a time. */
  readonly multiple: boolean;
  readonly onPick: (value: string) => void;
  /** What the box above a long list is for, said for anyone who cannot see where it sits. */
  readonly searchLabel: string;
  readonly searchPlaceholder: string;
  /** What to say when this period held nothing at all. */
  readonly nothingLabel: string;
  /** What to say when it held things but none of them match what was typed. */
  readonly noMatchLabel: string;
  /** Whether as many values as may be asked for at once have already been picked. */
  readonly full?: boolean;
  /** What to say when they have. */
  readonly fullLabel?: string;
}

/**
 * Above this many, finding one by eye is slower than typing three letters of it.
 *
 * A box over four options is chrome in front of a list somebody has already read; a list of two
 * hundred towns without one is unusable. Eight is where a list stops being something you take in
 * at a glance.
 */
const SEARCHABLE_FROM = 8;

/**
 * The values one thing about a visit turned out to hold, ready to be picked from.
 *
 * Real checkboxes and real radio buttons, so what a reader hears is "checked" and "one of five"
 * with nothing written here to say so, and so the control works under a keyboard, a screen reader
 * and a voice command without any of it being wired up by hand. That is the whole reason the
 * list is not a set of pressable rows.
 */
export function OptionList({
  options,
  chosen,
  multiple,
  onPick,
  searchLabel,
  searchPlaceholder,
  nothingLabel,
  noMatchLabel,
  full = false,
  fullLabel,
}: OptionListProps) {
  const format = useFormatter();
  const searchId = useId();
  const group = useId();
  const [typed, setTyped] = useState('');

  const searchable = options.length > SEARCHABLE_FROM;
  const wanted = typed.trim().toLowerCase();
  const shown = wanted
    ? options.filter((option) => option.label.toLowerCase().includes(wanted))
    : options;

  return (
    <div className="flex flex-col">
      {searchable ? (
        <div className="border-b border-border p-2">
          <label className="sr-only" htmlFor={searchId}>
            {searchLabel}
          </label>
          <div className="relative">
            <Search
              aria-hidden
              className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-foreground-subtle"
            />
            <input
              id={searchId}
              type="search"
              value={typed}
              onChange={(event) => setTyped(event.target.value)}
              placeholder={searchPlaceholder}
              className={cn(
                'glow-control h-9 w-full rounded-md border border-border bg-surface pr-3 pl-8',
                'text-sm text-foreground placeholder:text-foreground-subtle',
              )}
            />
          </div>
        </div>
      ) : null}

      {shown.length === 0 ? (
        <p className="px-4 py-8 text-center text-sm text-foreground-muted">
          {options.length === 0 ? nothingLabel : noMatchLabel}
        </p>
      ) : (
        <ul className="scroll-hint max-h-64 overflow-y-auto overscroll-contain p-1.5">
          {shown.map((option) => (
            <li key={option.value}>
              <label
                className={cn(
                  'flex cursor-pointer items-center gap-2.5 rounded-md px-2 py-2 text-sm',
                  'hover:bg-surface-muted has-[:focus-visible]:bg-surface-muted',
                  'has-[:focus-visible]:outline-2 has-[:focus-visible]:-outline-offset-2',
                  'has-[:focus-visible]:outline-accent-strong',
                  'has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-55',
                  'has-[:disabled]:hover:bg-transparent',
                )}
              >
                <input
                  type={multiple ? 'checkbox' : 'radio'}
                  name={multiple ? undefined : group}
                  checked={chosen.includes(option.value)}
                  onChange={() => onPick(option.value)}
                  // Everything already picked stays pickable, because taking one off is how a
                  // reader gets back under the limit; only what would go over it stops offering.
                  disabled={full && !chosen.includes(option.value)}
                  className="size-4 shrink-0 accent-accent"
                />
                {/*
                  Isolated so a value written right to left keeps the figure beside it in place,
                  and readable in full by resting on it where it had to be cut short.
                */}
                <bdi className="min-w-0 flex-1 truncate" title={option.label}>
                  {option.label}
                </bdi>
                {/*
                  A real space between the name and the figure, so what a screen reader reads out
                  is "Phones 412" rather than one word nobody would recognise. The gap on screen
                  is drawn by the layout and says nothing to anybody listening.
                */}
                {option.count === undefined ? null : (
                  <>
                    {' '}
                    <span className="shrink-0 text-foreground-muted tabular-nums">
                      {format.number(option.count)}
                    </span>
                  </>
                )}
              </label>
            </li>
          ))}
        </ul>
      )}

      {/*
        Said as well as shown. A row that has quietly stopped responding is a fault as far as
        anybody can tell, and the rest of the list dimming is only visible to somebody looking at
        it.
      */}
      {full && fullLabel ? (
        <output className="block border-t border-border px-3 py-2 text-xs text-foreground-muted">
          {fullLabel}
        </output>
      ) : null}
    </div>
  );
}
