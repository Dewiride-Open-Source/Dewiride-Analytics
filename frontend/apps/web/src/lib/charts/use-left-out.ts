'use client';

import { useFormatter, useTranslations } from 'next-intl';
import { useMemo } from 'react';
import { monthsInFull } from '@/lib/charts/bucket-names';

/**
 * Sentences said one after another, leaving out any that are not said.
 *
 * @param said Each sentence, or nothing where it is not said.
 */
export function sentences(...said: readonly (string | null)[]): string {
  return said.filter((sentence) => sentence !== null).join(' ');
}

/**
 * The sentences naming the months a website's figures leave out, or nothing where they leave none
 * out. A month never counted and a month kept before its visits were all judged are two different
 * facts, and each is named as the one it is: a gap in the drawing and a month nobody came in can
 * look alike, and a year that leaves one out looks like any other year.
 *
 * @param missing Every month never counted, written `2025-06`.
 * @param unsettled Every month kept before its visits were all judged.
 * @param timeZoneId The website's zone, which the months are named in.
 */
export function useLeftOut(
  missing: readonly string[],
  unsettled: readonly string[],
  timeZoneId: string,
): string | null {
  const t = useTranslations('dashboard.chart');
  const format = useFormatter();

  return useMemo(() => {
    const said = sentences(
      missing.length === 0
        ? null
        : t('uncounted', {
            count: missing.length,
            months: monthsInFull(missing, timeZoneId, format),
          }),
      unsettled.length === 0
        ? null
        : t('unsettled', {
            count: unsettled.length,
            months: monthsInFull(unsettled, timeZoneId, format),
          }),
    );

    return said === '' ? null : said;
  }, [missing, unsettled, timeZoneId, format, t]);
}
