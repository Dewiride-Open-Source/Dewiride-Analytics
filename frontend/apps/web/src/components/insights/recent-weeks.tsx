'use client';

import { useFormatter, useTranslations } from 'next-intl';
import { useId, useMemo } from 'react';
import { CHANGE_ARROWS, CHANGE_TONES, writtenMove } from '@/components/dashboard/metric-card';
import { HeatLegend, type HeatMarks } from '@/components/insights/heat-legend';
import { STUCK } from '@/components/insights/months-table';
import { Card } from '@/components/ui/card';
import { NO_FIGURE, weekdayName, writableDay, writeFigure } from '@/lib/analytics/all-time';
import { HEAT_CELLS, heatStep } from '@/lib/analytics/heat';
import {
  busiestDay,
  type DayCell,
  RECENT_WEEKS,
  type WeekRow,
  weekMove,
  weeksOf,
} from '@/lib/analytics/recent-weeks';
import type { PostInsights } from '@/lib/api/schemas';
import { cn } from '@/lib/styling';
import { useOverflowing } from '@/lib/use-overflowing';

type Format = ReturnType<typeof useFormatter>;

/** Monday to Sunday, as a place in the week. */
const WEEK = [0, 1, 2, 3, 4, 5, 6];

/** Every week the recent weeks can run to, newest first, as the rows standing in for them. */
const EVERY_WEEK = Array.from({ length: RECENT_WEEKS }, (_, back) => back);

/** The cards standing in for the weeks on a phone: enough to show there are several. */
const CARDS_WAITING = [0, 1];

/** The card's own spacing. */
const CARD = 'flex min-w-0 flex-col gap-4 p-5 sm:p-6';

/** The table, and its stand-in, whose columns have to fall in the same places. */
const TABLE =
  'w-full min-w-[48rem] table-fixed border-separate border-spacing-[3px] text-[13px] tabular-nums';

/** A day with no figure to shade. */
const UNSHADED = 'text-foreground-muted';

/** The dashed edge round today, whose figure is still growing. */
const SO_FAR = 'outline-2 -outline-offset-2 outline-current outline-dashed';

/** The legend of days still on their way, which can say no more than what the shades mean. */
const NO_MARKS: HeatMarks = {};

/** A week's card on a phone, and its stand-in. */
const WEEK_CARD = 'rounded-md border border-border p-3';

/** What a week's card holds: its seven days across. */
const WEEK_TILES = 'mt-2 grid grid-cols-7 gap-1';

/** A day's tile on a phone. */
const TILE =
  'flex min-h-12 flex-col items-center justify-center gap-0.5 rounded-sm px-0.5 py-1 text-center text-xs tabular-nums';

/**
 * What a day wears: its shade on the scale, with a dashed edge today while its figure is still
 * growing, or quiet ink alone where it has no figure to shade.
 */
function shadeOf(cell: DayCell, busiest: number): string {
  if (cell.pageViews === null) {
    return UNSHADED;
  }

  return cn(HEAT_CELLS[heatStep(cell.pageViews, busiest)], cell.state === 'today' && SO_FAR);
}

/** A week written as the days it runs over, with the year where the weeks run across a new one. */
function weekCalled({ first, last }: WeekRow, withYear: boolean, format: Format): string {
  return format.dateTimeRange(writableDay(first), writableDay(last), {
    month: 'short',
    day: 'numeric',
    ...(withYear ? { year: 'numeric' } : {}),
    timeZone: 'UTC',
  });
}

/**
 * The marks the days hold besides their shades, named in the legend only where a day wears one: the
 * days before measuring began and those before the post was published, in the order they fall.
 */
function useWeekMarks(rows: readonly WeekRow[]): HeatMarks {
  const t = useTranslations('insights.post.weeks.legend');
  const months = useTranslations('insights.months.legend');
  const post = useTranslations('insights.post.months.legend');
  const states = new Set(rows.flatMap((row) => row.days.map((cell) => cell.state)));

  return {
    soFar: states.has('today') ? t('today') : undefined,
    before: [
      ...(states.has('before') ? [months('unmeasured')] : []),
      ...(states.has('unpublished') ? [post('unpublished')] : []),
    ],
  };
}

interface RecentWeeksProps {
  /** The post's days, from a Monday to today, or nothing while they are on their way. */
  readonly days: PostInsights['days'] | undefined;
  /** The post's first day, or nothing where it has none. */
  readonly from: string | null;
  /**
   * The day the post was published, where no day before it can have held one of its views; nothing
   * where any of them may have, or while the post is on its way.
   */
  readonly publishedOn: string | null;
}

/**
 * A post's recent weeks, a day at a time: the week running now first, each week's days shaded
 * against the busiest of them, what the week came to, its views for each day, and how that moved
 * against the week before.
 *
 * A table on anything wide enough for seven days and three figures beside them, and on a phone a card
 * to each week with its days as tiles. One card whether or not the days are in, and nothing at all
 * where there is no week to show.
 */
export function RecentWeeks({ days, from, publishedOn }: RecentWeeksProps) {
  const t = useTranslations('insights.post.weeks');
  const heading = useId();
  const rows = useMemo(
    () => (days === undefined ? undefined : weeksOf(days, from, publishedOn)),
    [days, from, publishedOn],
  );

  if (rows?.length === 0) {
    return null;
  }

  return (
    <Card className={CARD}>
      <h2 id={heading} className="text-base font-semibold text-foreground">
        {t('title')}
      </h2>
      {rows === undefined ? <WeeksWaiting /> : <WeeksShown rows={rows} heading={heading} />}
    </Card>
  );
}

interface WeeksShownProps {
  /** The weeks, newest first. */
  readonly rows: readonly WeekRow[];
  /** What names the region the table scrolls in. */
  readonly heading: string;
}

/** The weeks themselves, as a table or as cards, and what their marks mean. */
function WeeksShown({ rows, heading }: WeeksShownProps) {
  const t = useTranslations('insights.post.weeks');
  const marks = useWeekMarks(rows);
  const busiest = busiestDay(rows);
  const newest = rows[0]?.last ?? '';
  const oldest = rows.at(-1)?.first ?? newest;
  const laid: WeeksLaid = {
    rows,
    busiest,
    withYear: newest.slice(0, 4) !== oldest.slice(0, 4),
  };

  return (
    <>
      <WeeksTable {...laid} heading={heading} />
      <div className="flex flex-col gap-3 sm:hidden">
        <p className="text-sm text-foreground-muted">{t('caption')}</p>
        <WeekCards {...laid} />
      </div>
      <HeatLegend marks={marks} />
    </>
  );
}

/** How the weeks are laid out, as the table and the cards both lay them. */
interface WeeksLaid {
  readonly rows: readonly WeekRow[];
  /** The most any day was read, which every day is shaded against. */
  readonly busiest: number;
  /** Whether each week carries its year, as it does where the weeks run across a new one. */
  readonly withYear: boolean;
}

/** The columns of the table: the week, its seven days, and the three figures each week ends on. */
function WeeksHead() {
  const t = useTranslations('insights.post.weeks');
  const format = useFormatter();

  return (
    <thead>
      <tr className="text-xs text-foreground-muted">
        <th scope="col" className={cn(STUCK, 'left-0 w-36 px-2 py-1 text-left font-medium')}>
          {t('week')}
        </th>
        {WEEK.map((place) => (
          <th key={place} scope="col" className="px-1 py-1 text-center font-medium">
            {weekdayName(place, 'short', format)}
          </th>
        ))}
        <th scope="col" className="w-16 px-2 py-1 text-right font-medium">
          {t('total')}
        </th>
        <th scope="col" className="w-28 whitespace-nowrap px-2 py-1 text-right font-medium">
          {t('perDay')}
        </th>
        <th scope="col" className="w-28 px-2 py-1 text-left font-medium">
          {t('change')}
        </th>
      </tr>
    </thead>
  );
}

interface WeeksTableProps extends WeeksLaid {
  /** What names the region the table scrolls in. */
  readonly heading: string;
}

/**
 * The weeks as a table, each week's name held in place while its days scroll beside it. The table
 * scrolls inside its card, never the page, and only takes a stop on the way through the screen while
 * there is something to scroll to.
 */
function WeeksTable({ rows, busiest, withYear, heading }: WeeksTableProps) {
  const t = useTranslations('insights.post.weeks');
  const format = useFormatter();
  const [region, overflowing] = useOverflowing<HTMLElement>();

  return (
    <section
      ref={region}
      aria-labelledby={heading}
      // A region that scrolls has to be reachable without a pointer.
      // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
      tabIndex={overflowing ? 0 : undefined}
      className="relative hidden overflow-x-auto overscroll-x-contain rounded-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong sm:block"
    >
      <table className={TABLE}>
        <caption className="mb-2 text-left text-sm text-foreground-muted">
          <span className="sticky left-0 inline-block">{t('caption')}</span>
        </caption>
        <WeeksHead />
        <tbody>
          {rows.map((row, place) => (
            <tr key={row.first}>
              <th
                scope="row"
                className={cn(
                  STUCK,
                  'left-0 px-2 text-left text-sm leading-tight font-medium text-foreground',
                )}
              >
                {weekCalled(row, withYear, format)}
              </th>
              {row.days.map((cell) => (
                <td
                  key={cell.day}
                  className={cn('h-11 rounded-sm px-1 text-center', shadeOf(cell, busiest))}
                >
                  <DayFigure cell={cell} />
                </td>
              ))}
              <td className="px-2 text-right text-sm font-semibold text-foreground">
                {format.number(row.total)}
              </td>
              <td className="px-2 text-right text-sm text-foreground">
                <WeekPerDay row={row} />
              </td>
              <td className="px-2 text-left text-sm">
                <WeekMoved row={row} oldest={place === rows.length - 1} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

/** Where a figure would stand that is not there, and in words why. */
function Absent({ words }: { readonly words: string }) {
  return (
    <>
      <span aria-hidden>{NO_FIGURE}</span>
      <span className="sr-only">{words}</span>
    </>
  );
}

/**
 * What a day says, in a cell of the table or on a tile alike: its figure, its figure so far, or in
 * words why it has none. Nought on a day nobody read is a different fact from a day before measuring
 * began, a day before the post was published, or one still to come.
 */
function DayFigure({ cell }: { readonly cell: DayCell }) {
  const t = useTranslations('insights.months');
  const post = useTranslations('insights.post.months');
  const format = useFormatter();

  switch (cell.state) {
    case 'future':
      return <span className="sr-only">{t('future')}</span>;
    case 'before':
      return <Absent words={t('unmeasured')} />;
    case 'unpublished':
      return <Absent words={post('unpublished')} />;
    default:
      return (
        <>
          {format.number(cell.pageViews ?? 0)}
          {cell.state === 'today' ? <span className="sr-only"> {t('soFar')}</span> : null}
        </>
      );
  }
}

/** A week's views for each day, or in words why it has none yet. */
function WeekPerDay({ row }: { readonly row: WeekRow }) {
  const t = useTranslations('insights.post.weeks');
  const format = useFormatter();

  if (row.perDay === null) {
    return <Absent words={t('running')} />;
  }

  return writeFigure(row.perDay, 'average', format);
}

interface WeekMovedProps {
  readonly row: WeekRow;
  /** Whether it is the oldest week shown, which has nothing before it to be set against. */
  readonly oldest: boolean;
  /** Whether to say what the move is set against, where nothing around it says so. */
  readonly against?: boolean;
}

/**
 * How a week's views for each day moved against the week before's, in words as well as by its colour
 * and its arrow; or why there is nothing to say: no week before it, or none of its days finished yet.
 */
function WeekMoved({ row, oldest, against = false }: WeekMovedProps) {
  const t = useTranslations('insights.post.weeks');
  const format = useFormatter();
  const { change } = row;

  if (change === null) {
    return row.running && !oldest ? (
      <span className="text-foreground-muted">{t('running')}</span>
    ) : (
      <Absent words={t('first')} />
    );
  }

  const Arrow = CHANGE_ARROWS[change.direction];

  return (
    <span
      className={cn('inline-flex items-center gap-1 font-medium', CHANGE_TONES[change.direction])}
    >
      <Arrow aria-hidden className="size-3.5 shrink-0" />
      <span>
        {t(`moved.${weekMove(change)}`, { amount: writtenMove(change.fraction, format) })}
        {against ? <span className="sr-only"> {t('againstBefore')}</span> : null}
      </span>
    </span>
  );
}

/**
 * The weeks on a phone: a card to each, its seven days as tiles, what it came to beside its name and
 * how it moved beneath. The same days shaded the same way as the table they stand in for.
 */
function WeekCards({ rows, busiest, withYear }: WeeksLaid) {
  const t = useTranslations('insights.post.weeks');
  const format = useFormatter();

  return (
    <ul className="flex flex-col gap-3">
      {rows.map((row, place) => (
        <li key={row.first} className={WEEK_CARD}>
          <div className="flex items-baseline justify-between gap-3">
            <h3 className="text-sm font-semibold text-foreground">
              {weekCalled(row, withYear, format)}
            </h3>
            <p className="shrink-0 text-sm text-foreground-muted tabular-nums">
              {t('total')}{' '}
              <span className="font-semibold text-foreground">{format.number(row.total)}</span>
            </p>
          </div>
          <ul className={WEEK_TILES}>
            {row.days.map((cell, weekday) => (
              <li key={cell.day} className={cn(TILE, shadeOf(cell, busiest))}>
                <span aria-hidden className="text-[11px]">
                  {weekdayName(weekday, 'narrow', format)}
                </span>
                <span className="sr-only">
                  {format.dateTime(writableDay(cell.day), { dateStyle: 'full', timeZone: 'UTC' })}
                </span>
                <span className="font-medium">
                  <DayFigure cell={cell} />
                </span>
              </li>
            ))}
          </ul>
          <div className="mt-2 flex flex-wrap items-center justify-between gap-x-3 gap-y-1 text-sm">
            <p className="text-foreground-muted tabular-nums">
              {row.perDay === null
                ? null
                : t('perDayShort', { figure: writeFigure(row.perDay, 'average', format) })}
            </p>
            <p>
              <WeekMoved row={row} oldest={place === rows.length - 1} against />
            </p>
          </div>
        </li>
      ))}
    </ul>
  );
}

/**
 * The weeks while their days are on their way: as many as there can be, since how many will be shown
 * cannot be known before the answer. The caption and the names of the days are the card's own.
 */
function WeeksWaiting() {
  const t = useTranslations('insights.post.weeks');

  return (
    <>
      <div className="hidden sm:block">
        <p className="mb-2 text-sm text-foreground-muted">{t('caption')}</p>
        {/* Inert as well as hidden from a screen reader: a box too narrow for its table is a stop for
            the keyboard, and this one is gone when the days land. */}
        <div aria-hidden inert className="overflow-x-auto">
          <table className={TABLE}>
            <WeeksHead />
            <tbody>
              {EVERY_WEEK.map((week) => (
                <tr key={week}>
                  <th className={cn(STUCK, 'left-0 px-2')}>
                    <span className="block h-4 w-24 animate-pulse rounded-sm bg-surface-muted" />
                  </th>
                  {WEEK.map((place) => (
                    <td key={place} className="h-11 animate-pulse rounded-sm bg-surface-muted" />
                  ))}
                  <td colSpan={3} className="px-2">
                    <span className="ml-auto block h-4 w-40 animate-pulse rounded-sm bg-surface-muted" />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="flex flex-col gap-3 sm:hidden">
        <p className="text-sm text-foreground-muted">{t('caption')}</p>
        {CARDS_WAITING.map((card) => (
          <div key={card} aria-hidden className={WEEK_CARD}>
            <div className="flex h-5 items-center justify-between gap-3">
              <span className="h-4 w-24 animate-pulse rounded-sm bg-surface-muted" />
              <span className="h-4 w-16 animate-pulse rounded-sm bg-surface-muted" />
            </div>
            <div className={WEEK_TILES}>
              {WEEK.map((place) => (
                <div key={place} className={cn(TILE, 'animate-pulse bg-surface-muted')} />
              ))}
            </div>
          </div>
        ))}
      </div>

      <HeatLegend marks={NO_MARKS} />
    </>
  );
}
