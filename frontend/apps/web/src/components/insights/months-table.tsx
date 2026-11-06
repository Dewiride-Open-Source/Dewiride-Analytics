'use client';

import { useFormatter, useTranslations } from 'next-intl';
import { useId, useMemo } from 'react';
import { ListSwitch } from '@/components/dashboard/ranked-list';
import { HeatLegend } from '@/components/insights/heat-legend';
import { Card } from '@/components/ui/card';
import {
  figureOf,
  largestOf,
  MEASURES,
  type Measure,
  type MonthCell,
  type YearRow,
  yearFigure,
  yearsOf,
} from '@/lib/analytics/all-time';
import { HEAT_CELLS, heatStep, LEFT_OUT_CELL } from '@/lib/analytics/heat';
import type { Insights } from '@/lib/api/schemas';
import { cn } from '@/lib/styling';
import { useOverflowing } from '@/lib/use-overflowing';

type Format = ReturnType<typeof useFormatter>;

/** Stands in for a figure that is not there, in a cell or beside a year. */
const NO_FIGURE = '–';

/** Below this an average is written with a decimal, since the difference between 1 and 1.4 is real. */
const WHOLE_FROM = 10;

/**
 * Below this a decimal rounds an average to nought, on a month somebody read, so it is written to its
 * first digit instead.
 */
const ROUNDS_TO_NOUGHT = 0.05;

/** A figure longer than this is set smaller, so it stays inside its cell. */
const LONG_FIGURE = 7;

/** January to December, as a place in the year. */
const MONTH_PLACES = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

/** The card's own spacing. */
const CARD = 'flex min-w-0 flex-col gap-4 p-5 sm:p-6';

/** The years of a record still on its way, which has none to lay out yet. */
const NO_YEARS: readonly YearRow[] = [];

/** The table, and its stand-in, whose columns have to fall in the same places. */
const TABLE =
  'w-full min-w-[57.5rem] table-fixed border-separate border-spacing-[3px] text-[13px] tabular-nums';

/** A month with no figure to shade. */
const UNSHADED = 'text-foreground-muted';

/** A year's card on a phone, and its stand-in. */
const YEAR_CARD = 'rounded-md border border-border p-3';

/** What a year's card holds: three months across and four down. */
const YEAR_TILES = 'mt-2 grid grid-cols-3 gap-1.5';

/** A month's tile on a phone. */
const TILE =
  'flex min-h-14 flex-col items-center justify-center gap-0.5 rounded-sm px-1 py-1.5 text-center text-sm tabular-nums';

/**
 * The shared edge of the sticky columns: the cells scrolling under them would otherwise show through
 * the gaps the table leaves between its rows.
 */
const STUCK = 'sticky z-10 bg-surface shadow-[0_0_0_3px_var(--surface)]';

/** A figure as a month or a year shows it. */
function written(figure: number | null, measure: Measure, format: Format): string {
  if (figure === null) {
    return NO_FIGURE;
  }

  if (measure === 'average' && figure > 0 && figure < ROUNDS_TO_NOUGHT) {
    return format.number(figure, { maximumSignificantDigits: 1 });
  }

  return measure === 'average' && figure < WHOLE_FROM
    ? format.number(figure, { maximumFractionDigits: 1 })
    : format.number(figure, { maximumFractionDigits: 0 });
}

/** The name of a month, which every year shares. */
function monthCalled(place: number, format: Format): string {
  return format.dateTime(new Date(Date.UTC(2024, place, 1)), {
    month: 'short',
    timeZone: 'UTC',
  });
}

/**
 * What a month wears: the hatching of a month left out, its shade on the scale with a dashed edge while
 * it is still running, or quiet ink alone where it has no figure to shade.
 */
function shadeOf(cell: MonthCell, figure: number | null, largest: number): string {
  if (cell.state === 'notCounted' || cell.state === 'notJudged') {
    return LEFT_OUT_CELL;
  }

  if (figure === null) {
    return UNSHADED;
  }

  return cn(
    HEAT_CELLS[heatStep(figure, largest)],
    cell.state === 'running' && 'outline-2 -outline-offset-2 outline-current outline-dashed',
  );
}

interface MeasureChoice {
  /** What each month is being read as, which outlives the card. */
  readonly measure: Measure;
  readonly onMeasure: (measure: Measure) => void;
}

interface MonthsTableProps extends MeasureChoice {
  /** What the record says, or nothing while it is on its way. */
  readonly insights: Insights | undefined;
}

/**
 * Every month of a website's record, a year to a row, read as how much it held or how much a day.
 *
 * A table on anything wide enough for twelve columns, and on a phone a card to each year with its
 * months as tiles, because a table scrolled sideways on a phone shows three months and hides the rest.
 * Either way every month is shaded against the busiest of them, so the good months stand out before
 * a single number is read.
 *
 * One card whether or not its figures are in: its heading, its switch and its legend stand through
 * the wait, and only the months between them change. The switch already works while the figures are
 * on their way, what it chooses is what they arrive under, and whoever is on it when they land is
 * still on it.
 */
export function MonthsTable({ insights, measure, onMeasure }: MonthsTableProps) {
  const heading = useId();
  const rows = useMemo(() => (insights === undefined ? NO_YEARS : yearsOf(insights)), [insights]);
  const largest = useMemo(() => largestOf(rows, measure), [rows, measure]);

  return (
    <Card className={CARD}>
      <MonthsHeading id={heading} measure={measure} onMeasure={onMeasure} />

      {insights === undefined ? (
        <MonthsWaiting measure={measure} />
      ) : (
        <>
          <YearsTable rows={rows} measure={measure} largest={largest} heading={heading} />
          <YearCards rows={rows} measure={measure} largest={largest} />
        </>
      )}

      <HeatLegend rows={rows} />
    </Card>
  );
}

/**
 * The months while their figures are on their way: a year's worth, since how many years there are
 * cannot be known before the answer. The caption and the names of the months are the card's own.
 */
function MonthsWaiting({ measure }: { readonly measure: Measure }) {
  const t = useTranslations('insights.months');
  const format = useFormatter();

  return (
    <>
      <div className="hidden sm:block">
        <p className="mb-2 text-sm text-foreground-muted">{t(`caption.${measure}`)}</p>
        {/* Inert as well as hidden from a screen reader: a box too narrow for its table is a stop for
            the keyboard, and this one is gone when the figures land. */}
        <div aria-hidden inert className="overflow-x-auto">
          <table className={TABLE}>
            <YearsHead measure={measure} />
            <tbody>
              <tr>
                <th className={cn(STUCK, 'left-0 px-2')}>
                  <span className="block h-4 w-10 animate-pulse rounded-sm bg-surface-muted" />
                </th>
                {MONTH_PLACES.map((place) => (
                  <td key={place} className="h-11 animate-pulse rounded-sm bg-surface-muted" />
                ))}
                <td className={cn(STUCK, 'right-0 px-2')}>
                  <span className="ml-auto block h-4 w-14 animate-pulse rounded-sm bg-surface-muted" />
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div className="flex flex-col gap-3 sm:hidden">
        <p className="text-sm text-foreground-muted">{t(`caption.${measure}`)}</p>
        <div aria-hidden className={YEAR_CARD}>
          <div className="flex h-5 items-center justify-between gap-3">
            <span className="h-4 w-10 animate-pulse rounded-sm bg-surface-muted" />
            <span className="h-4 w-20 animate-pulse rounded-sm bg-surface-muted" />
          </div>
          <div className={YEAR_TILES}>
            {MONTH_PLACES.map((place) => (
              <div
                key={place}
                className={cn(TILE, 'animate-pulse bg-surface-muted text-foreground-muted')}
              >
                <span className="text-[11px]">{monthCalled(place, format)}</span>
                <span className="h-5" />
              </div>
            ))}
          </div>
        </div>
      </div>
    </>
  );
}

interface MonthsHeadingProps extends MeasureChoice {
  /** What names the region the table scrolls in, once there is one. */
  readonly id: string;
}

/** What the card is about, and the switch for how its months are read. */
function MonthsHeading({ id, measure, onMeasure }: MonthsHeadingProps) {
  const t = useTranslations('insights.months');
  const options = MEASURES.map((value) => ({ value, label: t(`measure.${value}`) }));

  return (
    <header className="flex flex-wrap items-center justify-between gap-x-4 gap-y-3">
      <h2 id={id} className="text-base font-semibold text-foreground">
        {t('title')}
      </h2>
      <ListSwitch
        label={t('measure.label')}
        options={options}
        value={measure}
        onChange={onMeasure}
      />
    </header>
  );
}

/** The columns of the table: the year, its twelve months, and the figure each year ends on. */
function YearsHead({ measure }: { readonly measure: Measure }) {
  const t = useTranslations('insights.months');
  const format = useFormatter();

  return (
    <thead>
      <tr className="text-xs text-foreground-muted">
        <th scope="col" className={cn(STUCK, 'left-0 w-16 px-2 py-1 text-left font-medium')}>
          {t('year')}
        </th>
        {MONTH_PLACES.map((place) => (
          <th key={place} scope="col" className="px-1 py-1 text-center font-medium">
            {monthCalled(place, format)}
          </th>
        ))}
        <th scope="col" className={cn(STUCK, 'right-0 w-24 px-2 py-1 text-right font-medium')}>
          {t(measure)}
        </th>
      </tr>
    </thead>
  );
}

interface YearsProps {
  readonly rows: readonly YearRow[];
  readonly measure: Measure;
  /** The largest figure any month shows, which every month is shaded against. */
  readonly largest: number;
}

interface YearsTableProps extends YearsProps {
  /** What names the region the table scrolls in. */
  readonly heading: string;
}

/**
 * The record as a table, its years and their figures held in place while the months scroll between
 * them. The table scrolls inside its card, never the page, and only takes a stop on the way through
 * the screen while there is something to scroll to. The box it scrolls in is positioned so that it
 * also holds whatever is written only for a screen reader, which would otherwise widen the page.
 */
function YearsTable({ rows, measure, largest, heading }: YearsTableProps) {
  const t = useTranslations('insights.months');
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
          <span className="sticky left-0 inline-block">{t(`caption.${measure}`)}</span>
        </caption>
        <YearsHead measure={measure} />
        <tbody>
          {rows.map((row) => (
            <tr key={row.year}>
              <th
                scope="row"
                className={cn(STUCK, 'left-0 px-2 text-left text-sm font-medium text-foreground')}
              >
                {yearCalled(row.year, format)}
              </th>
              {row.months.map((cell) => {
                const figure = figureOf(cell, measure);

                return (
                  <td
                    key={cell.month}
                    className={cn(
                      'h-11 rounded-sm px-1 text-center',
                      shadeOf(cell, figure, largest),
                    )}
                  >
                    <MonthFigure cell={cell} figure={figure} measure={measure} />
                  </td>
                );
              })}
              <td
                className={cn(
                  STUCK,
                  'right-0 px-2 text-right text-sm font-semibold text-foreground',
                )}
              >
                {written(yearFigure(row, measure), measure, format)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

/** A year's name, as the reader's language writes one. */
function yearCalled(year: string, format: Format): string {
  return format.dateTime(new Date(Date.UTC(Number(year), 0, 1)), {
    year: 'numeric',
    timeZone: 'UTC',
  });
}

interface MonthFigureProps {
  readonly cell: MonthCell;
  readonly figure: number | null;
  readonly measure: Measure;
}

/**
 * What a month says, in a cell of the table or on a tile alike.
 *
 * A figure, a figure so far, or in words why there is none: nought in a month nobody read is a
 * different fact from a month before measuring began, one still to come, or one nobody counted, and
 * each reads as what it is.
 */
function MonthFigure({ cell, figure, measure }: MonthFigureProps) {
  const t = useTranslations('insights.months');
  const chart = useTranslations('dashboard.chart');
  const format = useFormatter();

  switch (cell.state) {
    case 'notCounted':
      return <span className="block text-[11px] leading-tight">{chart('notCounted')}</span>;
    case 'notJudged':
      return <span className="block text-[11px] leading-tight">{chart('notFullyJudged')}</span>;
    case 'future':
      return <span className="sr-only">{t('future')}</span>;
    case 'before':
      return (
        <>
          <span aria-hidden>{NO_FIGURE}</span>
          <span className="sr-only">{t('unmeasured')}</span>
        </>
      );
    default: {
      const said = written(figure, measure, format);

      return (
        <>
          <span className={cn(said.length > LONG_FIGURE && 'text-[11px]')}>{said}</span>
          {cell.state === 'running' ? <span className="sr-only"> {t('soFar')}</span> : null}
        </>
      );
    }
  }
}

/**
 * The record on a phone: a card to each year, its twelve months as tiles, its own figure beside its
 * name. The same months shaded the same way as the table they stand in for.
 */
function YearCards({ rows, measure, largest }: YearsProps) {
  const t = useTranslations('insights.months');
  const format = useFormatter();

  return (
    <div className="flex flex-col gap-3 sm:hidden">
      <p className="text-sm text-foreground-muted">{t(`caption.${measure}`)}</p>
      <ul className="flex flex-col gap-3">
        {rows.map((row) => (
          <li key={row.year} className={YEAR_CARD}>
            <div className="flex items-baseline justify-between gap-3">
              <h3 className="text-sm font-semibold text-foreground">
                {yearCalled(row.year, format)}
              </h3>
              <p className="text-sm text-foreground-muted tabular-nums">
                {t(measure)}{' '}
                <span className="font-semibold text-foreground">
                  {written(yearFigure(row, measure), measure, format)}
                </span>
              </p>
            </div>
            <ul className={YEAR_TILES}>
              {row.months.map((cell, place) => {
                const figure = figureOf(cell, measure);

                return (
                  <li key={cell.month} className={cn(TILE, shadeOf(cell, figure, largest))}>
                    <span className="text-[11px]">{monthCalled(place, format)}</span>
                    <span className="font-medium">
                      <MonthFigure cell={cell} figure={figure} measure={measure} />
                    </span>
                  </li>
                );
              })}
            </ul>
          </li>
        ))}
      </ul>
    </div>
  );
}
