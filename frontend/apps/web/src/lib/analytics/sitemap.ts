import type { SitemapStatus } from '@/lib/api/schemas';

/** The longest address the engine keeps a sitemap under, so one is stopped here, not refused there. */
export const LONGEST_SITEMAP_ADDRESS = 2048;

/** The engine's word for an address that cannot be one of a website's sitemaps. */
export const SITEMAP_REFUSED = 'SiteSitemapRejected';

/**
 * How often somebody watching a first reading is told how it is going.
 *
 * Five seconds, because finding out whether a sitemap can be read at all takes moments, and somebody
 * who has just pressed a button is waiting to hear.
 */
const WHILE_THE_SITEMAP_IS_READ = 5_000;

/**
 * How often somebody watching a website's pages being read is told how far that has gone.
 *
 * Half a minute, because they are read one a second at most and nobody is waiting on any one of them.
 */
const WHILE_ITS_PAGES_ARE_READ = 30_000;

/**
 * How long to leave it before asking how a reading is going, or nothing where the answer will stand.
 *
 * A website's pages can be left unread on an installation that has stopped reading, and asking then
 * would go on for as long as anybody left the panel open with nothing ever coming of it.
 *
 * @param status The last answer, if there has been one.
 * @returns How many milliseconds to wait, or false not to ask again.
 */
export function askAgainIn(status: SitemapStatus | undefined): number | false {
  if (status === undefined || status.state === 'off') {
    return false;
  }

  if (status.state === 'waiting') {
    return WHILE_THE_SITEMAP_IS_READ;
  }

  return status.reading > 0 ? WHILE_ITS_PAGES_ARE_READ : false;
}

/**
 * Whether what somebody typed could be the address of a sitemap at all.
 *
 * Only the mistakes nothing could make right: not a web address, or one naming no website. Whether
 * it is on this website is the engine's to say, because a website can be published at addresses this
 * screen has never been told about, and the answer here would be a guess.
 *
 * @param typed What was typed, without the space around it.
 * @returns Whether it is worth asking the engine.
 */
export function couldBeASitemap(typed: string): boolean {
  try {
    const { protocol, hostname } = new URL(typed);

    return (protocol === 'https:' || protocol === 'http:') && hostname.includes('.');
  } catch {
    return false;
  }
}
