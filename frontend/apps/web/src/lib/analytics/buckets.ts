import {
  type AnalyticsWindow,
  type DaySpan,
  dayOf,
  daysIn,
  type Granularity,
  monthOf,
} from '@/lib/analytics/period';

/**
 * The days each bucket of a picture covers.
 *
 * The engine names a bucket by the moment its hour, day, week, month or year begins, and that is all
 * it says about it. A week or a month at either end of a period is usually only partly inside it —
 * the last thirty days begin on whatever weekday it was thirty days ago — and only the days inside
 * it were counted. Everything that writes a bucket out, or narrows the screen to one, reads those
 * days from here, so a bucket is never named by days that were not counted in it.
 */

/** One bucket of a picture: the moment it is named by, and the days of the period it covers. */
export interface BucketDays extends DaySpan {
  /** The moment the engine names the bucket by, where its hour, day, week, month or year begins. */
  readonly start: string;
  /**
   * Whether it covers the whole of its week, month or year, rather than only the part of it the
   * period reaches. An hour and a day are always whole.
   */
  readonly whole: boolean;
}

const DAYS_IN_A_WEEK = 7;

/**
 * Whether the screen can be narrowed to the days of a bucket: whether every one of them is still
 * held visit by visit, from the first day still kept on.
 *
 * Every bucket of an ordinary period is, since the period never reaches back past that day. A month
 * of all time older than it is held as that month's totals and nothing finer, so the screen has
 * nothing to show for its days.
 *
 * @param bucket The days a bucket covers.
 * @param firstKept The first day a website still holds in full, written the way a period names one.
 */
export function heldInDetail(bucket: DaySpan, firstKept: string): boolean {
  return bucket.first >= firstKept;
}

/** What each way of cutting calls one bucket. */
interface Cut {
  /** Whether a bucket can run over more than one day, which an hour and a day never do. */
  readonly spansDays: boolean;
  /** Whether a bucket's days are the whole of one. */
  readonly whole: (days: DaySpan) => boolean;
}

const CUTS: Readonly<Record<Granularity, Cut>> = {
  hour: { spansDays: false, whole: () => true },
  day: { spansDays: false, whole: () => true },
  week: { spansDays: true, whole: (days) => daysIn(days) === DAYS_IN_A_WEEK },
  month: {
    spansDays: true,
    whole: (days) => {
      const month = monthOf(days.first);

      return days.first === month.first && days.last === month.last;
    },
  },
  year: {
    spansDays: true,
    whole: (days) => {
      const year = days.first.slice(0, 4);

      return days.first === `${year}-01-01` && days.last === `${year}-12-31`;
    },
  },
};

/**
 * Every bucket of a picture, with the days of the period it covers.
 *
 * A bucket begins on the day of its own start or of the period's, whichever is later. An hour or a
 * day ends on that same day; a week, a month or a year ends where the next bucket begins or the
 * period does. Every day is read where the website is, since that is where the bucket was cut, and the
 * first is clipped here rather than trusted to arrive clipped, so it holds however the engine names
 * a first bucket that began before the period did.
 *
 * @param starts The moment each bucket is named by, oldest first.
 * @param window The window the picture was asked for.
 * @param granularity How finely the picture is cut.
 * @param timeZone The site's reporting zone.
 */
export function bucketDays(
  starts: readonly string[],
  window: AnalyticsWindow,
  granularity: Granularity,
  timeZone: string,
): readonly BucketDays[] {
  const from = Date.parse(window.from);
  const cut = CUTS[granularity];

  return starts.map((start, at) => {
    const first = dayOf(timeZone, new Date(Math.max(Date.parse(start), from)));
    const days = {
      first,
      // A longer bucket holds everything up to a millisecond before whatever follows it.
      last: cut.spansDays
        ? dayOf(timeZone, new Date(Date.parse(starts[at + 1] ?? window.to) - 1))
        : first,
    };

    return { start, ...days, whole: cut.whole(days) };
  });
}
