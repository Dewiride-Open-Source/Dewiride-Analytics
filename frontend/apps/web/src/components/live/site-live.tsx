'use client';

import { Pause, Play, Radio } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useState } from 'react';
import { ListEmpty } from '@/components/dashboard/ranked-list';
import { LiveActivity } from '@/components/live/live-activity';
import { LiveHeadline } from '@/components/live/live-headline';
import { LivePages } from '@/components/live/live-pages';
import { LiveVisitors } from '@/components/live/live-visitors';
import { Button, buttonStyle } from '@/components/ui/button';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Link } from '@/i18n/navigation';
import { type LiveRow, rowsToShow } from '@/lib/analytics/live';
import type { Live, Site } from '@/lib/api/schemas';
import { useLiveTraffic } from '@/lib/queries/sites';
import { DASHBOARD } from '@/lib/routes';
import { cn } from '@/lib/styling';

interface SiteLiveProps {
  readonly site: Site;
}

/**
 * One website, at this moment.
 *
 * The screen asks the same question over and over, so almost everything here is about what happens
 * between one answer and the next. The last answer stays on screen while the next is on its way and
 * stays there if it never comes; a tab nobody is looking at asks nothing; and a row somebody has
 * opened is held even after its visitor has gone, because a panel disappearing from under a reader
 * is the one failure a screen like this can actually cause.
 */
export function SiteLive({ site }: SiteLiveProps) {
  const t = useTranslations('live');
  const [watching, setWatching] = useState(true);
  const [opened, setOpened] = useState<ReadonlySet<string>>(() => new Set());
  const live = useLiveTraffic(site.id, watching);

  // The rows on screen are worked out once per reading rather than on every render, because they
  // are not a function of the reading alone: a row the reader has open outlives the visitor behind
  // it, and that can only be decided by comparing the reading that has arrived with the one that
  // is already drawn.
  const [shown, setShown] = useState<readonly LiveRow[]>([]);
  const [answered, setAnswered] = useState<Live | undefined>(undefined);

  if (live.data !== answered) {
    setAnswered(live.data);
    setShown(
      live.data === undefined ? [] : rowsToShow(live.data.visitors, shown, opened, live.data.at),
    );
  }

  function hold(visitor: string, open: boolean) {
    setOpened((held) => {
      const next = new Set(held);

      if (open) {
        next.add(visitor);
      } else {
        next.delete(visitor);
      }

      return next;
    });
  }

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="truncate text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
            {t('title')}
          </h1>
          <p className="max-w-2xl text-sm text-foreground-muted">
            {t('caption', { site: site.displayName })}
          </p>
        </div>

        {live.data === undefined ? null : (
          <Freshness
            at={live.data.at}
            watching={watching}
            outOfTouch={live.isError}
            timeZoneId={site.timeZoneId}
            onWatch={setWatching}
          />
        )}
      </header>

      <Reading
        site={site}
        answer={live.data}
        failure={live.data === undefined && live.isError ? live.error : null}
        rows={shown}
        opened={opened}
        onOpen={hold}
      />
    </div>
  );
}

/**
 * The summary of the half hour on one side and the detail of it on the other.
 *
 * The narrow side is narrow on purpose: the count and the pages being read are each a glance,
 * while the shape of the half hour and the list of who is here are both read across. Below that
 * width everything stacks in the order somebody asks it in — how many, what they are reading, what
 * the half hour looked like, and then who each of them is.
 *
 * Held while the first answer is on its way as well, so that the screen does not rearrange itself
 * around whoever is reading it the moment the answer lands.
 */
const COLUMNS = 'grid gap-6 xl:grid-cols-[22rem_minmax(0,1fr)] xl:items-start';

interface ReadingProps {
  readonly site: Site;
  /** The newest reading, or nothing while the first one is still on its way. */
  readonly answer: Live | undefined;
  /** Why there will never be one, where the very first attempt was refused. */
  readonly failure: unknown;
  readonly rows: readonly LiveRow[];
  readonly opened: ReadonlySet<string>;
  readonly onOpen: (visitor: string, open: boolean) => void;
}

/**
 * What the screen has to show, which is one of four things.
 *
 * A refusal only takes the screen over when there has never been an answer. Once a reading is on
 * screen it stays there through as many failed attempts as it takes, and the strip above says so
 * quietly: a red panel appearing and vanishing every ten seconds is worse than the fault it reports.
 */
function Reading({ site, answer, failure, rows, opened, onOpen }: ReadingProps) {
  const t = useTranslations('live.empty');

  if (failure !== null) {
    return <FailureNotice error={failure} />;
  }

  if (answer === undefined) {
    return <LiveShapes />;
  }

  // A designed state rather than a headline reading nought above a list with nothing in it. The
  // figure and the empty card would be saying the same thing twice, and only one of them carries
  // the reason and the way out of it. Keyed on the count rather than the list, because the count
  // is the whole and the list is what fitted; and a row somebody is holding open keeps the screen.
  if (answer.visitorsSeen === 0 && rows.length === 0) {
    return (
      <ListEmpty
        icon={Radio}
        title={t('title')}
        body={t('body', { site: site.domain })}
        action={
          <Link href={DASHBOARD} className={cn(buttonStyle({ tone: 'secondary', size: 'sm' }))}>
            {t('action')}
          </Link>
        }
      />
    );
  }

  return (
    <div className={COLUMNS}>
      <div className="flex flex-col gap-6">
        <LiveHeadline answer={answer} />

        <LivePages pages={answer.pages} visitorsSeen={answer.visitorsSeen} />
      </div>

      <div className="flex flex-col gap-6">
        <LiveActivity
          minutes={answer.minutes}
          at={answer.at}
          timeZoneId={site.timeZoneId}
          siteName={site.displayName}
        />

        <LiveVisitors
          siteId={site.id}
          rows={rows}
          visitorsSeen={answer.visitorsSeen}
          at={answer.at}
          timeZoneId={site.timeZoneId}
          opened={opened}
          onOpen={onOpen}
        />
      </div>
    </div>
  );
}

interface FreshnessProps {
  /** When the reading on screen was taken, by the engine's clock. */
  readonly at: string;
  readonly watching: boolean;
  /** Whether the last attempt to renew it failed while an earlier one was still on screen. */
  readonly outOfTouch: boolean;
  readonly timeZoneId: string;
  readonly onWatch: (watching: boolean) => void;
}

/**
 * How old what is on screen is, and the way to stop it moving.
 *
 * It states the moment the reading was taken rather than counting seconds since, because a counter
 * would be a second thing on the screen changing every second and would say nothing the stamp does
 * not. Holding the screen still is offered because reading a list that rearranges itself under you
 * is the one thing a live screen makes harder than a static one.
 */
function Freshness({ at, watching, outOfTouch, timeZoneId, onWatch }: FreshnessProps) {
  const t = useTranslations('live.refresh');
  const format = useFormatter();

  const time = format.dateTime(new Date(at), {
    timeZone: timeZoneId,
    hour: 'numeric',
    minute: '2-digit',
    second: '2-digit',
  });

  const running = watching && !outOfTouch;

  return (
    <div className="flex items-center gap-3">
      <p
        className={cn(
          'flex items-center gap-2 text-xs tabular-nums',
          outOfTouch ? 'text-danger' : 'text-foreground-muted',
        )}
      >
        <span
          aria-hidden
          className={cn(
            'size-2 shrink-0 rounded-full',
            outOfTouch ? 'bg-danger' : 'bg-positive',
            running ? 'animate-pulse' : 'opacity-60',
          )}
        />
        {t(freshnessWord(watching, outOfTouch), { time })}
      </p>

      <Button tone="secondary" size="sm" onClick={() => onWatch(!watching)}>
        {watching ? (
          <Pause aria-hidden className="size-4" />
        ) : (
          <Play aria-hidden className="size-4" />
        )}
        {watching ? t('pause') : t('resume')}
      </Button>
    </div>
  );
}

/**
 * The shape of the screen, held while its first reading is on its way.
 *
 * Shared with the frame around it, which draws the same shape under a heading that is not there
 * yet, so that a screen opened cold settles into itself once rather than twice.
 */
export function LiveShapes() {
  return (
    <div className={COLUMNS}>
      <div className="flex flex-col gap-6">
        <Shape height="h-44" />
        <Shape height="h-56" />
      </div>

      <div className="flex flex-col gap-6">
        <Shape height="h-96" />
        <Shape height="h-64" />
      </div>
    </div>
  );
}

/** One card's worth of nothing, held at roughly the height the card will be. */
function Shape({ height }: { readonly height: string }) {
  return (
    <div className={cn(height, 'animate-pulse rounded-lg border border-border bg-surface-muted')} />
  );
}

/**
 * Which of the three things the strip has to say.
 *
 * Being out of touch outranks the other two: a screen that has stopped hearing from the engine is
 * out of touch whether or not the reader had also stopped it, and saying it is paused would be
 * telling them the older news.
 */
function freshnessWord(watching: boolean, outOfTouch: boolean): 'outOfTouch' | 'live' | 'held' {
  if (outOfTouch) {
    return 'outOfTouch';
  }

  return watching ? 'live' : 'held';
}
