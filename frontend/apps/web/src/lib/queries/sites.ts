'use client';

import { useQuery } from '@tanstack/react-query';
import type { AnalyticsWindow } from '@/lib/analytics/period';
import { listSites, readOverview, readSeries } from '@/lib/api/endpoints';
import type { SeriesMetric } from '@/lib/api/schemas';
import { overviewKey, seriesKey, sitesKey } from './keys';

/** How long an answer about traffic is treated as current before it is asked again. */
const FRESH_FOR = 30_000;

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
