'use client';

import {
  keepPreviousData,
  type QueryClient,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { EVERY_JOURNEY, type JourneyFilters } from '@/lib/analytics/journeys';
import type { Population } from '@/lib/analytics/people-only';
import type { AnalyticsWindow, Granularity } from '@/lib/analytics/period';
import type { NewSite } from '@/lib/api/endpoints';
import {
  addSite,
  listSites,
  readActions,
  readDevices,
  readEngagement,
  readFacets,
  readLive,
  readLiveTrail,
  readLocations,
  readOverview,
  readPageEngagement,
  readPages,
  readSeries,
  readSoftware,
  readSources,
  readTraffic,
  readTrafficSeries,
  readVisitJourney,
  readVisitPages,
  readVisits,
  readVisitTotals,
} from '@/lib/api/endpoints';
import type {
  ActionGrouping,
  EngagementRanking,
  LocationGrouping,
  SeriesMetric,
  Session,
  SoftwareGrouping,
  SourceGrouping,
  VisitPosition,
} from '@/lib/api/schemas';
import {
  actionsKey,
  devicesKey,
  engagementKey,
  facetsKey,
  liveKey,
  liveTrailKey,
  locationsKey,
  overviewKey,
  pageEngagementKey,
  pagesKey,
  seriesKey,
  sitesKey,
  softwareKey,
  sourcesKey,
  trafficKey,
  trafficSeriesKey,
  visitJourneyKey,
  visitPagesKey,
  visitsKey,
  visitTotalsKey,
} from './keys';
import { sessionKey } from './session';

/** How long an answer about traffic is treated as current before it is asked again. */
const FRESH_FOR = 30_000;

/** The same, for answers that only change as visits finish. */
const JUDGED_FRESH_FOR = 120_000;

/**
 * How often a screen about the present moment asks again.
 *
 * Ten seconds. Slower and somebody watching their site being swept sees it in stills; faster and
 * the reading costs more than it tells anybody, since a visitor counts towards the answer for a
 * full half hour and almost nothing in it can change from one second to the next.
 */
const LIVE_EVERY = 10_000;

/**
 * The websites the signed-in person is allowed to look at.
 *
 * Asked for only once somebody is signed in. The bar across the top is on every screen, including
 * the two nobody is signed in on, and asking there would be a refusal on every first visit.
 *
 * @param enabled Whether to ask at all.
 */
export function useSites(enabled = true) {
  return useQuery({
    queryKey: sitesKey,
    queryFn: listSites,
    enabled,
    retry: false,
    staleTime: 60_000,
  });
}

/** Headline totals for one website over a period, for one population. */
export function useOverview(siteId: string, window: AnalyticsWindow, population: Population) {
  return useQuery({
    queryKey: overviewKey(siteId, window, population),
    queryFn: () => readOverview(siteId, window, population),
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/**
 * One measure for one website, counted in buckets of the given size across a period.
 *
 * Asked only while it is being drawn. The picture on the overview answers two questions and shows
 * one at a time, and a screen that opens on the other has no reason to read this.
 *
 * @param enabled Whether to ask at all.
 */
export function useSeries(
  siteId: string,
  metric: SeriesMetric,
  window: AnalyticsWindow,
  population: Population,
  granularity: Granularity,
  enabled = true,
) {
  return useQuery({
    queryKey: seriesKey(siteId, metric, window, population, granularity),
    queryFn: () => readSeries(siteId, metric, window, population, granularity),
    enabled,
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/**
 * One slice of the pages on a website over a period.
 *
 * The slice already fetched stays on screen while the next one is being read, so moving through
 * the list slides from one set of rows to the next instead of collapsing the list to a blank box
 * and pushing everything below it up the screen.
 */
export function usePages(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
  limit: number,
  offset: number,
) {
  return useQuery({
    queryKey: pagesKey(siteId, window, population, limit, offset),
    queryFn: () => readPages(siteId, window, population, limit, offset),
    placeholderData: keepPreviousData,
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/**
 * One slice of the places a period's audience was in.
 *
 * Kept on screen while the next slice is read, and while the reader switches between countries
 * and towns, so neither move collapses the card and shoves everything below it up the page.
 */
export function useLocations(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
  grouping: LocationGrouping,
  limit: number,
  offset: number,
) {
  return useQuery({
    queryKey: locationsKey(siteId, window, population, grouping, limit, offset),
    queryFn: () => readLocations(siteId, window, population, grouping, limit, offset),
    placeholderData: keepPreviousData,
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/**
 * One slice of where a period's visitors came from.
 *
 * Kept on screen while the next slice is read, and while the reader switches between sending
 * sites and sending pages, so neither move collapses the card and shoves everything below it up
 * the page.
 */
export function useSources(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
  grouping: SourceGrouping,
  limit: number,
  offset: number,
) {
  return useQuery({
    queryKey: sourcesKey(siteId, window, population, grouping, limit, offset),
    queryFn: () => readSources(siteId, window, population, grouping, limit, offset),
    placeholderData: keepPreviousData,
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/**
 * How a period's audience divides between kinds of device.
 *
 * Always asked, whichever way the card is being read: it carries the total the card states, and
 * the two lists beside it are answers about the same audience.
 */
export function useDevices(siteId: string, window: AnalyticsWindow, population: Population) {
  return useQuery({
    queryKey: devicesKey(siteId, window, population),
    queryFn: () => readDevices(siteId, window, population),
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/**
 * One slice of the software a period's audience used.
 *
 * Asked only while it is being looked at — a card opened on the device split has no reason to
 * fetch a browser list nobody has asked for — and kept on screen while the next slice is read.
 */
export function useSoftware(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
  grouping: SoftwareGrouping,
  limit: number,
  offset: number,
  enabled: boolean,
) {
  return useQuery({
    queryKey: softwareKey(siteId, window, population, grouping, limit, offset),
    queryFn: () => readSoftware(siteId, window, population, grouping, limit, offset),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/**
 * Starts measuring another website.
 *
 * The list of websites is asked for again rather than patched, because what somebody may see is
 * the engine's answer rather than this screen's arithmetic. Exactly the list and nothing under it:
 * asking by prefix would match every question already answered about every other website and send
 * the lot round again, none of which a website being added can have changed.
 */
export function useAddSite() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: (site: NewSite) => addSite(site, proofFrom(cache)),
    onSuccess: () => {
      void cache.invalidateQueries({ queryKey: sitesKey, exact: true });
    },
  });
}

/**
 * The proof-of-origin value the engine last issued.
 *
 * Read at the moment of use rather than held, because it belongs to the identity it was issued to
 * and a fresh one arrives with every answer that changes who is signed in.
 */
function proofFrom(cache: QueryClient): string {
  const proof = cache.getQueryData<Session>(sessionKey)?.token;

  if (!proof) {
    throw new Error('No session has been read yet, so nothing can be submitted.');
  }

  return proof;
}

/**
 * One slice of what a period's visitors operated, most pressed first.
 *
 * Asked only while it is being looked at, and kept on screen while the next slice is read.
 */
export function useActions(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
  grouping: ActionGrouping,
  limit: number,
  offset: number,
  enabled: boolean,
) {
  return useQuery({
    queryKey: actionsKey(siteId, window, population, grouping, limit, offset),
    queryFn: () => readActions(siteId, window, population, grouping, limit, offset),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/**
 * Judged visits over a period, grouped by what generated them.
 *
 * Held current for longer than the headline totals. A visit is not judged until it has finished,
 * so this answer moves at the pace visits end rather than at the pace pages are read, and asking
 * again every half minute would be asking the same question repeatedly.
 *
 * @param enabled Whether to ask at all. A screen that only needs the answer while it is kept to
 * people has no reason to pay for it the rest of the time.
 */
export function useTraffic(siteId: string, window: AnalyticsWindow, enabled = true) {
  return useQuery({
    queryKey: trafficKey(siteId, window),
    queryFn: () => readTraffic(siteId, window),
    enabled,
    retry: false,
    staleTime: JUDGED_FRESH_FOR,
  });
}

/**
 * What generated a period's traffic, bucket by bucket.
 *
 * Held current for as long as the breakdown it agrees with, and asked only while it is being
 * drawn. Both count visits that have finished, so both move at the pace visits end.
 *
 * @param enabled Whether to ask at all.
 */
export function useTrafficSeries(
  siteId: string,
  window: AnalyticsWindow,
  granularity: Granularity,
  enabled = true,
) {
  return useQuery({
    queryKey: trafficSeriesKey(siteId, window, granularity),
    queryFn: () => readTrafficSeries(siteId, window, granularity),
    enabled,
    retry: false,
    staleTime: JUDGED_FRESH_FOR,
  });
}

/**
 * One slice of a website's judged visits, newest first, with the evidence behind each verdict.
 *
 * The slice on screen is kept while the next is read, so moving through the list does not empty
 * the card and drop everything below it up the page between one slice and the next — and the same
 * while a reader narrows the list, which is otherwise the moment the screen collapses under the
 * controls they are still using.
 */
export function useVisits(
  siteId: string,
  window: AnalyticsWindow,
  limit: number,
  offset: number,
  filters: JourneyFilters = EVERY_JOURNEY,
) {
  return useQuery({
    queryKey: visitsKey(siteId, window, limit, offset, filters),
    queryFn: () => readVisits(siteId, window, limit, offset, filters),
    retry: false,
    placeholderData: keepPreviousData,
    staleTime: JUDGED_FRESH_FOR,
  });
}

/**
 * What this period's judged visits held, for the controls that narrow the list.
 *
 * Only asked once somebody reaches for those controls. Working out what a period held means going
 * back over the period's whole activity, which is worth doing for a reader who is about to narrow
 * the list and worth nothing at all on a screen they are only glancing at.
 *
 * The last answer is kept while a new one is read, so the values on offer do not empty themselves
 * under somebody's hand when the period changes beneath them.
 */
export function useFacets(siteId: string, window: AnalyticsWindow, wanted: boolean) {
  return useQuery({
    queryKey: facetsKey(siteId, window),
    queryFn: () => readFacets(siteId, window),
    enabled: wanted,
    retry: false,
    placeholderData: keepPreviousData,
    staleTime: JUDGED_FRESH_FOR,
  });
}

/**
 * How a period's pages were actually read.
 *
 * Always asked, whichever way the card is being read: it carries the coverage the card states,
 * and the list beside it is an answer about the same readings.
 */
export function useEngagement(siteId: string, window: AnalyticsWindow, population: Population) {
  return useQuery({
    queryKey: engagementKey(siteId, window, population),
    queryFn: () => readEngagement(siteId, window, population),
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/**
 * One slice of a period's pages ranked by how they were read.
 *
 * Asked only while it is being looked at, and kept on screen while the next slice is read.
 */
export function usePageEngagement(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
  ranking: EngagementRanking,
  limit: number,
  offset: number,
  enabled: boolean,
) {
  return useQuery({
    queryKey: pageEngagementKey(siteId, window, population, ranking, limit, offset),
    queryFn: () => readPageEngagement(siteId, window, population, ranking, limit, offset),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/**
 * How a period's visits went.
 *
 * Always asked, whichever way the card beside it is being read: it carries the total every share
 * on that card is taken against.
 */
export function useVisitTotals(siteId: string, window: AnalyticsWindow, population: Population) {
  return useQuery({
    queryKey: visitTotalsKey(siteId, window, population),
    queryFn: () => readVisitTotals(siteId, window, population),
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/**
 * One slice of the pages a period's visits began or ended on.
 *
 * Kept on screen while the next slice is read, and while the reader switches between arrivals and
 * departures, so neither move collapses the card and shoves everything below it up the page.
 */
export function useVisitPages(
  siteId: string,
  window: AnalyticsWindow,
  population: Population,
  position: VisitPosition,
  limit: number,
  offset: number,
) {
  return useQuery({
    queryKey: visitPagesKey(siteId, window, population, position, limit, offset),
    queryFn: () => readVisitPages(siteId, window, population, position, limit, offset),
    placeholderData: keepPreviousData,
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/**
 * The pages one visit went through.
 *
 * Asked only once somebody opens the visit. A screenful of visits is twenty-five journeys nobody
 * has asked to see, and each of them is a separate question of the store. Once read it is kept for
 * good: a finished visit's journey cannot change.
 */
export function useVisitJourney(siteId: string, visit: string, enabled: boolean) {
  return useQuery({
    queryKey: visitJourneyKey(siteId, visit),
    queryFn: () => readVisitJourney(siteId, visit),
    enabled,
    retry: false,
    staleTime: Infinity,
  });
}

/**
 * Who is on a website at this moment, asked again for as long as somebody is watching.
 *
 * Never held as current, because an answer about now is out of date the instant it is given: a tab
 * come back to asks straight away rather than showing the moment it was left on. A tab nobody is
 * looking at asks nothing at all — the beat still falls and the question is skipped — so a screen
 * left open overnight costs a screen left open rather than a night of readings.
 *
 * Whatever arrived last stays on screen while the next is on its way, and stays there if the next
 * one never comes. A live screen that emptied itself the moment one reading was refused would spend
 * a flaky minute telling somebody their site was deserted.
 *
 * @param watching Whether to keep asking. Off while the reader has held the screen still.
 */
export function useLiveTraffic(siteId: string, watching: boolean) {
  return useQuery({
    queryKey: liveKey(siteId),
    queryFn: () => readLive(siteId),
    retry: false,
    staleTime: 0,
    refetchInterval: watching ? LIVE_EVERY : false,
    refetchOnWindowFocus: watching,
  });
}

/**
 * What one visitor who is here has been doing, from the moment somebody opens their row.
 *
 * Only an opened row asks anything, so the cost of this screen is what the reader is actually
 * looking at rather than one question per visitor on it.
 *
 * Asked on no beat of its own: the beat is the live reading's, and each reading's instant renames
 * the question. A row still here is asked about under every new reading, and a row whose visitor
 * has gone keeps the instant it was last seen in, so it is never asked again and its trail stays
 * exactly as it stood — a row somebody is part-way through reading must not empty itself
 * underneath them. The answer to the last instant stays on screen until the answer to the next one
 * lands, and an answer over a fixed instant cannot change, so one is never asked for twice while
 * its row is on screen. Answers to instants no row asks about any more are let go on the cache's
 * ordinary terms, since every beat would otherwise leave one more behind.
 *
 * @param at When the reading the row was drawn from was taken.
 * @param opened Whether the row has been opened. Nothing is asked for until it has.
 */
export function useLiveTrail(siteId: string, visitor: string, at: string, opened: boolean) {
  return useQuery({
    queryKey: liveTrailKey(siteId, visitor, at),
    queryFn: () => readLiveTrail(siteId, visitor, at),
    enabled: opened,
    retry: false,
    placeholderData: keepPreviousData,
    staleTime: Infinity,
  });
}
