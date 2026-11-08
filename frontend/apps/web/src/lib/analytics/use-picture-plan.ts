'use client';

import { type RefObject, useCallback, useMemo, useRef, useState } from 'react';
import { CHART_BYS, type ChartBy, type ChartCutting } from '@/lib/analytics/chart-by';
import {
  type AnalyticsWindow,
  allTimeCutsFor,
  bucketsIn,
  cutOf,
  cutsFor,
  type DaySpan,
  daysIn,
  type Granularity,
  namedPeriod,
  type Period,
  type PeriodPreset,
  shortestPeriodFor,
} from '@/lib/analytics/period';
import { useChartBy } from '@/lib/analytics/use-chart-by';

/** The control that names the period, and the way to a period chosen from anywhere else. */
export interface HandOver {
  readonly control: RefObject<HTMLSelectElement | null>;
  readonly moveTo: (period: Period) => void;
}

/**
 * Chooses a period from a bucket of the picture or a row of its table, and hands the reader to the
 * control that names the period.
 *
 * It is the one thing on the screen that says what changed: the row that was pressed has gone with
 * the old period. It is the way to change it again, too. The page follows only where there was a
 * position to move — a row the reader had reached — and stays where it is after a press on the
 * picture, which nothing can hold, so a tap on a phone does not jump the page.
 *
 * @param choose Makes a period the one on screen.
 */
export function useHandOver(choose: (period: Period) => void): HandOver {
  const control = useRef<HTMLSelectElement>(null);

  const moveTo = useCallback(
    (next: Period) => {
      const held = document.activeElement !== null && document.activeElement !== document.body;

      choose(next);
      control.current?.focus({ preventScroll: !held });
    },
    [choose],
  );

  return { control, moveTo };
}

/**
 * The way from the days of a pressed bucket to the period, or nothing where there is nothing
 * narrower to go to.
 *
 * The days of a bucket pressed on the picture become the period, which everything read over the
 * period then re-reads: a day for a day or an hour, and for a week, a month or a year the days of
 * it the period reaches. On a period that is already one day there is nothing narrower, so the
 * picture is not offered as something to press.
 *
 * Days that are today, yesterday, this month, last month or this year where the website is become
 * the named period the list already offers for them, so the control calls them what the reader
 * calls them rather than a stretch of their own choosing, and they are remembered by that name.
 * The moment is taken at the press, which is when the reader said which days they meant. The
 * chooser behind the control names days typed into it the same way, on every screen that offers
 * it, so every way of asking for the same days arrives under one name.
 *
 * @param manyDays Whether the picture covers more than one day.
 * @param moveTo Makes a period the one on screen, and hands the reader to the control naming it.
 * @param timeZoneId The website's zone, which the days are named in.
 */
export function usePickDays(
  manyDays: boolean,
  moveTo: (period: Period) => void,
  timeZoneId: string,
): ((days: DaySpan) => void) | undefined {
  return useMemo(
    () =>
      manyDays
        ? (days: DaySpan) =>
            moveTo(
              namedPeriod(
                { kind: 'chosen', first: days.first, last: days.last },
                timeZoneId,
                new Date(),
              ),
            )
        : undefined,
    [manyDays, moveTo, timeZoneId],
  );
}

/** How finely the picture is cut, and the ways it may be. */
interface CutPicture {
  readonly granularity: Granularity;
  /**
   * The ways offered, or nothing where there is no choice to make: a period too short to offer any,
   * or all time while every day of it falls in one calendar year.
   */
  readonly cutting: ChartCutting | undefined;
  /** The period a way of cutting the picture last moved the screen to, or nothing. */
  readonly movedTo: PeriodPreset | null;
}

/** The ways the picture on screen can be cut, and the ways its list names. */
interface Ways {
  /** What the picture can be cut by, its own way first. */
  readonly offered: readonly ChartBy[];
  /** What the list names: the ways offered, and those it would move to another period for. */
  readonly listed: readonly ChartBy[];
}

/**
 * The ways a picture can be cut, and the ways its list names.
 *
 * Under all time, the months and — once the record runs across a first of January — the years, and
 * nothing else to list: all time is every period there is, and none is longer to move to. Until the
 * record is known it is the months alone, which every record can be cut by; the picture is not
 * drawn before then, so nothing is asked of a way that would not last. Under any other period, what
 * its length offers, listed beside the days, weeks and months a longer period would show, and beside
 * the years only where the record has some to show.
 *
 * @param allTime Whether the screen is on all time.
 * @param span The days the period on screen covers.
 * @param record The days of the record, from its first day to today, or nothing while that is not
 * known.
 */
function waysToCut(allTime: boolean, span: DaySpan, record: DaySpan | null): Ways {
  if (allTime) {
    const offered = allTimeCutsFor(record);

    return { offered, listed: offered };
  }

  const years = allTimeCutsFor(record).includes('year');

  return {
    offered: cutsFor(span),
    listed: CHART_BYS.filter((cut) => cut !== 'year' || years),
  };
}

/**
 * How finely the picture of a period is cut: the way somebody chose where the period offers it, and
 * the period's own way where it does not, with the choice kept for a period that does.
 *
 * A way the period offers is drawn at once, and the period's own way leaves the address plain, as
 * every default does. A way the period is too short for moves the screen to the shortest named
 * period that offers it, with the choice kept — somebody looking at a week who asks for weeks is
 * shown the last thirty days a week at a time, and for years all time — and the period it moved to
 * is handed back, to be said rather than shown.
 *
 * The reader stays on the list. A list read with the arrow keys chooses on every key on some
 * systems, so a list that sent the reader to another control on a choice would send them there on
 * the first key, before they had heard what the rest of it offers.
 *
 * No list is offered where it would name a single way, which is a control that can change nothing.
 *
 * @param ways The ways the picture can be cut, and the ways its list names.
 * @param choose Makes a period the one on screen.
 */
function useCut({ offered, listed }: Ways, choose: (period: Period) => void): CutPicture {
  const { by, showBy } = useChartBy();
  const [movedTo, setMovedTo] = useState<PeriodPreset | null>(null);

  const onChoose = useCallback(
    (cut: ChartBy) => {
      if (offered.includes(cut)) {
        showBy(cut === offered[0] ? null : cut);

        return;
      }

      const shortest = shortestPeriodFor(cut);

      showBy(cut);
      choose({ kind: 'preset', preset: shortest });
      setMovedTo(shortest);
    },
    [offered, showBy, choose],
  );

  const cutting = useMemo(
    () => (offered.length === 0 || listed.length < 2 ? undefined : { offered, listed, onChoose }),
    [offered, listed, onChoose],
  );

  return { granularity: cutOf(offered, by), cutting, movedTo };
}

/** The days a picture is drawn over, and the window it is counted over, where not the period's. */
export interface DrawnOver {
  readonly span: DaySpan;
  readonly window: AnalyticsWindow;
}

/** What the picture on screen is planned from. */
export interface PictureFrame {
  readonly allTime: boolean;
  /** The days the period on screen covers. */
  readonly span: DaySpan;
  /** The window the period's picture is counted over. */
  readonly window: AnalyticsWindow;
  /**
   * The days and the window the picture of the period is drawn over, where they begin later than the
   * period does — a post's opens on the day it was published — and the period's own where left out.
   * The ways the picture can be cut are the period's either way.
   */
  readonly drawn?: DrawnOver;
  /** The record's first day, or nothing while that is not known or where there is none. */
  readonly firstDay: string | null;
  /** The days of the record, from its first day to today. */
  readonly recordDays: DaySpan;
}

/** How the picture on screen is cut, the days it covers, and how many buckets it is due. */
export interface PicturePlan extends CutPicture {
  /**
   * The days the picture covers: the period's, or as many of them as it is drawn over, or under all
   * time every day of the record.
   */
  readonly pictured: DaySpan;
  /** Whether those days are more than one. */
  readonly manyDays: boolean;
  /**
   * How many buckets the picture is due to have, worked out before its answer is in — or nothing
   * under all time until the record says where its first month begins.
   */
  readonly bucketsDue: number | undefined;
}

/**
 * How the picture on screen is cut, the days it covers, and how many buckets it is due.
 *
 * The number of buckets is worked out before the picture's answer is in, so whether it can be drawn
 * as anything but columns is settled from the start, rather than offered while the answer is on its
 * way and taken back when it arrives. Under all time it waits for the record, which says where the
 * first month begins.
 *
 * @param frame What the picture is planned from.
 * @param choose Makes a period the one on screen.
 */
export function usePicturePlan(
  { allTime, span, window, drawn, firstDay, recordDays }: PictureFrame,
  choose: (period: Period) => void,
): PicturePlan {
  const pictured = allTime ? recordDays : (drawn?.span ?? span);
  const counted = drawn?.window ?? window;
  const ways = useMemo(
    () => waysToCut(allTime, span, firstDay === null ? null : recordDays),
    [allTime, span, firstDay, recordDays],
  );
  const cut = useCut(ways, choose);
  const bucketsDue = useMemo(
    () =>
      allTime && firstDay === null ? undefined : bucketsIn(pictured, counted, cut.granularity),
    [allTime, firstDay, pictured, counted, cut.granularity],
  );

  return { ...cut, pictured, manyDays: daysIn(pictured) > 1, bucketsDue };
}
