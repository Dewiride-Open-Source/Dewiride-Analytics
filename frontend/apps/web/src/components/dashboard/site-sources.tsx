'use client';

import { Share2 } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useState } from 'react';
import {
  ListEmpty,
  ListSwitch,
  ListWaiting,
  RankedNav,
  RankedRow,
} from '@/components/dashboard/ranked-list';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import type { AnalyticsWindow } from '@/lib/analytics/period';
import type { SiteSource, SourceGrouping, SourceKind } from '@/lib/api/schemas';
import { useSources } from '@/lib/queries/sites';

interface SiteSourcesProps {
  readonly siteId: string;
  readonly window: AnalyticsWindow;
}

/** How many sources are shown at once, matching the lists around it. */
const PER_PAGE = 10;

/**
 * Where a website's visitors came from, over a period.
 *
 * One list read three ways rather than three cards, and they answer the same question at widening
 * detail. It opens on the overall shape — how much of an audience search brings, how much arrives
 * by nothing that named itself — because a list of website addresses answers that only for a
 * reader who already knows which of the names are search engines. Sites narrows it to who, and
 * pages to which article. Switching returns to the top, since a position in one list means
 * nothing in another.
 *
 * The screen above gives this a key that changes with the website and the period, so choosing
 * either starts a fresh list rather than leaving somebody on the fourth screenful of a list that
 * no longer exists.
 */
export function SiteSources({ siteId, window }: SiteSourcesProps) {
  const t = useTranslations('dashboard.sources');
  const [grouping, setGrouping] = useState<SourceGrouping>('kind');
  const [offset, setOffset] = useState(0);
  const sources = useSources(siteId, window, grouping, PER_PAGE, offset);

  function regroup(next: SourceGrouping) {
    setGrouping(next);
    setOffset(0);
  }

  if (sources.isError) {
    return <FailureNotice error={sources.error} />;
  }

  if (sources.isPending) {
    return <ListWaiting />;
  }

  if (sources.data.totalSources === 0) {
    return <ListEmpty icon={Share2} title={t('empty.title')} body={t('empty.body')} />;
  }

  return (
    <SourceList
      sources={sources.data.sources}
      visitors={sources.data.visitors}
      totalSources={sources.data.totalSources}
      mostVisitors={sources.data.mostVisitors}
      grouping={grouping}
      offset={offset}
      busy={sources.isFetching}
      onRegroup={regroup}
      onMove={setOffset}
    />
  );
}

interface SourceListProps {
  /** The slice on screen, busiest first, as the engine returned it. */
  readonly sources: readonly SiteSource[];
  /** Visitors across the whole period, which every share is taken against. */
  readonly visitors: number;
  /** How many sources the period holds altogether. */
  readonly totalSources: number;
  /** Visitors from the busiest source, which every bar is drawn against. */
  readonly mostVisitors: number;
  readonly grouping: SourceGrouping;
  /** How many sources were passed over to reach this slice. */
  readonly offset: number;
  /** Whether the next slice is still being read. */
  readonly busy: boolean;
  readonly onRegroup: (grouping: SourceGrouping) => void;
  readonly onMove: (offset: number) => void;
}

function SourceList({
  sources,
  visitors,
  totalSources,
  mostVisitors,
  grouping,
  offset,
  busy,
  onRegroup,
  onMove,
}: SourceListProps) {
  const t = useTranslations('dashboard.sources');

  return (
    <Card className="flex flex-col gap-4 p-5 sm:p-6">
      <header className="flex flex-wrap items-center justify-between gap-x-4 gap-y-3">
        <div className="flex flex-col gap-0.5">
          <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>
          <p className="text-sm text-foreground-muted tabular-nums">
            {t('total', { count: visitors })}
          </p>
        </div>
        <ListSwitch
          label={t('grouping.label')}
          options={[
            { value: 'kind', label: t('grouping.kind') },
            { value: 'site', label: t('grouping.site') },
            { value: 'page', label: t('grouping.page') },
          ]}
          value={grouping}
          onChange={onRegroup}
        />
      </header>

      <ul className="flex flex-col gap-0.5">
        {sources.map((source) => (
          <RankedRow
            key={source.source === '' ? 'direct' : source.source}
            name={<SourceName source={source} grouping={grouping} />}
            detail={
              <>
                {t('visitors', { count: source.visitors })}
                <span aria-hidden> · </span>
                {t('views', { count: source.pageViews })}
              </>
            }
            part={source.visitors}
            whole={visitors}
            most={mostVisitors}
          />
        ))}
      </ul>

      {/*
        Shown only when it is the answer rather than a footnote to it. A site whose visitors nearly
        all arrive naming nowhere reads as a site nothing links to, and without this the screen
        leaves that looking like a finding rather than what it usually is — people arriving by ways
        that do not carry a link.
      */}
      {mostlyDirect(sources, visitors) ? (
        <p className="rounded-md bg-surface-muted px-3 py-2 text-sm text-foreground-muted">
          {t('mostlyDirect')}
        </p>
      ) : null}

      <RankedNav
        label={t('nav.label')}
        offset={offset}
        shown={sources.length}
        total={totalSources}
        step={PER_PAGE}
        busy={busy}
        onMove={onMove}
      />
    </Card>
  );
}

interface SourceNameProps {
  readonly source: SiteSource;
  readonly grouping: SourceGrouping;
}

/**
 * What one row is called.
 *
 * Written as text and never as a link. The address comes from whoever visited the site, so a
 * clickable one would put a stranger's destination a mis-click away from the person reading their
 * own numbers.
 *
 * A page row is shown as its site with the rest of the address quieter beside it, so a screenful
 * of pages from one place still reads as a list of pages rather than of near-identical addresses.
 *
 * On the overall list the row is one of a closed set of kinds, and the engine's word for each is a
 * wire format rather than something to put in front of a reader — so it is looked up in the
 * message catalogue and shown in the reader's own language.
 */
function SourceName({ source, grouping }: SourceNameProps) {
  const t = useTranslations('dashboard.sources');
  const kinds = useTranslations('dashboard.sources.kind');

  if (source.source === '') {
    return <span className="text-foreground-muted">{t('direct')}</span>;
  }

  if (grouping === 'kind') {
    // A kind this screen has no words for falls back to the same answer the engine gives anything
    // it does not recognise, which is the only honest one available: it came from a link
    // somewhere. Naming it as anything more particular would be a claim nothing supports.
    return kinds(isKnownKind(source.source) ? source.source : 'link');
  }

  const rest = source.source.slice(source.site.length);

  return (
    <bdi>
      {source.site}
      {rest === '' ? null : <span className="text-foreground-muted">{rest}</span>}
    </bdi>
  );
}

/** The kinds this screen has words for, so an unrecognised one cannot reach a reader raw. */
const KINDS: readonly SourceKind[] = ['search', 'assistant', 'social', 'link'];

function isKnownKind(value: string): value is SourceKind {
  return (KINDS as readonly string[]).includes(value);
}

/**
 * Whether most of a period's visitors arrived naming nowhere at all.
 *
 * Only the slice on screen is examined, and that is sufficient: sources are ordered by how many
 * visitors they sent, so one holding more than half of them is always the first row.
 */
function mostlyDirect(sources: readonly SiteSource[], visitors: number): boolean {
  const direct = sources.find((source) => source.source === '');

  return direct !== undefined && direct.visitors * 2 > visitors;
}
