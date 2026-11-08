import { type JourneyFilters, narrowingParams } from '@/lib/analytics/journeys';
import type { Population } from '@/lib/analytics/people-only';
import type {
  AllTimeGranularity,
  AnalyticsWindow,
  Granularity,
  PeriodGranularity,
} from '@/lib/analytics/period';
import type { PostsListing } from '@/lib/analytics/posts';
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

/**
 * Headline totals over a period, for one population.
 *
 * The population is part of the name because it is part of the question: the overview kept to
 * people and the one the screen checks it against are two answers, and under everybody they are
 * one — which is what lets the check cost nothing.
 */
export function overviewKey(siteId: string, window: AnalyticsWindow, population: Population) {
  return ['sites', siteId, 'overview', window.from, window.to, population] as const;
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
  population: Population,
  granularity: Granularity,
) {
  return [
    'sites',
    siteId,
    'series',
    metric,
    window.from,
    window.to,
    population,
    granularity,
  ] as const;
}

/**
 * A website's totals across all time, for one population.
 *
 * No window in the name, because all time has none: it is every month the engine holds, and the
 * same question asked a minute later is the same question with a newer answer. The website's zone
 * is in it, because the engine cuts every month — and names the first day — where the website is
 * now, and the screen reads them there: an answer cut in a zone the website has left is an answer to
 * a different question.
 */
export function allTimeKey(siteId: string, timeZoneId: string, population: Population) {
  return ['sites', siteId, 'all-time', timeZoneId, population] as const;
}

/**
 * What a website's whole record says, for one population, in its zone: the screen about the record
 * reads all of it from one answer, and the zone is in the name for the reason it is in all time's.
 * A website not yet known has a name here as well, under which nothing is ever asked.
 */
export function insightsKey(
  siteId: string | undefined,
  timeZoneId: string | undefined,
  population: Population,
) {
  return ['sites', siteId, 'insights', timeZoneId, population] as const;
}

/** How far reading a website's sitemap has got, as far as its posts are concerned. */
export interface PostsReadingStage {
  /** When the last reading of the sitemap itself finished, or nothing where none has. */
  readonly readAt: string | null;
  /** Whether every page it lists has been read since. */
  readonly pagesRead: boolean;
}

/**
 * A website's posts as its whole record reads them, for one population, in its zone, as of how far
 * reading its sitemap has got.
 *
 * The zone is in the name for the reason it is in the record's: the calendar and every day a post
 * was published on are the website's own. How far reading has got is in it because a page only says
 * whether it is a post, and when it was published, once it has been read: a reading finishing, and
 * its pages finishing after it, are each a new question, and the lists follow them without being
 * asked for again on every beat of watching how reading goes.
 */
export function insightsPostsKey(
  siteId: string,
  timeZoneId: string,
  population: Population,
  stage: PostsReadingStage,
) {
  return ['sites', siteId, 'insights', 'posts', timeZoneId, population, stage] as const;
}

/**
 * One page of a website's posts, for one population, in its zone, as of how far reading its sitemap
 * has got. What was searched for, the order and the page are each a different question; the zone and
 * how far reading has got are in the name for the reasons they are in the posts' own. The list a page
 * belongs to — the website, its zone and the population — is named first, so a page can be told from
 * a page of another list by the start of its name.
 */
export function postsPageKey(
  siteId: string,
  timeZoneId: string,
  population: Population,
  { search, order, limit, offset }: PostsListing,
  stage: PostsReadingStage,
) {
  return [
    'sites',
    siteId,
    'posts',
    timeZoneId,
    population,
    search,
    order,
    limit,
    offset,
    stage,
  ] as const;
}

/**
 * One post's whole record, for one population, in its website's zone: the months and the days of a
 * post are the website's own, so the zone is in the name for the reason it is in the record's. The
 * post is named by its path. A website not yet known, or an address naming no post, has a name here
 * as well, under which nothing is ever asked.
 */
export function postInsightsKey(
  siteId: string | undefined,
  timeZoneId: string | undefined,
  path: string | null,
  population: Population,
) {
  return ['sites', siteId, 'insights', 'post', timeZoneId, path, population] as const;
}

/**
 * One post's views across a period, at one size of bucket, for one population. The post is named by
 * its path, and the size of the bucket is in the name for the reason it is in a website's series.
 */
export function postSeriesKey(
  siteId: string,
  path: string,
  window: AnalyticsWindow,
  population: Population,
  granularity: PeriodGranularity,
) {
  return [
    'sites',
    siteId,
    'insights',
    'post',
    'series',
    path,
    window.from,
    window.to,
    population,
    granularity,
  ] as const;
}

/** One measure across all time, at one size of bucket, for one population, in the website's zone. */
export function allTimeSeriesKey(
  siteId: string,
  timeZoneId: string,
  metric: SeriesMetric,
  granularity: AllTimeGranularity,
  population: Population,
) {
  return [
    'sites',
    siteId,
    'all-time',
    timeZoneId,
    'series',
    metric,
    granularity,
    population,
  ] as const;
}

/** What generated a website's traffic across all time, at one size of bucket, in its zone. */
export function allTimeTrafficKey(
  siteId: string,
  timeZoneId: string,
  granularity: AllTimeGranularity,
) {
  return ['sites', siteId, 'all-time', timeZoneId, 'traffic', granularity] as const;
}

export function pagesKey(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
  limit: number,
  offset: number,
) {
  return ['sites', siteId, 'pages', window.from, window.to, population, limit, offset] as const;
}

export function locationsKey(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
  grouping: LocationGrouping,
  limit: number,
  offset: number,
) {
  return [
    'sites',
    siteId,
    'locations',
    window.from,
    window.to,
    population,
    grouping,
    limit,
    offset,
  ] as const;
}

export function sourcesKey(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
  grouping: SourceGrouping,
  limit: number,
  offset: number,
) {
  return [
    'sites',
    siteId,
    'sources',
    window.from,
    window.to,
    population,
    grouping,
    limit,
    offset,
  ] as const;
}

export function devicesKey(siteId: string, window: AnalyticsWindow, population: Population) {
  return ['sites', siteId, 'devices', window.from, window.to, population] as const;
}

export function softwareKey(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
  grouping: SoftwareGrouping,
  limit: number,
  offset: number,
) {
  return [
    'sites',
    siteId,
    'software',
    window.from,
    window.to,
    population,
    grouping,
    limit,
    offset,
  ] as const;
}

export function actionsKey(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
  grouping: ActionGrouping,
  limit: number,
  offset: number,
) {
  return [
    'sites',
    siteId,
    'actions',
    window.from,
    window.to,
    population,
    grouping,
    limit,
    offset,
  ] as const;
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

/**
 * How reading a website's sitemap is going, filed apart from the settings because it changes by
 * itself while they stand still.
 */
export function sitemapStatusKey(siteId: string) {
  return ['sites', siteId, 'sitemap'] as const;
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

/**
 * What is happening on a website now, filed under the website alone.
 *
 * Every other reading is named after the days it covers. This one asks the same question every
 * time, so its name never moves — which is precisely what gives the answer arriving ten seconds
 * from now somewhere to sit, and what keeps the last one on screen while it is on its way.
 */
export function liveKey(siteId: string) {
  return ['sites', siteId, 'live'] as const;
}

/**
 * One visitor's trail through the minutes of one reading.
 *
 * Filed under the reading it belongs to, so a new reading is a new question and the old answer
 * stays on screen until the new one lands.
 */
export function liveTrailKey(siteId: string, visitor: string, at: string) {
  return ['sites', siteId, 'live', visitor, 'trail', at] as const;
}

export function serverKeysKey(siteId: string) {
  return ['sites', siteId, 'server-keys'] as const;
}

export function engagementKey(siteId: string, window: AnalyticsWindow, population: Population) {
  return ['sites', siteId, 'engagement', window.from, window.to, population] as const;
}

export function pageEngagementKey(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
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
    population,
    ranking,
    limit,
    offset,
  ] as const;
}

export function visitTotalsKey(siteId: string, window: AnalyticsWindow, population: Population) {
  return ['sites', siteId, 'visits', 'totals', window.from, window.to, population] as const;
}

export function visitPagesKey(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
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
    population,
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
