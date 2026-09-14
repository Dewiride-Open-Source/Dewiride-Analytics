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
import type { Granularity } from '@/lib/analytics/period';
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
  /** Whether only the visits judged to be people are drawn. */
  readonly peopleOnly: boolean;
  readonly onPeopleOnly: (peopleOnly: boolean) => void;
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
 * The picture at the top of a website's overview, in whichever of its views is being read.
 *
 * Three pictures of the same days. One counts everything a website recorded as it happened and
 * says how much of it was read. The other two count visits that have finished and been judged:
 * who and what they were, or how much the people among them read. Recorded and judged will not
 * add up, so neither is ever labelled as the other, and the picture being read says which it is.
 *
 * Whichever is on, the same figures are published twice — once as a drawing, and once as a table
 * anybody can open. A canvas tells a screen reader nothing at all, and a chart whose numbers exist
 * only as pixels is a chart some of this product's readers simply do not have.
 */
export function TrafficChart({
  view,
  onView,
  peopleOnly,
  onPeopleOnly,
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
          {/*
            Green while it is on, because green is the tone a person is drawn in everywhere else
            on the dashboard. The control then reads as the green band rather than as a second
            accent beside the one the comparison wears.
          */}
          <Button
            tone="secondary"
            size="sm"
            aria-pressed={peopleOnly}
            aria-label={t('people.label')}
            onClick={() => onPeopleOnly(!peopleOnly)}
            className={peopleOnly ? 'border-positive/40 bg-positive/12 text-positive' : undefined}
          >
            <UserRound aria-hidden className="size-4" />
            {t('people.action')}
          </Button>
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
          onShowActivity={() => {
            // Everything the website recorded, drawn as how much: the one picture that waits on
            // nothing being judged. Only what is not already so is written, because an address
            // written to unchanged is still another entry in the history.
            if (peopleOnly) {
              onPeopleOnly(false);
            }

            if (view !== 'activity') {
              onView('activity');
            }
          }}
          onShowEveryone={() => onPeopleOnly(false)}
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
}

interface DrawnProps extends ViewProps {
  readonly view: ChartView;
  readonly peopleOnly: boolean;
  readonly activity: readonly TrafficPoint[] | undefined;
  readonly who: TrafficSeries | undefined;
  readonly onShowActivity: () => void;
  readonly onShowEveryone: () => void;
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
  onShowActivity,
  onShowEveryone,
}: DrawnProps) {
  if (view === 'activity' && !peopleOnly) {
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

  if (who === undefined) {
    return <Settling />;
  }

  if (view === 'who') {
    return (
      <WhoView
        series={who}
        comparison={comparison}
        siteName={siteName}
        drawing={drawing}
        buckets={buckets}
        peopleOnly={peopleOnly}
        onShowActivity={onShowActivity}
        onShowEveryone={onShowEveryone}
      />
    );
  }

  return (
    <PeopleView
      series={who}
      comparison={comparison}
      siteName={siteName}
      drawing={drawing}
      buckets={buckets}
      onShowActivity={onShowActivity}
      onShowEveryone={onShowEveryone}
    />
  );
}

interface ActivityViewProps extends ViewProps {
  readonly points: readonly TrafficPoint[];
}

/** Page views and visitors across the period. */
function ActivityView({ points, comparison, siteName, drawing, buckets }: ActivityViewProps) {
  const t = useTranslations('dashboard.chart');
  const format = useFormatter();
  const { granularity, zone } = buckets;

  const starts = useMemo(() => points.map((point) => point.start), [points]);

  const labels = useMemo(
    () => starts.map((start) => write(start, format, buckets)),
    [starts, format, buckets],
  );

  // Counted in an hour, distinct visitors are the people who were there in that hour, which is a
  // different figure from the day's. Naming it the day's would be a claim the numbers do not make.
  const names = useMemo(
    () => [t('pageViews'), granularity === 'day' ? t('visitors') : t('visitorsByHour')] as const,
    [t, granularity],
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
    (palette: ChartPalette) => activityOption({ labels, measures, drawing, earlier }, palette),
    [labels, measures, drawing, earlier],
  );

  return (
    <>
      <Keys items={measureKeys(measures, earlier)} />

      <Picture option={option} label={t('summary', { site: siteName })} />

      <p className="text-xs text-foreground-subtle">
        {granularity === 'day' ? t('days', { zone }) : t('hours', { zone })}
        {earlier ? <> {t('against.lines', { days: comparison.days })}</> : null}
      </p>

      <FiguresTable
        keys={starts}
        labels={labels}
        granularity={granularity}
        columns={measureColumns(measures, earlier)}
        stillJudging={null}
      />
    </>
  );
}

interface PeopleViewProps extends ViewProps {
  readonly series: TrafficSeries;
  readonly onShowActivity: () => void;
  readonly onShowEveryone: () => void;
}

/**
 * How much the people a website is for read, across the period.
 *
 * Counts finished visits judged to be people and the pages those visits read — the population of
 * the picture of who came, not of the page-view figures in the cards above, which count everything
 * the website recorded as it happened. The two will not agree, and the caption says which this is.
 */
function PeopleView({
  series,
  comparison,
  siteName,
  drawing,
  buckets,
  onShowActivity,
  onShowEveryone,
}: PeopleViewProps) {
  const t = useTranslations('dashboard.chart');
  const format = useFormatter();
  const { granularity, zone } = buckets;

  const people = useMemo(() => peopleIn(series), [series]);
  const bands = useMemo(() => bandsIn(series), [series]);
  const stillJudging = useMemo(() => stillJudgingFrom(series), [series]);

  const labels = useMemo(
    () => series.buckets.map((bucket) => write(bucket, format, buckets)),
    [series, format, buckets],
  );

  const names = useMemo(() => [t('people.pageViews'), t('people.visits')] as const, [t]);

  const measures = useMemo(() => measured(names, people.pageViews, people.visits), [names, people]);

  const before = comparison.on ? comparison.who : undefined;

  const earlier = useMemo(() => {
    if (!before) {
      return undefined;
    }

    const theirs = peopleIn(before);

    return measured(
      [t('against.measure', { name: names[0] }), t('against.measure', { name: names[1] })],
      theirs.pageViews,
      theirs.visits,
    );
  }, [before, names, t]);

  const option = useCallback(
    (palette: ChartPalette) =>
      activityOption({ labels, measures, drawing, stillJudging, earlier }, palette),
    [labels, measures, drawing, stillJudging, earlier],
  );

  if (series.groups.length === 0) {
    return <NothingJudged onShowActivity={onShowActivity} />;
  }

  if (!bands.some((band) => band.tone === 'people')) {
    return <NobodyDrawn onShowEveryone={onShowEveryone} />;
  }

  return (
    <>
      <Keys items={measureKeys(measures, earlier)} />

      <Picture option={option} label={t('people.summary', { site: siteName })} />

      <p className="text-xs text-foreground-subtle">
        {granularity === 'day' ? t('people.days', { zone }) : t('people.hours', { zone })}
        {stillJudging === null ? null : <> {t('people.judging')}</>}
        {earlier ? <> {t('against.lines', { days: comparison.days })}</> : null}
      </p>

      <FiguresTable
        keys={series.buckets}
        labels={labels}
        granularity={granularity}
        columns={measureColumns(measures, earlier)}
        stillJudging={stillJudging}
      />
    </>
  );
}

interface WhoViewProps extends ViewProps {
  readonly series: TrafficSeries;
  /** Whether the stack is kept to the people a website is for. */
  readonly peopleOnly: boolean;
  readonly onShowActivity: () => void;
  readonly onShowEveryone: () => void;
}

/** Who and what visited, stacked across the period. */
function WhoView({
  series,
  comparison,
  siteName,
  drawing,
  buckets,
  peopleOnly,
  onShowActivity,
  onShowEveryone,
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
        ? { name: t('against.measure', { name: names.people }), visits: peopleIn(before).visits }
        : { name: t('against.measure', { name: t('who.total') }), visits: totalsIn(before) }),
    [before, peopleOnly, names, t],
  );

  const option = useCallback(
    (palette: ChartPalette) =>
      whoOption({ labels, bands, names, drawing, stillJudging, earlier }, palette),
    [labels, bands, names, drawing, stillJudging, earlier],
  );

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

  if (bands.length === 0) {
    return <NobodyDrawn onShowEveryone={onShowEveryone} />;
  }

  return (
    <>
      <Keys
        items={[
          ...bands.map((band) => ({ fill: TONE_FILLS[band.tone], label: names[band.tone] })),
          ...(earlier ? [{ fill: 'bg-foreground', label: earlier.name, dashed: true }] : []),
        ]}
      />

      <Picture option={option} label={t(`${wording}.summary`, { site: siteName })} />

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
 * straight away, in the one picture on this card that waits on nothing being judged — and that is
 * everybody's picture, whether or not this one was kept to people.
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

interface NobodyDrawnProps {
  readonly onShowEveryone: () => void;
}

/**
 * A picture kept to people, over a period judged to hold none.
 *
 * An answer rather than a matter of waiting, and the way out is everyone: the picture was kept to
 * people on purpose, and the control that did so is the one to undo it.
 */
function NobodyDrawn({ onShowEveryone }: NobodyDrawnProps) {
  const t = useTranslations('dashboard.chart');

  return (
    <NothingDrawn
      icon={UserRound}
      body={t('people.none')}
      action={
        <Button tone="secondary" size="sm" onClick={onShowEveryone}>
          {t('people.noneAction')}
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
}

/** The figures a picture was drawn from, as a table with a row a bucket. */
function FiguresTable({ keys, labels, granularity, columns, stillJudging }: FiguresTableProps) {
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
}

/** A row's bucket, marked beneath where that bucket has not finished being judged. */
function BucketHeading({ label, filling }: BucketHeadingProps) {
  const t = useTranslations('dashboard.chart');

  return (
    <th scope="row" className="py-1.5 pr-3 font-normal last:pr-0 sm:pr-4">
      <span className="whitespace-nowrap">{label}</span>
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
