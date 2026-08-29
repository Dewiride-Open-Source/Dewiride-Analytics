'use client';

import { ChevronRight } from 'lucide-react';
import { useLocale, useTranslations } from 'next-intl';
import { useMemo } from 'react';
import { PlaceCredit, RoutingCredit } from '@/components/dashboard/place-credit';
import { QUIET_PILL, VerdictBadge } from '@/components/dashboard/verdict-badge';
import { VisitEvidence } from '@/components/dashboard/visit-evidence';
import { readWords, VisitTrail } from '@/components/dashboard/visit-trail';
import { Card } from '@/components/ui/card';
import { activeNow, type LiveRow, minutesSince } from '@/lib/analytics/live';
import { readablePath } from '@/lib/analytics/pages';
import { countryNames } from '@/lib/analytics/places';
import { placeOf, readOn } from '@/lib/analytics/visit-context';
import type { LiveVisitor, VisitContext } from '@/lib/api/schemas';
import { useLiveTrail } from '@/lib/queries/sites';
import { cn } from '@/lib/styling';

interface LiveVisitorsProps {
  readonly siteId: string;
  /** The rows to draw, in the order the engine gave them. */
  readonly rows: readonly LiveRow[];
  /** How many visitors the half hour holds altogether, which may be more than were carried. */
  readonly visitorsSeen: number;
  /** When the reading was taken, so every "how long ago" is measured against one clock. */
  readonly at: string;
  /** The site's own zone, so a step is stamped with the time it happened where the site is. */
  readonly timeZoneId: string;
  /** Which rows the reader has open. */
  readonly opened: ReadonlySet<string>;
  readonly onOpen: (visitor: string, open: boolean) => void;
}

/**
 * Everybody who has been on the website in the last half hour, most recently active first.
 *
 * Each row can be opened onto the pages that visitor has been through, for the same reason every
 * verdict on the journeys screen can be taken apart: a product whose proposition is that a
 * conclusion can be explained has to explain the ones it is drawing while somebody watches.
 *
 * Most rows carry no conclusion, and that is the honest state rather than a missing one. A visitor
 * is named here only where the naming rests on something they cannot take back — a company that
 * vouches for the address, a crawler that says what it is, a request for a page only an intruder
 * asks for. Everybody else is counted, listed, and answered on the journeys screen once their visit
 * has finished.
 */
export function LiveVisitors({
  siteId,
  rows,
  visitorsSeen,
  at,
  timeZoneId,
  opened,
  onOpen,
}: LiveVisitorsProps) {
  const t = useTranslations('live.list');
  const locale = useLocale();
  const here = rows.filter((row) => row.stillHere).length;
  const placed = rows.some((row) => row.visitor.context.countryCode !== '');
  const networked = rows.some((row) => row.visitor.context.network !== '');

  // Built once for the whole list rather than once per row. Building one of these costs enough to
  // notice down a list, which is precisely what this is.
  const countryName = useMemo(() => countryNames(locale), [locale]);

  return (
    <Card className="flex flex-col gap-3 p-5 sm:p-6">
      <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>

      <div className="flex flex-col">
        {rows.map((row) => (
          <VisitorRow
            key={row.visitor.visitor}
            siteId={siteId}
            row={row}
            at={at}
            countryName={countryName}
            timeZoneId={timeZoneId}
            open={opened.has(row.visitor.visitor)}
            onOpen={onOpen}
          />
        ))}
      </div>

      {/*
        Said only where the list is actually short of the count beside it. A sweep can put hundreds
        of visitors on a site in ten minutes, and the figure on the card above has to stay the true
        one — so this is where the list admits it is not all of them.
      */}
      {visitorsSeen > here ? (
        <p className="text-xs text-foreground-subtle tabular-nums">
          {t('capped', { count: here })}
        </p>
      ) : null}

      {/*
        The licences behind a town and behind a network each ask for a link back wherever their
        results appear, and each goes only where its own results are.
      */}
      {placed || networked ? (
        <div className="flex flex-col gap-1 border-t border-border pt-3">
          {placed ? <PlaceCredit /> : null}
          {networked ? <RoutingCredit /> : null}
        </div>
      ) : null}
    </Card>
  );
}

interface VisitorRowProps {
  readonly siteId: string;
  readonly row: LiveRow;
  readonly at: string;
  /** Writes a country code out in the reader's language, built once for the whole list. */
  readonly countryName: (code: string) => string | null;
  readonly timeZoneId: string;
  readonly open: boolean;
  readonly onOpen: (visitor: string, open: boolean) => void;
}

/**
 * One visitor, and what they have been doing.
 *
 * Kept shut until somebody asks for it. A screenful is a great many visitors, and reading every
 * trail nobody has opened would be that many questions of the store on every beat.
 */
function VisitorRow({ siteId, row, at, countryName, timeZoneId, open, onOpen }: VisitorRowProps) {
  const t = useTranslations('live.list');
  const strengths = useTranslations('verdicts.strength');
  const evidence = useTranslations('verdicts.evidence');
  const trail = useLiveTrail(siteId, row.visitor.visitor, open, row.stillHere);

  // Written by whoever asked for the page, so it is shown as text and never followed. Whether it
  // is described in the present depends on how long ago they were last heard from: a visitor still
  // counted by the half hour may have closed the tab twenty minutes ago.
  const address = readablePath(row.visitor.currentPath);
  const present = row.stillHere && activeNow(at, row.visitor.lastSeen);

  return (
    <details
      open={open}
      className="group border-t border-border first:border-t-0"
      onToggle={(event) => onOpen(row.visitor.visitor, event.currentTarget.open)}
    >
      <summary
        className={cn(
          'flex cursor-pointer list-none items-center gap-3 py-3',
          '[&::-webkit-details-marker]:hidden',
        )}
      >
        <ChevronRight
          aria-hidden
          className="size-4 shrink-0 text-foreground-subtle transition-transform group-open:rotate-90"
        />

        <span className="flex min-w-0 flex-1 flex-col gap-1">
          <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <Naming visitor={row.visitor} />
            {row.visitor.strength === null ? null : (
              <span className="text-xs text-foreground-muted">
                {strengths(row.visitor.strength)}
              </span>
            )}
            {row.stillHere ? null : (
              <span className="text-xs text-foreground-subtle">{t('gone')}</span>
            )}
          </span>

          <bdi className="truncate text-sm text-foreground-muted" title={address}>
            {present ? t('reading', { path: address }) : t('lastOn', { path: address })}
          </bdi>

          {/*
            Only while the row is shut. Opening it lays the same two facts out in full, with who
            sent the visitor and whose network they came over beside them, an inch below — and a
            screen that says "Leeds, United Kingdom" twice in two lines reads as a mistake.
          */}
          {open ? null : <Whereabouts context={row.visitor.context} countryName={countryName} />}
        </span>

        {/*
          Stacked on a phone and side by side from a tablet up. Neither figure is dropped at the
          small size: how many pages somebody has been through and how long ago they were last
          heard from are the two things a reader scans this list for.
        */}
        <span className="flex shrink-0 flex-col items-end gap-0.5 text-xs tabular-nums sm:flex-row sm:items-center sm:gap-3 sm:text-sm">
          <span className="text-foreground-muted">
            {t('pages', { count: row.visitor.pageCount })}
          </span>
          <time dateTime={row.visitor.lastSeen} className="text-foreground-subtle">
            {t('active', { minutes: minutesSince(at, row.visitor.lastSeen) })}
          </time>
        </span>
      </summary>

      <div className="flex flex-col gap-4 pb-4 pl-7">
        <VisitTrail
          context={row.visitor.context}
          steps={trail.data?.steps}
          failed={trail.isError}
          pageCount={row.visitor.pageCount}
          timeZoneId={timeZoneId}
        />

        {row.visitor.category === null ? null : (
          <>
            <VisitEvidence title={evidence('supporting')} reasons={row.visitor.supporting} />

            {row.visitor.contradicting.length > 0 ? (
              <VisitEvidence
                title={evidence('contradicting')}
                reasons={row.visitor.contradicting}
              />
            ) : null}
          </>
        )}
      </div>
    </details>
  );
}

/**
 * Roughly where a visitor is and what they are reading on, on the row itself.
 *
 * Two rows saying "Still watching" are the same row to a reader until one of them says Leeds and a
 * phone and the other says a hosting company in Amsterdam. That is the difference somebody scans a
 * live list for, and asking them to open every row to find it is asking them to open every row.
 *
 * Left out entirely where nothing established either, rather than written as two absences. The
 * whole set of facts, including who sent them and whose network they arrived over, is under the
 * row when it is opened.
 */
function Whereabouts({
  context,
  countryName,
}: {
  readonly context: VisitContext;
  readonly countryName: (code: string) => string | null;
}) {
  const t = useTranslations('dashboard.journey.about');

  const place = placeOf(context, countryName(context.countryCode));
  const read = readOn(context);

  if (place === null && read === null) {
    return null;
  }

  return (
    <span className="flex flex-wrap items-center gap-x-1.5 text-xs text-foreground-subtle">
      {place === null ? null : <bdi>{place}</bdi>}

      {/*
        The separator travels with the words it introduces rather than standing between them as a
        piece of its own, so that a narrow screen wrapping the two facts apart does not leave a
        stray middot hanging off the end of the first line.
      */}
      {read === null ? null : (
        <span className="flex items-center gap-x-1.5">
          {place === null ? null : <span aria-hidden>·</span>}
          <bdi>{readWords(read, t)}</bdi>
        </span>
      )}
    </span>
  );
}

/**
 * What this visitor is, or that nothing has been settled about them yet.
 *
 * The unnamed marker is drawn exactly as the badge for a category nothing could be said about,
 * because it means the same thing to a reader and should not read as a different kind of answer.
 * It says what is true — somebody is watching — rather than guessing to fill the column.
 */
function Naming({ visitor }: { readonly visitor: LiveVisitor }) {
  const t = useTranslations('live.list');

  if (visitor.category !== null) {
    return <VerdictBadge category={visitor.category} />;
  }

  return <span className={QUIET_PILL}>{t('unnamed')}</span>;
}
