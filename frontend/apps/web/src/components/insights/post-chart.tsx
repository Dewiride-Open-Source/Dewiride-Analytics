'use client';

import { BookOpen, CalendarClock } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useCallback, useMemo } from 'react';
import {
  type Buckets,
  cutShortNote,
  FiguresTable,
  type JudgingNote,
  judgingNote,
  PeriodMoved,
  PictureNotes,
  quietIn,
  ShowBy,
  useDrawingOptions,
  useFigure,
  usePick,
  useWritten,
} from '@/components/charts/cut-picture';
import { Keys } from '@/components/charts/keys';
import { NothingDrawn, Picture, Settling } from '@/components/charts/picture';
import { activityOption, type Measure } from '@/components/dashboard/activity-chart';
import { PeriodPicker } from '@/components/dashboard/period-picker';
import { ListSwitch } from '@/components/dashboard/ranked-list';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { writableDay } from '@/lib/analytics/all-time';
import type { Population } from '@/lib/analytics/people-only';
import {
  type AllTimeGranularity,
  type AnalyticsWindow,
  crossesYears,
  type DaySpan,
  daysIn,
  type Granularity,
  isAllTime,
  keptFrom,
  type PeriodGranularity,
  spanFor,
  todayIn,
  windowFor,
  windowForSpan,
} from '@/lib/analytics/period';
import { leftOutOf, postOpensOn, recordPicture, type ViewsPicture } from '@/lib/analytics/post';
import { judgedNoneOf, stillJudgingFrom } from '@/lib/analytics/traffic-series';
import { usePeriod } from '@/lib/analytics/use-period';
import {
  type DrawnOver,
  useHandOver,
  usePickDays,
  usePicturePlan,
} from '@/lib/analytics/use-picture-plan';
import type { PostInsights, Site } from '@/lib/api/schemas';
import { type Drawing, drawingFor } from '@/lib/charts/drawing';
import type { ChartPalette } from '@/lib/charts/palette';
import { useDrawing } from '@/lib/charts/use-drawing';
import { sentences, useLeftOut } from '@/lib/charts/use-left-out';
import { usePostSeries } from '@/lib/queries/sites';
import { readableZone } from '@/lib/time-zones';

/** The card's own spacing, which its stand-in keeps as well. */
const CARD = 'flex min-w-0 flex-col gap-4 p-5 sm:p-6';

/**
 * The card's heading and its controls: on one line wherever they fit, and the controls on lines of
 * their own beneath the heading on a phone.
 */
const HEADER = 'flex flex-wrap items-start justify-between gap-3';

/** The card's heading, set on the line the controls beside it sit on. */
const TITLE = 'text-base font-semibold text-foreground sm:leading-9';

/** The card's controls, and the places held for them while the post's record is on its way. */
const CONTROLS = 'flex w-full flex-wrap items-start gap-2 sm:w-auto';

/** No month left out: the same list every time, so nothing is drawn again for it. */
const NONE_LEFT_OUT: readonly string[] = Object.freeze([]);

interface PostChartProps {
  readonly site: Site;
  /** The post's record: the day the post begins on, and the months all time is drawn from. */
  readonly answer: PostInsights;
  /** Whose views are counted. */
  readonly population: Population;
  /** Whether they are the people's, which the picture is then named for. */
  readonly peopleOnly: boolean;
}

/**
 * A post's views over the period somebody chose, drawn as the overview draws a website's: cut by the
 * day, the week or the month, and all time by the month or the year, in the same ways of drawing, with
 * the same press on a bucket to narrow the period to its days and the same figures as a table.
 *
 * The period is chosen on this card, the one card on the screen about a period: every other reads the
 * post's whole record. It is the overview's own period, carried in the address and remembered, so
 * somebody reading their website over the last 30 days reads its posts over the same days. One
 * measure, the post's views, and nothing set behind it to compare with.
 *
 * All time is drawn from the months the post's record already holds, and begins on the post's own
 * first day rather than the website's: years are offered only where the post itself runs across a new
 * year, and its first month is written as the days of it the post was counted on. Every other period
 * is asked of the engine, which counts the post's views from the reports — from the day the post was
 * published where that falls inside the period, so the days before it are not drawn as days nobody
 * read it, and not at all for a period that ends before it.
 */
export function PostChart({ site, answer, population, peopleOnly }: PostChartProps) {
  const t = useTranslations('insights.post.chart');
  const chart = useTranslations('dashboard.chart');
  const { period, choose } = usePeriod({ seeding: true });
  const { drawing: remembered, choose: drawAs } = useDrawing();
  const { timeZoneId } = site;

  // Resolved once per period rather than on every render, as the overview resolves it: the window is
  // part of the name the answer is cached under, and one that moved with the clock would never find
  // a cached answer.
  const { now, span, window, firstKept } = useMemo(() => {
    const resolvedAt = new Date();

    return {
      now: resolvedAt,
      span: spanFor(period, timeZoneId, resolvedAt),
      window: windowFor(period, timeZoneId, resolvedAt),
      // Where the days held one by one begin, which is as far back as all time's picture can be
      // pressed into: a month older than it is held as its totals and nothing finer.
      firstKept: keptFrom(timeZoneId, resolvedAt),
    };
  }, [period, timeZoneId]);

  const { control, moveTo } = useHandOver(choose);
  const allTime = isAllTime(period);
  // The post's own days, from the first its record counts to today: the days all time covers, and
  // what says whether they run across a new year.
  const recordDays = useMemo(() => {
    const today = todayIn(timeZoneId, now);

    return { first: answer.from ?? today, last: today };
  }, [answer.from, timeZoneId, now]);
  const { drawn, opens, publishedAfter } = useOpening({
    answer,
    span,
    window,
    allTime,
    timeZoneId,
  });
  const { pictured, granularity, cutting, movedTo, manyDays, bucketsDue } = usePicturePlan(
    { allTime, span, window, drawn, firstDay: answer.from, recordDays },
    choose,
  );

  const { views, problem, missing } = usePostPicture({
    site,
    answer,
    population,
    window: drawn?.window ?? window,
    granularity,
    allTime,
    asking: publishedAfter === null,
  });

  // A picture with too few buckets for a shape is drawn as columns whatever was asked for, and offers
  // nothing else. How many it has is its answer's, and until that is in the number it is due to have,
  // so the ways of drawing it are settled before it arrives rather than offered and taken back.
  const counted = views?.points.length ?? bucketsDue;
  const drawing = drawingFor('activity', remembered, counted);
  const styles = useDrawingOptions('activity', counted);

  const zone = readableZone(timeZoneId);
  // Gathered once rather than assembled on the way down: writing a bucket out memoises against it.
  const buckets = useMemo<Buckets>(
    () => ({ timeZoneId, zone, granularity, manyDays, manyYears: crossesYears(pictured) }),
    [timeZoneId, zone, granularity, manyDays, pictured],
  );

  // A press narrows the period, so it is offered wherever the period holds more than one day, though
  // the picture of a post published inside it may hold fewer.
  const pickDays = usePickDays(allTime ? manyDays : daysIn(span) > 1, moveTo, timeZoneId);

  return (
    <Card className={CARD}>
      <header className={HEADER}>
        <h2 className={TITLE}>{t('title')}</h2>
        {/*
          The period first: it is what every other control here cuts and draws, and on a phone it
          takes a line of its own with the days it covers beneath it, the rest following on the next.
        */}
        <div className={CONTROLS}>
          <PeriodPicker
            ref={control}
            value={period}
            onChange={choose}
            timeZoneId={timeZoneId}
            since={answer.from}
            nothingHeld={answer.from === null}
          />
          {cutting ? <ShowBy granularity={granularity} cutting={cutting} /> : null}
          {styles.length > 1 ? (
            <ListSwitch
              label={chart('drawing.label')}
              options={styles}
              value={drawing}
              onChange={drawAs}
            />
          ) : null}
        </div>
      </header>

      <PeriodMoved movedTo={movedTo} period={period} />

      <ViewsPlace
        views={views}
        problem={problem}
        allTime={allTime}
        publishedAfter={publishedAfter}
        opens={opens}
        peopleOnly={peopleOnly}
        drawing={drawing}
        buckets={buckets}
        onPick={pickDays}
        // All time reaches back past the days held visit by visit; a month older than them is held
        // as its totals alone, and is not pressed into.
        pressableFrom={allTime ? firstKept : undefined}
        missing={missing}
      />
    </Card>
  );
}

/**
 * The card while the post's record is on its way: its heading, and the places of its controls and of
 * the picture held, each standing in for what it will be.
 *
 * The controls' places are laid out as the controls are: the period with the line beneath it, and
 * beside it how the picture is cut and how it is drawn — which on a phone take a line of their own
 * under the period — so the card keeps its height when they arrive.
 */
export function PostChartWaiting() {
  const t = useTranslations('insights.post.chart');

  return (
    <Card className={CARD}>
      <header className={HEADER}>
        <h2 className={TITLE}>{t('title')}</h2>
        <span aria-hidden className={CONTROLS}>
          <span className="flex w-full flex-col gap-1 sm:w-44">
            <span className="h-9 animate-pulse rounded-md bg-surface-muted" />
            <span className="h-4" />
          </span>
          <span className="h-9 w-30 animate-pulse rounded-md bg-surface-muted" />
          <span className="h-9 w-28 animate-pulse rounded-md bg-surface-muted" />
        </span>
      </header>
      <Settling />
    </Card>
  );
}

/** What the opening of a post's picture over the period on screen is worked out from. */
interface OpeningAsked {
  readonly answer: PostInsights;
  /** The days the period on screen covers. */
  readonly span: DaySpan;
  /** The window the period's picture is counted over. */
  readonly window: AnalyticsWindow;
  readonly allTime: boolean;
  /** The website's zone, which the day the post was published begins in. */
  readonly timeZoneId: string;
}

/** Where a post's picture over the period on screen opens. */
interface Opening {
  /** The days and the window it is drawn over, where it opens later than the period does. */
  readonly drawn: DrawnOver | undefined;
  /** The day it opens on, where that is later than the period's own first day. */
  readonly opens: string | null;
  /** The day the post was published, where the period ends before it, and nothing is drawn. */
  readonly publishedAfter: string | null;
}

/** A picture drawn over every day of the period. */
const WITH_THE_PERIOD: Opening = { drawn: undefined, opens: null, publishedAfter: null };

/**
 * Where a post's picture over the period on screen opens: on the day the post was published where
 * that falls inside the period, at the midnight it begins where the website is; nowhere where the
 * period ends before it; and otherwise on the period's own first day. All time is drawn from the
 * post's record, which opens on the post's first day already.
 */
function useOpening({ answer, span, window, allTime, timeZoneId }: OpeningAsked): Opening {
  return useMemo(() => {
    const day = allTime ? null : postOpensOn(answer, span);

    if (day === null) {
      return WITH_THE_PERIOD;
    }

    if (day > span.last) {
      return { ...WITH_THE_PERIOD, publishedAfter: day };
    }

    return {
      drawn: {
        span: { first: day, last: span.last },
        window: { from: windowForSpan({ first: day, last: day }, timeZoneId).from, to: window.to },
      },
      opens: day,
      publishedAfter: null,
    };
  }, [answer, span, window, allTime, timeZoneId]);
}

/** What a post's picture over the period on screen is drawn from. */
interface PictureAsked {
  readonly site: Site;
  readonly answer: PostInsights;
  readonly population: Population;
  /** The window a period's picture is counted over. */
  readonly window: AnalyticsWindow;
  readonly granularity: Granularity;
  readonly allTime: boolean;
  /** Whether a period's picture is asked for at all, which it is not where nothing could be in it. */
  readonly asking: boolean;
}

/** A post's picture over the period on screen, as far as it has arrived. */
interface PostPicture {
  /**
   * Its views: all time's from the post's record, and any other period's once the engine has
   * answered — nothing while that answer is on its way, or under all time where the record has no
   * first day to open on.
   */
  readonly views: ViewsPicture | undefined;
  /** Why they could not be had, or nothing. */
  readonly problem: Error | null;
  /** The months they leave out because they were never counted for the post, written `2025-06`. */
  readonly missing: readonly string[];
}

/**
 * The picture of a post over the period on screen: all time's from the months of its record, which
 * the screen already holds, and any other period's asked of the engine. Only the one being drawn is
 * read; all time is never asked for, nor a period that ends before the post was published.
 */
function usePostPicture({
  site,
  answer,
  population,
  window,
  granularity,
  allTime,
  asking,
}: PictureAsked): PostPicture {
  const series = usePostSeries(
    site.id,
    answer.post.path,
    window,
    population,
    askedBy(granularity),
    !allTime && asking,
  );
  const cut: AllTimeGranularity = granularity === 'year' ? 'year' : 'month';
  const record = useMemo(
    () => (allTime ? recordPicture(answer, cut, site.timeZoneId) : undefined),
    [allTime, answer, cut, site.timeZoneId],
  );
  // Said by the record the picture is drawn from, so a month is never called left out by an answer
  // read at another moment.
  const missing = useMemo(
    () => (allTime ? leftOutOf(answer).missing : NONE_LEFT_OUT),
    [allTime, answer],
  );

  return allTime
    ? { views: record, problem: null, missing }
    : { views: series.data, problem: series.error, missing };
}

/**
 * How a period's picture is asked for, which is how it is cut. Only all time is ever cut by the year,
 * and its picture is drawn from the post's months rather than asked for, so a year is filed as the
 * months it is drawn from and never sent.
 *
 * @param granularity How the picture is cut.
 */
function askedBy(granularity: Granularity): PeriodGranularity {
  return granularity === 'year' ? 'month' : granularity;
}

interface ViewsShownProps {
  /** Whether the views are the people's, which they are then named for. */
  readonly peopleOnly: boolean;
  readonly drawing: Drawing;
  /** How the picture is cut, and the zone it is read in. */
  readonly buckets: Buckets;
  /** The way from a bucket into the days it covers, where one is offered. */
  readonly onPick: ((days: DaySpan) => void) | undefined;
  /** The first day a bucket may begin on and still be pressed, where not every bucket can be. */
  readonly pressableFrom: string | undefined;
  /** The months the views leave out because they were never counted, written `2025-06`. */
  readonly missing: readonly string[];
  /**
   * The day the picture opens on, where that is later than the period's own first day: the day the
   * post was published.
   */
  readonly opens: string | null;
}

interface ViewsPlaceProps extends ViewsShownProps {
  /** The views, or nothing where there are none to draw yet. */
  readonly views: ViewsPicture | undefined;
  /** Why they could not be had, or nothing. */
  readonly problem: Error | null;
  /** Whether the picture is all time's, which is drawn from the record and never on its way. */
  readonly allTime: boolean;
  /** The day the post was published, where the period ends before it. */
  readonly publishedAfter: string | null;
}

/**
 * What stands under the card's controls: the picture, the shape of one while the views are on their
 * way, or what went wrong — said in the card, with its controls kept, so another period is a choice
 * away. A period that ends before the post was published is said rather than asked about.
 */
function ViewsPlace({ views, problem, allTime, publishedAfter, ...shown }: ViewsPlaceProps) {
  if (publishedAfter !== null) {
    return <PublishedAfter day={publishedAfter} />;
  }

  if (problem !== null) {
    return <FailureNotice error={problem} />;
  }

  if (views !== undefined) {
    return <ViewsDrawn views={views} {...shown} />;
  }

  // All time is drawn from the record, which is in: a record with no first day has no month to draw.
  return allTime ? (
    <NothingRead peopleOnly={shown.peopleOnly} judging={null} leftOut={null} />
  ) : (
    <Settling />
  );
}

interface ViewsDrawnProps extends ViewsShownProps {
  readonly views: ViewsPicture;
}

/**
 * The post's views across the period: one measure, keyed and tabled under the post's own name for it,
 * and the same figures published again as a table, since a canvas tells a screen reader nothing.
 *
 * Kept to people, the buckets the engine has not finished judging are washed, as on the overview. A
 * period nobody read the post in is a sentence rather than a run of noughts, and a month never counted
 * for the post a gap rather than a nought. A picture that opens on the day the post was published says
 * so beneath it, since the period named above it begins earlier.
 */
function ViewsDrawn({
  views,
  peopleOnly,
  drawing,
  buckets,
  onPick,
  pressableFrom,
  missing,
  opens,
}: ViewsDrawnProps) {
  const t = useTranslations('insights.post.chart');
  const format = useFormatter();
  const { granularity, zone, timeZoneId } = buckets;
  const { points } = views;

  const starts = useMemo(() => points.map((point) => point.bucketStart), [points]);
  const stillJudging = useMemo(
    () => stillJudgingFrom({ to: views.to, completeTo: views.completeTo, buckets: starts }),
    [views.to, views.completeTo, starts],
  );
  const judging = judgingNote(
    stillJudging,
    judgedNoneOf(views, points.length, stillJudging),
    'people',
  );
  const { days, labels, rows, titles } = useWritten(starts, views, buckets);
  const figure = useFigure(days, NONE_LEFT_OUT);
  const leftOut = useLeftOut(missing, NONE_LEFT_OUT, timeZoneId);
  const quiet = useMemo(() => quietIn(days, stillJudging), [days, stillJudging]);
  const { pick, pressable } = usePick(days, onPick, pressableFrom);

  const measure = useMemo<Measure>(
    () => ({
      name: t(peopleOnly ? 'measurePeople' : 'measure'),
      values: points.map((point) => point.value),
    }),
    [t, peopleOnly, points],
  );

  const option = useCallback(
    (palette: ChartPalette) =>
      activityOption(
        { labels, titles, granularity, measures: [measure], figure, drawing, quiet },
        palette,
      ),
    [labels, titles, granularity, measure, figure, drawing, quiet],
  );

  if (measure.values.every((value) => value === 0)) {
    return <NothingRead peopleOnly={peopleOnly} judging={judging} leftOut={leftOut} />;
  }

  return (
    <>
      <Keys items={[{ fill: 'bg-chart-1', label: measure.name }]} />

      <Picture
        option={option}
        label={t(peopleOnly ? 'summaryPeople' : 'summary')}
        onPick={pick}
        pressable={pressable}
      />

      <PictureNotes
        cut={t(`cut.${granularity}`, { zone })}
        opens={opens === null ? null : t('opens', { date: writtenDay(opens, format) })}
        cutShort={cutShortNote(granularity, quiet, days.length, drawing)}
        judging={judging}
        leftOut={leftOut}
        against={null}
      />

      <FiguresTable
        days={days}
        rows={rows}
        buckets={buckets}
        columns={[{ key: 'views', name: measure.name, values: measure.values }]}
        stillJudging={stillJudging}
        missing={missing}
        unsettled={NONE_LEFT_OUT}
        onPick={pick}
        pressable={pressable}
      />
    </>
  );
}

interface NothingReadProps {
  /** Whether the views are the people's, which the sentence then says. */
  readonly peopleOnly: boolean;
  /** The clause about the buckets still being judged, where there are any. */
  readonly judging: JudgingNote | null;
  /** The sentences naming the months the views leave out, where they leave any out. */
  readonly leftOut: string | null;
}

/**
 * A period nobody read the post in: said rather than drawn, with what that leaves open — buckets
 * still being judged, which may yet hold people, and months never counted, which may have held
 * readers. No way out is offered, since the period it was read over is chosen just above.
 */
function NothingRead({ peopleOnly, judging, leftOut }: NothingReadProps) {
  const t = useTranslations('insights.post.chart');
  const chart = useTranslations('dashboard.chart');

  return (
    <NothingDrawn
      icon={BookOpen}
      body={sentences(
        t(peopleOnly ? 'nonePeople' : 'none'),
        judging === null ? null : chart(judging),
        leftOut,
      )}
    />
  );
}

/**
 * A period that ends before the post was published: said rather than drawn, since a run of noughts
 * would say nobody read a post that was not there to be read. As with a period nobody read it in, the
 * way to another period is the control just above.
 */
function PublishedAfter({ day }: { readonly day: string }) {
  const t = useTranslations('insights.post.chart');
  const format = useFormatter();

  return <NothingDrawn icon={CalendarClock} body={t('after', { date: writtenDay(day, format) })} />;
}

/**
 * A day as the post's own line writes the day it was published: `November 3, 2026`.
 *
 * @param day Written `2026-11-03`.
 * @param format The reader's own formatter.
 */
function writtenDay(day: string, format: ReturnType<typeof useFormatter>): string {
  return format.dateTime(writableDay(day), { dateStyle: 'long', timeZone: 'UTC' });
}
