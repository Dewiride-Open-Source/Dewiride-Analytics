'use client';

import {
  ChartArea,
  ChartColumn,
  ChartLine,
  History,
  type LucideIcon,
  ScanSearch,
} from 'lucide-react';
import { type DateTimeFormatOptions, useFormatter, useTranslations } from 'next-intl';
import { type ReactNode, useCallback, useMemo } from 'react';
import { Chart } from '@/components/charts/chart';
import { activityOption } from '@/components/dashboard/activity-chart';
import { ListSwitch } from '@/components/dashboard/ranked-list';
import { TONE_FILLS } from '@/components/dashboard/verdict-badge';
import { whoOption } from '@/components/dashboard/who-chart';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { CHART_VIEWS, type ChartView } from '@/lib/analytics/chart-view';
import type { Granularity } from '@/lib/analytics/period';
import { bandsIn, stillJudgingFrom, totalsIn } from '@/lib/analytics/traffic-series';
import { TONE_ORDER, type VerdictTone } from '@/lib/analytics/verdicts';
import type { TrafficSeries } from '@/lib/api/schemas';
import { type Drawing, drawingFor, drawingsFor } from '@/lib/charts/drawing';
import type { ChartPalette } from '@/lib/charts/palette';
import { useDrawing } from '@/lib/charts/use-drawing';

/** One bucket of traffic, already lined up across both measures. */
export interface TrafficPoint {
  readonly start: string;
  readonly pageViews: number;
  readonly visitors: number;
}

/**
 * The period immediately before the one being read, drawn behind it.
 *
 * Asked for rather than always on. A second set of lines is a real cost to reading the first, and
 * most of the time somebody wants the shape of this period rather than of two at once — which is
 * why the headline numbers above say which way they moved whether or not this is being drawn.
 */
export interface TrafficComparison {
  /** Whether it is being drawn. */
  readonly on: boolean;
  readonly onChange: (against: boolean) => void;
  /** How much was read over it, or nothing while that is still on its way. */
  readonly activity: readonly TrafficPoint[] | undefined;
  /** Who came over it, or nothing while that is still on its way. */
  readonly who: TrafficSeries | undefined;
  /** The days it covers, already written in the website's own zone. */
  readonly days: string;
}

/** How the buckets are written, which every part of this card reads back the same way. */
interface Buckets {
  /** The site's own zone, so a bucket is read back in the day it was counted in. */
  readonly timeZoneId: string;
  /** The place whose clock those buckets follow, such as `Kolkata`. */
  readonly zone: string;
  readonly granularity: Granularity;
  /** Whether the period covers more than one day, which decides how a time is written. */
  readonly manyDays: boolean;
  /** Whether it covers more than one year, which decides whether a day carries one. */
  readonly manyYears: boolean;
}

interface TrafficChartProps extends Buckets {
  readonly view: ChartView;
  readonly onView: (view: ChartView) => void;
  /** Page views and visitors, or nothing while they are still on their way. */
  readonly activity: readonly TrafficPoint[] | undefined;
  /** What generated the traffic, or nothing while that is still on its way. */
  readonly who: TrafficSeries | undefined;
  /** Whichever of the two could not be read, where one could not. */
  readonly problem: unknown;
  readonly comparison: TrafficComparison;
  readonly siteName: string;
}

/**
 * The picture at the top of a website's overview, in whichever of its two views is being read.
 *
 * The views answer different questions about the same days and count different things doing it:
 * one counts everything a website recorded as it happened, the other counts visits that have
 * finished and been judged. They will not add up, so neither is ever labelled as the other, and
 * the view being read says which it is.
 *
 * Whichever is on, the same figures are published twice — once as a drawing, and once as a table
 * anybody can open. A canvas tells a screen reader nothing at all, and a chart whose numbers exist
 * only as pixels is a chart some of this product's readers simply do not have.
 */
export function TrafficChart({
  view,
  onView,
  activity,
  who,
  problem,
  comparison,
  siteName,
  timeZoneId,
  zone,
  granularity,
  manyDays,
  manyYears,
}: TrafficChartProps) {
  const t = useTranslations('dashboard.chart');
  const { drawing: remembered, choose } = useDrawing();
  const drawing = drawingFor(view, remembered);

  // Gathered once rather than assembled on the way down. Writing a bucket out is what each view
  // memoises against, and a fresh object every render would throw that away — at an hour a bucket
  // over a month, seven hundred dates rewritten for nothing.
  const buckets = useMemo(
    () => ({ timeZoneId, zone, granularity, manyDays, manyYears }),
    [timeZoneId, zone, granularity, manyDays, manyYears],
  );

  const views = useMemo(
    () => CHART_VIEWS.map((one) => ({ value: one, label: t(`view.${one}`) })),
    [t],
  );

  const styles = useMemo(
    () =>
      drawingsFor(view).map((one) => ({
        value: one,
        label: t(`drawing.${one}`),
        icon: PICTURES[one],
      })),
    [t, view],
  );

  return (
    <Card className="flex flex-col gap-4 p-5 sm:p-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>
        <div className="flex flex-wrap items-center gap-2">
          <ListSwitch label={t('view.label')} options={views} value={view} onChange={onView} />
          <ListSwitch
            label={t('drawing.label')}
            options={styles}
            value={drawing}
            onChange={choose}
          />
          <Button
            tone="secondary"
            size="sm"
            aria-pressed={comparison.on}
            aria-label={t('against.label')}
            onClick={() => comparison.onChange(!comparison.on)}
            className={
              comparison.on ? 'border-accent-strong bg-accent-soft text-accent-strong' : undefined
            }
          >
            <History aria-hidden className="size-4" />
            {t('against.action')}
          </Button>
        </div>
      </header>

      {problem === null || problem === undefined ? (
        <Drawn
          view={view}
          activity={activity}
          who={who}
          comparison={comparison}
          siteName={siteName}
          drawing={drawing}
          buckets={buckets}
          onShowActivity={() => onView('activity')}
        />
      ) : (
        <FailureNotice error={problem} />
      )}
    </Card>
  );
}

interface DrawnProps {
  readonly view: ChartView;
  readonly activity: readonly TrafficPoint[] | undefined;
  readonly who: TrafficSeries | undefined;
  readonly comparison: TrafficComparison;
  readonly siteName: string;
  readonly drawing: Drawing;
  readonly buckets: Buckets;
  readonly onShowActivity: () => void;
}

/** The picture the current view calls for, or the shape of one while its figures are on the way. */
function Drawn({
  view,
  activity,
  who,
  comparison,
  siteName,
  drawing,
  buckets,
  onShowActivity,
}: DrawnProps) {
  if (view === 'who') {
    return who === undefined ? (
      <Settling />
    ) : (
      <WhoView
        series={who}
        comparison={comparison}
        siteName={siteName}
        drawing={drawing}
        buckets={buckets}
        onShowActivity={onShowActivity}
      />
    );
  }

  return activity === undefined ? (
    <Settling />
  ) : (
    <ActivityView
      points={activity}
      comparison={comparison}
      siteName={siteName}
      drawing={drawing}
      buckets={buckets}
    />
  );
}

interface ActivityViewProps {
  readonly points: readonly TrafficPoint[];
  readonly comparison: TrafficComparison;
  readonly siteName: string;
  readonly drawing: Drawing;
  readonly buckets: Buckets;
}

/** Page views and visitors across the period. */
function ActivityView({ points, comparison, siteName, drawing, buckets }: ActivityViewProps) {
  const t = useTranslations('dashboard.chart');
  const format = useFormatter();
  const { granularity, zone } = buckets;

  const labels = useMemo(
    () => points.map((point) => write(point.start, format, buckets)),
    [points, format, buckets],
  );

  // Counted in an hour, distinct visitors are the people who were there in that hour, which is a
  // different figure from the day's. Naming it the day's would be a claim the numbers do not make.
  const names = useMemo(
    () => [t('pageViews'), granularity === 'day' ? t('visitors') : t('visitorsByHour')] as const,
    [t, granularity],
  );

  const pageViews = useMemo(() => points.map((point) => point.pageViews), [points]);
  const visitors = useMemo(() => points.map((point) => point.visitors), [points]);
  const before = comparison.on ? comparison.activity : undefined;

  const earlier = useMemo(
    () =>
      before && {
        names: [
          t('against.measure', { name: names[0] }),
          t('against.measure', { name: names[1] }),
        ] as const,
        pageViews: before.map((point) => point.pageViews),
        visitors: before.map((point) => point.visitors),
      },
    [before, names, t],
  );

  const option = useCallback(
    (palette: ChartPalette) =>
      activityOption({ labels, names, pageViews, visitors, drawing, earlier }, palette),
    [labels, names, pageViews, visitors, drawing, earlier],
  );

  return (
    <>
      <Keys
        items={[
          { fill: 'bg-chart-1', label: names[0] },
          { fill: 'bg-chart-2', label: names[1] },
          ...(earlier
            ? [
                { fill: 'bg-chart-1', label: earlier.names[0], dashed: true },
                { fill: 'bg-chart-2', label: earlier.names[1], dashed: true },
              ]
            : []),
        ]}
      />

      <Picture option={option} label={t('summary', { site: siteName })} />

      <p className="text-xs text-foreground-subtle">
        {granularity === 'day' ? t('days', { zone }) : t('hours', { zone })}
        {earlier ? <> {t('against.lines', { days: comparison.days })}</> : null}
      </p>

      <Figures label={t('table')}>
        <thead className="text-xs text-foreground-subtle">
          <tr>
            <th scope="col" className={`${CELL} font-medium`}>
              {granularity === 'day' ? t('columnDay') : t('columnHour')}
            </th>
            <th scope="col" className={`${CELL} text-right font-medium`}>
              {names[0]}
            </th>
            <th scope="col" className={`${CELL} text-right font-medium`}>
              {names[1]}
            </th>
            {earlier ? (
              <>
                <th scope="col" className={`${CELL} text-right font-medium`}>
                  {earlier.names[0]}
                </th>
                <th scope="col" className={`${CELL} text-right font-medium`}>
                  {earlier.names[1]}
                </th>
              </>
            ) : null}
          </tr>
        </thead>
        <tbody className="text-foreground-muted">
          {points.map((point, at) => (
            <tr key={point.start} className="border-t border-border">
              <th scope="row" className={`${CELL} font-normal`}>
                {labels[at]}
              </th>
              <td className={FIGURE}>{format.number(point.pageViews)}</td>
              <td className={FIGURE}>{format.number(point.visitors)}</td>
              {earlier ? (
                <>
                  <td className={FIGURE}>{count(earlier.pageViews[at], format)}</td>
                  <td className={FIGURE}>{count(earlier.visitors[at], format)}</td>
                </>
              ) : null}
            </tr>
          ))}
        </tbody>
      </Figures>
    </>
  );
}

interface WhoViewProps {
  readonly series: TrafficSeries;
  readonly comparison: TrafficComparison;
  readonly siteName: string;
  readonly drawing: Drawing;
  readonly buckets: Buckets;
  readonly onShowActivity: () => void;
}

/** Who and what visited, stacked across the period. */
function WhoView({ series, comparison, siteName, drawing, buckets, onShowActivity }: WhoViewProps) {
  const t = useTranslations('dashboard.chart');
  const tones = useTranslations('verdicts.tone');
  const format = useFormatter();
  const { granularity, zone } = buckets;

  const bands = useMemo(() => bandsIn(series), [series]);
  const totals = useMemo(() => totalsIn(series), [series]);
  const stillJudging = useMemo(() => stillJudgingFrom(series), [series]);

  const labels = useMemo(
    () => series.buckets.map((bucket) => write(bucket, format, buckets)),
    [series, format, buckets],
  );

  const names = useMemo(
    () =>
      Object.fromEntries(TONE_ORDER.map((tone) => [tone, tones(tone)])) as Record<
        VerdictTone,
        string
      >,
    [tones],
  );

  const before = comparison.on ? comparison.who : undefined;

  const earlier = useMemo(
    () =>
      before && {
        name: t('against.measure', { name: t('who.total') }),
        visits: totalsIn(before),
      },
    [before, t],
  );

  const option = useCallback(
    (palette: ChartPalette) =>
      whoOption({ labels, bands, names, drawing, stillJudging, earlier }, palette),
    [labels, bands, names, drawing, stillJudging, earlier],
  );

  if (bands.length === 0) {
    return (
      <NothingJudged body={t('who.none')} action={t('who.noneAction')} onAction={onShowActivity} />
    );
  }

  return (
    <>
      <Keys
        items={[
          ...bands.map((band) => ({ fill: TONE_FILLS[band.tone], label: names[band.tone] })),
          ...(earlier ? [{ fill: 'bg-foreground', label: earlier.name, dashed: true }] : []),
        ]}
      />

      <Picture option={option} label={t('who.summary', { site: siteName })} />

      <p className="text-xs text-foreground-subtle">
        {granularity === 'day' ? t('who.days', { zone }) : t('who.hours', { zone })}
        {stillJudging === null ? null : <> {t('who.judging')}</>}
        {earlier ? <> {t('who.against', { days: comparison.days })}</> : null}
      </p>

      <Figures label={t('table')}>
        <thead className="text-xs text-foreground-subtle">
          <tr>
            <th scope="col" className={`${CELL} font-medium`}>
              {granularity === 'day' ? t('columnDay') : t('columnHour')}
            </th>
            {/*
              The whole first, then what it was made of. A reader wants the day's figure before its
              parts, and on a phone the columns run off the side of the card — so the one number
              everybody came for is the one that is there without anybody having to scroll for it.
            */}
            <th scope="col" className={`${CELL} text-right font-medium`}>
              {t('who.total')}
            </th>
            {earlier ? (
              <th scope="col" className={`${CELL} text-right font-medium`}>
                {earlier.name}
              </th>
            ) : null}
            {bands.map((band) => (
              <th key={band.tone} scope="col" className={`${CELL} text-right font-medium`}>
                {names[band.tone]}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="text-foreground-muted">
          {series.buckets.map((bucket, at) => (
            <tr key={bucket} className="border-t border-border">
              <th scope="row" className="py-1.5 pr-3 font-normal last:pr-0 sm:pr-4">
                <span className="whitespace-nowrap">{labels[at]}</span>
                {/*
                  Beneath the bucket rather than beside it. Said inline it would set the width of
                  the first column for every row in the table, on account of the one or two rows
                  that carry it.
                */}
                {stillJudging !== null && at >= stillJudging ? (
                  <span className="block whitespace-nowrap text-xs text-foreground-subtle">
                    {t('who.stillJudging')}
                  </span>
                ) : null}
              </th>
              <td className={`${FIGURE} font-medium text-foreground`}>
                {count(totals[at], format)}
              </td>
              {earlier ? <td className={FIGURE}>{count(earlier.visits[at], format)}</td> : null}
              {bands.map((band) => (
                <td key={band.tone} className={FIGURE}>
                  {format.number(band.visits[at] ?? 0)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </Figures>
    </>
  );
}

/** One cell of either table, spaced so the columns read apart and the last one sits flush. */
const CELL = 'whitespace-nowrap py-1.5 pr-3 last:pr-0 sm:pr-4';

/** The same, for a cell holding a number. */
const FIGURE = `${CELL} text-right tabular-nums`;

/** Stands in for a bucket that the period being compared with never had. */
const MISSING = '—';

/** A figure written out, or a mark where the period it belongs to did not reach that bucket. */
function count(value: number | undefined, format: ReturnType<typeof useFormatter>): string {
  return value === undefined ? MISSING : format.number(value);
}

/** The picture each drawing style is offered under. */
const PICTURES: Readonly<Record<Drawing, LucideIcon>> = {
  line: ChartLine,
  columns: ChartColumn,
  area: ChartArea,
};

/**
 * How a bucket is written, which depends on how wide it is and what it sits among.
 *
 * An hour on its own needs no date beside it when every other bucket is from the same day, and
 * needs one the moment they are not. A day needs no year until the period runs across one, which
 * a period of a year and a day can.
 */
function labelling(buckets: Buckets): DateTimeFormatOptions {
  const timeZone = buckets.timeZoneId;

  if (buckets.granularity === 'day') {
    return buckets.manyYears
      ? { timeZone, day: 'numeric', month: 'short', year: 'numeric' }
      : { timeZone, day: 'numeric', month: 'short' };
  }

  // No minutes on either. A bucket an hour wide always begins on the hour, so the two zeroes say
  // nothing and cost the width that lets a day's worth of labels sit side by side on a phone.
  return buckets.manyDays
    ? { timeZone, day: 'numeric', month: 'short', hour: 'numeric' }
    : { timeZone, hour: 'numeric' };
}

/**
 * One bucket, written where the website is.
 *
 * A bucket is cut where the site is, so it has to be read back there too. Written without a zone
 * it is read in whichever one the person looking happens to be in, and a day counted in Kolkata
 * is labelled as the day before for anybody reading in London.
 */
function write(bucket: string, format: ReturnType<typeof useFormatter>, buckets: Buckets): string {
  return format.dateTime(new Date(bucket), labelling(buckets));
}

/** One entry in the key above the drawing. */
interface ChartKey {
  /** The class that paints the mark beside the words. */
  readonly fill: string;
  readonly label: string;
  /** Whether it stands for the period before, which is drawn as a dashed line. */
  readonly dashed?: boolean;
}

/** What each colour on the drawing means. */
function Keys({ items }: { readonly items: readonly ChartKey[] }) {
  return (
    <ul className="flex flex-wrap items-center gap-x-4 gap-y-1.5 text-xs text-foreground-muted">
      {items.map((item) => (
        <li key={item.label} className="flex items-center gap-1.5">
          {item.dashed ? (
            <span aria-hidden className="flex shrink-0 items-center gap-0.5 opacity-60">
              <span className={`h-0.5 w-1.5 rounded-full ${item.fill}`} />
              <span className={`h-0.5 w-1.5 rounded-full ${item.fill}`} />
            </span>
          ) : (
            <span aria-hidden className={`size-2 shrink-0 rounded-full ${item.fill}`} />
          )}
          {item.label}
        </li>
      ))}
    </ul>
  );
}

interface PictureProps {
  readonly option: (palette: ChartPalette) => Record<string, unknown>;
  readonly label: string;
}

/** The drawing itself, at the one height both views keep so switching does not jump the page. */
function Picture({ option, label }: PictureProps) {
  return (
    <div className="h-56 w-full sm:h-72">
      <Chart option={option} label={label} />
    </div>
  );
}

/** The same figures as a table, for anybody who reads rather than looks. */
function Figures({ label, children }: { readonly label: string; readonly children: ReactNode }) {
  return (
    <details className="group border-t border-border pt-3">
      <summary className="cursor-pointer text-sm font-medium text-foreground-muted marker:text-foreground-subtle hover:text-foreground">
        {label}
      </summary>
      <div className="mt-3 max-h-72 overflow-auto">
        <table className="w-full min-w-max text-left text-sm">{children}</table>
      </div>
    </details>
  );
}

/** The shape of the drawing, held while its figures are on their way. */
function Settling() {
  return <div className="h-56 w-full animate-pulse rounded-md bg-surface-muted sm:h-72" />;
}

interface NothingJudgedProps {
  readonly body: string;
  readonly action: string;
  readonly onAction: () => void;
}

/**
 * A period whose visits have not been judged yet.
 *
 * A designed state rather than an absence, and it carries the one thing worth doing from here.
 * A website whose verdicts are still coming has usually had plenty of traffic, and how much of it
 * there was can be read straight away.
 */
function NothingJudged({ body, action, onAction }: NothingJudgedProps) {
  return (
    <div className="flex h-56 flex-col items-center justify-center gap-3 text-center sm:h-72">
      <span
        aria-hidden
        className="flex size-11 items-center justify-center rounded-full bg-accent-soft"
      >
        <ScanSearch className="size-5 text-accent-strong" />
      </span>
      <p className="max-w-xs text-sm text-foreground-muted">{body}</p>
      <Button tone="secondary" size="sm" onClick={onAction}>
        {action}
      </Button>
    </div>
  );
}
