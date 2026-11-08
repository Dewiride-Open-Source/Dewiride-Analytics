import { figureAcross, type MonthsRecord, yearFigure, yearsOf } from '@/lib/analytics/all-time';
import { readablePath } from '@/lib/analytics/pages';
import { type Population, withPopulation } from '@/lib/analytics/people-only';
import {
  type AllTimeGranularity,
  type AnalyticsWindow,
  type DaySpan,
  windowForSpan,
} from '@/lib/analytics/period';
import { viewsCountedSince } from '@/lib/analytics/posts';
import { ApiError } from '@/lib/api/problem';
import type { InsightsPost, PostInsights } from '@/lib/api/schemas';
import { INSIGHTS_POST } from '@/lib/routes';

/**
 * The screen about one post: the address that names it, what stands on it while its record is asked
 * for, and what that record says across the post's whole life.
 */

/** What the post a screen is about is called in its address. */
export const POST_KEY = 'path';

/**
 * What the list of every post a screen about one post was opened from is called in its address,
 * where it was opened from there.
 */
export const LIST_KEY = 'list';

/**
 * The address of the screen about one post, carrying whose views it counts and, where it is opened
 * from the list of every post, which page of that list it leads back to.
 *
 * The post is named by the path its views are counted under, written by the browser's own rules for
 * an address, so whatever it holds reads back exactly as it was written. So is the list, as the
 * list's own address writes it, the first page of the list as it opens being written as nothing.
 *
 * @param path The post's path, as the engine named it.
 * @param population Whose views the screen counts.
 * @param list The list it is opened from, where it is opened from one.
 */
export function postScreen(path: string, population: Population, list?: string): string {
  const asked = new URLSearchParams({ [POST_KEY]: path });

  if (list !== undefined) {
    asked.set(LIST_KEY, list);
  }

  return `${INSIGHTS_POST}?${withPopulation(asked, population)}`;
}

/**
 * The post an address names, or nothing where it names none.
 *
 * The address is written by whoever sent the link and is never shown: it is only ever put to the
 * engine, which finds the post among those the website lists or says there is none. Every path a post
 * is listed under begins at the root of its website, so anything else names nothing to ask about.
 *
 * @param written What the address says, or nothing where it says nothing.
 */
export function readPostPath(written: string | null): string | null {
  return written?.startsWith('/') ? written : null;
}

/**
 * What a post is called: its title, or the address its views are counted under where its page gave
 * none. Either came from the website's own pages, so it is only ever written as text.
 *
 * @param post The post, as the engine named it.
 */
export function postCalled(post: Pick<InsightsPost, 'title' | 'path'>): string {
  return post.title ?? readablePath(post.path);
}

/**
 * The months of a post's record left out for having been kept before its visits were all judged:
 * none. A month of posts kept before judging had finished with it is shown as it was kept rather than
 * left out, so `unjudged` is always empty for a post; `completeTo` covers the months that can still be
 * counted again.
 */
const NONE_UNJUDGED: readonly string[] = Object.freeze([]);

/**
 * The months a post's figures leave out: every month never counted for it. None is ever left out for
 * having been kept before its visits were all judged.
 *
 * @param answer The post's record.
 */
export function leftOutOf({ months }: Pick<PostInsights, 'months'>): {
  readonly missing: readonly string[];
  readonly unjudged: readonly string[];
} {
  return {
    missing: months.filter((month) => month.pageViews === null).map((month) => month.month),
    unjudged: NONE_UNJUDGED,
  };
}

/**
 * A post's months as a record a table of months lays out.
 *
 * @param answer The post's record.
 */
export function monthsRecordOf({ months }: Pick<PostInsights, 'months'>): MonthsRecord {
  return { months, unjudged: NONE_UNJUDGED };
}

/** What any question about a post gave back, as far as it has got. */
interface PostAnswer {
  readonly data: PostInsights | undefined;
  readonly error: Error | null;
}

/**
 * What stands on the screen about one post: its record on its way, a post the website does not list,
 * a refusal, a post nobody has read, one none of whose readers was a person, or the record itself.
 */
export type PostShown =
  | { readonly state: 'waiting' }
  | { readonly state: 'missing' }
  | { readonly state: 'refused'; readonly error: Error | null }
  | { readonly state: 'unread' | 'absent' | 'ready'; readonly answer: PostInsights };

/**
 * Whether a post's record holds no views, and leaves no month out of saying so.
 *
 * @param answer The post's record, or nothing while it is on its way.
 */
export function readsNothing(answer: PostInsights | undefined): boolean {
  return answer?.post.pageViews === 0 && leftOutOf(answer).missing.length === 0;
}

/** Whether an answer says the post is not there to be read. */
function saysMissing({ error }: PostAnswer): boolean {
  return error instanceof ApiError && error.missing;
}

/** An answer still on its way, or one that could not be had. */
function awaited({ error }: PostAnswer): PostShown {
  return error === null ? { state: 'waiting' } : { state: 'refused', error };
}

/**
 * What stands on the screen, from everybody's record of the post and the record of whoever the screen
 * is kept to.
 *
 * An address naming no post, and a post the website does not list, are the same answer whoever the
 * screen is kept to and are said before anything else. Otherwise it is decided as the screen about a
 * website's record decides it: views to show, or months the answer says were left out, are the
 * record; nought with nothing left out is only an answer once everybody's record says whether anybody
 * read the post, and is then a post nobody has read or one none of whose readers was judged a person.
 * An answer with nothing to show that could not be had is said to be refused, and an answer already
 * read stays on the screen when asking again fails for any other reason.
 *
 * @param path The post the address names, or nothing where it names none.
 * @param everybody Everybody's record of the post, as it is being asked for.
 * @param own The record of whoever the screen is kept to, as it is being asked for.
 */
export function postShown(path: string | null, everybody: PostAnswer, own: PostAnswer): PostShown {
  if (path === null || saysMissing(own) || saysMissing(everybody)) {
    return { state: 'missing' };
  }

  if (own.data === undefined) {
    return awaited(own);
  }

  if (!readsNothing(own.data)) {
    return { state: 'ready', answer: own.data };
  }

  if (everybody.data === undefined) {
    return awaited(everybody);
  }

  return readsNothing(everybody.data)
    ? { state: 'unread', answer: everybody.data }
    : { state: 'absent', answer: own.data };
}

/** The month a post was read most in, and how much. */
export interface BestMonth {
  /** Written `2026-08`. */
  readonly month: string;
  readonly pageViews: number;
  /** Whether it is the month running now, whose figure is still growing. */
  readonly running: boolean;
}

/** What a post's whole record comes to. */
export interface PostHighlights {
  /** Its views, as Insights counts them. */
  readonly views: number;
  /** Its views for each day measured across the months with figures, or nothing where none has days. */
  readonly perDay: number | null;
  /** The month it was read most in, or nothing where it was read in none. */
  readonly bestMonth: BestMonth | null;
}

/**
 * The month a post was read most in: the earliest of several sharing the most, and none where it was
 * read in no month at all.
 */
function bestMonthOf(months: PostInsights['months']): BestMonth | null {
  const running = months.at(-1)?.month;
  let best: BestMonth | null = null;

  for (const { month, pageViews } of months) {
    if (pageViews !== null && pageViews > (best?.pageViews ?? 0)) {
      best = { month, pageViews, running: month === running };
    }
  }

  return best;
}

/**
 * What a post's whole record comes to.
 *
 * Its views are the figure Insights gave it, so the number somebody pressed is the number they land
 * on. Its views for each day are read across its months exactly as the table of them reads a year,
 * so the two never disagree; they are never added up into its views, which the months need not
 * match.
 *
 * @param answer The post's record.
 */
export function postHighlights(answer: PostInsights): PostHighlights {
  const months = yearsOf(monthsRecordOf(answer)).flatMap((row) => row.months);

  return {
    views: answer.post.pageViews,
    perDay: figureAcross(months, 'average'),
    bestMonth: bestMonthOf(answer.months),
  };
}

/**
 * What a month before a post's first is: before it was published, before its views were measured,
 * or, for a post that does not say when it was published, a month nobody read it in.
 */
export type BeforePost = 'unpublished' | 'unmeasured' | 'unread';

/**
 * What one of the months before a post's first is called.
 *
 * A post's months begin with the month it was published, or the month it was first read where that
 * came earlier, and never before its views are counted from. Those before a dated post's first month
 * are therefore before it was published, unless it was published before its views were counted.
 * A post that does not say when it was published begins with the first month that may hold its
 * views, so a month before it, from the month its views are counted from on, held none of them, and
 * one before that was not measured at all.
 *
 * @param answer The post's record.
 * @param month One of the months before its first, written `2026-08`.
 */
export function beforeOfPost(
  { post, viewsFrom, months }: Pick<PostInsights, 'post' | 'viewsFrom' | 'months'>,
  month: string,
): BeforePost {
  const first = months[0]?.month;

  if (first === undefined) {
    return 'unmeasured';
  }

  if (post.published === null) {
    return viewsFrom !== null && month >= viewsFrom ? 'unread' : 'unmeasured';
  }

  return post.published.slice(0, 7) >= first ? 'unpublished' : 'unmeasured';
}

/**
 * The month a post's views are said to be counted since, where some of its life went uncounted.
 *
 * A post published before its views were counted from had views nobody counted. One that does not
 * say when it was published is said to be counted since then wherever the website's own record
 * reaches further back than its views, as Insights says of every post.
 *
 * @param answer The post's record.
 * @returns The month, written `2026-08`, or nothing where every view since the post began is counted.
 */
export function postViewsSince({
  post,
  viewsFrom,
  firstDay,
}: Pick<PostInsights, 'post' | 'viewsFrom' | 'firstDay'>): string | null {
  if (post.published === null) {
    return viewsCountedSince(viewsFrom, firstDay?.slice(0, 7));
  }

  return viewsFrom !== null && post.published.slice(0, 7) < viewsFrom ? viewsFrom : null;
}

/**
 * The day a post began, where no day before it can have held one of its views: the day it was
 * published — or nothing, where any day before its record begins may have.
 *
 * Only where the post's record begins on the day it was published. A post that says no date, or one
 * whose views were counted only from later, may have been read on any day before its record begins.
 * Nor where any of its recent days before that day holds a view — a post dated later than it was
 * first read — so the date a post gives never sets aside a view its recent weeks show.
 *
 * @param answer The post's record.
 * @returns The day, written `2026-11-03`, or nothing.
 */
export function postBeginsOn({
  post,
  from,
  days,
}: Pick<PostInsights, 'post' | 'from' | 'days'>): string | null {
  if (from === null || post.published !== from) {
    return null;
  }

  return days.some((day) => day.day < from && (day.pageViews ?? 0) > 0) ? null : from;
}

/**
 * The day a picture of a post over a period opens on, where the post was published after the period
 * began: the day it was published — or nothing, where the picture is drawn over every day of the
 * period.
 *
 * A day before a post was published is not a day nobody read it, and drawn as nought it would say
 * so. A period that ends before that day has nothing in it to draw at all.
 *
 * Opened only on the day the post began, where no day before it can have held one of its views. Any
 * other post may have been read on any day before its record begins, and each of those days is drawn
 * as it was counted.
 *
 * @param answer The post's record.
 * @param span The days of the period on screen.
 * @returns The day, written `2026-11-03`, or nothing.
 */
export function postOpensOn(
  answer: Pick<PostInsights, 'post' | 'from' | 'days'>,
  span: DaySpan,
): string | null {
  const began = postBeginsOn(answer);

  return began !== null && began > span.first ? began : null;
}

/** One bucket of a picture of a post's views: the moment it begins, and its views. */
export interface ViewsPoint {
  readonly bucketStart: string;
  /** Its views, or nothing for a month never counted for the post, or a year of nothing else. */
  readonly value: number | null;
}

/**
 * A post's views across a picture, in the shape the engine answers a period's in: the window they
 * were counted over, the instant they are settled up to, and each bucket's views, oldest first.
 */
export interface ViewsPicture extends AnalyticsWindow {
  readonly completeTo: string;
  readonly points: readonly ViewsPoint[];
}

/**
 * All time's picture of a post, drawn from the months of its record rather than asked for: one bucket
 * a month, or a year, each named by the moment it begins where the website is, as the engine names a
 * bucket.
 *
 * A month never counted for the post stays nothing, and is drawn as a gap rather than as a month
 * nobody read it in. A year is read across its months exactly as the table of them reads it — the
 * months with figures added up, and nothing only where not one of them has a figure — so the two
 * never disagree. Neither is ever added up into the post's views, which the months need not match.
 *
 * The picture opens at the midnight the post's first day begins, so its first month is written as the
 * days of it the post's record counts rather than as a whole month the post was not there for.
 *
 * @param answer The post's record.
 * @param granularity Whether the picture is cut by the month or by the year.
 * @param timeZoneId The website's zone, which the months are cut in.
 * @returns The picture, or nothing where the record has no first day to open it on.
 */
export function recordPicture(
  { from, to, completeTo, months }: Pick<PostInsights, 'from' | 'to' | 'completeTo' | 'months'>,
  granularity: AllTimeGranularity,
  timeZoneId: string,
): ViewsPicture | undefined {
  if (from === null) {
    return undefined;
  }

  const points =
    granularity === 'year'
      ? [...yearsOf(monthsRecordOf({ months }))].reverse().map((row) => ({
          bucketStart: dayBegins(`${row.year}-01-01`, timeZoneId),
          value: yearFigure(row, 'total'),
        }))
      : months.map(({ month, pageViews }) => ({
          bucketStart: dayBegins(`${month}-01`, timeZoneId),
          value: pageViews,
        }));

  return { from: dayBegins(from, timeZoneId), to, completeTo, points };
}

/**
 * The moment a day begins where the website is.
 *
 * @param day Written `2026-08-19`.
 * @param timeZoneId The website's zone.
 */
function dayBegins(day: string, timeZoneId: string): string {
  return windowForSpan({ first: day, last: day }, timeZoneId).from;
}
