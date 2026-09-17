'use client';

import {
  ChartArea,
  ChartColumn,
  ChartLine,
  History,
  type LucideIcon,
  ScanSearch,
  UserRound,
} from 'lucide-react';
import { type DateTimeFormatOptions, useFormatter, useTranslations } from 'next-intl';
import { useCallback, useMemo } from 'react';
import { CELL, FIGURE, Figures } from '@/components/charts/figures';
import { type ChartKey, Keys } from '@/components/charts/keys';
import { NothingDrawn, Picture, Settling } from '@/components/charts/picture';
import { activityOption, type Measure } from '@/components/dashboard/activity-chart';
import { ListSwitch } from '@/components/dashboard/ranked-list';
import { TONE_FILLS } from '@/components/dashboard/verdict-badge';
import { whoOption } from '@/components/dashboard/who-chart';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { CHART_VIEWS, type ChartView } from '@/lib/analytics/chart-view';
import { dayOf, type Granularity } from '@/lib/analytics/period';
import { bandsIn, peopleIn, stillJudgingFrom, totalsIn } from '@/lib/analytics/traffic-series';
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

/** How much was read across the period, lined up bucket by bucket, and where the judging stops. */
export interface Activity {
  readonly points: readonly TrafficPoint[];
  /**
   * The first bucket still being judged, or nothing when every figure is settled — which
   * everybody's figures always are, since every report counts as it arrives.
   */
  readonly stillJudging: number | null;
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
  /**
   * Whether every figure is kept to the visits judged to be people, which decides what the
   * picture is called and whether its tail is washed.
   */
  readonly peopleOnly: boolean;
  /** Page views and visitors, or nothing while they are still on their way. */
  readonly activity: Activity | undefined;
  /** What generated the traffic, or nothing while that is still on its way. */
  readonly who: TrafficSeries | undefined;
  /** Whichever of the two could not be read, where one could not. */
  readonly problem: unknown;
  readonly comparison: TrafficComparison;
  readonly siteName: string;
  /**
   * Told which day somebody pressed on the picture, where the period can still be narrowed to one.
   *
   * Left out on a period that is already a single day: there is nothing narrower to look at, so
   * neither the picture nor the rows of its table are offered as something to press.
   */
  readonly onPickDay?: (day: string) => void;
}

/**
 * The picture at the top of a website's overview, in whichever of its views is being read.
 *
 * Two pictures of the same days. One counts everything recorded as it happened, for whichever
 * population the screen is kept to, and says how much of it was read; the other counts visits that
 * have finished and been judged, and says who and what they were. The picture being read says
 * which it is.
 *
 * Whichever is on, the same figures are published twice — once as a drawing, and once as a table
 * anybody can open. A canvas tells a screen reader nothing at all, and a chart whose numbers exist
 * only as pixels is a chart some of this product's readers simply do not have.
 */
export function TrafficChart({
  view,
  onView,
  peopleOnly,
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
  onPickDay,
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
          peopleOnly={peopleOnly}
          activity={activity}
          who={who}
          comparison={comparison}
          siteName={siteName}
          drawing={drawing}
          buckets={buckets}
          onPickDay={onPickDay}
          onShowActivity={() => onView('activity')}
        />
      ) : (
        <FailureNotice error={problem} />
      )}
    </Card>
  );
}

/** What every picture on the card is drawn from, whichever question it answers. */
interface ViewProps {
  readonly comparison: TrafficComparison;
  readonly siteName: string;
  readonly drawing: Drawing;
  readonly buckets: Buckets;
  /** The way from a bucket into the day it falls in, where one is offered. */
  readonly onPickDay?: (day: string) => void;
}

interface DrawnProps extends ViewProps {
  readonly view: ChartView;
  readonly peopleOnly: boolean;
  readonly activity: Activity | undefined;
  readonly who: TrafficSeries | undefined;
  readonly onShowActivity: () => void;
}

/** The picture the current view calls for, or the shape of one while its figures are on the way. */
function Drawn({
  view,
  peopleOnly,
  activity,
  who,
  comparison,
  siteName,
  drawing,
  buckets,
  onPickDay,
  onShowActivity,
}: DrawnProps) {
  if (view === 'activity') {
    return activity === undefined ? (
      <Settling />
    ) : (
      <ActivityView
        activity={activity}
        peopleOnly={peopleOnly}
        comparison={comparison}
        siteName={siteName}
        drawing={drawing}
        buckets={buckets}
        onPickDay={onPickDay}
      />
    );
  }

  if (who === undefined) {
    return <Settling />;
  }

  return (
    <WhoView
      series={who}
      comparison={comparison}
      siteName={siteName}
      drawing={drawing}
      buckets={buckets}
      peopleOnly={peopleOnly}
      onPickDay={onPickDay}
      onShowActivity={onShowActivity}
    />
  );
}

/**
 * The way from a bucket of the picture into the day it falls in, or nothing where that is not
 * offered.
 *
 * A bucket is named by the instant it begins at, and the day it belongs to is that instant's day
 * where the website is: a day picks itself, and an hour of a two-day period picks the day it
 * falls in. The same function serves the picture and the rows of its table, so the two cannot
 * disagree about where a press lands. A press the surface places just past the last bucket — its
 * far edge rounds up to one more than there are — names no day.
 */
function usePick(
  starts: readonly string[],
  timeZoneId: string,
  onPickDay: ((day: string) => void) | undefined,
): ((index: number) => void) | undefined {
  return useMemo(() => {
    if (!onPickDay) {
      return undefined;
    }

    return (index: number) => {
      const start = starts[index];

      if (start !== undefined) {
        onPickDay(dayOf(timeZoneId, new Date(start)));
      }
    };
  }, [starts, timeZoneId, onPickDay]);
}

interface ActivityViewProps extends ViewProps {
  readonly activity: Activity;
  /** Whether the figures are the people's, which is what they are then called. */
  readonly peopleOnly: boolean;
}

/**
 * Page views and visitors across the period.
 *
 * The same arithmetic as the cards above, over the same population, so the table under the
 * picture adds up to them. Kept to people, the measures are named for the people and the buckets
 * the engine has not finished judging are washed, exactly as the picture of who came washes them.
 */
function ActivityView({
  activity,
  peopleOnly,
  comparison,
  siteName,
  drawing,
  buckets,
  onPickDay,
}: ActivityViewProps) {
  const t = useTranslations('dashboard.chart');
  const format = useFormatter();
  const { granularity, zone } = buckets;
  const { points, stillJudging } = activity;

  // Where the picture's words are found: the people's measures carry the people in their names.
  const wording = peopleOnly ? 'people.activity' : 'activity';

  const starts = useMemo(() => points.map((point) => point.start), [points]);

  const labels = useMemo(
    () => starts.map((start) => write(start, format, buckets)),
    [starts, format, buckets],
  );

  // Counted in an hour, distinct visitors are the people who were there in that hour, which is a
  // different figure from the day's. Naming it the day's would be a claim the numbers do not make.
  const names = useMemo(
    () =>
      [
        t(`${wording}.pageViews`),
        granularity === 'day' ? t(`${wording}.visitors`) : t(`${wording}.visitorsByHour`),
      ] as const,
    [t, wording, granularity],
  );

  const measures = useMemo(
    () =>
      measured(
        names,
        points.map((point) => point.pageViews),
        points.map((point) => point.visitors),
      ),
    [names, points],
  );

  const before = comparison.on ? comparison.activity : undefined;

  const earlier = useMemo(
    () =>
      before &&
      measured(
        [t('against.measure', { name: names[0] }), t('against.measure', { name: names[1] })],
        before.map((point) => point.pageViews),
        before.map((point) => point.visitors),
      ),
    [before, names, t],
  );

  const option = useCallback(
    (palette: ChartPalette) =>
      activityOption({ labels, measures, drawing, stillJudging, earlier }, palette),
    [labels, measures, drawing, stillJudging, earlier],
  );

  const pick = usePick(starts, buckets.timeZoneId, onPickDay);

  return (
    <>
      <Keys items={measureKeys(measures, earlier)} />

      <Picture option={option} label={t(`${wording}.summary`, { site: siteName })} onPick={pick} />

      <p className="text-xs text-foreground-subtle">
        {granularity === 'day' ? t(`${wording}.days`, { zone }) : t(`${wording}.hours`, { zone })}
        {stillJudging === null ? null : <> {t('people.judging')}</>}
        {earlier ? <> {t('against.lines', { days: comparison.days })}</> : null}
      </p>

      <FiguresTable
        keys={starts}
        labels={labels}
        granularity={granularity}
        columns={measureColumns(measures, earlier)}
        stillJudging={stillJudging}
        onPick={pick}
      />
    </>
  );
}

interface WhoViewProps extends ViewProps {
  readonly series: TrafficSeries;
  /** Whether the stack is kept to the people a website is for. */
  readonly peopleOnly: boolean;
  readonly onShowActivity: () => void;
}

/** Who and what visited, stacked across the period. */
function WhoView({
  series,
  comparison,
  siteName,
  drawing,
  buckets,
  peopleOnly,
  onPickDay,
  onShowActivity,
}: WhoViewProps) {
  const t = useTranslations('dashboard.chart');
  const tones = useTranslations('verdicts.tone');
  const format = useFormatter();
  const { granularity, zone } = buckets;

  // Which of the two pictures is being drawn, since its words are found under that name.
  const wording = peopleOnly ? 'people' : 'who';

  const bands = useMemo(() => {
    const all = bandsIn(series);

    return peopleOnly ? all.filter((band) => band.tone === 'people') : all;
  }, [series, peopleOnly]);

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

  // The whole of the earlier period, or its people alone where that is all this one is showing:
  // a dashed line for everybody behind a stack kept to people would tower over it and say nothing
  // about whether more of them came.
  const earlier = useMemo(
    () =>
      before &&
      (peopleOnly
        ? { name: t('against.measure', { name: names.people }), visits: peopleIn(before) }
        : { name: t('against.measure', { name: t('who.total') }), visits: totalsIn(before) }),
    [before, peopleOnly, names, t],
  );

  const option = useCallback(
    (palette: ChartPalette) =>
      whoOption({ labels, bands, names, drawing, stillJudging, earlier }, palette),
    [labels, bands, names, drawing, stillJudging, earlier],
  );

  const pick = usePick(series.buckets, buckets.timeZoneId, onPickDay);

  // The whole first, then what it was made of: a reader wants the day's figure before its parts,
  // and on a phone the columns run off the side of the card, so the one number everybody came for
  // is the one that is there without anybody having to scroll for it. Kept to people, the one
  // band is that number, and the period before follows it.
  const columns = useMemo<readonly Column[]>(() => {
    const parts = bands.map((band) => ({
      key: band.tone,
      name: names[band.tone],
      values: band.visits,
    }));
    const before = earlier ? [{ key: 'earlier', name: earlier.name, values: earlier.visits }] : [];

    return peopleOnly
      ? [...parts, ...before]
      : [{ key: 'whole', name: t('who.total'), values: totals, whole: true }, ...before, ...parts];
  }, [bands, names, earlier, peopleOnly, totals, t]);

  if (series.groups.length === 0) {
    return <NothingJudged onShowActivity={onShowActivity} />;
  }

  // An answer rather than a matter of waiting. No way out is offered here, because the control
  // that kept the screen to people sits above the card and is the way out of it.
  if (bands.length === 0) {
    return <NothingDrawn icon={UserRound} body={t('people.none')} />;
  }

  return (
    <>
      <Keys
        items={[
          ...bands.map((band) => ({ fill: TONE_FILLS[band.tone], label: names[band.tone] })),
          ...(earlier ? [{ fill: 'bg-foreground', label: earlier.name, dashed: true }] : []),
        ]}
      />

      <Picture option={option} label={t(`${wording}.summary`, { site: siteName })} onPick={pick} />

      <p className="text-xs text-foreground-subtle">
        {granularity === 'day' ? t(`${wording}.days`, { zone }) : t(`${wording}.hours`, { zone })}
        {stillJudging === null ? null : <> {t(`${wording}.judging`)}</>}
        {earlier ? <> {t('who.against', { days: comparison.days })}</> : null}
      </p>

      <FiguresTable
        keys={series.buckets}
        labels={labels}
        granularity={granularity}
        columns={columns}
        stillJudging={stillJudging}
        onPick={pick}
      />
    </>
  );
}

interface NothingJudgedProps {
  readonly onShowActivity: () => void;
}

/**
 * A judged picture of a period nothing has been judged in yet.
 *
 * A website whose verdicts are still coming has usually had plenty of traffic, so the way out is
 * offered here rather than left for somebody to find: how much of it there was can be read
 * straight away, in the one picture on this card that waits on nothing being judged. Kept to
 * people, that picture is the people's how much, which the engine answers from the same reports
 * the cards above are counted from.
 */
function NothingJudged({ onShowActivity }: NothingJudgedProps) {
  const t = useTranslations('dashboard.chart');

  return (
    <NothingDrawn
      icon={ScanSearch}
      body={t('who.none')}
      action={
        <Button tone="secondary" size="sm" onClick={onShowActivity}>
          {t('who.noneAction')}
        </Button>
      }
    />
  );
}

/** One column of figures, a figure a bucket. */
interface Column extends Measure {
  /** What tells this column from the others, since two may share a name. */
  readonly key: string;
  /** Whether it is the whole the other columns are parts of, which is set in a heavier hand. */
  readonly whole?: boolean;
}

interface FiguresTableProps {
  /** What tells one row from another, one per bucket. */
  readonly keys: readonly string[];
  readonly labels: readonly string[];
  readonly granularity: Granularity;
  /** The columns, in the order they are read. */
  readonly columns: readonly Column[];
  /** The first bucket still being judged, or nothing when every figure in the table is settled. */
  readonly stillJudging: number | null;
  /** Narrows the period to the day a row falls in, where that is offered. */
  readonly onPick?: (index: number) => void;
}

/** The figures a picture was drawn from, as a table with a row a bucket. */
function FiguresTable({
  keys,
  labels,
  granularity,
  columns,
  stillJudging,
  onPick,
}: FiguresTableProps) {
  const t = useTranslations('dashboard.chart');
  const format = useFormatter();

  return (
    <Figures label={t('table')}>
      <thead className="text-xs text-foreground-subtle">
        <tr>
          <th scope="col" className={`${CELL} font-medium`}>
            {granularity === 'day' ? t('columnDay') : t('columnHour')}
          </th>
          {columns.map((column) => (
            <th key={column.key} scope="col" className={`${CELL} text-right font-medium`}>
              {column.name}
            </th>
          ))}
        </tr>
      </thead>
      <tbody className="text-foreground-muted">
        {keys.map((key, at) => (
          <tr key={key} className="border-t border-border">
            <BucketHeading
              label={labels[at]}
              filling={stillJudging !== null && at >= stillJudging}
              at={at}
              onPick={onPick}
            />
            {columns.map((column) => (
              <td
                key={column.key}
                className={column.whole ? `${FIGURE} font-medium text-foreground` : FIGURE}
              >
                {count(column.values[at], format)}
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </Figures>
  );
}

interface BucketHeadingProps {
  readonly label: string | undefined;
  /** Whether the bucket is one of those still being judged. */
  readonly filling: boolean;
  /** Which row this is, which is what a press on it hands back. */
  readonly at: number;
  readonly onPick?: (index: number) => void;
}

/**
 * A row's bucket, pressable where the period can be narrowed to the day it falls in, and marked
 * beneath where that bucket has not finished being judged.
 */
function BucketHeading({ label, filling, at, onPick }: BucketHeadingProps) {
  const t = useTranslations('dashboard.chart');

  return (
    <th scope="row" className="py-1.5 pr-3 font-normal last:pr-0 sm:pr-4">
      {/*
        The bucket's own name is the control, so the table gains no column for it and keeps its
        width on a phone. It is the way in for anybody without a pointer, and lands on the day the
        bucket falls in — on a two-day period cut by the hour, the day the hour belongs to, which
        its label already names.
      */}
      {onPick && label !== undefined ? (
        <button
          type="button"
          onClick={() => onPick(at)}
          aria-label={t('drill.row', { bucket: label })}
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
      {filling ? (
        <span className="block whitespace-nowrap text-xs text-foreground-subtle">
          {t('who.stillJudging')}
        </span>
      ) : null}
    </th>
  );
}

/** Two measures of a period, in the order they are drawn, keyed and tabled. */
function measured(
  names: readonly [string, string],
  first: readonly number[],
  second: readonly number[],
): readonly [Measure, Measure] {
  return [
    { name: names[0], values: first },
    { name: names[1], values: second },
  ];
}

/** Two measures as the columns of a table, with the period before's beside them where drawn. */
function measureColumns(
  measures: readonly [Measure, Measure],
  earlier: readonly [Measure, Measure] | undefined,
): readonly Column[] {
  return (earlier ? [...measures, ...earlier] : measures).map((measure) => ({
    ...measure,
    key: measure.name,
  }));
}

/**
 * The key above two measures, each in the chart's own colour for it.
 *
 * The period before is keyed dashed in the same two colours, so that the pair a rise or a fall is
 * read off is a pair in the key as well.
 */
function measureKeys(
  measures: readonly [Measure, Measure],
  earlier: readonly [Measure, Measure] | undefined,
): readonly ChartKey[] {
  return [
    { fill: 'bg-chart-1', label: measures[0].name },
    { fill: 'bg-chart-2', label: measures[1].name },
    ...(earlier
      ? [
          { fill: 'bg-chart-1', label: earlier[0].name, dashed: true },
          { fill: 'bg-chart-2', label: earlier[1].name, dashed: true },
        ]
      : []),
  ];
}

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
