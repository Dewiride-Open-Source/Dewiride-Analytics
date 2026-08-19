'use client';

import { MonitorSmartphone } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
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
import { shareOf } from '@/lib/analytics/share';
import type { DeviceKind, SiteDevice, SiteSoftware, SoftwareGrouping } from '@/lib/api/schemas';
import { useDevices, useSoftware } from '@/lib/queries/sites';

interface SiteDevicesProps {
  readonly siteId: string;
  readonly window: AnalyticsWindow;
}

/** How many names are shown at once, matching the lists around it. */
const PER_PAGE = 10;

/** The three ways the card can be read, in the order they are offered. */
type DeviceView = 'device' | SoftwareGrouping;

/**
 * The fill each kind of device is drawn in.
 *
 * Two hues in two weights, and a grey for the visits nothing could be established about. They
 * differ in lightness as well as in colour, so the bar still separates for a reader who cannot
 * separate the hues — and the colour is never the answer in any case: every kind is named in the
 * list beneath, and the bar is hidden from anyone reading rather than looking.
 */
const DEVICE_FILLS: Readonly<Record<DeviceKind, string>> = {
  desktop: 'bg-chart-1',
  phone: 'bg-chart-2',
  tablet: 'bg-chart-1/50',
  other: 'bg-chart-2/50',
  unknown: 'bg-foreground-subtle',
};

/**
 * What a website's readers were reading on, over a period.
 *
 * Three answers about one audience rather than three cards: which kind of thing they read on,
 * which browser, and which system. The device split is always asked for — it carries the total
 * the card states, and it is five rows whichever way the card is being read — while the two lists
 * are asked for only while somebody is looking at them.
 *
 * The screen above gives this a key that changes with the website and the period, so choosing
 * either starts afresh rather than leaving somebody on a screenful that no longer exists.
 */
export function SiteDevices({ siteId, window }: SiteDevicesProps) {
  const t = useTranslations('dashboard.devices');
  const [view, setView] = useState<DeviceView>('device');
  const [offset, setOffset] = useState(0);
  const grouping: SoftwareGrouping = view === 'system' ? 'system' : 'browser';
  const devices = useDevices(siteId, window);
  const software = useSoftware(siteId, window, grouping, PER_PAGE, offset, view !== 'device');

  function show(next: DeviceView) {
    setView(next);
    setOffset(0);
  }

  if (devices.isError) {
    return <FailureNotice error={devices.error} />;
  }

  if (devices.isPending) {
    return <ListWaiting />;
  }

  if (devices.data.visitors === 0) {
    return <ListEmpty icon={MonitorSmartphone} title={t('empty.title')} body={t('empty.body')} />;
  }

  const visitors = devices.data.visitors;

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
          label={t('view.label')}
          options={[
            { value: 'device', label: t('view.device') },
            { value: 'browser', label: t('view.browser') },
            { value: 'system', label: t('view.system') },
          ]}
          value={view}
          onChange={show}
        />
      </header>

      {view === 'device' ? (
        <DeviceSplit devices={devices.data.devices} visitors={visitors} />
      ) : (
        <SoftwareList
          software={software.data?.names}
          failure={software.error}
          grouping={grouping}
          offset={offset}
          busy={software.isFetching}
          totalNames={software.data?.totalNames ?? 0}
          mostVisitors={software.data?.mostVisitors ?? 0}
          visitors={software.data?.visitors ?? visitors}
          onMove={setOffset}
        />
      )}
    </Card>
  );
}

interface DeviceSplitProps {
  readonly devices: readonly SiteDevice[];
  readonly visitors: number;
}

/**
 * How a period's readers divide between kinds of device.
 *
 * A bar and a list rather than a ranked list on its own, because there are only ever a handful of
 * kinds and the answer people come for is the proportion between them. The bar is the summary and
 * the list is the answer: the words carry the figures, and the bar is hidden from a screen reader
 * because it would tell them nothing the list does not.
 */
function DeviceSplit({ devices, visitors }: DeviceSplitProps) {
  const t = useTranslations('dashboard.devices');
  const kinds = useTranslations('dashboard.devices.kind');
  const format = useFormatter();

  return (
    <>
      <div
        aria-hidden
        className="flex h-2.5 w-full gap-0.5 overflow-hidden rounded-full bg-surface-muted"
      >
        {devices.map((device) => (
          <span
            key={device.kind}
            className={DEVICE_FILLS[device.kind]}
            style={{ width: sliver(device.visitors, visitors) }}
          />
        ))}
      </div>

      <ul className="flex flex-col">
        {devices.map((device) => (
          <li
            key={device.kind}
            className="flex flex-col gap-1 border-t border-border py-2.5 first:border-t-0 first:pt-0 sm:flex-row sm:items-center sm:gap-3"
          >
            <span className="flex items-center gap-2 sm:flex-1">
              <span
                aria-hidden
                className={`size-2.5 shrink-0 rounded-full ${DEVICE_FILLS[device.kind]}`}
              />
              <span className="text-sm text-foreground">{kinds(device.kind)}</span>
            </span>
            <span className="flex items-center justify-between gap-3 sm:justify-end">
              <span className="text-sm text-foreground-muted tabular-nums">
                {t('visitors', { count: device.visitors })}
                <span aria-hidden> · </span>
                {t('views', { count: device.pageViews })}
              </span>
              <span className="w-12 shrink-0 text-right text-sm font-medium text-foreground tabular-nums">
                {format.number(shareOf(device.visitors, visitors), {
                  style: 'percent',
                  maximumFractionDigits: 0,
                })}
              </span>
            </span>
          </li>
        ))}
      </ul>

      {/*
        Shown only when it is the answer rather than a footnote to it. A website whose visits
        mostly carry nothing that names a device is usually a website whose traffic is mostly not
        people, and without this the card reads as though the product had failed at its job. It
        says what happened and points at the card that says what those visits were, and stops
        short of naming a cause it cannot establish.
      */}
      {mostlyUnknown(devices, visitors) ? (
        <p className="rounded-md bg-surface-muted px-3 py-2 text-sm text-foreground-muted">
          {t('mostlyUnknown')}
        </p>
      ) : null}
    </>
  );
}

interface SoftwareListProps {
  /** The slice on screen, or nothing while the first one is being read. */
  readonly software: readonly SiteSoftware[] | undefined;
  /** Why the list could not be read, where it could not be. */
  readonly failure: Error | null;
  readonly grouping: SoftwareGrouping;
  /** Readers across the whole period, which every share is taken against. */
  readonly visitors: number;
  /** How many names the period holds altogether. */
  readonly totalNames: number;
  /** Readers on the commonest name, which every bar is drawn against. */
  readonly mostVisitors: number;
  /** How many names were passed over to reach this slice. */
  readonly offset: number;
  /** Whether the next slice is still being read. */
  readonly busy: boolean;
  readonly onMove: (offset: number) => void;
}

/**
 * The browsers or the operating systems a period's readers used.
 *
 * Kept inside the card rather than replacing it while an answer is on its way, so switching
 * between the three views moves the rows instead of collapsing the card and pushing everything
 * below it up the page.
 */
function SoftwareList({
  software,
  failure,
  grouping,
  visitors,
  totalNames,
  mostVisitors,
  offset,
  busy,
  onMove,
}: SoftwareListProps) {
  const t = useTranslations('dashboard.devices');

  if (failure) {
    return <FailureNotice error={failure} />;
  }

  if (!software) {
    return <div className="h-40 animate-pulse rounded-md bg-surface-muted" />;
  }

  return (
    <>
      <ul className="flex flex-col gap-0.5">
        {software.map((name) => (
          <RankedRow
            key={name.name}
            name={
              name.name === '' ? (
                <span className="text-foreground-muted">{t('unknown')}</span>
              ) : (
                name.name
              )
            }
            detail={
              <>
                {t('visitors', { count: name.visitors })}
                <span aria-hidden> · </span>
                {t('views', { count: name.pageViews })}
              </>
            }
            part={name.visitors}
            whole={visitors}
            most={mostVisitors}
          />
        ))}
      </ul>

      <RankedNav
        label={grouping === 'browser' ? t('nav.browsers') : t('nav.systems')}
        offset={offset}
        shown={software.length}
        total={totalNames}
        step={PER_PAGE}
        busy={busy}
        onMove={onMove}
      />
    </>
  );
}

/**
 * Whether most of a period's readers carried nothing that names a device.
 *
 * Only the unresolved kind is examined, and that is enough: the kinds are ordered by how many
 * readers were on them, so one holding more than half of them is always the first row.
 */
function mostlyUnknown(devices: readonly SiteDevice[], visitors: number): boolean {
  const nameless = devices.find((device) => device.kind === 'unknown');

  return nameless !== undefined && nameless.visitors * 2 > visitors;
}

/** A kind's share of the bar, kept off the scale's edge so a sliver is still visible. */
function sliver(part: number, whole: number): string {
  return `${Math.max(shareOf(part, whole) * 100, 1.5)}%`;
}
