'use client';

import {
  ChartArea,
  ChartColumn,
  ChartLine,
  ChevronDown,
  type LucideIcon,
  RotateCcwClock,
  ScanSearch,
  UserRound,
} from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { type ReactNode, useCallback, useMemo } from 'react';
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
import { type BucketDays, bucketDays, heldInDetail } from '@/lib/analytics/buckets';
import { CHART_BYS, type ChartBy } from '@/lib/analytics/chart-by';
import { CHART_VIEWS, type ChartView } from '@/lib/analytics/chart-view';
import {
  type AnalyticsWindow,
  type DaySpan,
  type Granularity,
  shortestPeriodFor,
} from '@/lib/analytics/period';
import {
  bandsIn,
  judgedNoneOf,
  peopleIn,
  stillJudgingFrom,
  totalsIn,
} from '@/lib/analytics/traffic-series';
import { TONE_ORDER, type VerdictTone } from '@/lib/analytics/verdicts';
import type { TrafficPicture, TrafficSeries } from '@/lib/api/schemas';
import {
  type BucketWriting,
  bucketNames,
  monthsInFull,
  monthsOfItsYear,
} from '@/lib/charts/bucket-names';
import { type Drawing, drawingFor, drawingsFor } from '@/lib/charts/drawing';
import { type FigureWriter, figureWriter } from '@/lib/charts/frame';
import type { ChartPalette } from '@/lib/charts/palette';
import { useDrawing } from '@/lib/charts/use-drawing';
import { type Quiet, quietensCutShort } from '@/lib/charts/wash';

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

/**
 * The ways the period on screen may be cut, and where a choice of one goes.
 *
 * Every way another period would show is listed beside the ways this one offers. Choosing one it
 * does not offer is still a choice, and where it goes is the screen's to decide: the screen owns the
 * period, and a way of cutting this period cannot show is a different period.
 */
export interface ChartCutting {
  /** The ways this period can be cut, its own way first. */
  readonly offered: readonly ChartBy[];
  /**
   * The ways the list names, in order: those this period offers and those it would move to another
   * period for. A way no period on screen can show for this website is left off it.
   */
  readonly listed: readonly ChartBy[];
  readonly onChoose: (cut: ChartBy) => void;
}

/** How the buckets are cut and written, which every part of this card reads back the same way. */
interface Buckets extends BucketWriting {
  /** The place whose clock those buckets follow, such as `Kolkata`. */
  readonly zone: string;
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

  const styles = useMemo(
    () =>
      drawingsFor(view, counted).map((one) => ({
        value: one,
        label: t(`drawing.${one}`),
        icon: PICTURES[one],
      })),
    [t, view, counted],
  );

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

interface ShowByProps {
  /** The way the picture is being cut, which is what the closed list reads as. */
  readonly granularity: Granularity;
  readonly cutting: ChartCutting;
}

/**
 * How finely the picture is cut.
 *
 * The browser's own list, as the period beside the website's name is: it opens correctly on a
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
function ShowBy({ granularity, cutting }: ShowByProps) {
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

/** The buckets of a picture with the days each covers, and each written for where it is read. */
interface Written {
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
function useWritten(starts: readonly string[], window: AnalyticsWindow, buckets: Buckets): Written {
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
function usePick(
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
interface Pressing {
  /** Narrows the screen to the days of a bucket, or nothing where no bucket can be pressed. */
  readonly pick: ((index: number) => void) | undefined;
  /** Which buckets can be pressed, or nothing where every one of them can. */
  readonly pressable: ((index: number) => boolean) | undefined;
}

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
 * @param judgedNone Whether not one bucket of the period has settled, as {@link judgedNoneOf} says.
 * @param wording Which picture's words to use.
 * @returns The sentence's key, or nothing.
 */
function judgingNote(
  stillJudging: number | null,
  judgedNone: boolean,
  wording: 'people' | 'who',
): `${'people' | 'who'}.${'judging' | 'judgingAll'}` | null {
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
function quietIn(days: readonly BucketDays[], stillJudging: number | null): Quiet {
  const ends = days.length > 1;

  return {
    cutAtStart: ends && days[0]?.whole === false,
    cutAtEnd: ends && days.at(-1)?.whole === false,
    judging: stillJudging,
  };
}

/** The ways of cutting a picture whose ends the period can cut short. */
type CutShort = Exclude<Granularity, 'hour' | 'day'>;

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
function cutShortNote(
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

/**
 * How the card a hover opens writes a figure: as the reader writes a number, and a bucket with no
 * figure as the table calls it.
 *
 * @param days Each bucket and the days it covers, which is what says why one has no figure.
 * @param unsettled Every month kept before its visits were all judged.
 */
function useFigure(days: readonly BucketDays[], unsettled: readonly string[]): FigureWriter {
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
 * The sentences naming the months a picture's figures leave out, or nothing where they leave none
 * out. A month never counted and a month kept before its visits were all judged are two different
 * facts, and each is named as the one it is: a gap in the drawing and a month nobody came in can
 * look alike, and a year that leaves one out looks like any other year.
 *
 * @param missing Every month never counted, written `2025-06`.
 * @param unsettled Every month kept before its visits were all judged.
 * @param timeZoneId The website's zone, which the months are named in.
 */
function useLeftOut(
  missing: readonly string[],
  unsettled: readonly string[],
  timeZoneId: string,
): string | null {
  const t = useTranslations('dashboard.chart');
  const format = useFormatter();

  return useMemo(() => {
    const said = sentences(
      missing.length === 0
        ? null
        : t('uncounted', {
            count: missing.length,
            months: monthsInFull(missing, timeZoneId, format),
          }),
      unsettled.length === 0
        ? null
        : t('unsettled', {
            count: unsettled.length,
            months: monthsInFull(unsettled, timeZoneId, format),
          }),
    );

    return said === '' ? null : said;
  }, [missing, unsettled, timeZoneId, format, t]);
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
  /** The clause about the ends the period cuts short, where the drawing fades them. */
  readonly cutShort: `cutShort.${CutShort}` | null;
  /** The clause about the buckets still being judged, where there are any. */
  readonly judging: `${'people' | 'who'}.${'judging' | 'judgingAll'}` | null;
  /** The sentences naming the months the figures leave out, where they leave any out. */
  readonly leftOut: string | null;
  /** Which days the dashed lines are, where they are drawn. */
  readonly against: string | null;
}

/**
 * The line under a picture: where its buckets are cut, and each thing about its figures a reader
 * needs before reading them off it — only those that are true of this picture.
 */
function PictureNotes({ cut, cutShort, judging, leftOut, against }: PictureNotesProps) {
  const t = useTranslations('dashboard.chart');

  return (
    <p className="text-xs text-foreground-subtle">
      {cut}
      {cutShort === null ? null : <> {t(cutShort)}</>}
      {judging === null ? null : <> {t(judging)}</>}
      {leftOut === null ? null : <> {leftOut}</>}
      {against === null ? null : <> {against}</>}
    </p>
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

/** One column of figures, a figure a bucket. */
interface Column extends Measure {
  /** What tells this column from the others, since two may share a name. */
  readonly key: string;
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
function FiguresTable({
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

/**
 * Sentences said one after another, leaving out any that are not said.
 *
 * @param said Each sentence, or nothing where it is not said.
 */
function sentences(...said: readonly (string | null)[]): string {
  return said.filter((sentence) => sentence !== null).join(' ');
}

/** The picture each drawing style is offered under. */
const PICTURES: Readonly<Record<Drawing, LucideIcon>> = {
  line: ChartLine,
  columns: ChartColumn,
  area: ChartArea,
};
