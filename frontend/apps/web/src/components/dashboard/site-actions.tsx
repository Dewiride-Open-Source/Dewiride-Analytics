'use client';

import { MousePointerClick } from 'lucide-react';
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
import type { ActionGrouping, ControlKind, SiteAction } from '@/lib/api/schemas';
import type { AnalyticsWindow } from '@/lib/analytics/period';
import { useActions } from '@/lib/queries/sites';

interface SiteActionsProps {
  readonly siteId: string;
  readonly window: AnalyticsWindow;
}

/** How many rows are shown at once, matching the lists around it. */
const PER_PAGE = 10;

/**
 * What a website's visitors pressed, over a period.
 *
 * Two questions about the same presses: what people used, and where the presses that led off the
 * site led to. Where a press led on the site is deliberately absent — the pages themselves already
 * answer that, and carrying it here would rank the site against itself and bury the handful of
 * places it actually sends people.
 *
 * The screen above gives this a key that changes with the website and the period, so choosing
 * either starts afresh rather than leaving somebody on a screenful that no longer exists.
 */
export function SiteActions({ siteId, window }: SiteActionsProps) {
  const t = useTranslations('dashboard.presses');
  const [grouping, setGrouping] = useState<ActionGrouping>('control');
  const [offset, setOffset] = useState(0);
  const controls = useActions(siteId, window, 'control', PER_PAGE, 0, true);
  const shown = useActions(siteId, window, grouping, PER_PAGE, offset, true);

  // What the list on screen is counted against, which is not always every press: the presses
  // that led off the site are their own total, and a share taken against the larger one would
  // quietly mean something else.
  const counted = shown.data?.presses ?? controls.data?.presses ?? 0;

  function show(next: ActionGrouping) {
    setGrouping(next);
    setOffset(0);
  }

  if (controls.isError) {
    return <FailureNotice error={controls.error} />;
  }

  if (controls.isPending) {
    return <ListWaiting />;
  }

  if (controls.data.presses === 0) {
    return <ListEmpty icon={MousePointerClick} title={t('empty.title')} body={t('empty.body')} />;
  }

  return (
    <Card className="flex flex-col gap-4 p-5 sm:p-6">
      <header className="flex flex-wrap items-center justify-between gap-x-4 gap-y-3">
        <div className="flex flex-col gap-0.5">
          <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>
          {counted > 0 ? (
            <p className="text-sm text-foreground-muted tabular-nums">
              {t(`caption.${grouping}`, { count: counted })}
            </p>
          ) : null}
        </div>
        <ListSwitch
          label={t('view.label')}
          options={[
            { value: 'control', label: t('view.control') },
            { value: 'destination', label: t('view.destination') },
          ]}
          value={grouping}
          onChange={show}
        />
      </header>

      <PressList
        rows={shown.data?.controls}
        failure={shown.error}
        grouping={grouping}
        presses={shown.data?.presses ?? 0}
        totalControls={shown.data?.totalControls ?? 0}
        most={shown.data?.mostPresses ?? 0}
        offset={offset}
        busy={shown.isFetching}
        onMove={setOffset}
      />
    </Card>
  );
}

interface PressListProps {
  /** The slice on screen, or nothing while the first one is being read. */
  readonly rows: readonly SiteAction[] | undefined;
  /** Why the list could not be read, where it could not be. */
  readonly failure: Error | null;
  readonly grouping: ActionGrouping;
  /** Presses across the whole period at this grouping, which every share is taken against. */
  readonly presses: number;
  /** How many distinct rows the period holds, so a reader knows how far the list runs. */
  readonly totalControls: number;
  /** Presses on the most pressed row, which every bar is drawn against. */
  readonly most: number;
  /** How many rows were passed over to reach this slice. */
  readonly offset: number;
  /** Whether the next slice is still being read. */
  readonly busy: boolean;
  readonly onMove: (offset: number) => void;
}

/** One slice of what was pressed, most pressed first. */
function PressList({
  rows,
  failure,
  grouping,
  presses,
  totalControls,
  most,
  offset,
  busy,
  onMove,
}: PressListProps) {
  const t = useTranslations('dashboard.presses');

  if (failure) {
    return <FailureNotice error={failure} />;
  }

  if (!rows) {
    return <div className="h-64 animate-pulse rounded-md bg-surface-muted" />;
  }

  if (rows.length === 0 && offset === 0) {
    return (
      <p className="rounded-md border border-dashed border-border px-4 py-8 text-center text-sm text-foreground-muted">
        {t(`none.${grouping}`)}
      </p>
    );
  }

  return (
    <>
      <ul className="flex flex-col gap-0.5">
        {rows.map((row) => (
          <RankedRow
            key={`${row.control}:${row.name}`}
            name={<Named row={row} grouping={grouping} />}
            hint={row.name}
            detail={t('presses', { count: row.presses })}
            part={row.presses}
            whole={presses}
            most={most}
          />
        ))}
      </ul>

      <RankedNav
        label={t('nav.label')}
        offset={offset}
        shown={rows.length}
        total={totalControls}
        step={PER_PAGE}
        busy={busy}
        onMove={onMove}
      />
    </>
  );
}

/**
 * What one row is called.
 *
 * A name is written by whoever wrote the page, so it reaches the screen as text and nothing else —
 * never a link, and never markup. What sort of thing it was is said in a word beside it, drawn
 * from this dashboard's own catalogue rather than from anything the page said, so a site cannot
 * write its own labels onto somebody's screen. A site that gave its control no name at all gets a
 * row saying so, which is both honest and the prompt it needs to go and name the thing.
 */
function Named({ row, grouping }: { readonly row: SiteAction; readonly grouping: ActionGrouping }) {
  const t = useTranslations('dashboard.presses');

  if (grouping === 'destination') {
    return <bdi className="truncate">{row.name}</bdi>;
  }

  return (
    <span className="flex min-w-0 items-baseline gap-2">
      {row.name ? (
        <bdi className="truncate">{row.name}</bdi>
      ) : (
        <span className="truncate text-foreground-muted italic">{t('unnamed')}</span>
      )}
      <span className="shrink-0 text-xs text-foreground-subtle">{t(kindOf(row.control))}</span>
    </span>
  );
}

/**
 * Which word describes a kind of control.
 *
 * Looked up through a fixed map rather than by building a key from the answer, so a value this
 * dashboard has never seen reaches the catalogue as a word it holds instead of as a missing one.
 */
function kindOf(control: ControlKind): string {
  return CONTROL_WORDS[control] ?? CONTROL_WORDS.unknown;
}

const CONTROL_WORDS: Readonly<Record<ControlKind, string>> = {
  link: 'control.link',
  button: 'control.button',
  field: 'control.field',
  unknown: 'control.unknown',
};
