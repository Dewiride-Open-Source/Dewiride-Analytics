/**
 * The tag that starts the beacon.
 *
 * Everything this file does is read its own script element and hand what it finds to the measuring
 * half. The element has to be read here and on the first line: a browser only reports which script
 * is running while that script's own body is running, and by the time any callback, timer or event
 * handler asks, the answer is nothing.
 */

import { start } from './beacon';

const tag = document.currentScript as HTMLScriptElement | null;

const site = tag?.getAttribute('data-site');

if (tag?.src && site) {
  start({
    siteId: site,
    // Worked out from where this file was loaded from, so the tag carries one address rather than
    // two that can disagree. An installation served under a path keeps that path.
    endpoint: new URL(tag.getAttribute('data-collector') || 'collect', tag.src).href,
    correlationId: tag.getAttribute('data-correlation') || undefined,
  });
}
