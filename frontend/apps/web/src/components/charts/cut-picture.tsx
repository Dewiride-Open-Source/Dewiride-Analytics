'use client';

import { ChartArea, ChartColumn, ChartLine, ChevronDown, type LucideIcon } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useMemo } from 'react';
import { CELL, FIGURE, Figures } from '@/components/charts/figures';
import { type BucketDays, bucketDays, heldInDetail } from '@/lib/analytics/buckets';
import { CHART_BYS, type ChartCutting } from '@/lib/analytics/chart-by';
import type { ChartView } from '@/lib/analytics/chart-view';
import {
  type AnalyticsWindow,
  type DaySpan,
  type Granularity,
  type Period,
  type PeriodPreset,
  samePeriod,
  shortestPeriodFor,
} from '@/lib/analytics/period';
import { type BucketWriting, bucketNames, monthsOfItsYear } from '@/lib/charts/bucket-names';
import { type Drawing, drawingsFor } from '@/lib/charts/drawing';
import { type FigureWriter, figureWriter } from '@/lib/charts/frame';
import { type Quiet, quietensCutShort } from '@/lib/charts/wash';

/** How the buckets are cut and written, which every part of the card reads back the same way. */
export interface Buckets extends BucketWriting {
  /** The place whose clock those buckets follow, such as `Kolkata`. */
  readonly zone: string;
}

interface ShowByProps {
  /** The way the picture is being cut, which is what the closed list reads as. */
  readonly granularity: Granularity;
  readonly cutting: ChartCutting;
}

/**
 * How finely the picture is cut.
 *
 * The browser's own list, as the control that names the period is: it opens correctly on a
 * phone, and it is reachable by keyboard and by voice without anything being wired up. Narrow and
 * of a fixed width, so it reads as the one way being drawn and keeps the controls beside it on the
 * same line on a phone; the list it opens is as wide as its longest entry.
 *
 * Every way another period would show is listed on every period long enough to have a choice. One
 * the period on screen is too short for is listed with the period choosing it moves to, so somebody
 * looking at a week finds the way to weeks from there, in the list itself, rather than from a refusal
 * or a note nobody can read without a pointer. Years are listed only where all time would show
 * them, and all time lists only the ways it offers itself.
 */
export function ShowBy({ granularity, cutting }: ShowByProps) {
  const t = useTranslations('dashboard.chart.by');
  const periods = useTranslations('dashboard.period.presets');
  const { offered, listed, onChoose } = cutting;

  const options = useMemo(
    () =>
      listed.map((cut) => ({
        value: cut,
        label: offered.includes(cut)
          ? t(cut)
          : t('moves', { cut: t(cut), period: periods(shortestPeriodFor(cut)) }),
      })),
    [listed, offered, t, periods],
  );

  return (
    <span className="relative flex items-center">
      <select
        aria-label={t('label')}
        value={granularity}
        onChange={(event) => {
          const chosen = CHART_BYS.find((cut) => cut === event.target.value);

          if (chosen !== undefined) {
            onChoose(chosen);
          }
        }}
        className="select-trigger h-9 w-30 cursor-pointer appearance-none truncate rounded-md border border-border bg-surface pr-8 pl-3 text-sm font-medium text-foreground"
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      <ChevronDown
        aria-hidden
        className="pointer-events-none absolute right-2.5 size-4 text-foreground-muted"
      />
    </span>
  );
}

/** The picture each drawing style is offered under. */
const DRAWING_ICONS: Readonly<Record<Drawing, LucideIcon>> = {
  line: ChartLine,
  columns: ChartColumn,
  area: ChartArea,
};

/** One way of drawing a picture, as the switch over it offers it. */
interface DrawingOption {
  readonly value: Drawing;
  /** What the style is called, which is what the switch announces for it. */
  readonly label: string;
  readonly icon: LucideIcon;
}

/**
 * The styles a picture may be drawn in, each named and pictured as the switch over it offers it.
 *
 * @param view The question the picture answers.
 * @param buckets How many buckets the picture has, where that is known.
 */
export function useDrawingOptions(
  view: ChartView,
  buckets: number | undefined,
): readonly DrawingOption[] {
  const t = useTranslations('dashboard.chart');

  return useMemo(
    () =>
      drawingsFor(view, buckets).map((one) => ({
        value: one,
        label: t(`drawing.${one}`),
        icon: DRAWING_ICONS[one],
      })),
    [t, view, buckets],
  );
}

interface PeriodMovedProps {
  /** The period a way of cutting the picture last moved the screen to, or nothing. */
  readonly movedTo: PeriodPreset | null;
  /** The period on screen. */
  readonly period: Period;
}

/**
 * The period a way of cutting the picture moved the screen to, said to anybody listening.
 *
 * Heard rather than seen. The way of cutting the picture that moved the period is still what the
 * list beside the picture reads, and somebody choosing from it without looking would not otherwise
 * learn that the days changed with it. Said for as long as the screen is on the period it moved to,
 * so going back to the earlier one and choosing again is said again.
 */
export function PeriodMoved({ movedTo, period }: PeriodMovedProps) {
  const t = useTranslations('dashboard');
  const presets = useTranslations('dashboard.period.presets');

  return (
    <p aria-live="polite" className="sr-only">
      {movedTo === null ? null : movedHere(movedTo, period, t, presets)}
    </p>
  );
}

/**
 * What is said, to anybody listening, of the period a way of cutting the picture moved the screen
 * to — for as long as the screen is on it, so going back to the earlier one and choosing again is
 * said again — or nothing once it has moved on.
 *
 * @param movedTo The period the screen was moved to.
 * @param period The period on screen.
 * @param t The dashboard's words.
 * @param presets The names of the named periods.
 */
function movedHere(
  movedTo: PeriodPreset,
  period: Period,
  t: ReturnType<typeof useTranslations<'dashboard'>>,
  presets: ReturnType<typeof useTranslations<'dashboard.period.presets'>>,
): string | null {
  return samePeriod(period, { kind: 'preset', preset: movedTo })
    ? t('chart.by.moved', { period: presets(movedTo) })
    : null;
}

/** The buckets of a picture with the days each covers, and each written for where it is read. */
export interface Written {
  readonly days: readonly BucketDays[];
  /** Briefly, under the axis. */
  readonly labels: readonly string[];
  /** Down the first column of the table. */
  readonly rows: readonly string[];
  /** In full, over the card a hover opens. */
  readonly titles: readonly string[];
}

/**
 * The days each bucket covers, and each bucket written from them for the three places it is read:
 * briefly under the axis, a row to itself in the table, and in full where it is read on its own.
 *
 * @param starts The moment each bucket is named by.
 * @param window The window the picture was counted over, which cuts the first and last bucket.
 * @param buckets How the picture is cut and written.
 */
export function useWritten(
  starts: readonly string[],
  window: AnalyticsWindow,
  buckets: Buckets,
): Written {
  const format = useFormatter();
  const { from, to } = window;

  return useMemo(() => {
    const days = bucketDays(starts, { from, to }, buckets.granularity, buckets.timeZoneId);

    return { days, ...bucketNames(days, buckets, format) };
  }, [starts, from, to, buckets, format]);
}

/**
 * The way from a bucket of the picture into the days it covers, or nothing where that is not
 * offered.
 *
 * A day hands back itself, and an hour of a two-day period the day it falls in; a week, a month or
 * a year hands back the days of it the period reaches. The same function serves the picture and the
 * rows of its table, so the two cannot disagree about where a press lands. A press the surface
 * places just past the last bucket — its far edge rounds up to one more than there are — names no
 * days, and neither does one on a bucket older than the first day a press is offered from.
 *
 * @param days The days each bucket covers.
 * @param onPick Where the days of a pressed bucket go, where pressing one is offered at all.
 * @param pressableFrom The first day a bucket may begin on and still be pressed, where not every
 * bucket can be.
 */
export function usePick(
  days: readonly BucketDays[],
  onPick: ((days: DaySpan) => void) | undefined,
  pressableFrom: string | undefined,
): Pressing {
  return useMemo(() => {
    if (!onPick) {
      return { pick: undefined, pressable: undefined };
    }

    const pressable =
      pressableFrom === undefined
        ? undefined
        : (index: number) => {
            const bucket = days[index];

            return bucket !== undefined && heldInDetail(bucket, pressableFrom);
          };

    return {
      pick: (index: number) => {
        const bucket = days[index];

        if (bucket !== undefined && (pressable?.(index) ?? true)) {
          onPick({ first: bucket.first, last: bucket.last });
        }
      },
      pressable,
    };
  }, [days, onPick, pressableFrom]);
}

/** The way from the buckets of a picture into their days, and which of them it is offered on. */
export interface Pressing {
  /** Narrows the screen to the days of a bucket, or nothing where no bucket can be pressed. */
  readonly pick: ((index: number) => void) | undefined;
  /** Which buckets can be pressed, or nothing where every one of them can. */
  readonly pressable: ((index: number) => boolean) | undefined;
}

/** The key of the clause under a picture about the buckets the engine has not finished judging. */
export type JudgingNote = `${'people' | 'who'}.${'judging' | 'judgingAll'}`;

/**
 * What the line under a picture adds about the buckets the engine has not finished judging.
 *
 * Nothing where every bucket is settled, the newest where only the end of the period is still
 * filling, and the whole period where not one bucket has settled — which is what a reader is shown
 * while the engine is stopped or still working through a backlog, and at the start of any period
 * whose first bucket ended less than an idle timeout ago. Calling that "the newest" would read as
 * though the rest were final. A period whose one bucket is still running is not that, unless
 * judging has reached none of it: everything in it up to the last half hour has been judged.
 *
 * @param stillJudging The first bucket still being judged, or nothing where every one is settled.
 * @param judgedNone Whether not one bucket of the period has settled, as `judgedNoneOf` says.
 * @param wording Which picture's words to use.
 * @returns The sentence's key, or nothing.
 */
export function judgingNote(
  stillJudging: number | null,
  judgedNone: boolean,
  wording: 'people' | 'who',
): JudgingNote | null {
  if (stillJudging === null) {
    return null;
  }

  return judgedNone ? `${wording}.judgingAll` : `${wording}.judging`;
}

/**
 * Which buckets of the picture are drawn quietly, and why.
 *
 * A first or last week, month or year the period cuts short — the Sunday that is all "Last 90 days"
 * holds of its first week, or the six weeks of the year a website was first measured in — and every
 * bucket from the first still being judged. Only the two ends can be cut short, so nothing between
 * them ever is, and a picture of a single bucket has no ends to set apart from anything. Whether
 * they are drawn quietly is the drawing's to settle: only where something between them stays at
 * full strength to read them against.
 *
 * @param days The days each bucket covers.
 * @param stillJudging The first bucket still being judged, or nothing where every one is settled.
 */
export function quietIn(days: readonly BucketDays[], stillJudging: number | null): Quiet {
  const ends = days.length > 1;

  return {
    cutAtStart: ends && days[0]?.whole === false,
    cutAtEnd: ends && days.at(-1)?.whole === false,
    judging: stillJudging,
  };
}

/** The ways of cutting a picture whose ends the period can cut short. */
export type CutShort = Exclude<Granularity, 'hour' | 'day'>;

/**
 * The clause under a picture saying the weeks, months or years the period cuts short are drawn
 * fainter, or nothing where the drawing leaves them as they are.
 *
 * Fainter rather than lighter, because the ends are faded towards the card and the card is dark in
 * the dark theme. Said only where it is true: the ends are left at full strength wherever nothing
 * between them would be — and a faint end the caption says nothing about reads as one more thing
 * still being judged.
 *
 * @param granularity How the picture is cut.
 * @param quiet Which buckets of the picture are drawn quietly.
 * @param buckets How many buckets the picture has.
 * @param drawing How the picture is drawn.
 * @returns The sentence's key, or nothing.
 */
export function cutShortNote(
  granularity: Granularity,
  quiet: Quiet,
  buckets: number,
  drawing: Drawing,
): `cutShort.${CutShort}` | null {
  if (granularity === 'hour' || granularity === 'day') {
    return null;
  }

  return quietensCutShort(quiet, buckets, drawing === 'columns') ? `cutShort.${granularity}` : null;
}

/**
 * How the card a hover opens writes a figure: as the reader writes a number, and a bucket with no
 * figure as the table calls it.
 *
 * @param days Each bucket and the days it covers, which is what says why one has no figure.
 * @param unsettled Every month kept before its visits were all judged.
 */
export function useFigure(days: readonly BucketDays[], unsettled: readonly string[]): FigureWriter {
  const t = useTranslations('dashboard.chart');
  const format = useFormatter();

  return useMemo(
    () =>
      figureWriter(
        (value) => format.number(value),
        (at) => t(nothingIn(at === undefined ? undefined : days[at], unsettled)),
      ),
    [format, t, days, unsettled],
  );
}

/**
 * What a bucket with no figure says in its place: that it was not fully judged, wherever a month of
 * it was counted and kept before its visits were all judged — a year holding one beside a month
 * never counted was counted in part, and its row and the caption name the rest — and otherwise
 * that it was never counted.
 *
 * @param bucket The days the bucket covers, or nothing where it is not known which bucket it is.
 * @param unsettled Every month kept before its visits were all judged, written `2025-06`.
 */
function nothingIn(
  bucket: BucketDays | undefined,
  unsettled: readonly string[],
): 'notCounted' | 'notFullyJudged' {
  if (bucket === undefined) {
    return 'notCounted';
  }

  const first = bucket.first.slice(0, 7);
  const last = bucket.last.slice(0, 7);

  return unsettled.some((month) => month >= first && month <= last)
    ? 'notFullyJudged'
    : 'notCounted';
}

interface PictureNotesProps {
  /** Where the picture's buckets are cut, which every picture says. */
  readonly cut: string;
  /** Where the picture begins, where that is later than the period does. */
  readonly opens?: string | null;
  /** The clause about the ends the period cuts short, where the drawing fades them. */
  readonly cutShort: `cutShort.${CutShort}` | null;
  /** The clause about the buckets still being judged, where there are any. */
  readonly judging: JudgingNote | null;
  /** The sentences naming the months the figures leave out, where they leave any out. */
  readonly leftOut: string | null;
  /** Which days the dashed lines are, where they are drawn. */
  readonly against: string | null;
}

/**
 * The line under a picture: where its buckets are cut, and each thing about its figures a reader
 * needs before reading them off it — only those that are true of this picture.
 */
export function PictureNotes({
  cut,
  opens = null,
  cutShort,
  judging,
  leftOut,
  against,
}: PictureNotesProps) {
  const t = useTranslations('dashboard.chart');

  return (
    <p className="text-xs text-foreground-subtle">
      {cut}
      {opens === null ? null : <> {opens}</>}
      {cutShort === null ? null : <> {t(cutShort)}</>}
      {judging === null ? null : <> {t(judging)}</>}
      {leftOut === null ? null : <> {leftOut}</>}
      {against === null ? null : <> {against}</>}
    </p>
  );
}

/** One column of figures, a figure a bucket. */
export interface Column {
  /** What tells this column from the others, since two may share a name. */
  readonly key: string;
  /** What the column is headed with, in the reader's own language. */
  readonly name: string;
  /** The figure for each bucket, or nothing for a bucket that has none. */
  readonly values: readonly (number | null)[];
  /** Whether it is the whole the other columns are parts of, which is set in a heavier hand. */
  readonly whole?: boolean;
}

interface FiguresTableProps {
  /** Each bucket and the days it covers, one row each. */
  readonly days: readonly BucketDays[];
  /** What each row is headed with. */
  readonly rows: readonly string[];
  /** How the buckets are cut, and the zone they are read in. */
  readonly buckets: Buckets;
  /** The columns, in the order they are read. */
  readonly columns: readonly Column[];
  /** The first bucket still being judged, or nothing when every figure in the table is settled. */
  readonly stillJudging: number | null;
  /** The months the figures leave out because they were never counted, written `2025-06`. */
  readonly missing: readonly string[];
  /** The months they leave out because their visits were not all judged before they were kept. */
  readonly unsettled: readonly string[];
  /** Narrows the period to the days a row covers, where that is offered. */
  readonly onPick?: (index: number) => void;
  /** Which rows can be pressed, where only some of them can. */
  readonly pressable?: (index: number) => boolean;
}

/**
 * The figures a picture was drawn from, as a table with a row a bucket.
 *
 * A month left out says why in its own row: never counted, or not fully judged. A year adds up
 * the months of it that have figures, so its figure stands wherever any of them has one — and its
 * row names the months it leaves out, of either kind. A year none of whose months has a figure
 * says why in its own cells, as a month does.
 */
export function FiguresTable({
  days,
  rows,
  buckets,
  columns,
  stillJudging,
  missing,
  unsettled,
  onPick,
  pressable,
}: FiguresTableProps) {
  const t = useTranslations('dashboard.chart');
  const format = useFormatter();
  const { granularity, timeZoneId } = buckets;
  const leftOut = useMemo(
    () => [...missing, ...unsettled].sort((one, other) => one.localeCompare(other)),
    [missing, unsettled],
  );

  return (
    <Figures label={t('table')}>
      <thead className="text-xs text-foreground-subtle">
        <tr>
          <th scope="col" className={`${CELL} font-medium`}>
            {t(`column.${granularity}`)}
          </th>
          {columns.map((column) => (
            <th key={column.key} scope="col" className={`${CELL} text-right font-medium`}>
              {column.name}
            </th>
          ))}
        </tr>
      </thead>
      <tbody className="text-foreground-muted">
        {days.map((bucket, at) => (
          <tr key={bucket.start} className="border-t border-border">
            <BucketHeading
              label={rows[at]}
              oneDay={bucket.first === bucket.last}
              filling={stillJudging !== null && at >= stillJudging}
              leavesOut={
                granularity === 'year' ? leftOutOf(bucket, leftOut, timeZoneId, format) : undefined
              }
              at={at}
              onPick={(pressable?.(at) ?? true) ? onPick : undefined}
            />
            {columns.map((column) => (
              <td
                key={column.key}
                className={column.whole ? `${FIGURE} font-medium text-foreground` : FIGURE}
              >
                {count(column.values[at], format, t(nothingIn(bucket, unsettled)))}
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </Figures>
  );
}

/**
 * The months a year's figure leaves out, named, or nothing where it leaves none out.
 *
 * @param bucket The days of the year the period covers.
 * @param leftOut Every month the figures leave out, written `2025-06`, in order.
 * @param timeZoneId The website's zone, which the months are named in.
 * @param format The reader's own formatter.
 */
function leftOutOf(
  bucket: BucketDays,
  leftOut: readonly string[],
  timeZoneId: string,
  format: ReturnType<typeof useFormatter>,
): string | undefined {
  const first = bucket.first.slice(0, 7);
  const last = bucket.last.slice(0, 7);
  const months = leftOut.filter((month) => month >= first && month <= last);

  return months.length === 0 ? undefined : monthsOfItsYear(months, timeZoneId, format);
}

interface BucketHeadingProps {
  readonly label: string | undefined;
  /** Whether the bucket covers a single day of the period, which is what a press on it says. */
  readonly oneDay: boolean;
  /** Whether the bucket is one of those still being judged. */
  readonly filling: boolean;
  /**
   * The months its figure leaves out — never counted, or kept before their visits were all judged —
   * named, where there are any.
   */
  readonly leavesOut?: string;
  /** Which row this is, which is what a press on it hands back. */
  readonly at: number;
  readonly onPick?: (index: number) => void;
}

/**
 * A row's bucket, pressable where the period can be narrowed to the days it covers, and marked
 * beneath where its figure leaves out months — never counted, or kept before their visits were all
 * judged — and where that bucket has not finished being judged.
 */
function BucketHeading({ label, oneDay, filling, leavesOut, at, onPick }: BucketHeadingProps) {
  const t = useTranslations('dashboard.chart');

  return (
    <th scope="row" className="py-1.5 pr-3 font-normal last:pr-0 sm:pr-4">
      {/*
        The bucket's own name is the control, so the table gains no column for it and keeps its
        width on a phone. It is the way in for anybody without a pointer, and lands on the days the
        bucket covers — on a two-day period cut by the hour, the day the hour belongs to, and on a
        period cut by the week or the month, the days of it the period reaches — which its label
        already names.
      */}
      {onPick && label !== undefined ? (
        <button
          type="button"
          onClick={() => onPick(at)}
          aria-label={t(oneDay ? 'drill.row' : 'drill.rows', { bucket: label })}
          className="whitespace-nowrap rounded-sm underline decoration-foreground-subtle decoration-dotted underline-offset-4 hover:text-foreground hover:decoration-solid"
        >
          {label}
        </button>
      ) : (
        <span className="whitespace-nowrap">{label}</span>
      )}
      {/*
        Beneath the bucket rather than beside it. Said inline it would set the width of the first
        column for every row in the table, on account of the one or two rows that carry it.
      */}
      {leavesOut === undefined ? null : (
        <span className="block text-xs text-foreground-subtle">
          {t('leavesOut', { months: leavesOut })}
        </span>
      )}
      {filling ? (
        <span className="block whitespace-nowrap text-xs text-foreground-subtle">
          {t('who.stillJudging')}
        </span>
      ) : null}
    </th>
  );
}

/** Stands in for a bucket that the period being compared with never had. */
const MISSING = '—';

/**
 * A figure written out, a mark where the period it belongs to did not reach that bucket, and the
 * words saying why a bucket has no figure where it has none.
 *
 * @param value The figure, nothing for a bucket with no figure, or undefined beyond the period.
 * @param format The reader's own formatter.
 * @param unknown What a bucket with no figure is called: not counted, or not fully judged.
 */
function count(
  value: number | null | undefined,
  format: ReturnType<typeof useFormatter>,
  unknown: string,
): string {
  if (value === undefined) {
    return MISSING;
  }

  return value === null ? unknown : format.number(value);
}
