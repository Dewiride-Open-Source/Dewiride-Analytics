/**
 * The files the dashboard hands out, and the addresses it serves them from.
 *
 * Served by the dashboard itself, for the reason the beacon is: it is the one address certain to be
 * reachable by whoever is reading the screen.
 */

/**
 * The WordPress plugin, packaged the way WordPress's own uploader takes it.
 *
 * The version is the one the reader is told they are downloading, so it has to be the version
 * inside the file. Packaging the plugin fails when this disagrees with the plugin's own header,
 * the constant it declares, or its readme.
 */
export const WORDPRESS_PLUGIN = {
  href: '/downloads/dewiride-analytics.zip',
  version: '1.1.0',
} as const;
