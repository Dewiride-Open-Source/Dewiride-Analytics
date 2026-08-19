'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import type { AnalyticsWindow } from '@/lib/analytics/period';
import {
  listSites,
  readOverview,
  readPages,
  readSeries,
  readTraffic,
  readVisits,
} from '@/lib/api/endpoints';
import type { SeriesMetric } from '@/lib/api/schemas';
import { overviewKey, pagesKey, seriesKey, sitesKey, trafficKey, visitsKey } from './keys';

/** How long an answer about traffic is treated as current before it is asked again. */
const FRESH_FOR = 30_000;

/** The same, for answers that only change as visits finish. */
const JUDGED_FRESH_FOR = 120_000;

/** The websites the signed-in person is allowed to look at. */
export function useSites() {
  return useQuery({
    queryKey: sitesKey,
    queryFn: listSites,
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

/** The most recent judged visits for one website, with the evidence behind each verdict. */
export function useVisits(siteId: string, window: AnalyticsWindow, limit: number) {
  return useQuery({
    queryKey: visitsKey(siteId, window, limit),
    queryFn: () => readVisits(siteId, window, limit),
    retry: false,
    staleTime: JUDGED_FRESH_FOR,
  });
}
