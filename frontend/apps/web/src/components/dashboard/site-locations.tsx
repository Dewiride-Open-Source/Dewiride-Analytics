'use client';

import { MapPin } from 'lucide-react';
import { useLocale, useTranslations } from 'next-intl';
import { type ReactNode, useMemo, useState } from 'react';
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
import { countryNames } from '@/lib/analytics/places';
import type { LocationGrouping, SiteLocation } from '@/lib/api/schemas';
import { useLocations } from '@/lib/queries/sites';

interface SiteLocationsProps {
  readonly siteId: string;
  readonly window: AnalyticsWindow;
}

/** How many places are shown at once, matching the page list beneath it. */
const PER_PAGE = 10;

/**
 * Where a website's readers were, over a period.
 *
 * Countries and towns are the same list read two ways rather than two cards, because they answer
 * the same question at two levels of detail and nobody wants both on screen at once. Switching
 * between them returns to the top, since a position in one list means nothing in the other.
 *
 * The screen above gives this a key that changes with the website and the period, so choosing
 * either starts a fresh list rather than leaving somebody on the fourth screenful of a list that
 * no longer exists.
 */
export function SiteLocations({ siteId, window }: SiteLocationsProps) {
  const t = useTranslations('dashboard.locations');
  const [grouping, setGrouping] = useState<LocationGrouping>('country');
  const [offset, setOffset] = useState(0);
  const places = useLocations(siteId, window, grouping, PER_PAGE, offset);

  function regroup(next: LocationGrouping) {
    setGrouping(next);
    setOffset(0);
  }

  if (places.isError) {
    return <FailureNotice error={places.error} />;
  }

  if (places.isPending) {
    return <ListWaiting />;
  }

  if (places.data.totalPlaces === 0) {
    return <ListEmpty icon={MapPin} title={t('empty.title')} body={t('empty.body')} />;
  }

  return (
    <PlaceList
      places={places.data.places}
      visitors={places.data.visitors}
      totalPlaces={places.data.totalPlaces}
      mostVisitors={places.data.mostVisitors}
      grouping={grouping}
      offset={offset}
      busy={places.isFetching}
      onRegroup={regroup}
      onMove={setOffset}
    />
  );
}

interface PlaceListProps {
  /** The slice on screen, busiest first, as the engine returned it. */
  readonly places: readonly SiteLocation[];
  /** Readers across the whole period, which every share is taken against. */
  readonly visitors: number;
  /** How many places the period holds altogether. */
  readonly totalPlaces: number;
  /** Readers in the busiest place, which every bar is drawn against. */
  readonly mostVisitors: number;
  readonly grouping: LocationGrouping;
  /** How many places were passed over to reach this slice. */
  readonly offset: number;
  /** Whether the next slice is still being read. */
  readonly busy: boolean;
  readonly onRegroup: (grouping: LocationGrouping) => void;
  readonly onMove: (offset: number) => void;
}

function PlaceList({
  places,
  visitors,
  totalPlaces,
  mostVisitors,
  grouping,
  offset,
  busy,
  onRegroup,
  onMove,
}: PlaceListProps) {
  const t = useTranslations('dashboard.locations');
  const locale = useLocale();
  const named = useMemo(() => countryNames(locale), [locale]);

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
            { value: 'country', label: t('grouping.country') },
            { value: 'town', label: t('grouping.town') },
          ]}
          value={grouping}
          onChange={onRegroup}
        />
      </header>

      <ul className="flex flex-col gap-0.5">
        {places.map((place) => (
          <RankedRow
            key={`${place.countryCode}:${place.place}`}
            name={<PlaceName place={place} grouping={grouping} named={named} />}
            detail={
              <>
                {t('visitors', { count: place.visitors })}
                <span aria-hidden> · </span>
                {t('views', { count: place.pageViews })}
              </>
            }
            part={place.visitors}
            whole={visitors}
            most={mostVisitors}
          />
        ))}
      </ul>

      {/*
        Shown only when it is the answer rather than a footnote to it. A site whose visitors
        almost all resolve to nowhere is nearly always a site whose engine never sees their
        address, and without this the screen reports that as though it were a finding about the
        readers instead of a setting somebody can change.
      */}
      {mostlyUnplaced(places, visitors, grouping) ? (
        <p className="rounded-md bg-surface-muted px-3 py-2 text-sm text-foreground-muted">
          {t('mostlyUnplaced')}
        </p>
      ) : null}

      <RankedNav
        label={t('nav.label')}
        offset={offset}
        shown={places.length}
        total={totalPlaces}
        step={PER_PAGE}
        busy={busy}
        onMove={onMove}
      />

      {/*
        Required rather than courteous. The data behind these rows is published under a licence
        whose one condition is a link back from any page that shows results from it, so this line
        goes wherever the rows go. What a town is worth is said beside it only when towns are
        what is on screen — a caveat about something a reader is not looking at is noise.
      */}
      <p className="text-xs text-foreground-subtle">
        {t.rich('attribution', { source: creditLink })}
        {grouping === 'town' ? ` ${t('estimate')}` : null}
      </p>
    </Card>
  );
}

/** The link back the place data's licence requires wherever its results are shown. */
function creditLink(label: ReactNode) {
  return (
    <a
      href="https://db-ip.com"
      target="_blank"
      rel="noreferrer"
      className="underline underline-offset-2 hover:text-foreground-muted"
    >
      {label}
    </a>
  );
}

interface PlaceNameProps {
  readonly place: SiteLocation;
  readonly grouping: LocationGrouping;
  readonly named: (code: string) => string | null;
}

/**
 * What one row is called.
 *
 * A country is written in the reader's own language from its stored code. A town is written as
 * the geolocation data spells it — which is English, whoever is reading — with its country beside
 * it, because a great many town names belong to more than one country.
 *
 * A place nothing could be established about says so. It is a common row on an installation whose
 * proxy does not pass its visitors' addresses through, and hiding it would leave that install
 * looking like it had barely any readers.
 */
function PlaceName({ place, grouping, named }: PlaceNameProps) {
  const t = useTranslations('dashboard.locations');
  const country = named(place.countryCode);

  if (grouping === 'country') {
    return country ?? <span className="text-foreground-muted">{t('unknown')}</span>;
  }

  if (place.place === '') {
    return (
      <span className="text-foreground-muted">
        {country ? t('unknownTownIn', { country }) : t('unknown')}
      </span>
    );
  }

  return (
    <>
      {place.place}
      {country ? <span className="text-foreground-muted">, {country}</span> : null}
    </>
  );
}

/**
 * Whether most of a period's visitors could not be placed at all.
 *
 * Asked of countries only. A visitor whose country is known and whose town is not is perfectly
 * ordinary — address ranges are allocated to networks rather than to streets — but a visitor with
 * no country resolved almost certainly arrived without a usable address.
 *
 * Only the slice on screen is examined, and that is sufficient: places are ordered by how many
 * visitors were in them, so one holding more than half of them is always the first row.
 */
function mostlyUnplaced(
  places: readonly SiteLocation[],
  visitors: number,
  grouping: LocationGrouping,
): boolean {
  if (grouping !== 'country') {
    return false;
  }

  const unplaced = places.find((place) => place.place === '');

  return unplaced !== undefined && unplaced.visitors * 2 > visitors;
}
