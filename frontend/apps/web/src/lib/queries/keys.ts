import type { AnalyticsWindow } from '@/lib/analytics/period';
import type { SeriesMetric } from '@/lib/api/schemas';

/**
 * Names the cached answers are filed under.
 *
 * Kept apart from the hooks that use them so that a hook which clears another area's cache does
 * not have to import that area's module and create a circle between the two.
 */

export const sitesKey = ['sites'] as const;

export function overviewKey(siteId: string, window: AnalyticsWindow) {
  return ['sites', siteId, 'overview', window.from, window.to] as const;
}

export function seriesKey(siteId: string, metric: SeriesMetric, window: AnalyticsWindow) {
  return ['sites', siteId, 'series', metric, window.from, window.to] as const;
}

export function trafficKey(siteId: string, window: AnalyticsWindow) {
  return ['sites', siteId, 'traffic', window.from, window.to] as const;
}

export function visitsKey(siteId: string, window: AnalyticsWindow, limit: number) {
  return ['sites', siteId, 'visits', window.from, window.to, limit] as const;
}

export function serverKeysKey(siteId: string) {
  return ['sites', siteId, 'server-keys'] as const;
}
