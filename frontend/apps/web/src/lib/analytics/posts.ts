import type { SitemapStatus } from '@/lib/api/schemas';

/**
 * What the screen about a website's record says about its posts, from how reading its sitemap stands,
 * and where a post can be opened from.
 */

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
