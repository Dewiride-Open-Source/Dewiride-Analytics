/**
 * The lines somebody pastes into their website.
 *
 * Built here rather than written out on the screen so that the address in them is the address the
 * reader is already looking at. A self-hoster reaches the dashboard on whichever name they gave it
 * — a machine on their network, a domain, a tunnel — and any address written into configuration
 * would be a second place for that to be wrong.
 */

/** Where the beacon is served from, relative to the address the dashboard answers on. */
const SCRIPT_PATH = '/dw.js';

/** Where the image fallback is served from. */
const PIXEL_PATH = '/collect/pixel.gif';

/**
 * The script tag: one line, on every page.
 *
 * `defer` rather than `async` so it never competes with the page it is measuring for parsing
 * time. Nothing it does is urgent — a report a few milliseconds later is the same report.
 *
 * @param origin The address the dashboard is being read on, without a trailing slash.
 * @param siteId The website's identifier.
 */
export function scriptTag(origin: string, siteId: string): string {
  return `<script defer src="${origin}${SCRIPT_PATH}" data-site="${siteId}"></script>`;
}

/**
 * The image tag: the same page view, recorded for a reader whose browser runs no scripts.
 *
 * The referrer policy is set on the tag itself, which overrides whatever the page as a whole
 * declares. Without it a browser sends only the site's name and not the page, and every reader
 * with scripting turned off would be recorded as having read the front page.
 *
 * @param origin The address the dashboard is being read on, without a trailing slash.
 * @param siteId The website's identifier.
 */
export function pixelTag(origin: string, siteId: string): string {
  return (
    `<noscript><img src="${origin}${PIXEL_PATH}?site=${siteId}" ` +
    'referrerpolicy="no-referrer-when-downgrade" alt="" width="1" height="1" ' +
    'style="position:absolute"></noscript>'
  );
}

/**
 * Both tags, in the order they are pasted.
 *
 * @param origin The address the dashboard is being read on. A trailing slash is removed.
 * @param siteId The website's identifier.
 */
export function trackingSnippet(origin: string, siteId: string): string {
  const address = origin.replace(/\/+$/, '');

  return `${scriptTag(address, siteId)}\n${pixelTag(address, siteId)}`;
}
