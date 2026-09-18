'use client';

import { CodeXml, KeyRound, ScanSearch, SlidersHorizontal, UserRound } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useMemo, useRef, useState } from 'react';
import { JudgedTraffic } from '@/components/dashboard/judged-traffic';
import { MetricCard, MetricCardSkeleton } from '@/components/dashboard/metric-card';
import { PeriodPicker } from '@/components/dashboard/period-picker';
import { PopulationSwitch } from '@/components/dashboard/population-switch';
import { ListEmpty, ListWaiting } from '@/components/dashboard/ranked-list';
import { ServerKeys } from '@/components/dashboard/server-keys';
import { SiteActions } from '@/components/dashboard/site-actions';
import { SiteDevices } from '@/components/dashboard/site-devices';
import { SiteFlow } from '@/components/dashboard/site-flow';
import { SiteLocations } from '@/components/dashboard/site-locations';
import { SitePages } from '@/components/dashboard/site-pages';
import { SiteReading } from '@/components/dashboard/site-reading';
import { SiteSettings } from '@/components/dashboard/site-settings';
import { SiteSources } from '@/components/dashboard/site-sources';
import { TrackingCode } from '@/components/dashboard/tracking-code';
import { type Activity, TrafficChart } from '@/components/dashboard/traffic-chart';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { changeBetween } from '@/lib/analytics/change';
import {
  crossesYears,
  daysIn,
  granularityFor,
  previousSpan,
  previousWindow,
  spanFor,
  spanInstants,
  windowFor,
} from '@/lib/analytics/period';
import { stillJudgingFrom } from '@/lib/analytics/traffic-series';
import { useChartView } from '@/lib/analytics/use-chart-view';
import { useComparison } from '@/lib/analytics/use-comparison';
import { usePeopleOnly } from '@/lib/analytics/use-people-only';
import { usePeriod } from '@/lib/analytics/use-period';
import type { Overview, Series, Site } from '@/lib/api/schemas';
import { useOverview, useSeries, useTraffic, useTrafficSeries } from '@/lib/queries/sites';
import { readableZone } from '@/lib/time-zones';

interface SiteOverviewProps {
  readonly site: Site;
}

/** One website, over one period. */
export function SiteOverview({ site }: SiteOverviewProps) {
  const t = useTranslations('dashboard');
  const metrics = useTranslations('dashboard.metrics');
  const install = useTranslations('install');
  const serverKeys = useTranslations('serverKeys');
  const settings = useTranslations('siteSettings');
  const format = useFormatter();
  const { period, choose } = usePeriod({ seeding: true });
  const { view, show } = useChartView();
  const { against, compare } = useComparison();
  const { peopleOnly, population, showOnlyPeople } = usePeopleOnly({ seeding: true });
  // The cards are labelled with what they count, in the same words the columns under the chart
  // wear, so the table adds up to the cards in the reader's own terms.
  const labels = useTranslations(peopleOnly ? 'dashboard.metrics.people' : 'dashboard.metrics');
  const [showingCode, setShowingCode] = useState(false);
  const [showingKeys, setShowingKeys] = useState(false);
  const [showingSettings, setShowingSettings] = useState(false);
  const periodControl = useRef<HTMLSelectElement>(null);

  // Resolved once per period rather than on every render: the window is part of the name each
  // answer is cached under, and one that moved with the clock would never find a cached answer.
  const span = useMemo(
    () => spanFor(period, site.timeZoneId, new Date()),
    [period, site.timeZoneId],
  );

  const window = useMemo(
    () => windowFor(period, site.timeZoneId, new Date()),
    [period, site.timeZoneId],
  );

  // The stretch immediately before this one, cut to the same length as the part of this one that
  // has actually happened. Every headline number is read against it.
  const earlierWindow = useMemo(
    () => previousWindow(period, site.timeZoneId, new Date()),
    [period, site.timeZoneId],
  );

  const granularity = granularityFor(span);
  const manyDays = daysIn(span) > 1;
  // Everybody is always asked about as well. It is the check that tells a website nobody has been
  // to from a period in which nobody was judged a person, and under everybody it is the same
  // question as the cards ask, filed under the same name, so it costs nothing.
  const everybody = useOverview(site.id, window, 'everybody');
  const overview = useOverview(site.id, window, population);
  const earlierOverview = useOverview(site.id, earlierWindow, population);
  // Read only while the screen is kept to people, to tell a period in which nobody was judged a
  // person from one in which nothing has been judged at all.
  const traffic = useTraffic(site.id, window, peopleOnly);

  // Only whichever picture is being drawn is asked for. How much was read comes from activity,
  // asked of the population the screen is kept to; who came comes from visits that have finished
  // and been judged, which already say who they were. Two stores, and a screen drawing one has no
  // reason to pay for the other.
  const drawingActivity = view === 'activity';
  const views = useSeries(site.id, 'pageviews', window, population, granularity, drawingActivity);
  const visitors = useSeries(site.id, 'visitors', window, population, granularity, drawingActivity);
  const who = useTrafficSeries(site.id, window, granularity, !drawingActivity);

  // The earlier period is cut into the same buckets as this one, so that the two can be read off
  // the same place on the axis. Asked for only once somebody puts it behind the drawing.
  const earlierViews = useSeries(
    site.id,
    'pageviews',
    earlierWindow,
    population,
    granularity,
    drawingActivity && against,
  );
  const earlierVisitors = useSeries(
    site.id,
    'visitors',
    earlierWindow,
    population,
    granularity,
    drawingActivity && against,
  );
  const earlierWho = useTrafficSeries(
    site.id,
    earlierWindow,
    granularity,
    !drawingActivity && against,
  );

  const activity = useMemo(() => align(views.data, visitors.data), [views.data, visitors.data]);

  const earlierPoints = useMemo(
    () => align(earlierViews.data, earlierVisitors.data)?.points,
    [earlierViews.data, earlierVisitors.data],
  );

  // Written out once, in the site's own days, so the drawing can say which days its dashed lines
  // actually cover rather than leaving somebody to work it out from the period they chose.
  const earlierDays = useMemo(() => {
    const covered = spanInstants(
      previousSpan(period, site.timeZoneId, new Date()),
      site.timeZoneId,
    );

    return format.dateTimeRange(covered.first, covered.last, {
      timeZone: site.timeZoneId,
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
  }, [period, site.timeZoneId, format]);

  const problem = (drawingActivity ? (views.error ?? visitors.error) : who.error) ?? null;
  const totals = overview.data;
  const before = earlierOverview.data;
  const comparedWith =
    period.kind === 'preset'
      ? metrics(`against.${period.preset}`)
      : metrics('against.chosen', { days: daysIn(span) });
  // Nobody at all, or nobody among the people: two different screens, and the second is only
  // possible while the screen is kept to people, since under everybody the two answers are one.
  // Neither is decided until everybody's answer is in, so a screen kept to people does not show
  // the people absent for the instant before it learns whether anybody at all was there.
  const silent = nought(everybody.data);
  const deserted = everybody.data !== undefined && !silent && nought(totals);
  const listing = `${site.id}:${window.from}:${window.to}:${population}`;

  // A day pressed on the picture becomes the period, which every panel on the screen then re-reads,
  // since the period is the whole screen's. On a period that is already one day there is nothing
  // narrower, so the picture is not offered as something to press.
  //
  // The picture is redrawn for the day and the row of its table that was pressed goes with the
  // week, so the reading position is handed to the control that now names the day: it says what
  // changed, and it is the way to change it again. The page follows only where there was a
  // position to move — a row the reader had reached — and stays where it is after a press on the
  // picture, which nothing can hold, so a tap on a phone does not jump the page.
  const pickDay = useMemo(
    () =>
      manyDays
        ? (day: string) => {
            const held =
              document.activeElement !== null && document.activeElement !== document.body;

            choose({ kind: 'chosen', first: day, last: day });
            periodControl.current?.focus({ preventScroll: !held });
          }
        : undefined,
    [manyDays, choose],
  );

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        {/*
          A heading and nothing more. Swapping between websites is a property of the session rather
          than of this screen, and lives in the bar across the top; a name that was also the control
          for changing it had to be the size of a heading to read as one, which made a picker the
          size of a heading.

          The address sits beneath the name only where it says something the name does not. A
          website keeps its address as its name until somebody renames it, and printing the same
          words twice reads as a mistake.
        */}
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="truncate text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
            {site.displayName}
          </h1>
          {site.displayName === site.domain ? null : (
            <p className="truncate text-sm text-foreground-muted">{site.domain}</p>
          )}
        </div>
        {/*
          Aligned by their tops rather than their middles. The period carries the dates it works
          out to on a second line, so a row centred on it would leave every button beside it
          sitting lower than the control they line up with.
        */}
        <div className="flex flex-wrap items-start gap-3">
          <Button tone="secondary" size="sm" onClick={() => setShowingCode(true)}>
            <CodeXml aria-hidden className="size-4" />
            {install('action')}
          </Button>
          <Button tone="secondary" size="sm" onClick={() => setShowingKeys(true)}>
            <KeyRound aria-hidden className="size-4" />
            {serverKeys('action')}
          </Button>
          <Button tone="secondary" size="sm" onClick={() => setShowingSettings(true)}>
            <SlidersHorizontal aria-hidden className="size-4" />
            {settings('action')}
          </Button>
          <PopulationSwitch peopleOnly={peopleOnly} onChange={showOnlyPeople} />
          <PeriodPicker
            ref={periodControl}
            value={period}
            onChange={choose}
            timeZoneId={site.timeZoneId}
          />
        </div>
      </header>

      {/* The one clause every figure below is read under. Said once, here, rather than on each card. */}
      {peopleOnly ? (
        <p className="-mt-2 text-sm text-foreground-muted">{t('population.caption')}</p>
      ) : null}

      {overview.isError ? <FailureNotice error={overview.error} /> : null}

      <div className="grid gap-4 sm:grid-cols-3">
        {totals === undefined ? (
          <>
            <MetricCardSkeleton />
            <MetricCardSkeleton />
            <MetricCardSkeleton />
          </>
        ) : (
          <>
            <MetricCard
              label={labels('pageViews.label')}
              value={format.number(totals.pageViews)}
              change={before && changeBetween(totals.pageViews, before.pageViews)}
              comparedWith={comparedWith}
            />
            <MetricCard
              label={labels('visitors.label')}
              value={format.number(totals.visitors)}
              change={before && changeBetween(totals.visitors, before.visitors)}
              comparedWith={comparedWith}
              note={metrics('visitors.note')}
            />
            <MetricCard
              label={labels('pagesPerVisitor.label')}
              value={perVisitor(totals.pageViews, totals.visitors, format)}
              change={before && changeBetween(pagesEach(totals), pagesEach(before))}
              comparedWith={comparedWith}
            />
          </>
        )}
      </div>

      {silent ? (
        <FirstVisit
          title={t('empty.title')}
          body={t('empty.body', { site: site.domain })}
          action={t('empty.action')}
          onAction={() => setShowingCode(true)}
        />
      ) : null}

      {deserted ? (
        <PeopleAbsent
          judged={traffic.data === undefined ? undefined : traffic.data.groups.length > 0}
          onShowEveryone={() => showOnlyPeople(false)}
        />
      ) : null}

      {silent || deserted ? null : (
        <>
          <TrafficChart
            view={view}
            onView={show}
            peopleOnly={peopleOnly}
            activity={activity}
            who={who.data}
            problem={problem}
            comparison={{
              on: against,
              onChange: compare,
              activity: earlierPoints,
              who: earlierWho.data,
              days: earlierDays,
            }}
            siteName={site.displayName}
            timeZoneId={site.timeZoneId}
            zone={readableZone(site.timeZoneId)}
            granularity={granularity}
            manyDays={manyDays}
            manyYears={crossesYears(span)}
            onPickDay={pickDay}
          />

          {/*
            Both lists are given a key that changes with the website and the period, so choosing
            either starts each list at its own beginning instead of leaving somebody on a
            screenful that no longer exists.
          */}
          {/*
            Where the visitors came from sits directly under the chart, above everything about what
            they then did. It is the first thing somebody looks for after seeing the shape of a
            week — a rise is a question, and this is where its answer usually is.
          */}
          <SiteSources
            key={`sources:${listing}`}
            siteId={site.id}
            window={window}
            population={population}
          />

          {/*
            Where the readers were and what they read on are two halves of the same question and
            sit side by side from a wide screen down, one above the other on anything narrower.
          */}
          <div className="grid gap-6 xl:grid-cols-2 xl:items-start">
            <SiteLocations
              key={`places:${listing}`}
              siteId={site.id}
              window={window}
              population={population}
            />

            <SiteDevices
              key={`devices:${listing}`}
              siteId={site.id}
              window={window}
              population={population}
            />
          </div>

          <SitePages
            key={`pages:${listing}`}
            siteId={site.id}
            window={window}
            population={population}
          />

          <SiteReading
            key={`reading:${listing}`}
            siteId={site.id}
            window={window}
            population={population}
            onShowCode={() => setShowingCode(true)}
          />

          <SiteFlow
            key={`flow:${listing}`}
            siteId={site.id}
            window={window}
            population={population}
          />

          <SiteActions
            key={`presses:${listing}`}
            siteId={site.id}
            window={window}
            population={population}
          />

          <JudgedTraffic key={`visits:${listing}`} site={site} window={window} />
        </>
      )}

      <TrackingCode
        open={showingCode}
        onClose={() => setShowingCode(false)}
        siteId={site.id}
        siteDomain={site.domain}
      />

      <ServerKeys
        open={showingKeys}
        onClose={() => setShowingKeys(false)}
        siteId={site.id}
        siteDomain={site.domain}
        timeZoneId={site.timeZoneId}
      />

      {/*
        Removing the website puts the panel away and nothing else. The list of websites is asked
        for again as part of the removal, and the screen settles on whichever one is left.
      */}
      <SiteSettings
        open={showingSettings}
        onClose={() => setShowingSettings(false)}
        site={site}
        onRemoved={() => setShowingSettings(false)}
      />
    </div>
  );
}

interface FirstVisitProps {
  readonly title: string;
  readonly body: string;
  readonly action: string;
  readonly onAction: () => void;
}

/**
 * The screen a website shows before anybody has been to it.
 *
 * It carries the one thing that would change it. A website with nothing on it yet is almost always
 * a website whose owner has not put the code on their pages, and sending them looking for it
 * elsewhere is how a first evening with a new product ends.
 */
function FirstVisit({ title, body, action, onAction }: FirstVisitProps) {
  return (
    <Card className="flex flex-col items-center gap-2 px-6 py-16 text-center">
      <span
        aria-hidden
        className="mb-2 flex size-12 items-center justify-center rounded-full bg-accent-soft"
      >
        <span className="size-2.5 animate-pulse rounded-full bg-accent" />
      </span>
      <h2 className="text-lg font-semibold text-foreground">{title}</h2>
      <p className="max-w-sm text-sm text-foreground-muted">{body}</p>
      <Button className="mt-4" onClick={onAction}>
        {action}
      </Button>
    </Card>
  );
}

/** Whether a period's totals came to nothing at all, once they have arrived. */
function nought(totals: Overview | undefined): boolean {
  return totals !== undefined && totals.pageViews === 0 && totals.visitors === 0;
}

interface PeopleAbsentProps {
  /** Whether anything in the period has been judged, or nothing until that is known. */
  readonly judged: boolean | undefined;
  readonly onShowEveryone: () => void;
}

/**
 * The screen a period shows when the website had traffic and none of it was counted as people.
 *
 * One state in place of the picture and every list, because eight cards each saying nothing is
 * eight ways of saying one thing. Two bodies, because "nobody was a person" and "nobody has been
 * looked at yet" are different facts; both offer everyone back, since the screen was kept to
 * people on purpose and the way out is the choice that kept it.
 */
function PeopleAbsent({ judged, onShowEveryone }: PeopleAbsentProps) {
  const t = useTranslations('dashboard.population.empty');

  if (judged === undefined) {
    return <ListWaiting />;
  }

  const reason = judged ? 'nobody' : 'unjudged';

  return (
    <ListEmpty
      icon={judged ? UserRound : ScanSearch}
      title={t(`${reason}.title`)}
      body={t(`${reason}.body`)}
      action={
        <Button tone="secondary" size="sm" onClick={onShowEveryone}>
          {t('action')}
        </Button>
      }
    />
  );
}

/**
 * Pages read per visitor as a bare figure, which is nought over a period nobody came in.
 *
 * A period with no visitors has no such figure, and the card shows a mark rather than a number
 * for it. Set beside another period it counts as none, which is what makes the first pages read
 * after a quiet week a rise from nothing rather than a share of nothing.
 */
function pagesEach(totals: { readonly pageViews: number; readonly visitors: number }): number {
  return totals.visitors > 0 ? totals.pageViews / totals.visitors : 0;
}

/** Pages read per visitor, on an average day, or nothing when nobody came. */
function perVisitor(
  pageViews: number,
  visitors: number,
  format: ReturnType<typeof useFormatter>,
): string | null {
  return visitors > 0 ? format.number(pageViews / visitors, { maximumFractionDigits: 1 }) : null;
}

/**
 * Two answers about the same buckets, joined into the rows a chart and a table both read, and
 * where the judging stops — which for everybody is nowhere, since every report counts as it
 * arrives.
 */
function align(views: Series | undefined, visitors: Series | undefined): Activity | undefined {
  if (!views || !visitors) {
    return undefined;
  }

  const byBucket = new Map(visitors.points.map((point) => [point.bucketStart, point.value]));
  const points = views.points.map((point) => ({
    start: point.bucketStart,
    pageViews: point.value,
    visitors: byBucket.get(point.bucketStart) ?? 0,
  }));

  return {
    points,
    stillJudging: stillJudgingFrom({
      to: views.to,
      completeTo: views.completeTo,
      buckets: points.map((point) => point.start),
    }),
  };
}
