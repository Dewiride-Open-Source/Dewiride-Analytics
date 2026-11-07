'use client';

import { useFormatter, useTranslations } from 'next-intl';
import {
  type ComponentProps,
  type FocusEvent,
  type KeyboardEvent,
  type MouseEvent,
  type PointerEvent,
  type ReactNode,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { CELL, FIGURE, Figures } from '@/components/charts/figures';
import { POSTS_CARD, POSTS_HEADING } from '@/components/insights/post-list';
import { Card } from '@/components/ui/card';
import { writableDay } from '@/lib/analytics/all-time';
import { HEAT_CELLS } from '@/lib/analytics/heat';
import {
  type CalendarMonth,
  type CalendarRange,
  calendarMonths,
  heldWithin,
  movedTo,
  type PostingDay,
  postingDaysNewestFirst,
  postingStep,
  postingTotals,
} from '@/lib/analytics/posting-calendar';
import { cn } from '@/lib/styling';

type Format = ReturnType<typeof useFormatter>;

/** Every place in a week, Monday first, as the rows of a month's block run down it. */
const WEEK = [0, 1, 2, 3, 4, 5, 6];

/** Every step a day can be shaded at, for the legend. */
const STEPS = [0, 1, 2, 3, 4, 5] as const;

/**
 * A day's square: a sixth of its month's column, less the gaps between the squares of the six weeks a
 * month can reach, so every month's squares are the same size and fill their column; and never larger
 * than a square reads as one.
 */
const SQUARE = 'size-[min(20px,calc((100cqw_-_14px)/6))] rounded-[3px] p-0';

/** How far above its day a tip stands, in pixels. */
const TIP_GAP = 6;

/** How a tip came to be shown, which decides what takes it away again. */
type Shown = 'pointer' | 'touch' | 'focus';

/** The tip over one day, and where it stands in the calendar. */
interface Tip {
  readonly day: string;
  readonly shown: Shown;
  /** The middle of the day's square, from the calendar's left edge. */
  readonly x: number;
  /** The top of the day's square, from the calendar's top edge. */
  readonly y: number;
  /** How wide the calendar is, which the tip is kept inside. */
  readonly width: number;
}

/** A day written out in full, weekday and all, as it is on the website's calendar. */
function dayInFull(day: string, format: Format): string {
  return format.dateTime(writableDay(day), { dateStyle: 'full', timeZone: 'UTC' });
}

/** The day's square an event happened on or in, or nothing where it was not on one. */
function dayCell(target: EventTarget | null): HTMLElement | null {
  return target instanceof Element ? target.closest<HTMLElement>('[data-day]') : null;
}

/** What the calendar is drawn from: the days it runs over, and the days among them that saw a post. */
interface Calendar extends CalendarRange {
  readonly days: readonly PostingDay[];
}

interface PostingCalendarProps {
  /** The calendar, or nothing while it is on its way. */
  readonly calendar: Calendar | undefined;
  /** What is said under it about the posts it leaves out, where it leaves any out. */
  readonly leftOut: ReactNode;
}

/**
 * When a website's posts were published over the last twelve months, a square to a day.
 *
 * A block to each month, its weeks as columns with Monday at the top, and each day shaded by how
 * many posts it saw, so a run of busy weeks and a quiet summer read at a glance. Pointing at a day,
 * pressing on it or reaching it with the keyboard says how many it saw. The same days are published
 * as a list for anybody who reads rather than looks.
 */
export function PostingCalendar({ calendar, leftOut }: PostingCalendarProps) {
  const t = useTranslations('insights.posts.calendar');
  const totals = calendar === undefined ? undefined : postingTotals(calendar.days);

  return (
    <Card className={POSTS_CARD}>
      <header className="flex flex-col gap-0.5">
        <h3 className={POSTS_HEADING}>{t('title')}</h3>
        {totals === undefined ? (
          <span
            aria-hidden
            className="block h-5 w-64 max-w-full animate-pulse rounded-sm bg-surface-muted"
          />
        ) : (
          <p className="text-sm text-foreground-muted">{t('summary', { ...totals })}</p>
        )}
      </header>

      {calendar === undefined ? (
        <div aria-hidden className="h-36 animate-pulse rounded-md bg-surface-muted" />
      ) : (
        <PostingDays calendar={calendar} />
      )}

      <CalendarLegend />
      {leftOut}

      {calendar !== undefined && totals !== undefined && totals.days > 0 ? (
        <PostingList days={calendar.days} />
      ) : null}
    </Card>
  );
}

/**
 * The days themselves, with the one tip that names whichever day is pointed at, pressed or reached.
 *
 * The whole calendar is one stop on the way through the screen, on the day the keyboard was last
 * left on, which begins as today. From there the arrows move a day up and down the weeks and a week
 * across them, the keys that page move a month, and Home and End go to the first day and to today,
 * never past either. A press never takes the keyboard there: it only shows the tip, and on a touch
 * screen a second press on the same day, or one anywhere else, takes it away. Escape takes it away
 * and leaves the keyboard where it is.
 */
function PostingDays({ calendar }: { readonly calendar: Calendar }) {
  const t = useTranslations('insights.posts.calendar');
  const format = useFormatter();
  const holder = useRef<HTMLDivElement>(null);
  const touched = useRef(false);
  const [left, setLeft] = useState(calendar.to);
  const [tip, setTip] = useState<Tip | null>(null);
  const { from, to, days } = calendar;
  const range = useMemo(() => ({ from, to }), [from, to]);
  const months = useMemo(() => calendarMonths(range, days), [range, days]);
  // Written once for the calendar, rather than again for every day each time the tip moves.
  const names = useMemo(
    () =>
      namesOf(months, (day) => t('tip', { posts: day.posts, date: dayInFull(day.day, format) })),
    [months, t, format],
  );
  const keyboard = heldWithin(left, range);
  const tapped = tip?.shown === 'touch';

  // A tip shown by a press stays until the next press, and a press anywhere but on a day ends it.
  useEffect(() => {
    if (!tapped) {
      return;
    }

    function away(event: Event) {
      const cell = dayCell(event.target);

      if (cell === null || !holder.current?.contains(cell)) {
        setTip(null);
      }
    }

    document.addEventListener('pointerdown', away);

    return () => document.removeEventListener('pointerdown', away);
  }, [tapped]);

  /** Shows the tip over a day, as it was asked for. */
  function show(cell: HTMLElement, shown: Shown) {
    const frame = holder.current?.getBoundingClientRect();
    const square = cell.getBoundingClientRect();

    if (frame === undefined) {
      return;
    }

    setTip({
      day: cell.dataset.day ?? '',
      shown,
      x: square.left + square.width / 2 - frame.left,
      y: square.top - frame.top,
      width: frame.width,
    });
  }

  /** Once a pointer has gone, the tip goes back to the day the keyboard is on, if it is on one. */
  function settle() {
    const focused = dayCell(document.activeElement);

    if (focused !== null && holder.current?.contains(focused)) {
      show(focused, 'focus');
    } else {
      setTip(null);
    }
  }

  const listening = {
    onPointerOver(event: PointerEvent) {
      if (event.pointerType !== 'mouse') {
        return;
      }

      const cell = dayCell(event.target);

      if (cell === null) {
        settle();
      } else {
        show(cell, 'pointer');
      }
    },
    onPointerLeave(event: PointerEvent) {
      if (event.pointerType === 'mouse' && tip?.shown === 'pointer') {
        settle();
      }
    },
    onPointerDown(event: PointerEvent) {
      touched.current = event.pointerType !== 'mouse';
    },
    onMouseDown(event: MouseEvent) {
      // A press is for the tip alone, so it never moves the keyboard to the day pressed on.
      if (dayCell(event.target) !== null) {
        event.preventDefault();
      }
    },
    onClick(event: MouseEvent) {
      const cell = dayCell(event.target);

      if (!touched.current || cell === null) {
        return;
      }

      if (tip?.shown === 'touch' && tip.day === cell.dataset.day) {
        setTip(null);
      } else {
        show(cell, 'touch');
      }
    },
    onFocus(event: FocusEvent) {
      const cell = dayCell(event.target);

      if (cell !== null) {
        setLeft(cell.dataset.day ?? to);
        show(cell, 'focus');
      }
    },
    onBlur(event: FocusEvent) {
      const still = dayCell(event.relatedTarget);

      if ((still === null || !holder.current?.contains(still)) && tip?.shown === 'focus') {
        setTip(null);
      }
    },
    onKeyDown(event: KeyboardEvent) {
      const cell = dayCell(event.target);

      if (cell === null) {
        return;
      }

      if (event.key === 'Escape') {
        setTip(null);

        return;
      }

      const next = movedTo(cell.dataset.day ?? to, event.key, range);

      if (next === null) {
        return;
      }

      event.preventDefault();

      const reached = holder.current?.querySelector<HTMLElement>(`[data-day="${next}"]`);

      if (reached) {
        reached.focus();
        // Focus does not move to where it already is, and the tip is still asked for there.
        show(reached, 'focus');
      }
    },
  };

  return (
    <div ref={holder} className="relative">
      <div className="grid grid-cols-3 gap-x-3 gap-y-4 sm:grid-cols-4 sm:gap-x-4 lg:grid-cols-6">
        {months.map((month, place) => (
          <MonthBlock
            key={month.month}
            month={month}
            withYear={place === 0 || month.month.endsWith('-01')}
            keyboard={keyboard}
            names={names}
            listening={listening}
          />
        ))}
      </div>

      {tip === null ? null : <DayTip tip={tip} words={names.get(tip.day) ?? ''} />}
    </div>
  );
}

/** What every day of the calendar is called, as its tip says it, by the day. */
function namesOf(
  months: readonly CalendarMonth[],
  said: (day: PostingDay) => string,
): ReadonlyMap<string, string> {
  return new Map(
    months
      .flatMap(({ weeks }) => weeks.flatMap(({ days }) => days.filter((day) => day !== null)))
      .map((day) => [day.day, said(day)]),
  );
}

/** Everything a month's block listens for, which is the calendar's own. */
type Listening = Pick<
  ComponentProps<'table'>,
  | 'onPointerOver'
  | 'onPointerLeave'
  | 'onPointerDown'
  | 'onMouseDown'
  | 'onClick'
  | 'onFocus'
  | 'onBlur'
  | 'onKeyDown'
>;

interface MonthBlockProps {
  readonly month: CalendarMonth;
  /** Whether its name carries the year, as the first block does and each January. */
  readonly withYear: boolean;
  /** The day the keyboard stops at on its way through the screen. */
  readonly keyboard: string;
  /** What each day is called, as its tip says it, by the day. */
  readonly names: ReadonlyMap<string, string>;
  readonly listening: Listening;
}

/**
 * One month: its name, and its days as a grid of a week to a column with Monday at the top.
 *
 * In forced colours a shade is painted out, so a day that saw a post is filled with the system's
 * own highlight and every other day is drawn as an outline.
 */
function MonthBlock({ month, withYear, keyboard, names, listening }: MonthBlockProps) {
  const format = useFormatter();
  const first = writableDay(`${month.month}-01`);

  return (
    <div className="@container flex min-w-0 flex-col gap-1.5">
      <p aria-hidden className="text-xs font-medium whitespace-nowrap text-foreground-muted">
        {format.dateTime(first, {
          month: 'short',
          ...(withYear ? { year: 'numeric' } : {}),
          timeZone: 'UTC',
        })}
      </p>
      <table
        role="grid"
        aria-label={format.dateTime(first, { month: 'long', year: 'numeric', timeZone: 'UTC' })}
        aria-readonly
        className="-mx-0.5 w-max border-separate border-spacing-0.5"
        {...listening}
      >
        <tbody>
          {WEEK.map((weekday) => (
            <tr key={weekday}>
              {month.weeks.map((week) => {
                const day = week.days[weekday] ?? null;

                return day === null ? (
                  // A place in the week the month does not reach.
                  <td key={week.monday} className={SQUARE} />
                ) : (
                  <td
                    key={week.monday}
                    role="gridcell"
                    data-day={day.day}
                    tabIndex={day.day === keyboard ? 0 : -1}
                    aria-label={names.get(day.day)}
                    className={cn(
                      SQUARE,
                      HEAT_CELLS[postingStep(day.posts)],
                      day.posts > 0
                        ? 'forced-colors:bg-[Highlight] forced-colors:forced-color-adjust-none'
                        : 'forced-colors:outline-1 forced-colors:outline-solid',
                      'focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent-strong',
                    )}
                  />
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * How many posts one day saw, over its square.
 *
 * Kept inside the calendar, so on a phone a day at either edge does not push its tip off the side of
 * the screen. For anybody looking only: each day already carries the same words for a screen reader.
 */
function DayTip({ tip, words }: { readonly tip: Tip; readonly words: string }) {
  return (
    <div
      aria-hidden
      className="glow-modal pointer-events-none absolute z-20 w-max max-w-64 rounded-md border border-border bg-surface px-2.5 py-1.5 text-xs font-medium text-foreground"
      style={{
        left: tip.x,
        top: tip.y - TIP_GAP,
        // Centred over the day, unless that would cross either edge of the calendar.
        translate: `clamp(${-tip.x}px, -50%, calc(${tip.width - tip.x}px - 100%)) -100%`,
      }}
    >
      {words}
    </div>
  );
}

/**
 * What the shades mean. Hidden in forced colours, which paint them out, and from a screen reader,
 * since every day says in words how many posts it saw.
 */
function CalendarLegend() {
  const t = useTranslations('insights.posts.calendar');

  return (
    <div
      aria-hidden
      className="flex items-center gap-1.5 text-xs text-foreground-muted forced-colors:hidden"
    >
      <span>{t('fewer')}</span>
      <span className="flex gap-0.5">
        {STEPS.map((step) => (
          <span key={step} className={cn('size-3 rounded-[3px]', HEAT_CELLS[step])} />
        ))}
      </span>
      <span>{t('more')}</span>
    </div>
  );
}

/** The days that saw a post, newest first, for anybody who reads rather than looks. */
function PostingList({ days }: { readonly days: readonly PostingDay[] }) {
  const t = useTranslations('insights.posts.calendar');
  const format = useFormatter();

  return (
    <Figures label={t('list')}>
      <thead>
        <tr className="border-b border-border text-xs text-foreground-muted">
          <th scope="col" className={`${CELL} font-medium`}>
            {t('column.day')}
          </th>
          <th scope="col" className={`${FIGURE} font-medium`}>
            {t('column.posts')}
          </th>
        </tr>
      </thead>
      <tbody>
        {postingDaysNewestFirst(days).map(({ day, posts }) => (
          <tr key={day} className="border-b border-border/60 last:border-0">
            <th scope="row" className={`${CELL} font-normal text-foreground`}>
              {dayInFull(day, format)}
            </th>
            <td className={`${FIGURE} text-foreground-muted`}>{format.number(posts)}</td>
          </tr>
        ))}
      </tbody>
    </Figures>
  );
}
