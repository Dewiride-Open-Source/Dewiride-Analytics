import { type PostsOrder, postsOrderSchema, type SitemapStatus } from '@/lib/api/schemas';

/**
 * What the screen about a website's record says about its posts, from how reading its sitemap stands,
 * where a post can be opened from, and how the list of every post is searched, ordered and paged.
 */

/** The orders the list of every post can be read in, as the engine names them. */
export const POSTS_ORDERS = postsOrderSchema.options;

/** The order a list says nothing about is read in: the most read first. */
export const DEFAULT_POSTS_ORDER: PostsOrder = 'most-read';

/**
 * How many posts a page of the list can hold, smallest first. A hundred is the most the engine names
 * in one answer.
 */
export const POSTS_PAGE_SIZES = [25, 50, 100] as const;

/** One of them. */
export type PostsPageSize = (typeof POSTS_PAGE_SIZES)[number];

/** How many posts a page holds where nobody has chosen. */
export const POSTS_PAGE_SIZE: PostsPageSize = 25;

/**
 * The longest search the engine reads, in the units a browser counts a box's length in. A search is
 * stopped here rather than refused there.
 */
export const POSTS_SEARCH_MOST = 100;

/**
 * How long typing has to stop for, in milliseconds, before what was typed is searched for: long
 * enough to pass between the letters of a word, short enough that the list follows as soon as the
 * word is down.
 */
export const SEARCH_SETTLES_AFTER = 300;

/**
 * The furthest page an address is read as naming. Far past the end of the longest list any sitemap is
 * read to, and near enough that where it begins is a whole number the engine reads.
 */
const FURTHEST_PAGE = 1_000_000;

/** One page of the list of every post: what it holds, in which order, and where it begins. */
export interface PostsListing {
  /** What the posts on it hold in their title or their address, or nothing where all of them are. */
  readonly search: string;
  readonly order: PostsOrder;
  /** How many posts the page holds. */
  readonly limit: number;
  /** How many posts are passed over to reach it. */
  readonly offset: number;
}

/** A run of what a search reads as the space between two words: any kind of space, or any control. */
const BETWEEN_WORDS = /[\p{Z}\p{Cc}]+/u;

/**
 * A search, read as the engine reads one: as words, each run of space or control characters between
 * them one space, with none before the first or after the last; and no longer than the engine reads,
 * cut where a character ends rather than between the two halves of one written in a pair.
 *
 * Read this way wherever a search comes from, so the box, the address and the question put to the
 * engine say one search the same way, and none of them can say one the engine would refuse.
 *
 * @param typed What was typed in the box, or what an address says was.
 * @returns The search, or nothing where it holds no word.
 */
export function searchFrom(typed: string): string {
  const words = typed
    .split(BETWEEN_WORDS)
    .filter((word) => word !== '')
    .join(' ');

  if (words.length <= POSTS_SEARCH_MOST) {
    return words;
  }

  const cutHalfway = isFirstHalf(words.charCodeAt(POSTS_SEARCH_MOST - 1));
  const cut = words.slice(0, cutHalfway ? POSTS_SEARCH_MOST - 1 : POSTS_SEARCH_MOST);

  return cut.endsWith(' ') ? cut.slice(0, -1) : cut;
}

/** Whether a unit of text is the first half of a character written in a pair of them. */
function isFirstHalf(unit: number): boolean {
  return unit >= 0xd800 && unit <= 0xdbff;
}

/**
 * The page an address names, counting from one, or nothing where it names none.
 *
 * Only a whole number is a page, written as one is: an address is written by whoever sent the link,
 * and anything else in it opens the list at its beginning.
 *
 * @param written What the address says.
 */
export function pageFrom(written: string): number | null {
  if (!/^[1-9]\d*$/.test(written)) {
    return null;
  }

  const page = Number(written);

  return page <= FURTHEST_PAGE ? page : null;
}

/**
 * What stands where a website's posts are shown.
 *
 * Its posts, wherever any are listed, whatever has happened to its sitemap since they were found.
 * Otherwise why there are none: nobody has named a sitemap, this server does not read them, it has
 * not been read yet, it could not be read, only part of it could, its pages are still being read, or
 * it was read and listed no posts.
 */
export type PostsLook =
  | 'listed'
  | 'none'
  | 'off'
  | 'waiting'
  | 'pages'
  | 'failed'
  | 'partly'
  | 'empty';

/**
 * Which of them a reading of the sitemap is.
 *
 * Decided in the order the website's settings decide the same reading, so the two never disagree: a
 * sitemap that could not be read, or could be read only in part, is said to be so whatever pages are
 * still to read, and pages still to read are what stands only where the sitemap itself was read.
 *
 * @param status How reading the sitemap stands.
 */
export function postsLook({ state, posts, reading }: SitemapStatus): PostsLook {
  if (posts > 0) {
    return 'listed';
  }

  if (state === 'none' || state === 'off' || state === 'waiting') {
    return state;
  }

  if (state === 'failed') {
    return 'failed';
  }

  if (state === 'partly-read') {
    return 'partly';
  }

  return reading > 0 ? 'pages' : 'empty';
}

/** What the card counting a website's posts says under its number. */
export type PostsNote =
  | 'listed'
  | 'none'
  | 'off'
  | 'waiting'
  | 'pages'
  | 'failed'
  | 'partly'
  | 'empty';

/**
 * Which of them a reading of the sitemap calls for.
 *
 * Posts that are listed are what the sitemap lists, unless reading it is switched off, a new one has
 * not been read yet or the last reading of it failed, when the number is the last that is known and
 * the card says why. With none listed, the card says why there is no number.
 *
 * @param status How reading the sitemap stands.
 */
export function postsNote(status: SitemapStatus): PostsNote {
  const look = postsLook(status);

  if (look !== 'listed') {
    return look;
  }

  const { state } = status;

  return state === 'off' || state === 'waiting' || state === 'failed' ? state : 'listed';
}

/**
 * The month a post's views are said to be counted since, where that is later than the record begins:
 * from the first month, the views are every view the record holds, and saying so would only be noise.
 *
 * @param viewsFrom The first month the views are counted from, written `2026-08`.
 * @param firstMonth The first month of the website's record, or nothing while it is not known.
 */
export function viewsCountedSince(
  viewsFrom: string | null,
  firstMonth: string | undefined,
): string | null {
  return viewsFrom !== null && firstMonth !== undefined && viewsFrom > firstMonth
    ? viewsFrom
    : null;
}

/**
 * Where a post can be opened from, or nothing where its address is not a web page's.
 *
 * The address comes from the website's own sitemap and is never trusted to be one: only an address
 * that reads as a web page's, over either of the two ways one is fetched, is ever made a link, so an
 * address that would run something on this screen or open anything other than a web page stays text.
 *
 * @param address The post's address, as its sitemap gave it.
 * @returns The address to link to, as the browser reads it, or nothing.
 */
export function postLink(address: string): string | null {
  try {
    const parsed = new URL(address);

    return parsed.protocol === 'https:' || parsed.protocol === 'http:' ? parsed.href : null;
  } catch {
    return null;
  }
}
