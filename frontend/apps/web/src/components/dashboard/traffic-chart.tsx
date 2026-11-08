'use client';

import { RotateCcwClock, ScanSearch, UserRound } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { type ReactNode, useCallback, useMemo } from 'react';
import {
  type Buckets,
  type Column,
  cutShortNote,
  FiguresTable,
  judgingNote,
  PictureNotes,
  quietIn,
  ShowBy,
  useDrawingOptions,
  useFigure,
  usePick,
  useWritten,
} from '@/components/charts/cut-picture';
import { type ChartKey, Keys } from '@/components/charts/keys';
import { NothingDrawn, Picture, Settling } from '@/components/charts/picture';
import { activityOption, type Measure } from '@/components/dashboard/activity-chart';
import { ListSwitch } from '@/components/dashboard/ranked-list';
import { TONE_FILLS } from '@/components/dashboard/verdict-badge';
import { whoOption } from '@/components/dashboard/who-chart';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import type { ChartCutting } from '@/lib/analytics/chart-by';
import { CHART_VIEWS, type ChartView } from '@/lib/analytics/chart-view';
import type { AnalyticsWindow, DaySpan } from '@/lib/analytics/period';
import {
  bandsIn,
  judgedNoneOf,
  peopleIn,
  stillJudgingFrom,
  totalsIn,
} from '@/lib/analytics/traffic-series';
import { TONE_ORDER, type VerdictTone } from '@/lib/analytics/verdicts';
import type { TrafficPicture, TrafficSeries } from '@/lib/api/schemas';
import { type Drawing, drawingFor } from '@/lib/charts/drawing';
import type { ChartPalette } from '@/lib/charts/palette';
import { useDrawing } from '@/lib/charts/use-drawing';
import { sentences, useLeftOut } from '@/lib/charts/use-left-out';

/**
 * One bucket of traffic, already lined up across both measures.
 *
 * A measure is nothing for a month of all time left out — never counted, or, for the people's
 * figures, kept before its visits were all judged — rather than nought: nobody knows how much was
 * read in it, which is not the same as nothing having been read.
 */
export interface TrafficPoint {
  readonly start: string;
  readonly pageViews: number | null;
  readonly visitors: number | null;
}

/**
 * How much was read across the period, lined up bucket by bucket, and where the judging stops.
 *
 * Carries the window it was counted over, since a week or a month at either end of it holds only
 * the days the window reaches, and a bucket is named by those days.
 */
export interface Activity extends AnalyticsWindow {
  readonly points: readonly TrafficPoint[];
  /**
   * The instant every figure is settled up to — the end of the window for everybody's figures,
   * since every report counts as it arrives.
   */
  readonly completeTo: string;
  /**
   * The first bucket still being judged, or nothing when every figure is settled — which
   * everybody's figures always are.
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
  readonly who: TrafficPicture | undefined;
  /** Whichever of the two could not be read, where one could not. */
  readonly problem: unknown;
  /**
   * The period before, where there is one to draw behind this one.
   *
   * Left out where the days it would be drawn from reach back past what a website still keeps, and
   * then nothing on the card offers it: a line drawn from days whose reports have gone is a line
   * drawn from nothing, and reads as a rise from nothing.
   */
  readonly comparison?: TrafficComparison;
  readonly siteName: string;
  /**
   * How finely the picture may be cut.
   *
   * Left out where there is no choice to make: on a period of two days or less, which is always
   * drawn an hour at a time, and on all time while every day of it falls in one calendar year,
   * which is always drawn a month at a time.
   */
  readonly cutting?: ChartCutting;
  /**
   * Told which days somebody pressed on the picture, where the period can still be narrowed.
   *
   * A bucket stands for the days of the period it covers: a day for a day or an hour, and for a
   * week or a month the days of it the period reaches. Left out on a period that is already a single
   * day: there is nothing narrower to look at, so neither the picture nor the rows of its table are
   * offered as something to press.
   */
  readonly onPick?: (days: DaySpan) => void;
  /**
   * The first day a bucket may begin on and still be pressed, where not every bucket can be.
   *
   * All time reaches back past the days a website still holds visit by visit, and a month older
   * than them is held as its totals alone: there is nothing narrower to show for it, so it is
   * neither pressed on the picture nor offered as a row to press.
   */
  readonly pressableFrom?: string;
  /**
   * The months the figures leave out because they were never counted, each written `2025-06` on
   * the website's calendar: named in the caption wherever there are any, and beside the year each
   * falls in, whose figure leaves it out without a mark of its own.
   */
  readonly missing?: readonly string[];
  /**
   * The months the figures leave out because they were kept before every visit in them had been
   * judged, written the same way: counted, so never called uncounted, but with no settled verdicts
   * for a figure counted from verdicts to stand on. Named apart from those never counted.
   */
  readonly unsettled?: readonly string[];
  /**
   * How many buckets the picture is due to have, known before its answer is — or nothing where that
   * is not known yet. It settles whether there is any choice of how to draw it.
   */
  readonly bucketsDue?: number;
}

/** No month left out: the same list every time, so a picture is not drawn again for nothing. */
const NONE_MISSING: readonly string[] = [];

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
  cutting,
  onPick,
  pressableFrom,
  missing = NONE_MISSING,
  unsettled = NONE_MISSING,
  bucketsDue,
}: TrafficChartProps) {
  const t = useTranslations('dashboard.chart');
  const { drawing: remembered, choose } = useDrawing();
  // A picture with too few buckets for a shape is drawn as columns whatever was asked for, and
  // offers nothing else to be drawn as, so the switch is not shown over it. How many it has is its
  // answer's, and until that is in the number it is due to have. Where neither is known — all time,
  // until its record says where it begins — whether the switch and the list of ways to cut the
  // picture are offered is not known either, so while the picture is on its way their places are
  // held without either being offered: neither is offered over a picture on its way and then taken
  // away. A record across a new year offers the list in its place, and the switch in its own where
  // it is drawn in three buckets or more — the usual record, its months across a new year, moves
  // nothing in the header — while a record within one calendar year gives up the list's place, and
  // one drawn in fewer than three buckets gives up the switch's. A picture that could not be had is
  // not on its way, and holds no place for controls that would never be offered.
  const counted =
    (view === 'activity' ? activity?.points.length : who?.buckets.length) ?? bucketsDue;
  const drawing = drawingFor(view, remembered, counted);
  const unknown = counted === undefined;
  const held = unknown && (problem === null || problem === undefined);

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

  const styles = useDrawingOptions(view, counted);

  return (
    <Card className="flex flex-col gap-4 p-5 sm:p-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>
        {/*
          Two groups that wrap as wholes rather than control by control. What is drawn sits with how
          it is drawn, and how finely it is cut with what it is set against, so on a phone the list
          and the button share a line and the picture of who came keeps its two switches on another.
          The picture of how much offers three ways of drawing, and on a phone that switch takes a
          line of its own rather than squeezing the view's names.
        */}
        <div className="flex flex-wrap items-center gap-2">
          <div className="flex flex-wrap items-center gap-2">
            <ListSwitch label={t('view.label')} options={views} value={view} onChange={onView} />
            {styles.length > 1 && (held || !unknown) ? (
              <Held held={held}>
                <ListSwitch
                  label={t('drawing.label')}
                  options={styles}
                  value={drawing}
                  onChange={choose}
                />
              </Held>
            ) : null}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {cutting || held ? (
              <Held held={cutting === undefined}>
                <ShowBy granularity={granularity} cutting={cutting ?? HELD_CUTTING} />
              </Held>
            ) : null}
            {comparison ? (
              <Button
                tone="secondary"
                size="sm"
                aria-pressed={comparison.on}
                aria-label={t('against.label')}
                onClick={() => comparison.onChange(!comparison.on)}
                className={
                  comparison.on
                    ? 'border-accent-strong bg-accent-soft text-accent-strong'
                    : undefined
                }
              >
                <RotateCcwClock aria-hidden className="size-4" />
                {t('against.action')}
              </Button>
            ) : null}
          </div>
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
          onPick={onPick}
          pressableFrom={pressableFrom}
          missing={missing}
          unsettled={unsettled}
          onShowActivity={() => onView('activity')}
        />
      ) : (
        <FailureNotice error={problem} />
      )}
    </Card>
  );
}

interface HeldProps {
  /** Whether the control's place is held rather than the control offered. */
  readonly held: boolean;
  readonly children: ReactNode;
}

/**
 * A control whose place is held while whether it is offered is not yet known: laid out all the
 * same, so the controls beside it and the picture below them stay where they are when it is
 * offered, and unseen, unheard and out of reach until it is.
 */
function Held({ held, children }: HeldProps) {
  return (
    <div
      className={held ? 'invisible contents' : 'contents'}
      aria-hidden={held || undefined}
      inert={held}
    >
      {children}
    </div>
  );
}

/**
 * What the list of ways to cut all time's picture holds its place as, until the record says what it
 * offers: the months and the years, which is what it offers wherever it offers a choice at all.
 */
const HELD_CUTTING: ChartCutting = {
  offered: ['month', 'year'],
  listed: ['month', 'year'],
  onChoose: () => undefined,
};

/** What every picture on the card is drawn from, whichever question it answers. */
interface ViewProps {
  readonly comparison: TrafficComparison | undefined;
  readonly siteName: string;
  readonly drawing: Drawing;
  readonly buckets: Buckets;
  /** The way from a bucket into the days it covers, where one is offered. */
  readonly onPick?: (days: DaySpan) => void;
  /** The first day a bucket may begin on and still be pressed, where not every bucket can be. */
  readonly pressableFrom?: string;
  /** The months the figures leave out because they were never counted, written `2025-06`. */
  readonly missing: readonly string[];
  /** The months they leave out because their visits were not all judged before they were kept. */
  readonly unsettled: readonly string[];
}

interface DrawnProps extends ViewProps {
  readonly view: ChartView;
  readonly peopleOnly: boolean;
  readonly activity: Activity | undefined;
  readonly who: TrafficPicture | undefined;
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
  onPick,
  pressableFrom,
  missing,
  unsettled,
  onShowActivity,
}: DrawnProps) {
  const shared = {
    comparison,
    siteName,
    drawing,
    buckets,
    onPick,
    pressableFrom,
    missing,
    unsettled,
  };

  if (view === 'activity') {
    return activity === undefined ? (
      <Settling />
    ) : (
      <ActivityView activity={activity} peopleOnly={peopleOnly} {...shared} />
    );
  }

  if (who === undefined) {
    return <Settling />;
  }

  return (
    <WhoView series={who} peopleOnly={peopleOnly} onShowActivity={onShowActivity} {...shared} />
  );
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
  onPick,
  pressableFrom,
  missing,
  unsettled,
}: ActivityViewProps) {
  const t = useTranslations('dashboard.chart');
  const { granularity, zone } = buckets;
  const { points, stillJudging } = activity;
  const note = judgingNote(
    stillJudging,
    judgedNoneOf(activity, points.length, stillJudging),
    'people',
  );

  // Where the picture's words are found: the people's measures carry the people in their names.
  const wording = peopleOnly ? 'people.activity' : 'activity';

  const starts = useMemo(() => points.map((point) => point.start), [points]);
  const { days, labels, rows, titles } = useWritten(starts, activity, buckets);
  const figure = useFigure(days, unsettled);
  const leftOut = useLeftOut(missing, unsettled, buckets.timeZoneId);

  // Counted in an hour, distinct visitors are the people who were there in that hour, which is a
  // different figure from the day's. Naming it the day's would be a claim the numbers do not make.
  // Over a week or a month they are counted exactly as the cards count them, and keep the cards'
  // name: somebody who comes back on another day counts again, as the caption beneath says.
  const names = useMemo(
    () =>
      [
        t(`${wording}.pageViews`),
        granularity === 'hour' ? t(`${wording}.visitorsByHour`) : t(`${wording}.visitors`),
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

  const shown = comparison?.on ? comparison : undefined;
  const before = shown?.activity;

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

  const quiet = useMemo(() => quietIn(days, stillJudging), [days, stillJudging]);

  const option = useCallback(
    (palette: ChartPalette) =>
      activityOption(
        { labels, titles, granularity, measures, figure, drawing, quiet, earlier },
        palette,
      ),
    [labels, titles, granularity, measures, figure, drawing, quiet, earlier],
  );

  const { pick, pressable } = usePick(days, onPick, pressableFrom);

  return (
    <>
      <Keys items={measureKeys(measures, earlier)} />

      <Picture
        option={option}
        label={t(`${wording}.summary`, { site: siteName })}
        onPick={pick}
        pressable={pressable}
      />

      <PictureNotes
        cut={t(`${wording}.cut.${granularity}`, { zone })}
        cutShort={cutShortNote(granularity, quiet, days.length, drawing)}
        judging={note}
        leftOut={leftOut}
        against={
          earlier && shown ? t('against.lines', { days: shown.days, cut: granularity }) : null
        }
      />

      <FiguresTable
        days={days}
        rows={rows}
        buckets={buckets}
        columns={measureColumns(measures, earlier)}
        stillJudging={stillJudging}
        missing={missing}
        unsettled={unsettled}
        onPick={pick}
        pressable={pressable}
      />
    </>
  );
}

interface WhoViewProps extends ViewProps {
  readonly series: TrafficPicture;
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
  onPick,
  pressableFrom,
  missing,
  unsettled,
  onShowActivity,
}: WhoViewProps) {
  const t = useTranslations('dashboard.chart');
  const tones = useTranslations('verdicts.tone');
  const { granularity, zone } = buckets;

  // Which of the two pictures is being drawn, since its words are found under that name.
  const wording = peopleOnly ? 'people' : 'who';

  const bands = useMemo(() => {
    const all = bandsIn(series);

    return peopleOnly ? all.filter((band) => band.tone === 'people') : all;
  }, [series, peopleOnly]);

  const totals = useMemo(() => totalsIn(series), [series]);
  const stillJudging = useMemo(() => stillJudgingFrom(series), [series]);
  const note = judgingNote(
    stillJudging,
    judgedNoneOf(series, series.buckets.length, stillJudging),
    wording,
  );
  const { days, labels, rows, titles } = useWritten(series.buckets, series, buckets);
  const figure = useFigure(days, unsettled);
  const leftOut = useLeftOut(missing, unsettled, buckets.timeZoneId);

  const names = useMemo(
    () =>
      Object.fromEntries(TONE_ORDER.map((tone) => [tone, tones(tone)])) as Record<
        VerdictTone,
        string
      >,
    [tones],
  );

  const shown = comparison?.on ? comparison : undefined;
  const before = shown?.who;

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

  const quiet = useMemo(() => quietIn(days, stillJudging), [days, stillJudging]);

  const option = useCallback(
    (palette: ChartPalette) =>
      whoOption(
        { labels, titles, granularity, bands, names, figure, drawing, quiet, earlier },
        palette,
      ),
    [labels, titles, granularity, bands, names, figure, drawing, quiet, earlier],
  );

  const { pick, pressable } = usePick(days, onPick, pressableFrom);

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

  // Either way the months left out are named: nothing judged, or no people, is only so of the
  // months that were counted and settled.
  if (series.groups.length === 0) {
    return <NothingJudged onShowActivity={onShowActivity} leftOut={leftOut} />;
  }

  // An answer rather than a matter of waiting — except over buckets still being judged, which may
  // yet hold people, and where it says so. No way out is offered here, because the control that
  // kept the screen to people sits above the card and is the way out of it.
  if (bands.length === 0) {
    return (
      <NothingDrawn
        icon={UserRound}
        body={sentences(t('people.none'), note === null ? null : t(note), leftOut)}
      />
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

      <Picture
        option={option}
        label={t(`${wording}.summary`, { site: siteName })}
        onPick={pick}
        pressable={pressable}
      />

      <PictureNotes
        cut={t(`${wording}.cut.${granularity}`, { zone })}
        cutShort={cutShortNote(granularity, quiet, days.length, drawing)}
        judging={note}
        leftOut={leftOut}
        against={earlier && shown ? t('who.against', { days: shown.days, cut: granularity }) : null}
      />

      <FiguresTable
        days={days}
        rows={rows}
        buckets={buckets}
        columns={columns}
        stillJudging={stillJudging}
        missing={missing}
        unsettled={unsettled}
        onPick={pick}
        pressable={pressable}
      />
    </>
  );
}

interface NothingJudgedProps {
  readonly onShowActivity: () => void;
  /** The sentences naming the months the picture leaves out, where it leaves any out. */
  readonly leftOut: string | null;
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
function NothingJudged({ onShowActivity, leftOut }: NothingJudgedProps) {
  const t = useTranslations('dashboard.chart');

  return (
    <NothingDrawn
      icon={ScanSearch}
      body={sentences(t('who.none'), leftOut)}
      action={
        <Button tone="secondary" size="sm" onClick={onShowActivity}>
          {t('who.noneAction')}
        </Button>
      }
    />
  );
}

/** Two measures of a period, in the order they are drawn, keyed and tabled. */
function measured(
  names: readonly [string, string],
  first: readonly (number | null)[],
  second: readonly (number | null)[],
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
