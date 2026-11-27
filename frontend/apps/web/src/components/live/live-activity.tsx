'use client';

import { Activity } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useCallback, useMemo } from 'react';
import { CELL, FIGURE, Figures } from '@/components/charts/figures';
import { Keys } from '@/components/charts/keys';
import { NothingDrawn, Picture } from '@/components/charts/picture';
import { minuteOption } from '@/components/live/minute-chart';
import { Card } from '@/components/ui/card';
import {
  anythingRead,
  arrivalKeptTo,
  type LiveNarrowing,
  pagesReadEachMinute,
  runningMinute,
} from '@/lib/analytics/live';
import type { LiveMinute } from '@/lib/api/schemas';
import type { ChartPalette } from '@/lib/charts/palette';
import { readableZone } from '@/lib/time-zones';

interface LiveActivityProps {
  /** The half hour, one bucket a minute, oldest first and never short of a minute. */
  readonly minutes: readonly LiveMinute[];
  /** When the reading was taken, which decides which minute is still running. */
  readonly at: string;
  /** The site's own zone, so a minute is read back on the clock it was counted on. */
  readonly timeZoneId: string;
  readonly siteName: string;
  /** What the list of who is here is kept to, which the drawing follows where it can. */
  readonly narrowing: LiveNarrowing;
}

/**
 * How much of a website has been read, minute by minute, across the last half hour.
 *
 * The shape of the half hour is what tells somebody whether what they are looking at is a website
 * being visited or a website being swept: thirty even columns and a hundred visitors is a machine,
 * and the same hundred in one column is a link that has just been posted somewhere.
 *
 * Kept to the visitors one kind of site sent while the list is kept to them, and named for them, so
 * the columns and the rows beside them describe the same people. Kept to something recognised, the
 * columns stay everybody's and keep everybody's name, because no minute can be kept to it.
 *
 * The minute still running is faded, said and marked in the table, all three. A fade alone is
 * invisible to anybody reading rather than looking, and left unqualified the newest column reads
 * as traffic falling off a cliff every time it is drawn.
 */
export function LiveActivity({ minutes, at, timeZoneId, siteName, narrowing }: LiveActivityProps) {
  const t = useTranslations('live.activity');
  const format = useFormatter();

  const kind = arrivalKeptTo(narrowing);
  const name = useMemo(
    () => (kind === null ? t('measure') : t('measureSentBy', { kind })),
    [kind, t],
  );
  const zone = useMemo(() => readableZone(timeZoneId), [timeZoneId]);
  const running = useMemo(() => runningMinute(minutes, at), [minutes, at]);
  const pageViews = useMemo(() => pagesReadEachMinute(minutes, narrowing), [minutes, narrowing]);

  // The clause is said only where there is a column to say it about. On a half hour that has gone
  // quiet the last minute is both still running and empty, and a sentence explaining a fade nobody
  // can see reads as an excuse for the silence rather than as a note about the newest column.
  const filling = running !== null && (pageViews[running] ?? 0) > 0;

  const labels = useMemo(
    () =>
      minutes.map((minute) =>
        format.dateTime(new Date(minute.start), {
          timeZone: timeZoneId,
          hour: 'numeric',
          minute: '2-digit',
        }),
      ),
    [minutes, format, timeZoneId],
  );

  const option = useCallback(
    (palette: ChartPalette) => minuteOption({ labels, name, pageViews, running }, palette),
    [labels, name, pageViews, running],
  );

  return (
    <Card className="flex flex-col gap-4 p-5 sm:p-6">
      <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>

      {anythingRead(pageViews) ? (
        <>
          <Keys items={[{ fill: 'bg-chart-1', label: name }]} />

          <Picture
            option={option}
            label={
              kind === null
                ? t('summary', { site: siteName })
                : t('summarySentBy', { site: siteName, kind })
            }
          />

          <p className="text-xs text-foreground-subtle">
            {t('zone', { zone })}
            {filling ? <> {t('filling')}</> : null}
          </p>

          <Figures label={t('table')}>
            <thead className="text-xs text-foreground-subtle">
              <tr>
                <th scope="col" className={`${CELL} font-medium`}>
                  {t('columnTime')}
                </th>
                <th scope="col" className={`${CELL} text-right font-medium`}>
                  {name}
                </th>
              </tr>
            </thead>
            <tbody className="text-foreground-muted">
              {minutes.map((minute, bucket) => (
                <tr key={minute.start} className="border-t border-border">
                  <th scope="row" className={`${CELL} font-normal`}>
                    <span className="whitespace-nowrap">{labels[bucket]}</span>
                    {/*
                      Beneath the minute rather than beside it. Said inline it would set the width
                      of the first column for all thirty rows on account of the one that carries it.
                    */}
                    {bucket === running ? (
                      <span className="block whitespace-nowrap text-xs text-foreground-subtle">
                        {t('stillFilling')}
                      </span>
                    ) : null}
                  </th>
                  <td className={FIGURE}>{format.number(pageViews[bucket] ?? 0)}</td>
                </tr>
              ))}
            </tbody>
          </Figures>
        </>
      ) : (
        // No way out offered from here. The screen around this card carries the one thing worth
        // doing about a website nobody is reading, and offering it twice makes it read as two.
        <NothingDrawn
          icon={Activity}
          body={kind === null ? t('none') : t('noneSentBy', { kind })}
        />
      )}
    </Card>
  );
}
