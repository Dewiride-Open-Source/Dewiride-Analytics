'use client';

import { BookOpenText } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useState } from 'react';
import {
  ListEmpty,
  ListSwitch,
  ListWaiting,
  RankedNav,
  RankedRow,
} from '@/components/dashboard/ranked-list';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { splitDuration } from '@/lib/analytics/duration';
import { readablePath } from '@/lib/analytics/pages';
import type { AnalyticsWindow } from '@/lib/analytics/period';
import { shareOf } from '@/lib/analytics/share';
import type { Engagement, EngagementRanking, PageEngagementRow } from '@/lib/api/schemas';
import { useEngagement, usePageEngagement } from '@/lib/queries/sites';

interface SiteReadingProps {
  readonly siteId: string;
  readonly window: AnalyticsWindow;
  /** Opens the tracking code, which is the one thing that would make this card say anything. */
  readonly onShowCode: () => void;
}

/** How many pages are shown at once, matching the lists around it. */
const PER_PAGE = 10;

/** The three ways the card can be read, in the order they are offered. */
type ReadingView = 'overall' | EngagementRanking;

/** How far down the page each band stands for, and the fill it is drawn in. */
const DEPTH_BANDS = [
  { key: 'top', fill: 'bg-chart-2/40' },
  { key: 'quarter', fill: 'bg-chart-2/70' },
  { key: 'half', fill: 'bg-chart-1/70' },
  { key: 'whole', fill: 'bg-chart-1' },
] as const;

/** Furthest a reader can get, which a depth bar is drawn against. */
const ALL_THE_WAY = 100;

/**
 * How a website's pages were actually read, over a period.
 *
 * Three answers about the same readings rather than three cards: how long a page held somebody,
 * how far down they got, and which pages did it best. The summary is always asked for — it
 * carries the coverage the card states, and it is a handful of figures whichever way the card is
 * being read — while the list is asked for only while somebody is looking at it.
 *
 * The screen above gives this a key that changes with the website and the period, so choosing
 * either starts afresh rather than leaving somebody on a screenful that no longer exists.
 */
export function SiteReading({ siteId, window, onShowCode }: SiteReadingProps) {
  const t = useTranslations('dashboard.reading');
  const [view, setView] = useState<ReadingView>('overall');
  const [offset, setOffset] = useState(0);
  const ranking: EngagementRanking = view === 'depth' ? 'depth' : 'attention';
  const reading = useEngagement(siteId, window);
  const pages = usePageEngagement(siteId, window, ranking, PER_PAGE, offset, view !== 'overall');

  function show(next: ReadingView) {
    setView(next);
    setOffset(0);
  }

  if (reading.isError) {
    return <FailureNotice error={reading.error} />;
  }

  if (reading.isPending) {
    return <ListWaiting />;
  }

  if (reading.data.readings === 0) {
    return <ListEmpty icon={BookOpenText} title={t('empty.title')} body={t('empty.body')} />;
  }

  // Every figure on this card is taken over the readings a browser reported progress for. With
  // none of them, the card has nothing to say and says so — rather than drawing a row of noughts,
  // which reads as an audience that did nothing instead of one nobody was watching.
  if (reading.data.measured === 0) {
    return <Unmeasured onShowCode={onShowCode} />;
  }

  return (
    <Card className="flex flex-col gap-5 p-5 sm:p-6">
      <header className="flex flex-wrap items-center justify-between gap-x-4 gap-y-3">
        <div className="flex flex-col gap-0.5">
          <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>
          <p className="text-sm text-foreground-muted tabular-nums">
            {reading.data.measured === reading.data.readings
              ? t('coverageAll', { readings: reading.data.readings })
              : t('coverage', {
                  measured: reading.data.measured,
                  readings: reading.data.readings,
                })}
          </p>
        </div>
        <ListSwitch
          label={t('view.label')}
          options={[
            { value: 'overall', label: t('view.overall') },
            { value: 'attention', label: t('view.attention') },
            { value: 'depth', label: t('view.depth') },
          ]}
          value={view}
          onChange={show}
        />
      </header>

      {view === 'overall' ? (
        <Overall reading={reading.data} />
      ) : (
        <ReadingList
          pages={pages.data?.pages}
          failure={pages.error}
          ranking={ranking}
          offset={offset}
          busy={pages.isFetching}
          totalPages={pages.data?.totalPages ?? 0}
          longest={pages.data?.longestMedianEngagedMs ?? 0}
          onMove={setOffset}
        />
      )}
    </Card>
  );
}

interface OverallProps {
  readonly reading: Engagement;
}

/** What a period's readings came to, across the whole website. */
function Overall({ reading }: OverallProps) {
  const t = useTranslations('dashboard.reading');
  const duration = useTranslations('dashboard.duration');
  const format = useFormatter();

  return (
    <>
      <div className="grid gap-4 sm:grid-cols-2">
        <Figure
          label={t('typical.label')}
          value={writeDuration(reading.medianEngagedMs, duration)}
          note={t('typical.note')}
        />
        <Figure
          label={t('interacted.label')}
          value={format.number(shareOf(reading.interacted, reading.measured), {
            style: 'percent',
            maximumFractionDigits: 0,
          })}
          note={t('interacted.note')}
        />
      </div>

      <section className="flex flex-col gap-3">
        <h3 className="text-sm font-medium text-foreground">{t('depth.label')}</h3>

        <div
          aria-hidden
          className="flex h-2.5 w-full gap-0.5 overflow-hidden rounded-full bg-surface-muted"
        >
          {DEPTH_BANDS.map((band) => (
            <span
              key={band.key}
              className={band.fill}
              style={{ width: sliver(reading.depths[band.key], reading.measured) }}
            />
          ))}
        </div>

        {/*
          A legend rather than a list, laid out to track the bar above it. Four rows stretched
          across a full-width card leave most of it empty, and the bands are a division of one
          whole rather than a ranking somebody reads down.
        */}
        <ul className="grid gap-x-6 gap-y-3 sm:grid-cols-2 xl:grid-cols-4">
          {DEPTH_BANDS.map((band) => (
            <li key={band.key} className="flex flex-col gap-1 border-t border-border pt-2.5">
              <span className="flex items-center gap-2">
                <span aria-hidden className={`size-2.5 shrink-0 rounded-full ${band.fill}`} />
                <span className="text-sm text-foreground-muted">{t(`depth.${band.key}`)}</span>
              </span>
              <span className="flex items-baseline gap-2">
                <span className="text-lg font-semibold text-foreground tabular-nums">
                  {format.number(shareOf(reading.depths[band.key], reading.measured), {
                    style: 'percent',
                    maximumFractionDigits: 0,
                  })}
                </span>
                <span className="text-sm text-foreground-muted tabular-nums">
                  {t('reads', { count: reading.depths[band.key] })}
                </span>
              </span>
            </li>
          ))}
        </ul>
      </section>
    </>
  );
}

interface FigureProps {
  readonly label: string;
  readonly value: string;
  readonly note: string;
}

/** One headline figure, with the one qualifier it genuinely needs. */
function Figure({ label, value, note }: FigureProps) {
  return (
    <div className="flex flex-col gap-1 rounded-md border border-border bg-surface-muted px-4 py-3">
      <p className="text-sm text-foreground-muted">{label}</p>
      <p className="text-2xl font-semibold text-foreground tabular-nums">{value}</p>
      <p className="text-xs text-foreground-subtle">{note}</p>
    </div>
  );
}

interface ReadingListProps {
  /** The slice on screen, or nothing while the first one is being read. */
  readonly pages: readonly PageEngagementRow[] | undefined;
  /** Why the list could not be read, where it could not be. */
  readonly failure: Error | null;
  readonly ranking: EngagementRanking;
  /** How many pages the period holds altogether. */
  readonly totalPages: number;
  /** The longest a page held anybody, which every bar is drawn against. */
  readonly longest: number;
  /** How many pages were passed over to reach this slice. */
  readonly offset: number;
  /** Whether the next slice is still being read. */
  readonly busy: boolean;
  readonly onMove: (offset: number) => void;
}

/**
 * A period's pages, ranked by how they were read.
 *
 * Both figures are on every row whichever way the list is ordered: the one it was ordered by ends
 * the row, and the other sits with the count beside it. A page that held somebody for four minutes
 * on the strength of a single reading is a different fact from one that did it four hundred times,
 * so the count is never left off.
 */
function ReadingList({
  pages,
  failure,
  ranking,
  totalPages,
  longest,
  offset,
  busy,
  onMove,
}: ReadingListProps) {
  const t = useTranslations('dashboard.reading');
  const duration = useTranslations('dashboard.duration');

  if (failure) {
    return <FailureNotice error={failure} />;
  }

  if (!pages) {
    return <div className="h-40 animate-pulse rounded-md bg-surface-muted" />;
  }

  return (
    <>
      <ul className="flex flex-col gap-0.5">
        {pages.map((page) => {
          // Written by whoever asked for the page, so it is shown as text and never followed.
          const address = readablePath(page.path);
          const byTime = ranking === 'attention';

          return (
            <RankedRow
              key={page.path}
              name={address}
              hint={address}
              detail={
                <>
                  {t('reads', { count: page.readings })}
                  <span aria-hidden> · </span>
                  {byTime
                    ? t('down', { percent: page.medianDepthPercent })
                    : writeDuration(page.medianEngagedMs, duration)}
                </>
              }
              part={byTime ? page.medianEngagedMs : page.medianDepthPercent}
              most={byTime ? longest : ALL_THE_WAY}
              figure={
                byTime
                  ? writeDuration(page.medianEngagedMs, duration)
                  : t('down', { percent: page.medianDepthPercent })
              }
            />
          );
        })}
      </ul>

      <RankedNav
        label={t('nav.label')}
        offset={offset}
        shown={pages.length}
        total={totalPages}
        step={PER_PAGE}
        busy={busy}
        onMove={onMove}
      />
    </>
  );
}

interface UnmeasuredProps {
  readonly onShowCode: () => void;
}

/**
 * What the card shows when the period had traffic but none of it could be measured.
 *
 * A designed state rather than a row of noughts. It is what a website reported only by its own
 * server looks like, and what a website looks like before its owner has pasted the tracking code
 * in — so it carries the one thing that would change it.
 */
function Unmeasured({ onShowCode }: UnmeasuredProps) {
  const t = useTranslations('dashboard.reading');

  return (
    <Card className="flex flex-col items-center gap-2 px-6 py-14 text-center">
      <span
        aria-hidden
        className="mb-2 flex size-12 items-center justify-center rounded-full bg-accent-soft"
      >
        <BookOpenText className="size-5 text-accent-strong" />
      </span>
      <h2 className="text-base font-semibold text-foreground">{t('unmeasured.title')}</h2>
      <p className="max-w-md text-sm text-foreground-muted">{t('unmeasured.body')}</p>
      <Button className="mt-4" tone="secondary" size="sm" onClick={onShowCode}>
        {t('unmeasured.action')}
      </Button>
    </Card>
  );
}

/** A length of time, written the way the reader's language writes one. */
function writeDuration(ms: number, t: ReturnType<typeof useTranslations<'dashboard.duration'>>) {
  const { minutes, seconds } = splitDuration(ms);

  return minutes > 0 ? t('long', { minutes, seconds }) : t('short', { seconds });
}

/** A band's share of the bar, kept off the scale's edge so a sliver is still visible. */
function sliver(part: number, whole: number): string {
  return `${Math.max(shareOf(part, whole) * 100, 1.5)}%`;
}
