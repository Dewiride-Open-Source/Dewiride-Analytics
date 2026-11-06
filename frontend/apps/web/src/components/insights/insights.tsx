'use client';

import { History, ScanSearch } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { type ReactNode, useId, useState } from 'react';
import { SiteScreen } from '@/components/chrome/site-screen';
import { MetricCard, MetricCardSkeleton } from '@/components/dashboard/metric-card';
import { PopulationSwitch } from '@/components/dashboard/population-switch';
import { ListEmpty, ListWaiting } from '@/components/dashboard/ranked-list';
import { TrackingCode } from '@/components/dashboard/tracking-code';
import { MonthsTable } from '@/components/insights/months-table';
import { PopularTimes } from '@/components/insights/popular-times';
import { Button } from '@/components/ui/button';
import { FailureNotice } from '@/components/ui/failure-notice';
import {
  JUDGING_BEHIND_AFTER,
  type Measure,
  type RecordShown,
  recordShown,
  writableDay,
} from '@/lib/analytics/all-time';
import { everJudged, reachedBy } from '@/lib/analytics/people-absence';
import { usePeopleOnly } from '@/lib/analytics/use-people-only';
import type { Insights as InsightsAnswer, Site } from '@/lib/api/schemas';
import { sentences, useLeftOut } from '@/lib/charts/use-left-out';
import { useAllTimeTraffic, useInsights } from '@/lib/queries/sites';
import { readableZone } from '@/lib/time-zones';

/**
 * What a website's whole record says.
 *
 * A screen of its own because it is looked back over rather than glanced at: the overview answers how
 * a stretch of days went, and this answers how the website has done since it began, when it is read
 * most, and how each month compares with every other. It has no period, since it is every month there
 * is, and who it counts is the one choice it takes.
 *
 * One screen from before its website is known until its figures are in. It is handed over both as what
 * waits and as the screen itself, and both stand in the same place, so it is drawn once and then only
 * filled in: whatever somebody is on when the website or the figures arrive is still under them.
 *
 * How the reader has chosen to read the record is held here, above the screen and above whatever
 * stands in its place, so that it is one choice whatever the screen passes through.
 */
export function Insights() {
  const [measure, setMeasure] = useState<Measure>('total');
  const [figuresOpen, setFiguresOpen] = useState(false);
  const reading: RecordReading = {
    measure,
    onMeasure: setMeasure,
    figuresOpen,
    onFiguresToggle: setFiguresOpen,
  };

  return (
    <SiteScreen waiting={<SiteInsights reading={reading} />}>
      {(site) => <SiteInsights site={site} reading={reading} />}
    </SiteScreen>
  );
}

/** How the reader has chosen to read the record. */
interface RecordReading {
  /** What each month is being read as. */
  readonly measure: Measure;
  readonly onMeasure: (measure: Measure) => void;
  /** Whether the figures behind the strips are unfolded. */
  readonly figuresOpen: boolean;
  readonly onFiguresToggle: (open: boolean) => void;
}

interface SiteInsightsProps {
  /** The website, or nothing while which one it is has still to be read. */
  readonly site?: Site;
  readonly reading: RecordReading;
}

/**
 * One website's record, for whoever the screen is kept to; and the same screen before its website is
 * known, with stand-ins for whatever only the website can say.
 *
 * Everybody is always asked about as well, since it is what tells a website nobody has been to from
 * one in which nobody was judged a person. Under everybody it is the same question filed under the
 * same name, so it costs nothing.
 */
function SiteInsights({ site, reading }: SiteInsightsProps) {
  // Remembered people are written into the address once there is a website for them to be the people
  // of: until then the screen may still turn out to be one the choice means nothing on.
  const { peopleOnly, population, showOnlyPeople } = usePeopleOnly({ seeding: site !== undefined });
  const [showingCode, setShowingCode] = useState(false);
  const everybody = useInsights(site, 'everybody');
  const own = useInsights(site, population);
  const shown = recordShown(everybody, own);
  const fallenBehind =
    shown.state === 'ready' &&
    Date.parse(shown.insights.to) - Date.parse(shown.insights.completeTo) > JUDGING_BEHIND_AFTER;

  return (
    <div className="flex flex-col gap-6">
      <ScreenHeading
        caption={
          <RecordCaption site={site} firstDay={(own.data ?? everybody.data)?.firstDay ?? null} />
        }
        peopleOnly={peopleOnly}
        onShowOnlyPeople={showOnlyPeople}
      />

      {/* Only above figures, or stand-ins for them: with no number on the screen it qualifies nothing. */}
      {peopleOnly && (shown.state === 'ready' || shown.state === 'waiting') ? (
        <KeptToPeople fallenBehind={fallenBehind} />
      ) : null}

      <RecordPlace
        shown={shown}
        site={site}
        peopleOnly={peopleOnly}
        reading={reading}
        onShowCode={() => setShowingCode(true)}
        onShowEveryone={() => showOnlyPeople(false)}
      />

      {site === undefined ? null : (
        <TrackingCode
          open={showingCode}
          onClose={() => setShowingCode(false)}
          siteId={site.id}
          siteDomain={site.domain}
        />
      )}
    </div>
  );
}

interface ScreenHeadingProps {
  /** What is under the screen's name: the website and how far back it reaches, as far as known. */
  readonly caption: ReactNode;
  readonly peopleOnly: boolean;
  readonly onShowOnlyPeople: (peopleOnly: boolean) => void;
}

/**
 * The screen's name and the one choice it takes. The words take the whole of a phone's width, so the
 * choice sits beneath them however long they turn out to be.
 */
function ScreenHeading({ caption, peopleOnly, onShowOnlyPeople }: ScreenHeadingProps) {
  const t = useTranslations('insights');

  return (
    <header className="flex flex-wrap items-end justify-between gap-4">
      <div className="flex w-full min-w-0 flex-col gap-1 sm:w-auto">
        <h1 className="truncate text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
          {t('title')}
        </h1>
        <p className="max-w-2xl text-sm text-foreground-muted">{caption}</p>
      </div>
      <PopulationSwitch peopleOnly={peopleOnly} onChange={onShowOnlyPeople} />
    </header>
  );
}

interface RecordCaptionProps {
  /** The website, or nothing while which one it is has still to be read. */
  readonly site: Site | undefined;
  /** The first day of its record, or nothing where it has none or that is not known yet. */
  readonly firstDay: string | null;
}

/** The website and how far back its record reaches, as far as either is known. */
function RecordCaption({ site, firstDay }: RecordCaptionProps) {
  const t = useTranslations('insights');
  const format = useFormatter();

  if (site === undefined) {
    return <span className="block h-5 w-40 animate-pulse rounded-sm bg-surface-muted" />;
  }

  if (firstDay === null) {
    return <>{site.displayName}</>;
  }

  return (
    <>
      {t('caption', {
        site: site.displayName,
        since: format.dateTime(writableDay(firstDay), { dateStyle: 'long', timeZone: 'UTC' }),
      })}
    </>
  );
}

interface KeptToPeopleProps {
  /** Whether the figures are further behind than visits waiting their turn explains. */
  readonly fallenBehind: boolean;
}

/** The one clause every figure below is read under. Said once, here, rather than on each card. */
function KeptToPeople({ fallenBehind }: KeptToPeopleProps) {
  const words = useTranslations('dashboard.population');
  const chart = useTranslations('dashboard.chart');

  return (
    <p className="-mt-2 text-sm text-foreground-muted">
      {sentences(words('caption'), fallenBehind ? chart('people.judging') : null)}
    </p>
  );
}

interface RecordPlaceProps {
  readonly shown: RecordShown;
  /** The website, or nothing while which one it is has still to be read. */
  readonly site: Site | undefined;
  readonly peopleOnly: boolean;
  readonly reading: RecordReading;
  readonly onShowCode: () => void;
  readonly onShowEveryone: () => void;
}

/**
 * What stands under the screen's heading: the record, or the one state in its place.
 *
 * The record on its way and the record itself are the same cards, and a record whose website is not
 * known yet can only be on its way.
 */
function RecordPlace({
  shown,
  site,
  peopleOnly,
  reading,
  onShowCode,
  onShowEveryone,
}: RecordPlaceProps) {
  const t = useTranslations('insights.empty');

  if (site === undefined || shown.state === 'waiting' || shown.state === 'ready') {
    return (
      <WholeRecord
        insights={shown.state === 'ready' ? shown.insights : undefined}
        site={site}
        peopleOnly={peopleOnly}
        reading={reading}
      />
    );
  }

  switch (shown.state) {
    case 'refused':
      return <FailureNotice error={shown.error} />;
    case 'nothing':
      return (
        <ListEmpty
          icon={History}
          title={t('title')}
          body={t('body', { site: site.domain })}
          action={<Button onClick={onShowCode}>{t('action')}</Button>}
        />
      );
    default:
      return <NoPeopleInRecord site={site} onShowEveryone={onShowEveryone} />;
  }
}

interface NoPeopleInRecordProps {
  readonly site: Site;
  readonly onShowEveryone: () => void;
}

/**
 * What stands in place of the record where there were visits and none was counted as a person.
 *
 * "Nobody has been looked at yet" and "none so far of the visits looked at" are different facts, told
 * apart by who came across the record, which is asked only here. A record runs up to the present, and
 * the present is always still being judged, so it can never be said that nobody at all was a person.
 * Either way it offers everyone back, since the screen was kept to people on purpose and the way out
 * is the choice that kept it.
 */
function NoPeopleInRecord({ site, onShowEveryone }: NoPeopleInRecordProps) {
  const t = useTranslations('insights.absent');
  const words = useTranslations('dashboard.population.empty');
  const judged = useAllTimeTraffic(site.id, site.timeZoneId, 'month');
  const everKinds = everJudged(judged).data;

  // An answer already read is still the answer when asking again fails.
  if (judged.data === undefined && judged.error !== null) {
    return <FailureNotice error={judged.error} />;
  }

  if (judged.data === undefined || everKinds === undefined) {
    return <ListWaiting />;
  }

  const reason = everKinds.groups.length > 0 ? 'nobodyYet' : 'unjudged';
  const notOneMonth = reason === 'unjudged' && reachedBy(judged.data).none;

  return (
    <ListEmpty
      icon={ScanSearch}
      title={t(`${reason}.title`)}
      body={notOneMonth ? t('unjudged.bodyAll') : t(`${reason}.body`)}
      action={
        <Button tone="secondary" size="sm" onClick={onShowEveryone}>
          {words('action')}
        </Button>
      }
    />
  );
}

interface WholeRecordProps {
  /** What the record says, or nothing while it is on its way. */
  readonly insights: InsightsAnswer | undefined;
  /** The website, or nothing while which one it is has still to be read. */
  readonly site: Site | undefined;
  readonly peopleOnly: boolean;
  readonly reading: RecordReading;
}

/**
 * Everything the record says, in the order somebody looking back would ask it; and the same cards
 * while it is on its way, each holding the place of its own figures.
 */
function WholeRecord({ insights, site, peopleOnly, reading }: WholeRecordProps) {
  return (
    <>
      <Highlights
        figures={insights}
        peopleOnly={peopleOnly}
        leftOut={
          insights && site ? <LeftOut insights={insights} timeZoneId={site.timeZoneId} /> : null
        }
      />
      <PopularTimes
        insights={insights}
        zone={site && readableZone(site.timeZoneId)}
        figuresOpen={reading.figuresOpen}
        onFiguresToggle={reading.onFiguresToggle}
      />
      <MonthsTable insights={insights} measure={reading.measure} onMeasure={reading.onMeasure} />
    </>
  );
}

interface LeftOutProps {
  readonly insights: InsightsAnswer;
  /** The website's zone, which the months are named in. */
  readonly timeZoneId: string;
}

/** The sentences naming the months a record's figures leave out, where it leaves any out. */
function LeftOut({ insights, timeZoneId }: LeftOutProps) {
  const leftOut = useLeftOut(insights.missing, insights.unjudged, timeZoneId);

  return leftOut === null ? null : <p className="text-sm text-foreground-muted">{leftOut}</p>;
}

interface HighlightsProps {
  /** The figures, or nothing while they are on their way. */
  readonly figures: Pick<InsightsAnswer, 'pageViews' | 'visitors'> | undefined;
  readonly peopleOnly: boolean;
  /** What says which months the figures leave out, where they leave any out. */
  readonly leftOut: ReactNode;
}

/**
 * The record's two headline figures, or their stand-ins until they are in. Labelled as the overview's
 * cards are, so the same figure is called the same thing wherever it is read.
 */
function Highlights({ figures, peopleOnly, leftOut }: HighlightsProps) {
  const t = useTranslations('insights.highlights');
  const metrics = useTranslations('dashboard.metrics');
  const labels = useTranslations(peopleOnly ? 'dashboard.metrics.people' : 'dashboard.metrics');
  const format = useFormatter();
  const heading = useId();

  return (
    <section aria-labelledby={heading} className="flex flex-col gap-3">
      <div className="flex flex-col gap-0.5">
        <h2 id={heading} className="text-base font-semibold text-foreground">
          {t('title')}
        </h2>
        {leftOut}
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        {figures === undefined ? (
          <>
            <MetricCardSkeleton label={labels('pageViews.label')} />
            <MetricCardSkeleton label={labels('visitors.label')} note={metrics('visitors.note')} />
          </>
        ) : (
          <>
            <MetricCard
              label={labels('pageViews.label')}
              value={format.number(figures.pageViews)}
            />
            <MetricCard
              label={labels('visitors.label')}
              value={format.number(figures.visitors)}
              note={metrics('visitors.note')}
            />
          </>
        )}
      </div>
    </section>
  );
}
