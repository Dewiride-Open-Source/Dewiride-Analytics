'use client';

import { X } from 'lucide-react';
import type { ReactNode } from 'react';
import { cn } from '@/lib/styling';

/**
 * The two small rounded controls a set of choices is made and unmade with.
 *
 * Two exports rather than one with a switch on it, because a thing you press to add and a thing
 * you press to take away are different controls: one holds a state and says which it is in, the
 * other stands for a choice already made and does exactly one thing. Sharing a shape is what makes
 * them read as a family; sharing a component would make each of them slightly wrong.
 */

/**
 * The shape the two share. Both are bordered controls, so both wear the glow every bordered control
 * in the product does: quiet at rest, the accent's bloom when pointed at, and its ring when reached
 * with a keyboard.
 */
const shape =
  'glow-control inline-flex max-w-full items-center gap-2 rounded-full border px-3 py-1.5 ' +
  'text-sm focus-visible:outline-2 focus-visible:outline-offset-2 ' +
  'focus-visible:outline-accent-strong';

interface ChipProps {
  /** Whether this choice is currently being asked for. */
  readonly pressed: boolean;
  readonly onPress: () => void;
  /** A marker before the name, where the choice has a colour of its own. */
  readonly leading?: ReactNode;
  /**
   * How many the choice stands for, already written the way the reader writes numbers, where a
   * figure belongs after its name.
   */
  readonly count?: string;
  /** Told when the keyboard arrives on the chip, for a set that must not take a chip from under it. */
  readonly onFocus?: () => void;
  /** Told when the keyboard leaves it. */
  readonly onBlur?: () => void;
  readonly children: ReactNode;
}

/** One choice among several, which pressing turns on and pressing again turns off. */
export function Chip({ pressed, onPress, leading, count, onFocus, onBlur, children }: ChipProps) {
  return (
    <button
      type="button"
      aria-pressed={pressed}
      onClick={onPress}
      onFocus={onFocus}
      onBlur={onBlur}
      className={cn(
        shape,
        pressed
          ? 'border-accent/40 bg-accent-soft font-medium text-accent-strong'
          : 'border-border bg-surface text-foreground-muted hover:bg-surface-muted hover:text-foreground',
      )}
    >
      {leading}
      {children}
      {count === undefined ? null : (
        <>
          {/*
            A real space between the name and the figure, so what a screen reader reads out is
            "A person 6" rather than one word nobody would recognise. The gap between them on
            screen is drawn by the layout and says nothing to anybody listening.
          */}{' '}
          <span className="tabular-nums">{count}</span>
        </>
      )}
    </button>
  );
}

interface ChoicePillProps {
  /** What about a visit this narrows, shown quietly in front of the value. */
  readonly of: string;
  /** The value itself, written the way a reader sees it everywhere else. */
  readonly value: string;
  /**
   * The direction the value runs in, where it runs one way whatever it is written in: a page's
   * address, which is read left to right even where a part of it is written right to left.
   */
  readonly dir?: 'ltr';
  /** What pressing it does, said in full, since the cross alone says nothing out loud. */
  readonly removeLabel: string;
  readonly onRemove: () => void;
}

/**
 * One choice already made, which pressing takes off.
 *
 * The whole pill is the control rather than a small cross inside it, so it can be hit with a
 * thumb. The cross is drawn for the eye and hidden from anyone listening, who is told in words
 * what pressing it will do.
 */
export function ChoicePill({ of, value, dir, removeLabel, onRemove }: ChoicePillProps) {
  return (
    <button
      type="button"
      onClick={onRemove}
      aria-label={removeLabel}
      className={cn(
        shape,
        'border-accent/40 bg-accent-soft text-accent-strong hover:border-accent/60',
        'hover:brightness-105',
      )}
    >
      {/*
        A real space between what this narrows and the value it narrows it to. The gap on screen is
        drawn by the layout, which says nothing to anybody listening and leaves the two run together
        for anything reading the words themselves.
      */}
      {/*
        Kept on one line, so a pill too wide for a phone loses the end of its value rather than
        breaking the words in front of it across two lines and reading as something gone wrong.
      */}
      <span className="shrink-0 whitespace-nowrap text-foreground-muted">{of}</span>{' '}
      {/*
        Isolated so that a value written right to left does not rearrange the label in front of
        it, and cut short with the whole of it still reachable by resting on it. The title sits on
        the value rather than on the pill, whose name already says what pressing it does.
      */}
      <bdi dir={dir} className="min-w-0 truncate font-medium" title={value}>
        {value}
      </bdi>
      <X aria-hidden className="size-3.5 shrink-0 opacity-70" />
    </button>
  );
}
