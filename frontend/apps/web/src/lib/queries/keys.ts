import { type JourneyFilters, narrowingParams } from '@/lib/analytics/journeys';
import type { AnalyticsWindow, Granularity } from '@/lib/analytics/period';
import type {
  ActionGrouping,
  EngagementRanking,
  LocationGrouping,
  SeriesMetric,
  SoftwareGrouping,
  SourceGrouping,
  VisitPosition,
} from '@/lib/api/schemas';

/**
 * Names the cached answers are filed under.
 *
 * Kept apart from the hooks that use them so that a hook which clears another area's cache does
 * not have to import that area's module and create a circle between the two.
 */

export const sitesKey = ['sites'] as const;

/**
 * The account somebody belongs to, everybody in it, and everybody waiting to join.
 *
 * One name for all three, because they are one answer: a screen that showed the people from one
 * read and the invitations from another would show somebody twice on the day they accepted.
 */
export const organizationKey = ['organization'] as const;

export function overviewKey(siteId: string, window: AnalyticsWindow) {
  return ['sites', siteId, 'overview', window.from, window.to] as const;
}

/**
 * One measure across a period, at one size of bucket.
 *
 * The size of the bucket is part of the name because the same window can be asked about twice —
 * a period of one day is drawn an hour at a time and a longer one a day at a time — and two
 * answers filed under one name would draw yesterday's shape with today's numbers.
 */
export function seriesKey(
  siteId: string,
  metric: SeriesMetric,
  window: AnalyticsWindow,
  granularity: Granularity,
) {
  return ['sites', siteId, 'series', metric, window.from, window.to, granularity] as const;
}

export function pagesKey(siteId: string, window: AnalyticsWindow, limit: number, offset: number) {
  return ['sites', siteId, 'pages', window.from, window.to, limit, offset] as const;
}

export function locationsKey(
  siteId: string,
  window: AnalyticsWindow,
  grouping: LocationGrouping,
  limit: number,
  offset: number,
) {
  return ['sites', siteId, 'locations', window.from, window.to, grouping, limit, offset] as const;
}

export function sourcesKey(
  siteId: string,
  window: AnalyticsWindow,
  grouping: SourceGrouping,
  limit: number,
  offset: number,
) {
  return ['sites', siteId, 'sources', window.from, window.to, grouping, limit, offset] as const;
}

export function devicesKey(siteId: string, window: AnalyticsWindow) {
  return ['sites', siteId, 'devices', window.from, window.to] as const;
}

export function softwareKey(
  siteId: string,
  window: AnalyticsWindow,
  grouping: SoftwareGrouping,
  limit: number,
  offset: number,
) {
  return ['sites', siteId, 'software', window.from, window.to, grouping, limit, offset] as const;
}

export function actionsKey(
  siteId: string,
  window: AnalyticsWindow,
  grouping: ActionGrouping,
  limit: number,
  offset: number,
) {
  return ['sites', siteId, 'actions', window.from, window.to, grouping, limit, offset] as const;
}

/**
 * Everything about one website that its owner decides.
 *
 * Not a question about a period, so it carries none: what a website is called and what it records
 * are the same answer whichever week somebody happens to be looking at.
 */
export function siteSettingsKey(siteId: string) {
  return ['sites', siteId, 'settings'] as const;
}

export function trafficKey(siteId: string, window: AnalyticsWindow) {
  return ['sites', siteId, 'traffic', window.from, window.to] as const;
}

/**
 * What generated a period's traffic, at one size of bucket.
 *
 * The bucket is part of the name for the same reason it is on the other series: one window
 * answered an hour at a time and a day at a time are two different answers, and two answers filed
 * under one name would draw one shape with the other's numbers.
 */
export function trafficSeriesKey(
  siteId: string,
  window: AnalyticsWindow,
  granularity: Granularity,
) {
  return ['sites', siteId, 'traffic', 'series', window.from, window.to, granularity] as const;
}

/**
 * One slice of a website's judged visits.
 *
 * What the reader narrowed to is part of the name, because it is part of the question: two slices
 * asked for at the same offset under different narrowings are two different answers, and an answer
 * kept under one name would be handed back for the other.
 *
 * The narrowing is written out by the same code that asks the engine for it, so the name and the
 * question cannot come apart — and two readers who picked the same values in a different order
 * arrive at one name and share one answer.
 */
export function visitsKey(
  siteId: string,
  window: AnalyticsWindow,
  limit: number,
  offset: number,
  filters: JourneyFilters,
) {
  return [
    'sites',
    siteId,
    'visits',
    window.from,
    window.to,
    limit,
    offset,
    narrowingParams(filters).toString(),
  ] as const;
}

/**
 * What a period's judged visits held, for the controls that narrow them.
 *
 * Carries no narrowing of its own: it describes the whole period, so the values on offer stay put
 * while somebody works through them rather than rearranging themselves after every press.
 */
export function facetsKey(siteId: string, window: AnalyticsWindow) {
  return ['sites', siteId, 'visits', 'facets', window.from, window.to] as const;
}

export function serverKeysKey(siteId: string) {
  return ['sites', siteId, 'server-keys'] as const;
}

export function engagementKey(siteId: string, window: AnalyticsWindow) {
  return ['sites', siteId, 'engagement', window.from, window.to] as const;
}

export function pageEngagementKey(
  siteId: string,
  window: AnalyticsWindow,
  ranking: EngagementRanking,
  limit: number,
  offset: number,
) {
  return [
    'sites',
    siteId,
    'engagement',
    'pages',
    window.from,
    window.to,
    ranking,
    limit,
    offset,
  ] as const;
}

export function visitTotalsKey(siteId: string, window: AnalyticsWindow) {
  return ['sites', siteId, 'visits', 'totals', window.from, window.to] as const;
}

export function visitPagesKey(
  siteId: string,
  window: AnalyticsWindow,
  position: VisitPosition,
  limit: number,
  offset: number,
) {
  return [
    'sites',
    siteId,
    'visits',
    'pages',
    window.from,
    window.to,
    position,
    limit,
    offset,
  ] as const;
}

/**
 * One visit's journey, filed under the visit rather than under a period.
 *
 * A journey cannot change once the visit has finished, which is the only state it is ever asked
 * for in, so it is not filed under the period somebody happened to be looking at when they opened
 * it.
 */
export function visitJourneyKey(siteId: string, visit: string) {
  return ['sites', siteId, 'visits', visit, 'journey'] as const;
}
