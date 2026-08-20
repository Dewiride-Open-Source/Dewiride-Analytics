'use client';

import {
  keepPreviousData,
  type QueryClient,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { EVERY_JOURNEY, type JourneyFilters } from '@/lib/analytics/journeys';
import type { AnalyticsWindow } from '@/lib/analytics/period';
import {
  addSite,
  listSites,
  readDevices,
  readEngagement,
  readOverview,
  readLocations,
  readPages,
  readSeries,
  readPageEngagement,
  readActions,
  readSoftware,
  readSources,
  readTraffic,
  readVisitJourney,
  readVisitPages,
  readVisitTotals,
  readVisits,
} from '@/lib/api/endpoints';
import type { NewSite } from '@/lib/api/endpoints';
import type {
  EngagementRanking,
  LocationGrouping,
  SeriesMetric,
  ActionGrouping,
  Session,
  SoftwareGrouping,
  SourceGrouping,
  VisitPosition,
} from '@/lib/api/schemas';
import {
  devicesKey,
  engagementKey,
  locationsKey,
  overviewKey,
  pagesKey,
  seriesKey,
  sitesKey,
  pageEngagementKey,
  actionsKey,
  softwareKey,
  sourcesKey,
  trafficKey,
  visitJourneyKey,
  visitPagesKey,
  visitTotalsKey,
  visitsKey,
} from './keys';
import { sessionKey } from './session';

/** How long an answer about traffic is treated as current before it is asked again. */
const FRESH_FOR = 30_000;

/** The same, for answers that only change as visits finish. */
const JUDGED_FRESH_FOR = 120_000;

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

/** Headline totals for one website over a period. */
export function useOverview(siteId: string, window: AnalyticsWindow) {
  return useQuery({
    queryKey: overviewKey(siteId, window),
    queryFn: () => readOverview(siteId, window),
    retry: false,
    staleTime: FRESH_FOR,
  });
}

/** One measure for one website, counted a day at a time across a period. */
export function useDailySeries(siteId: string, metric: SeriesMetric, window: AnalyticsWindow) {
  return useQuery({
    queryKey: seriesKey(siteId, metric, window),
    queryFn: () => readSeries(siteId, metric, window),
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
export function usePages(siteId: string, window: AnalyticsWindow, limit: number, offset: number) {
  return useQuery({
    queryKey: pagesKey(siteId, window, limit, offset),
    queryFn: () => readPages(siteId, window, limit, offset),
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
  grouping: LocationGrouping,
  limit: number,
  offset: number,
) {
  return useQuery({
    queryKey: locationsKey(siteId, window, grouping, limit, offset),
    queryFn: () => readLocations(siteId, window, grouping, limit, offset),
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
  grouping: SourceGrouping,
  limit: number,
  offset: number,
) {
  return useQuery({
    queryKey: sourcesKey(siteId, window, grouping, limit, offset),
    queryFn: () => readSources(siteId, window, grouping, limit, offset),
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
export function useDevices(siteId: string, window: AnalyticsWindow) {
  return useQuery({
    queryKey: devicesKey(siteId, window),
    queryFn: () => readDevices(siteId, window),
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
  grouping: SoftwareGrouping,
  limit: number,
  offset: number,
  enabled: boolean,
) {
  return useQuery({
    queryKey: softwareKey(siteId, window, grouping, limit, offset),
    queryFn: () => readSoftware(siteId, window, grouping, limit, offset),
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
  grouping: ActionGrouping,
  limit: number,
  offset: number,
  enabled: boolean,
) {
  return useQuery({
    queryKey: actionsKey(siteId, window, grouping, limit, offset),
    queryFn: () => readActions(siteId, window, grouping, limit, offset),
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
 */
export function useTraffic(siteId: string, window: AnalyticsWindow) {
  return useQuery({
    queryKey: trafficKey(siteId, window),
    queryFn: () => readTraffic(siteId, window),
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
 * How a period's pages were actually read.
 *
 * Always asked, whichever way the card is being read: it carries the coverage the card states,
 * and the list beside it is an answer about the same readings.
 */
export function useEngagement(siteId: string, window: AnalyticsWindow) {
  return useQuery({
    queryKey: engagementKey(siteId, window),
    queryFn: () => readEngagement(siteId, window),
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
  ranking: EngagementRanking,
  limit: number,
  offset: number,
  enabled: boolean,
) {
  return useQuery({
    queryKey: pageEngagementKey(siteId, window, ranking, limit, offset),
    queryFn: () => readPageEngagement(siteId, window, ranking, limit, offset),
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
export function useVisitTotals(siteId: string, window: AnalyticsWindow) {
  return useQuery({
    queryKey: visitTotalsKey(siteId, window),
    queryFn: () => readVisitTotals(siteId, window),
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
  position: VisitPosition,
  limit: number,
  offset: number,
) {
  return useQuery({
    queryKey: visitPagesKey(siteId, window, position, limit, offset),
    queryFn: () => readVisitPages(siteId, window, position, limit, offset),
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
