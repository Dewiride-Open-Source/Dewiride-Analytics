'use client';

import {
  CalendarClock,
  CodeXml,
  KeyRound,
  ScanSearch,
  SlidersHorizontal,
  UserRound,
} from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { type Ref, type RefObject, useCallback, useMemo, useRef, useState } from 'react';
import { PeriodMoved } from '@/components/charts/cut-picture';
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
import {
  type Activity,
  TrafficChart,
  type TrafficComparison,
} from '@/components/dashboard/traffic-chart';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { changeBetween } from '@/lib/analytics/change';
import {
  absence,
  type Breakdown,
  everJudged,
  type JudgingReach,
  reachedBy,
} from '@/lib/analytics/people-absence';
import type { Population } from '@/lib/analytics/people-only';
import {
  type AllTimeGranularity,
  type AnalyticsWindow,
  comparedSpan,
  comparedWindow,
  crossesYears,
  cutsFor,
  type DaySpan,
  daysIn,
  type Granularity,
  hasEarlier,
  isAllTime,
  keptFrom,
  type Period,
  previousWindow,
  spanFor,
  spanInstants,
  todayIn,
  windowFor,
  windowForSpan,
} from '@/lib/analytics/period';
import { judgedNoneOf, stillJudgingFrom } from '@/lib/analytics/traffic-series';
import { useChartView } from '@/lib/analytics/use-chart-view';
import { useComparison } from '@/lib/analytics/use-comparison';
import { usePeopleOnly } from '@/lib/analytics/use-people-only';
import { usePeriod } from '@/lib/analytics/use-period';
import { useHandOver, usePickDays, usePicturePlan } from '@/lib/analytics/use-picture-plan';
import type {
  AllTime,
  AllTimeSeries,
  AllTimeTraffic,
  Series,
  Site,
  TrafficPicture,
} from '@/lib/api/schemas';
import {
  useAllTime,
  useAllTimeSeries,
  useAllTimeTraffic,
  useOverview,
  useSeries,
  useTraffic,
  useTrafficSeries,
} from '@/lib/queries/sites';
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
  const { period, choose } = usePeriod({ seeding: true });
  const { view, show } = useChartView();
  const { peopleOnly, population, showOnlyPeople } = usePeopleOnly({ seeding: true });
  const [showingCode, setShowingCode] = useState(false);
  const [showingKeys, setShowingKeys] = useState(false);
  const [showingSettings, setShowingSettings] = useState(false);
  const clicksHeading = useRef<HTMLHeadingElement>(null);

  // Resolved once per period rather than on every render: the window is part of the name each
  // answer is cached under, and one that moved with the clock would never find a cached answer.
  // Every stretch is worked out from the one moment the period was resolved at, the dashed lines'
  // included, so none of them can stand an hour or a day apart from the picture they are read with.
  const { now, span, window, earlierWindow, firstKept } = useMemo(() => {
    const resolvedAt = new Date();

    return {
      now: resolvedAt,
      span: spanFor(period, site.timeZoneId, resolvedAt),
      window: windowFor(period, site.timeZoneId, resolvedAt),
      // The stretch immediately before this one, cut to the same length as the part of this one
      // that has actually happened. Every headline number is read against it — where it is still
      // kept: a period reaching back past the days a website still holds has nothing honest to be
      // measured against, and its cards say how much and nothing more.
      earlierWindow: previousWindow(period, site.timeZoneId, resolvedAt),
      // Where the days held one by one begin, which is as far back as all time's picture can be
      // pressed into: a month older than it is held as its totals and nothing finer.
      firstKept: keptFrom(site.timeZoneId, resolvedAt),
    };
  }, [period, site.timeZoneId]);

  const { control: periodControl, moveTo } = useHandOver(choose);
  const allTime = isAllTime(period);
  const { record, firstDay, nothingHeld, recordDays, reach } = useRecord(site, span, allTime, now);
  const { pictured, granularity, cutting, movedTo, manyDays, bucketsDue } = usePicturePlan(
    { allTime, span, window, firstDay, recordDays },
    choose,
  );
  // Everybody's record, where the screen is on all time and its cards and picture are read from it;
  // nothing on any other period.
  const allTimeRecord = allTime ? record : undefined;

  const headline = useHeadline({
    siteId: site.id,
    timeZoneId: site.timeZoneId,
    window,
    earlierWindow,
    population,
    record: allTimeRecord,
  });
  // Read only while the screen is kept to people, to tell a period in which nobody was judged a
  // person from one in which nothing has been judged at all: a period's own breakdown, and under
  // all time every month of it, asked only once all time has been found to hold no people — or the
  // breakdown of the twelve months its lists cover, once the state in their place needs it.
  const traffic = useTraffic(
    site.id,
    window,
    peopleOnly && (!allTime || quietForWantOfPeople(headline)),
  );
  const judgedEver = useAllTimeTraffic(
    site.id,
    site.timeZoneId,
    'month',
    allTime && headline.deserted,
  );

  // Only whichever picture is being drawn is asked for. How much was read comes from activity,
  // asked of the population the screen is kept to; who came comes from visits that have finished
  // and been judged, which already say who they were. Two stores, and a screen drawing one has no
  // reason to pay for the other.
  const drawingActivity = view === 'activity';
  const { activity, who, problem, leftOut } = usePicture({
    siteId: site.id,
    timeZoneId: site.timeZoneId,
    window,
    population,
    granularity,
    drawingActivity,
    record: allTimeRecord,
  });
  const comparison = useDashedLines({
    site,
    period,
    window,
    now,
    population,
    granularity,
    drawingActivity,
  });

  const { totals, before, measuring, silent, deserted } = headline;
  const comparedWith = againstWords(period, span, metrics);
  // Under all time the lists say once that they cover the last twelve months — only where the
  // figures above reach back before them. A website first measured since has its figures and its
  // lists over the same days, and the clause would have nothing to set apart.
  const listsShorter = allTime && firstDay !== null && firstDay < span.first;
  // All time reaches back past the days held visit by visit; a month older than them is held as its
  // totals alone, and is not pressed into.
  const pressableFrom = allTime ? firstKept : undefined;

  const pickDays = usePickDays(manyDays, moveTo, site.timeZoneId);

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
            since={firstDay}
            nothingHeld={nothingHeld}
            onReach={reach}
          />
        </div>
      </header>

      {/* The one clause every figure below is read under. Said once, here, rather than on each card. */}
      {peopleOnly ? (
        <p className="-mt-2 text-sm text-foreground-muted">{t('population.caption')}</p>
      ) : null}

      <PeriodMoved movedTo={movedTo} period={period} />

      {headline.failure === null ? null : <FailureNotice error={headline.failure} />}

      <div className="grid gap-4 sm:grid-cols-3">
        <HeadlineCards
          totals={totals}
          before={before}
          measuring={measuring}
          comparedWith={comparedWith}
          peopleOnly={peopleOnly}
          refused={headline.cardsRefused}
        />
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
          breakdown={allTime ? everJudged(judgedEver) : traffic}
          reached={reachedIn(drawingActivity, activity, who)}
          pictureProblem={problem}
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
            who={who}
            problem={problem}
            comparison={comparison}
            siteName={site.displayName}
            timeZoneId={site.timeZoneId}
            zone={readableZone(site.timeZoneId)}
            granularity={granularity}
            manyDays={manyDays}
            manyYears={crossesYears(pictured)}
            cutting={cutting}
            onPick={pickDays}
            pressableFrom={pressableFrom}
            missing={leftOut.missing}
            unsettled={leftOut.unjudged}
            bucketsDue={bucketsDue}
          />

          <ListsPlace
            headline={headline}
            breakdown={traffic}
            shorter={listsShorter}
            site={site}
            window={window}
            population={population}
            onShowCode={() => setShowingCode(true)}
            onShowSettings={() => setShowingSettings(true)}
            onShowEveryone={() => showOnlyPeople(false)}
            returnPoint={clicksHeading}
          />
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
        for again as part of the removal, and the screen settles on whichever one is left. Opened
        from the clicks card, the panel can take that card's own way in away with it — switching
        recording on does — so focus comes back to the card's heading, which by then says what
        changed, rather than to the top of the page.
      */}
      <SiteSettings
        open={showingSettings}
        onClose={() => setShowingSettings(false)}
        site={site}
        onRemoved={() => setShowingSettings(false)}
        returnFocusTo={clicksHeading}
      />
    </div>
  );
}

/** Where the website's record begins, as the screen asks it, and what follows from that. */
interface RecordRead {
  /** Everybody's totals across the record, as they are being asked for. */
  readonly record: RecordAnswer;
  /** The record's first day, or nothing while that is not known or where there is none. */
  readonly firstDay: string | null;
  /**
   * Whether the website has been asked about and nothing of it is held: never measured, or measured
   * only in months that went before any of them was kept.
   */
  readonly nothingHeld: boolean;
  /** The days of the record, from its first day to today — today alone while that is not known. */
  readonly recordDays: DaySpan;
  /** Says that somebody reached for the period. */
  readonly reach: () => void;
}

/**
 * Where the website's record begins: asked under all time, which is drawn from it; on a shorter
 * period wherever the ways of cutting its picture are listed, since years are listed there only
 * where all time would show them; and on any period once somebody reaches for the period, so the
 * chooser offers all time on whether the record reaches back further rather than on what was read
 * earlier, and all time chosen next finds its record waiting. Under everybody it is the question
 * all time's cards ask, filed under the same name.
 *
 * @param site The website on screen.
 * @param span The days the period on screen covers.
 * @param allTime Whether the screen is on all time.
 * @param now The moment the period was worked out at, which is when today is.
 */
function useRecord(site: Site, span: DaySpan, allTime: boolean, now: Date): RecordRead {
  const [reaching, setReaching] = useState(false);
  const reach = useCallback(() => setReaching(true), []);
  const record = useAllTime(
    site.id,
    site.timeZoneId,
    'everybody',
    allTime || cutsFor(span).length > 0 || reaching,
  );
  const firstDay = record.data?.firstDay ?? null;
  const recordDays = useMemo(() => {
    const today = todayIn(site.timeZoneId, now);

    return { first: firstDay ?? today, last: today };
  }, [firstDay, site.timeZoneId, now]);

  return {
    record,
    firstDay,
    // Known, rather than not yet known: a website asked about with nothing of it held has no first
    // day for all time to name.
    nothingHeld: record.data !== undefined && record.data.firstDay === null,
    recordDays,
    reach,
  };
}

interface ListsPlaceProps {
  /** The screen's headline figures, which settle what stands where the lists go. */
  readonly headline: Headline;
  /** What the lists' twelve months held of each kind of visit judged, as it is being asked for. */
  readonly breakdown: Breakdown;
  /** Whether the figures above reach back before the twelve months the lists cover. */
  readonly shorter: boolean;
  readonly site: Site;
  /** The window every list counts over. */
  readonly window: AnalyticsWindow;
  readonly population: Population;
  readonly onShowCode: () => void;
  readonly onShowSettings: () => void;
  readonly onShowEveryone: () => void;
  /** Where focus goes back to once the settings panel opened from the clicks card is put away. */
  readonly returnPoint: RefObject<HTMLHeadingElement | null>;
}

/**
 * What stands where the lists go: the lists, a wait, the one state saying their twelve months held
 * nothing or no people — or, where whether they held anything could not be had, what went wrong,
 * said there rather than above figures that were read.
 */
function ListsPlace({
  headline,
  breakdown,
  shorter,
  site,
  window,
  population,
  onShowCode,
  onShowSettings,
  onShowEveryone,
  returnPoint,
}: ListsPlaceProps) {
  const t = useTranslations('dashboard');
  const { lists, listsFailure } = headline;

  if (lists === 'waiting') {
    return <ListWaiting />;
  }

  if (lists === 'refused') {
    return <FailureNotice error={listsFailure} />;
  }

  if (lists === 'nothing' || lists === 'noPeople') {
    return (
      <ListsQuiet
        quiet={lists}
        breakdown={breakdown}
        domain={site.domain}
        onShowCode={onShowCode}
        onShowEveryone={onShowEveryone}
      />
    );
  }

  return (
    <>
      {/*
        Said once, where the lists begin. The figures above reach back to the first month of the
        website's record; every list is counted from the visits themselves, which go after a year.
      */}
      {shorter ? <p className="-mb-3 text-sm text-foreground-muted">{t('allTime.lists')}</p> : null}

      <PeriodLists
        site={site}
        window={window}
        population={population}
        onShowCode={onShowCode}
        onShowSettings={onShowSettings}
        returnPoint={returnPoint}
      />
    </>
  );
}

/**
 * Whether the lists' place is the state saying their twelve months held no people, which reads
 * what those months held of each kind of visit judged. Never where a screen stands in for the
 * lists — a website nobody has visited, or one with no people at all — since nothing reads it then.
 *
 * @param headline The screen's headline figures.
 */
function quietForWantOfPeople({ lists, silent, deserted }: Headline): boolean {
  return lists === 'noPeople' && !silent && !deserted;
}

interface PeriodListsProps {
  readonly site: Site;
  /** The window every list counts over. */
  readonly window: AnalyticsWindow;
  readonly population: Population;
  readonly onShowCode: () => void;
  readonly onShowSettings: () => void;
  /** Where focus goes back to once the settings opened from the clicks card are put away. */
  readonly returnPoint: Ref<HTMLHeadingElement>;
}

/**
 * Every list under the picture, each counted over the period from the visits themselves.
 *
 * Each is given a key that changes with the website, the period and the people it is kept to, so
 * changing any of them starts each list at its own beginning instead of leaving somebody on a
 * screenful that no longer exists.
 */
function PeriodLists({
  site,
  window,
  population,
  onShowCode,
  onShowSettings,
  returnPoint,
}: PeriodListsProps) {
  const listing = `${site.id}:${window.from}:${window.to}:${population}`;

  return (
    <>
      {/*
        Where the visitors came from sits directly under the chart, above everything about what
        they then did. It is the first thing somebody looks for after seeing the shape of a week —
        a rise is a question, and this is where its answer usually is.
      */}
      <SiteSources
        key={`sources:${listing}`}
        siteId={site.id}
        window={window}
        population={population}
      />

      {/*
        Where the readers were and what they read on are two halves of the same question and sit
        side by side from a wide screen down, one above the other on anything narrower.
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
        onShowCode={onShowCode}
      />

      <SiteFlow key={`flow:${listing}`} siteId={site.id} window={window} population={population} />

      <SiteActions
        key={`presses:${listing}`}
        siteId={site.id}
        window={window}
        population={population}
        mayChangeSettings={site.role !== 'viewer'}
        onShowSettings={onShowSettings}
        returnPoint={returnPoint}
      />

      <JudgedTraffic key={`visits:${listing}`} site={site} window={window} />
    </>
  );
}

interface ListsQuietProps {
  /** Whether nothing at all reached the website in the months the lists cover, or no people did. */
  readonly quiet: 'nothing' | 'noPeople';
  /** What those months held of each kind of visit judged, as it is being asked for. */
  readonly breakdown: Breakdown;
  /** The website's address, which the state names. */
  readonly domain: string;
  readonly onShowCode: () => void;
  readonly onShowEveryone: () => void;
}

/**
 * All time's lists, where nothing they could list reached the website in the twelve months they
 * cover.
 *
 * One state in place of the eight lists. Each list on its own would say it has nothing yet, which is
 * what a website nobody has visited says — beside figures above that go back further, it would be
 * eight ways of saying something untrue. It offers the one thing that would change it: the tracking
 * code where nothing came at all, whoever the screen is kept to, and everyone back where visits came
 * and none of them is counted among the people.
 *
 * The twelve months run to now, so they are always partly still being judged, and no people in them
 * is only ever no people so far. Whether anything in them was judged at all is what tells a website
 * none of whose recent visits has been looked at yet from one none of whose judged visits was a
 * person; where that could not be had the state says so, rather than guessing.
 */
function ListsQuiet({ quiet, breakdown, domain, onShowCode, onShowEveryone }: ListsQuietProps) {
  const t = useTranslations('dashboard');

  if (quiet === 'nothing') {
    return (
      <ListEmpty
        icon={CalendarClock}
        title={t('allTime.quiet.title')}
        body={t('allTime.quiet.body', { site: domain })}
        action={
          <Button tone="secondary" size="sm" onClick={onShowCode}>
            {t('empty.action')}
          </Button>
        }
      />
    );
  }

  if (breakdown.error !== null) {
    return <FailureNotice error={breakdown.error} />;
  }

  if (breakdown.data === undefined) {
    return <ListWaiting />;
  }

  const reason = breakdown.data.groups.length > 0 ? 'nobodyYet' : 'unjudged';

  return (
    <ListEmpty
      icon={ScanSearch}
      title={t(`allTime.quiet.people.${reason}.title`)}
      body={t(`allTime.quiet.people.${reason}.body`, { site: domain })}
      action={
        <Button tone="secondary" size="sm" onClick={onShowEveryone}>
          {t('population.empty.action')}
        </Button>
      }
    />
  );
}

/** The two figures every card is worked out from, over a period or over all time. */
interface Totals {
  readonly pageViews: number;
  readonly visitors: number;
}

interface HeadlineCardsProps {
  /** The period's own figures, or nothing until they are in. */
  readonly totals: Totals | undefined;
  /** The figures they are measured against, or nothing until they are in or where there are none. */
  readonly before: Totals | undefined;
  /** Whether the cards say how their numbers moved: there is an earlier stretch, and it was read. */
  readonly measuring: boolean;
  /**
   * What the figures are measured against, named the way somebody would say it out loud — or
   * nothing for all time, which has nothing before it.
   */
  readonly comparedWith: string | undefined;
  /** Whether the figures are kept to the visits judged to be people. */
  readonly peopleOnly: boolean;
  /** Whether the cards' own figures could not be had. */
  readonly refused: boolean;
}

/**
 * The period's three headline numbers, or their stand-ins until every figure they show is in.
 *
 * A card's own number and the one it is measured against are two answers, and a card drawn as each
 * arrived would grow a line when the second did. Held until both are in, each card is drawn once,
 * and its stand-in is laid out from the card's own words — the label kept to the population on
 * screen, and the sentence saying how the number moved where it will — so the picture the reader is
 * using below the cards stays where it is while a period's answers arrive one after the other, save
 * a line where a card's sentence breaks a line apart from its stand-in's.
 *
 * A stand-in promises a figure on its way, so figures that could not be had are shown as the mark for
 * no figure: the notice above says why, and a stand-in moving under it would say something is coming
 * when nothing is. Asked again and answered, the numbers take their place.
 */
function HeadlineCards({
  totals,
  before,
  measuring,
  comparedWith,
  peopleOnly,
  refused,
}: HeadlineCardsProps) {
  const metrics = useTranslations('dashboard.metrics');
  // The cards are labelled with what they count, in the same words the columns under the chart
  // wear, so the table adds up to the cards in the reader's own terms.
  const labels = useTranslations(peopleOnly ? 'dashboard.metrics.people' : 'dashboard.metrics');
  const format = useFormatter();

  if (totals === undefined && refused) {
    return (
      <>
        <MetricCard label={labels('pageViews.label')} value={null} />
        <MetricCard label={labels('visitors.label')} value={null} note={metrics('visitors.note')} />
        <MetricCard label={labels('pagesPerVisitor.label')} value={null} />
      </>
    );
  }

  if (totals === undefined || (measuring && before === undefined)) {
    const against = measuring ? comparedWith : undefined;

    return (
      <>
        <MetricCardSkeleton label={labels('pageViews.label')} comparedWith={against} />
        <MetricCardSkeleton
          label={labels('visitors.label')}
          comparedWith={against}
          note={metrics('visitors.note')}
        />
        <MetricCardSkeleton label={labels('pagesPerVisitor.label')} comparedWith={against} />
      </>
    );
  }

  return (
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
  );
}

/** No month left out: the same list every time, so nothing is drawn again for it. */
const NONE_MISSING: readonly string[] = [];

/** No month left out of either kind, the same every time for the same reason. */
const NOTHING_LEFT_OUT: LeftOut = { missing: NONE_MISSING, unjudged: NONE_MISSING };

/** Everybody's totals across all time, as far as they have arrived. */
interface RecordAnswer {
  readonly data: AllTime | undefined;
  readonly error: Error | null;
}

/** What the headline figures of the screen are read from, and what they say about it. */
interface HeadlineAsked {
  readonly siteId: string;
  /** The website's zone, which all time's months are cut in. */
  readonly timeZoneId: string;
  /** The window a period's cards count over, and under all time the window its lists count over. */
  readonly window: AnalyticsWindow;
  /** The window they are measured against, or nothing where there is none. */
  readonly earlierWindow: AnalyticsWindow | null;
  readonly population: Population;
  /**
   * Everybody's totals across all time, where the screen is on all time and its cards are read
   * from them; nothing for any other period.
   */
  readonly record: RecordAnswer | undefined;
}

/**
 * What stands where all time's lists go: the lists, a wait while it is not yet known whether the
 * twelve months they cover held anything, one state in place of them where those months held
 * nothing at all, or no people — or what went wrong, where whether they held anything could not be
 * had.
 */
type ListsShown = 'lists' | 'waiting' | 'nothing' | 'noPeople' | 'refused';

/** The screen's headline figures, and what they say about whether there is anything to show. */
interface Headline {
  /** The figures the cards show, or nothing until they are in. */
  readonly totals: Totals | undefined;
  /** The figures they are measured against, or nothing until they are in or where there are none. */
  readonly before: Totals | undefined;
  /** Whether the cards say how their numbers moved. */
  readonly measuring: boolean;
  /**
   * Whether nobody at all has been, over the period — or, under all time, whether nothing of the
   * website is held.
   */
  readonly silent: boolean;
  /** Whether there was traffic and none of it was counted among the people the screen is kept to. */
  readonly deserted: boolean;
  /**
   * What stands where the lists go. Only all time's can be anything but the lists themselves, since
   * any other period's lists cover the days the cards do.
   */
  readonly lists: ListsShown;
  /**
   * Why whether the lists' twelve months held anything could not be had, where it could not — said
   * in the lists' place, since the cards above were read — or nothing.
   */
  readonly listsFailure: Error | null;
  /**
   * Why the cards' own figures could not be had — or, under all time, everybody's record they are
   * read beside, which says whether anything of the website is held — or nothing.
   */
  readonly failure: Error | null;
  /**
   * Whether the cards' own figures could not be had. Everybody's record alone says nothing of them,
   * which may be on their way yet: their stand-ins stay until they have answered, one way or the other.
   */
  readonly cardsRefused: boolean;
}

/**
 * The screen's headline figures: a period's, measured against the stretch before it, or all time's,
 * which are measured against nothing.
 *
 * Everybody is always asked about as well. It is the check that tells a website nobody has been to
 * from one in which nobody was judged a person, and under everybody it is the same question as the
 * cards ask, filed under the same name, so it costs nothing. Under all time that check is whether the
 * website has a first day at all: a website first measured this morning has visits counted from them
 * straight away, and only one with no first day — nothing of it held — is waiting for a visit.
 * Everybody over the twelve months the lists cover is then what tells lists with nothing at all to
 * list from lists with no people to list.
 *
 * Nobody at all, or nobody among the people: two different screens, and the second is only possible
 * while the screen is kept to people, since under everybody the two answers are one. Neither is
 * decided until everybody's answer is in, so a screen kept to people does not show the people absent
 * for the instant before it learns whether anybody at all was there. Under all time the people are
 * never called absent while months are left out of their figures: nought then is only what was
 * counted, and the picture says which months were not.
 *
 * Under all time the period's own question is still asked, of the twelve months the lists cover, to
 * learn whether the lists have anything to list.
 */
function useHeadline({
  siteId,
  timeZoneId,
  window,
  earlierWindow,
  population,
  record,
}: HeadlineAsked): Headline {
  const allTime = record !== undefined;
  const everybody = useOverview(siteId, window, 'everybody');
  const overview = useOverview(siteId, window, population);
  const earlier = useOverview(siteId, earlierWindow ?? window, population, earlierWindow !== null);
  const sinceFirst = useAllTime(siteId, timeZoneId, population, allTime);

  if (record !== undefined) {
    const totals = sinceFirst.data;
    const silent = record.data?.firstDay === null;
    const lists = listsOver(everybody, overview, answered(record) && answered(sinceFirst));

    return {
      totals,
      before: undefined,
      measuring: false,
      silent,
      deserted: record.data !== undefined && !silent && nought(totals) && leavesNothingOut(totals),
      lists,
      listsFailure: lists === 'refused' ? (overview.error ?? everybody.error) : null,
      failure: sinceFirst.error ?? record.error,
      cardsRefused: sinceFirst.error !== null,
    };
  }

  const totals = overview.data;
  const silent = nought(everybody.data);

  return {
    totals,
    // Nothing where there is nothing to measure against. The question was never asked then, and
    // the name it would have been filed under is this period's own.
    before: earlierWindow === null ? undefined : earlier.data,
    // An earlier stretch that could not be read leaves the cards to say how much and nothing more.
    measuring: earlierWindow !== null && !earlier.isError,
    silent,
    deserted: everybody.data !== undefined && !silent && nought(totals),
    lists: 'lists',
    listsFailure: null,
    failure: overview.error,
    cardsRefused: overview.error !== null,
  };
}

/** What the picture of the screen is drawn from. */
interface PictureAsked {
  readonly siteId: string;
  readonly timeZoneId: string;
  /** The window a period's picture covers. */
  readonly window: AnalyticsWindow;
  readonly population: Population;
  readonly granularity: Granularity;
  /** Whether the picture is of how much was read rather than of who came. */
  readonly drawingActivity: boolean;
  /**
   * Under all time, everybody's record, which the picture waits for: the day it begins is the day
   * the first month is cut at, and settles how the picture may be cut and how its buckets are
   * written. Undefined for any other period.
   */
  readonly record: RecordAnswer | undefined;
}

/** The picture being drawn, as far as it has arrived, and why it could not be where it could not. */
interface PictureAnswers {
  readonly activity: Activity | undefined;
  readonly who: TrafficPicture | undefined;
  readonly problem: Error | null;
  /**
   * Under all time, the months the picture leaves out and why, as its own answer says: never
   * counted, and — for who came, or how much the people read — kept before their visits were all
   * judged. Said by the picture for itself, so it is never told why it has no figure by another
   * answer read at another moment; none for any other period, or while its answer is on its way.
   */
  readonly leftOut: LeftOut;
}

/**
 * The picture being drawn: a period's, cut the way it was asked for, or all time's, by the month or
 * the year.
 *
 * Only the one picture being drawn is asked for, from the one answer that draws it. All time's is
 * asked for once the record has said where it begins, and not before: its first month is cut at the
 * record's first day, so it is written as the days of it the record holds rather than as a whole
 * month nobody could have visited in — and how it is cut, whether its months carry their years and
 * which of them can be pressed are all settled by that day. Drawn before it,
 * every one of them would be drawn wrong and then drawn again. A record that could not be read is
 * the picture's failure as well.
 */
function usePicture({
  siteId,
  timeZoneId,
  window,
  population,
  granularity,
  drawingActivity,
  record,
}: PictureAsked): PictureAnswers {
  const allTime = record !== undefined;
  const since = record?.data?.firstDay ?? null;
  // A day it begins on, which a website with nothing of it held has none of — and nothing to draw.
  const asking = since !== null;
  const cut: AllTimeGranularity = granularity === 'year' ? 'year' : 'month';

  const views = useSeries(
    siteId,
    'pageviews',
    window,
    population,
    granularity,
    drawingActivity && !allTime,
  );
  const visitors = useSeries(
    siteId,
    'visitors',
    window,
    population,
    granularity,
    drawingActivity && !allTime,
  );
  const who = useTrafficSeries(siteId, window, granularity, !drawingActivity && !allTime);
  const recordViews = useAllTimeSeries(
    siteId,
    timeZoneId,
    'pageviews',
    cut,
    population,
    drawingActivity && asking,
  );
  const recordVisitors = useAllTimeSeries(
    siteId,
    timeZoneId,
    'visitors',
    cut,
    population,
    drawingActivity && asking,
  );
  const recordWho = useAllTimeTraffic(siteId, timeZoneId, cut, !drawingActivity && asking);

  const opens = useMemo(
    () =>
      since === null ? undefined : windowForSpan({ first: since, last: since }, timeZoneId).from,
    [since, timeZoneId],
  );

  const activity = useMemo(
    () =>
      allTime
        ? align(recordViews.data, recordVisitors.data, opens)
        : align(views.data, visitors.data),
    [allTime, recordViews.data, recordVisitors.data, opens, views.data, visitors.data],
  );

  // The months how much was read leaves out, as its two answers say. They are asked of the same
  // months and the same people, so they name the same months; both are read all the same, so a
  // month one of them leaves out alone is never left unsaid.
  const readLeftOut = useMemo(
    () => leftOutOfBoth(recordViews.data, recordVisitors.data),
    [recordViews.data, recordVisitors.data],
  );

  // Who came over all time opens where how much was read does, so both name the first month by
  // the days of it that were measured.
  const judged = useMemo(
    () =>
      recordWho.data === undefined || opens === undefined
        ? recordWho.data
        : { ...recordWho.data, from: opens },
    [recordWho.data, opens],
  );

  const drawn = allTime
    ? { views: recordViews, visitors: recordVisitors, who: recordWho }
    : { views, visitors, who };
  const unread = drawingActivity ? (drawn.views.error ?? drawn.visitors.error) : drawn.who.error;
  // Held whatever an answer kept from an earlier visit would draw: it would be drawn uncut.
  const held = allTime && !asking;
  const pictured = allTime ? judged : who.data;

  return {
    activity: held ? undefined : activity,
    who: held ? undefined : pictured,
    problem: record?.error ?? unread ?? null,
    leftOut: pictureLeavesOut(allTime, drawingActivity, readLeftOut, recordWho.data),
  };
}

/**
 * The months all time's picture leaves out and why, from the answer that draws it — none for any
 * other period, or while that answer is on its way.
 *
 * @param allTime Whether the picture is all time's.
 * @param drawingActivity Whether the picture is of how much was read rather than of who came.
 * @param read The months how much was read leaves out, or nothing until its answers are in.
 * @param who Who came, or nothing until it is in.
 */
function pictureLeavesOut(
  allTime: boolean,
  drawingActivity: boolean,
  read: LeftOut | undefined,
  who: AllTimeTraffic | undefined,
): LeftOut {
  if (!allTime) {
    return NOTHING_LEFT_OUT;
  }

  return (drawingActivity ? read : who) ?? NOTHING_LEFT_OUT;
}

/**
 * The months two answers about the same months leave out, each kind gathered from both, oldest
 * first — or nothing until both are in.
 *
 * @param one The first answer, or nothing until it is in.
 * @param other The second, or nothing until it is in.
 */
function leftOutOfBoth(one: LeftOut | undefined, other: LeftOut | undefined): LeftOut | undefined {
  if (one === undefined || other === undefined) {
    return undefined;
  }

  return {
    missing: union(one.missing, other.missing),
    unjudged: union(one.unjudged, other.unjudged),
  };
}

/** Two lists of months as one, each month once, oldest first. */
function union(one: readonly string[], other: readonly string[]): readonly string[] {
  return [...new Set([...one, ...other])].sort((first, second) => first.localeCompare(second));
}

/**
 * What a period's figures are measured against, named the way somebody would say it out loud — or
 * nothing for all time, which has nothing before it to be measured against.
 */
function againstWords(
  period: Period,
  span: DaySpan,
  metrics: ReturnType<typeof useTranslations<'dashboard.metrics'>>,
): string | undefined {
  if (period.kind === 'chosen') {
    return metrics('against.chosen', { days: daysIn(span) });
  }

  return hasEarlier(period.preset) ? metrics(`against.${period.preset}`) : undefined;
}

/** What the period before is drawn for. */
interface DashedLinesAsked {
  readonly site: Site;
  readonly period: Period;
  /** The window the picture asks about, which stands in where there is no earlier one to ask. */
  readonly window: AnalyticsWindow;
  /** The moment the period on screen was worked out at, which its earlier stretch is worked out from. */
  readonly now: Date;
  readonly population: Population;
  readonly granularity: Granularity;
  /** Whether the picture is of how much was read rather than of who came. */
  readonly drawingActivity: boolean;
}

/**
 * The period before, to set behind the picture, or nothing where there is none to set there.
 *
 * Its days are settled apart from the stretch the cards are measured against. Cut by the week or
 * the month they are moved back by whole weeks or months, so each earlier bucket covers the same
 * weekdays or the same days of the month as the bucket it is drawn beside; where they would reach
 * back past the days a website still keeps, the picture offers no comparison at all. Cut into the
 * same buckets as this period, so the two are read off the same place on the axis, and asked for
 * only once somebody puts it behind the drawing — never where there is no stretch to draw it from,
 * so the window handed over then is never sent.
 */
function useDashedLines({
  site,
  period,
  window,
  now,
  population,
  granularity,
  drawingActivity,
}: DashedLinesAsked): TrafficComparison | undefined {
  const format = useFormatter();
  const { against, compare } = useComparison();

  const lines = useMemo(
    () => comparedWindow(period, granularity, site.timeZoneId, now),
    [period, granularity, site.timeZoneId, now],
  );

  // Written out once, in the site's own days, so the drawing can say which days its dashed lines
  // actually cover rather than leaving somebody to work it out from the period they chose.
  const days = useMemo(() => {
    const covered = comparedSpan(period, granularity, site.timeZoneId, now);

    if (covered === null) {
      return null;
    }

    const instants = spanInstants(covered, site.timeZoneId);

    return format.dateTimeRange(instants.first, instants.last, {
      timeZone: site.timeZoneId,
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
  }, [period, granularity, site.timeZoneId, now, format]);

  const asking = against && lines !== null;
  const asked = lines ?? window;
  const views = useSeries(
    site.id,
    'pageviews',
    asked,
    population,
    granularity,
    drawingActivity && asking,
  );
  const visitors = useSeries(
    site.id,
    'visitors',
    asked,
    population,
    granularity,
    drawingActivity && asking,
  );
  const who = useTrafficSeries(site.id, asked, granularity, !drawingActivity && asking);

  const points = useMemo(
    () => align(views.data, visitors.data)?.points,
    [views.data, visitors.data],
  );

  return days === null
    ? undefined
    : { on: against, onChange: compare, activity: points, who: who.data, days };
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
function nought(totals: Totals | undefined): boolean {
  return totals?.pageViews === 0 && totals.visitors === 0;
}

/** The months an all-time answer leaves out, told apart by why, as the answer itself says. */
interface LeftOut {
  /** Those never counted — never kept, and no longer countable — written `2025-06`. */
  readonly missing: readonly string[];
  /** Those kept before judging had finished with them, which can no longer be counted again. */
  readonly unjudged: readonly string[];
}

/**
 * Whether an all-time answer leaves no month out, once it has arrived.
 *
 * @param answer The answer, or nothing until it is in.
 */
function leavesNothingOut(answer: LeftOut | undefined): boolean {
  return answer?.missing.length === 0 && answer.unjudged.length === 0;
}

/** Whether a question has been answered, one way or the other. */
function answered(answer: { readonly data: unknown; readonly error: Error | null }): boolean {
  return answer.data !== undefined || answer.error !== null;
}

/**
 * What stands where all time's lists go, from the twelve months they cover.
 *
 * The lists once the screen's own figures over those months say they held what the lists could
 * list, and not before. Until then the screen may yet turn out to be one with no people at all,
 * which stands in place of the lists itself, and lists set out for that moment would each ask a
 * year's question nobody would read. Where those months held nothing, the state in place of the
 * lists is settled only once the record and all time's own figures have answered: until then the
 * screen may yet turn out to be a website nobody has visited, or one with no people at all — and a
 * state shown for the moment before would be swapped for another. Nothing at all is said of any
 * population; no people only where everybody came.
 *
 * Where the screen's own figures, or everybody's, could not be had there is no telling what the
 * months held, and the screen says what went wrong rather than waiting for an answer that is not
 * coming — once the record and all time's own figures have answered, like any other state in place
 * of the lists, so a website nobody has visited, or one with no people at all, never shows a
 * refusal in a place it is about to give up.
 *
 * @param everybody Everybody's figures over the twelve months, as they are being asked for.
 * @param own The figures over them of whoever the screen is kept to, as they are being asked for.
 * @param settled Whether the record and all time's own figures have answered.
 */
function listsOver(
  everybody: { readonly data: Totals | undefined; readonly error: Error | null },
  own: { readonly data: Totals | undefined; readonly error: Error | null },
  settled: boolean,
): ListsShown {
  if (own.data === undefined) {
    return own.error !== null && settled ? 'refused' : 'waiting';
  }

  if (!nought(own.data)) {
    return 'lists';
  }

  if (!settled || !answered(everybody)) {
    return 'waiting';
  }

  if (everybody.data === undefined) {
    return 'refused';
  }

  return nought(everybody.data) ? 'nothing' : 'noPeople';
}

interface PeopleAbsentProps {
  /** The period's breakdown of what was judged, as it is being asked for. */
  readonly breakdown: Breakdown;
  /** Where the picture says judging has reached, or undefined until that is known. */
  readonly reached: JudgingReach | undefined;
  /** Why the picture the period was drawn from could not be had, or nothing. */
  readonly pictureProblem: Error | null;
  readonly onShowEveryone: () => void;
}

/**
 * The screen a period shows when the website had traffic and none of it was counted as people.
 *
 * One state in place of the picture and every list, because eight cards each saying nothing is
 * eight ways of saying one thing. Several bodies, because "nobody was a person", "nobody has been
 * looked at yet" and "nobody so far, with more still to look at" are different facts — and a
 * period judging has not finished is never reported as one with no people in it. Every one offers
 * everyone back, since the screen was kept to people on purpose and the way out is the choice that
 * kept it.
 *
 * Which of them it is takes both the breakdown and the picture. Where either could not be had the
 * screen says so, rather than guessing at an answer it had no way to check.
 */
function PeopleAbsent({ breakdown, reached, pictureProblem, onShowEveryone }: PeopleAbsentProps) {
  const t = useTranslations('dashboard.population.empty');
  const problem = pictureProblem ?? breakdown.error;

  if (problem !== null) {
    return <FailureNotice error={problem} />;
  }

  if (breakdown.data === undefined || reached === undefined) {
    return <ListWaiting />;
  }

  const { reason, body } = absence(breakdown.data.groups.length > 0, reached);

  return (
    <ListEmpty
      icon={reason === 'nobody' ? UserRound : ScanSearch}
      title={t(`${reason}.title`)}
      body={t(body)}
      action={
        <Button tone="secondary" size="sm" onClick={onShowEveryone}>
          {t('action')}
        </Button>
      }
    />
  );
}

/**
 * Where the picture being drawn says judging has reached: the first of its buckets still being
 * judged, nothing where every one has settled, and whether not one of its buckets has settled, as
 * `judgedNoneOf` decides it — or undefined until that picture is in.
 *
 * @param drawingActivity Whether the picture is of how much was read rather than of who came.
 * @param activity How much was read, where that is the picture.
 * @param who Who came, where that is the picture.
 */
function reachedIn(
  drawingActivity: boolean,
  activity: Activity | undefined,
  who: TrafficPicture | undefined,
): JudgingReach | undefined {
  if (drawingActivity) {
    return (
      activity && {
        stillJudging: activity.stillJudging,
        none: judgedNoneOf(activity, activity.points.length, activity.stillJudging),
      }
    );
  }

  return who && reachedBy(who);
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
 * Two answers about the same buckets, joined into the rows a chart and a table both read, with the
 * window they were counted over and where the judging stops — which for everybody is nowhere,
 * since every report counts as it arrives.
 *
 * A month of all time left out — never counted, or, for the people's figures, kept before its
 * visits were all judged — stays nothing in both measures, rather than becoming nought.
 *
 * @param views Page views, bucket by bucket.
 * @param visitors Visitors, over the same buckets.
 * @param opens Where the picture opens, where that is later than the first bucket begins — all
 * time's first day — so the first bucket is written as the days of it that were counted.
 */
function align(
  views: Series | AllTimeSeries | undefined,
  visitors: Series | AllTimeSeries | undefined,
  opens?: string,
): Activity | undefined {
  if (!views || !visitors) {
    return undefined;
  }

  const byBucket = new Map(visitors.points.map((point) => [point.bucketStart, point.value]));
  const points = views.points.map((point) => {
    const counted = byBucket.get(point.bucketStart);

    return {
      start: point.bucketStart,
      pageViews: point.value,
      visitors: counted === undefined ? 0 : counted,
    };
  });

  return {
    from: opens ?? views.from,
    to: views.to,
    completeTo: views.completeTo,
    points,
    stillJudging: stillJudgingFrom({
      to: views.to,
      completeTo: views.completeTo,
      buckets: points.map((point) => point.start),
    }),
  };
}
