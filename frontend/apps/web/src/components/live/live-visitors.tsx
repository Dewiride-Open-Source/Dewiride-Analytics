'use client';

import { ChevronRight } from 'lucide-react';
import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useMemo, useRef } from 'react';
import { ListCredits } from '@/components/dashboard/place-credit';
import { RowFact } from '@/components/dashboard/row-fact';
import { QUIET_PILL, VerdictBadge } from '@/components/dashboard/verdict-badge';
import { VisitEvidence } from '@/components/dashboard/visit-evidence';
import { fromWords, VisitTrail } from '@/components/dashboard/visit-trail';
import { Whereabouts } from '@/components/dashboard/whereabouts';
import { useChoiceWords } from '@/components/live/live-headline';
import { Card } from '@/components/ui/card';
import { ChoicePill } from '@/components/ui/chip';
import {
  activeNow,
  cameHereFrom,
  keptTo,
  type ListFoot,
  type LiveNarrowing,
  type LiveRow,
  listFoot,
  minutesSince,
  sameNarrowing,
} from '@/lib/analytics/live';
import { readablePath } from '@/lib/analytics/pages';
import { countryNames } from '@/lib/analytics/places';
import { namedAs } from '@/lib/analytics/verdicts';
import type { Live, LiveVisitor } from '@/lib/api/schemas';
import { useLiveTrail } from '@/lib/queries/sites';
import { cn } from '@/lib/styling';

interface LiveVisitorsProps {
  readonly siteId: string;
  /**
   * The reading the rows were drawn from: when it was taken, so every "how long ago" is measured
   * against one clock, and how many visitors it holds beyond the ones it carried.
   */
  readonly answer: Live;
  /** The rows to draw, in the order the engine gave them. */
  readonly rows: readonly LiveRow[];
  /** What the list is kept to. */
  readonly narrowing: LiveNarrowing;
  readonly onNarrow: (narrowing: LiveNarrowing) => void;
  /** The site's own zone, so a step is stamped with the time it happened where the site is. */
  readonly timeZoneId: string;
  /** Which rows the reader has open. */
  readonly opened: ReadonlySet<string>;
  readonly onOpen: (visitor: string, open: boolean) => void;
}

/**
 * Everybody who opened a page on the website in the last half hour, most recently active first.
 *
 * Opened one, rather than merely reported: a page left on a screen goes on reporting for as long as
 * it stays there, and the engine leaves a visitor whose only news is that off this list.
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
 *
 * A chip pressed on the card the screen leads with keeps the list to one kind of visitor, and the
 * pill beside the heading says which and shows everybody again. Rows keep the engine's order
 * whatever the list is kept to, and a row somebody is holding open after its visitor left stays
 * while it is one of them.
 */
export function LiveVisitors({
  siteId,
  answer,
  rows,
  narrowing,
  onNarrow,
  timeZoneId,
  opened,
  onOpen,
}: LiveVisitorsProps) {
  const t = useTranslations('live.list');
  const locale = useLocale();
  const words = useChoiceWords();
  const heading = useRef<HTMLHeadingElement>(null);
  const status = useRef<HTMLParagraphElement>(null);
  const narrowedBefore = useRef(narrowing);
  const kept = rows.filter((row) => keptTo(row.visitor, narrowing));

  // Once what the list is kept to changes, two things happen, and only then — never on a reading,
  // so a busy half hour does not read itself aloud every ten seconds, and a screen opening on what
  // its address names moves nothing and says nothing.
  //
  // What the list now shows is said to anybody listening, since a chip says only that it is
  // pressed. And a list kept to something new is brought into sight wherever it is not already: the
  // chips are on the card the screen leads with, which on anything narrower than a wide screen
  // stands three cards above this one, so the list would change out of sight and the press would
  // look as though it had done nothing. The keyboard goes with it, rather than being left on a chip
  // scrolled off the screen.
  useEffect(() => {
    const before = narrowedBefore.current;
    narrowedBefore.current = narrowing;

    if (sameNarrowing(before, narrowing)) {
      return;
    }

    if (status.current !== null) {
      status.current.textContent = saying(t, listFoot(answer, rows, narrowing), rows, narrowing);
    }

    if (narrowing !== null) {
      bringIntoSight(heading.current);
    }
  }, [narrowing, answer, rows, t]);

  const placed = kept.some((row) => row.visitor.context.countryCode !== '');
  const networked = kept.some((row) => row.visitor.context.network !== '');

  // Built once for the whole list rather than once per row. Building one of these costs enough to
  // notice down a list, which is precisely what this is.
  const countryName = useMemo(() => countryNames(locale), [locale]);

  // The pill goes the moment it is pressed, so the keyboard is handed to the heading of the list it
  // has just widened rather than dropped at the top of the page.
  function showEveryone() {
    heading.current?.focus({ preventScroll: true });
    onNarrow(null);
  }

  return (
    <Card className="flex flex-col gap-3 p-5 sm:p-6">
      {/*
        The pill sits beside the heading while there is room and drops beneath it on a phone,
        where it gives up the end of its words before it would push the screen wider.
      */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <h2 ref={heading} tabIndex={-1} className="text-base font-semibold text-foreground">
          {t('title')}
        </h2>

        {narrowing === null ? null : (
          <ChoicePill
            of={t('only')}
            value={words(narrowing)}
            removeLabel={t('showEveryone', { choice: words(narrowing) })}
            onRemove={showEveryone}
          />
        )}
      </div>

      <p ref={status} role="status" className="sr-only" />

      {kept.length === 0 ? null : (
        <div className="flex flex-col">
          {kept.map((row) => (
            <VisitorRow
              key={row.visitor.visitor}
              siteId={siteId}
              row={row}
              at={answer.at}
              countryName={countryName}
              timeZoneId={timeZoneId}
              open={opened.has(row.visitor.visitor)}
              onOpen={onOpen}
            />
          ))}
        </div>
      )}

      <FootOfList foot={listFoot(answer, rows, narrowing)} />

      <ListCredits placed={placed} networked={networked} />
    </Card>
  );
}

type ListWords = ReturnType<typeof useTranslations<'live.list'>>;

/**
 * What the list shows once what it is kept to has changed, as said to anybody listening.
 *
 * The sentence the foot of the list says, where it says one; otherwise how many it shows, since a
 * list showing every one of them has no foot to read out.
 *
 * @param t The list's words.
 * @param foot What the foot of the list says.
 * @param rows The rows on screen, kept to nothing.
 * @param narrowing What the list is kept to now.
 */
function saying(
  t: ListWords,
  foot: ListFoot,
  rows: readonly LiveRow[],
  narrowing: LiveNarrowing,
): string {
  if (narrowing === null) {
    return t('showingEveryone');
  }

  switch (foot.says) {
    case 'nobody':
      return t('nobody');
    case 'notAmongLatest':
      return t('notAmongLatest', { carried: foot.carried });
    case 'latest':
      return t('capped', { count: foot.shown });
    case 'nothing':
      return t('showing', {
        count: rows.filter((row) => row.stillHere && keptTo(row.visitor, narrowing)).length,
      });
  }
}

/**
 * Brings the list's heading into sight, and the keyboard with it, unless it is in sight already.
 *
 * In sight means below the bar across the top of the screen as well as above the bottom edge: the
 * page keeps a margin the height of that bar at its top, and a heading under the bar is as hidden as
 * one off the screen.
 *
 * @param heading The heading of the list, which carries the pill saying what the list is kept to.
 */
function bringIntoSight(heading: HTMLHeadingElement | null): void {
  if (heading === null || inSight(heading)) {
    return;
  }

  heading.focus({ preventScroll: true });
  heading.scrollIntoView({ block: 'start' });
}

/** Whether the whole of an element is on screen, clear of the bar across the top of it. */
function inSight(element: HTMLElement): boolean {
  const { top, bottom } = element.getBoundingClientRect();
  const underTheBar = Number.parseFloat(
    getComputedStyle(document.documentElement).scrollPaddingTop,
  );

  return top >= (Number.isNaN(underTheBar) ? 0 : underTheBar) && bottom <= window.innerHeight;
}

/**
 * What the list admits about the visitors it does not show.
 *
 * Said only where the list is actually short of what it is kept to. A sweep can put hundreds of
 * visitors on a site in ten minutes, and the figures the screen leads with have to stay the true
 * ones — so this is where the list admits it is not all of them, that nobody like that is here, or
 * that the ones who are lie further back than one reading reaches.
 */
function FootOfList({ foot }: { readonly foot: ListFoot }) {
  const t = useTranslations('live.list');

  switch (foot.says) {
    case 'nothing':
      return null;
    case 'latest':
      return (
        <p className="text-xs text-foreground-subtle tabular-nums">
          {t('capped', { count: foot.shown })}
        </p>
      );
    case 'nobody':
      return <p className="py-2 text-sm text-foreground-muted">{t('nobody')}</p>;
    case 'notAmongLatest':
      return (
        <p className="py-2 text-sm text-foreground-muted tabular-nums">
          {t('notAmongLatest', { carried: foot.carried })}
        </p>
      );
  }
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
  const about = useTranslations('dashboard.journey.about');
  const strengths = useTranslations('verdicts.strength');
  const evidence = useTranslations('verdicts.evidence');
  const trail = useLiveTrail(siteId, row.visitor.visitor, row.at, open);

  // Written by whoever asked for the page, so it is shown as text and never followed. Whether it
  // is described in the present depends on how long ago they were last heard from: a visitor still
  // counted by the half hour may have closed the tab twenty minutes ago.
  const address = readablePath(row.visitor.currentPath);
  const present = row.stillHere && activeNow(at, row.visitor.lastSeen);
  const name = namedAs(row.visitor.supporting);
  const from = cameHereFrom(row.visitor);

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
            {name === null ? null : (
              <bdi className="text-xs font-medium text-foreground">{name}</bdi>
            )}
            {row.stillHere ? null : (
              <span className="text-xs text-foreground-subtle">{t('gone')}</span>
            )}
            {/*
              How the visitor arrived goes last and after a middot: placed before "Gone" it would
              read "From Google Gone", and run on from the band it would read "strong signs From
              Google".
            */}
            <RowFact>{fromWords(from, about)}</RowFact>
          </span>

          <bdi className="truncate text-sm text-foreground-muted" title={address}>
            {present ? t('reading', { path: address }) : t('lastOn', { path: address })}
          </bdi>

          {/*
            Only while the row is shut. Opening it lays the same two facts out in full an inch
            below, with whose network the visitor came over beside them, and a screen that says
            "Leeds, United Kingdom" twice in two lines reads as a mistake. The first line stays,
            because it is the row's heading — what the panel is opened under — rather than a
            summary of what the panel goes on to say.
          */}
          {open ? null : (
            <Whereabouts context={row.visitor.context} from={from} countryName={countryName} />
          )}
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
          from={from}
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
