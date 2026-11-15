'use client';

import { Download } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { type ReactNode, useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { ListSwitch } from '@/components/dashboard/ranked-list';
import { IssuedKey } from '@/components/dashboard/server-keys';
import { Button, buttonStyle } from '@/components/ui/button';
import { CopyButton } from '@/components/ui/copy-button';
import { Dialog } from '@/components/ui/dialog';
import { FailureNotice } from '@/components/ui/failure-notice';
import type { ServerKey, SiteRole } from '@/lib/api/schemas';
import { WORDPRESS_PLUGIN } from '@/lib/downloads';
import { useCreateServerKey, useServerKeys } from '@/lib/queries/server-keys';
import { cn } from '@/lib/styling';
import { trackingSnippet } from '@/lib/tracker/snippet';

interface TrackingCodeProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly siteId: string;
  readonly siteDomain: string;
  /** What the reader may do with the website, which decides whether they can create a key. */
  readonly siteRole: SiteRole;
  /** IANA zone the website's days are counted in, so dates read the same everywhere. */
  readonly timeZoneId: string;
}

/** The ways of putting the tracking code on a website, in the order they are offered. */
type Route = 'any' | 'wordpress';

/**
 * The name a key for the plugin is created under, which is how the steps find it again: the same
 * in every language, as a product's name is.
 */
const WORDPRESS_KEY_NAME = 'WordPress';

/**
 * Everything somebody needs to start counting a website, for whatever it is built with.
 *
 * One panel read two ways rather than a panel each, because both end in the same place and nobody
 * needs both on screen at once. The choice is kept while the screen is, so somebody who closes the
 * panel half-way through and opens it again finds the steps they were following.
 */
export function TrackingCode({
  open,
  onClose,
  siteId,
  siteDomain,
  siteRole,
  timeZoneId,
}: TrackingCodeProps) {
  const t = useTranslations('install');
  const [route, setRoute] = useState<Route>('any');
  const [issued, setIssued] = useState<string | null>(null);
  const creating = useCreateServerKey(siteId);
  const origin = useDashboardOrigin();

  /**
   * Closes the panel, taking the one secret it may hold with it, and any refusal of an attempt to
   * create one.
   *
   * Cleared as the panel is dismissed, as the server keys panel clears its own, so that opening
   * the steps again cannot show a key somebody has already walked away from. The panel stays
   * mounted while it is closed, so nothing else would clear them.
   */
  function close() {
    setIssued(null);
    creating.reset();
    onClose();
  }

  return (
    <Dialog open={open} onClose={close} title={t('title')} closeLabel={t('close')}>
      <div className="flex flex-col gap-5">
        {/* Held to its own width: stretched across the panel, the choices sit in a bar of nothing. */}
        <div className="self-start">
          <ListSwitch
            label={t('route.label')}
            options={[
              { value: 'any', label: t('route.any') },
              { value: 'wordpress', label: t('route.wordpress') },
            ]}
            value={route}
            onChange={setRoute}
          />
        </div>

        {route === 'any' ? (
          <AnyWebsite origin={origin} siteId={siteId} siteDomain={siteDomain} />
        ) : (
          <WordPress origin={origin} siteId={siteId} siteDomain={siteDomain}>
            <RobotsStep
              open={open}
              siteId={siteId}
              siteDomain={siteDomain}
              siteRole={siteRole}
              timeZoneId={timeZoneId}
              creating={creating}
              issued={issued}
              onIssued={setIssued}
            />
          </WordPress>
        )}
      </div>
    </Dialog>
  );
}

/** Nothing about the address changes while the screen is open, so a subscriber is never called. */
const unchanging = () => () => {};

/**
 * The address the dashboard is being read on, and nothing on the server, which has no reader.
 *
 * The one address a reader is certain can be reached from outside — the engine may not be
 * published at all — and the one they already know, so it is never written into configuration as
 * a second place for it to be wrong. Read through the store rather than straight off the window so
 * that the server's paint and the browser's first one agree.
 */
function useDashboardOrigin(): string {
  return useSyncExternalStore(
    unchanging,
    () => window.location.origin,
    () => '',
  );
}

interface RouteProps {
  /** Where the dashboard answers, which the website reports to. */
  readonly origin: string;
  readonly siteId: string;
  readonly siteDomain: string;
}

/** The two lines that work on any website, ready to be taken away. */
function AnyWebsite({ origin, siteId, siteDomain }: RouteProps) {
  const t = useTranslations('install');
  const snippet = origin === '' ? '' : trackingSnippet(origin, siteId);

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-foreground-muted">{t('body', { site: siteDomain })}</p>

      {/*
        Wrapped rather than scrolled sideways, and the button sits below rather than over it.
        These are lines somebody checks before pasting them into their own website, and a copy
        button laid across the end of one hides the very part they are checking.
      */}
      <pre
        className={cn(
          'max-h-64 overflow-y-auto rounded-lg border border-border bg-surface-muted p-4',
          'text-xs leading-relaxed whitespace-pre-wrap text-foreground wrap-anywhere',
        )}
      >
        <code>{snippet}</code>
      </pre>

      <div className="flex justify-end">
        <CopyButton value={snippet} className="w-full sm:w-auto" />
      </div>

      <p className="text-sm text-foreground-muted">{t('next')}</p>
    </div>
  );
}

interface WordPressProps extends RouteProps {
  /** The step that adds robots to what is counted, last because nothing before it needs it. */
  readonly children: ReactNode;
}

/**
 * The plugin, the four steps between downloading it and a counted website, and the one that adds
 * robots to what is counted.
 *
 * Every name of something to press on the WordPress side is the one WordPress prints, so it can be
 * found on that screen by reading it off this one.
 */
function WordPress({ origin, siteId, siteDomain, children }: WordPressProps) {
  const t = useTranslations('install.wordpress');

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-foreground-muted">{t('body', { site: siteDomain })}</p>

      <ol className="flex flex-col">
        <Step number={1} title={t('download.title')}>
          <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
            <a href={WORDPRESS_PLUGIN.href} download className={buttonStyle({ size: 'md' })}>
              <Download aria-hidden className="size-4" />
              {t('download.action')}
            </a>
            <span className="text-sm text-foreground-muted tabular-nums">
              {t('download.version', { version: WORDPRESS_PLUGIN.version })}
            </span>
          </div>
        </Step>

        <Step number={2} title={t('upload.title')}>
          <p className="text-sm text-foreground-muted">
            {t.rich('upload.body', { ui: interfaceLabel })}
          </p>
        </Step>

        <Step number={3} title={t('connect.title')}>
          <p className="text-sm text-foreground-muted">
            {t.rich('connect.body', { ui: interfaceLabel })}
          </p>
          <CopyableValue name={t('connect.address')} value={origin} />
          <CopyableValue name={t('connect.siteId')} value={siteId} />
        </Step>

        <Step number={4} title={t('tidy.title')}>
          <p className="text-sm text-foreground-muted">{t('tidy.body')}</p>
        </Step>

        {children}
      </ol>

      <div className="flex flex-col gap-2 border-t border-border pt-4 text-sm text-foreground-muted">
        <p>{t('next')}</p>
        <p>{t.rich('update', { ui: interfaceLabel })}</p>
      </div>
    </div>
  );
}

interface RobotsStepProps {
  readonly open: boolean;
  readonly siteId: string;
  readonly siteDomain: string;
  readonly siteRole: SiteRole;
  readonly timeZoneId: string;
  /** The attempt to create a key, kept by the panel so that closing it clears a refusal. */
  readonly creating: ReturnType<typeof useCreateServerKey>;
  /** The secret of a key created here, at the only moment it is shown. */
  readonly issued: string | null;
  readonly onIssued: (secret: string) => void;
}

/**
 * The key that lets the plugin report what never runs the tracking code, made where somebody
 * setting up WordPress already is.
 *
 * The key is named for what uses it, and its secret is shown once, as on the server keys panel.
 * Somebody who may only view the website is told who can make one rather than shown a button that
 * would be refused. Once a key for WordPress exists, when it was last used is how somebody tells
 * the plugin is reporting — the dashboard has no other way to know. Until the keys have been read,
 * the button waits, so that a key that already exists is not made again by somebody who could not
 * see it yet. A new key takes focus as it appears, in place of the button that made it.
 */
function RobotsStep({
  open,
  siteId,
  siteDomain,
  siteRole,
  timeZoneId,
  creating,
  issued,
  onIssued,
}: RobotsStepProps) {
  const t = useTranslations('install.wordpress.robots');
  const format = useFormatter();
  const keys = useServerKeys(siteId, open);
  const issuedArea = useRef<HTMLDivElement>(null);
  const latest = latestNamed(keys.data, WORDPRESS_KEY_NAME);
  const label = latest ? t('another') : t('create');

  useEffect(() => {
    if (issued) {
      issuedArea.current?.focus();
    }
  }, [issued]);

  /**
   * Asks for a key without leaving a promise nobody is waiting on; a refusal is already on the
   * screen through the notice below.
   */
  function create() {
    creating.mutate(WORDPRESS_KEY_NAME, { onSuccess: (created) => onIssued(created.secret) });
  }

  return (
    <Step number={5} title={t('title')}>
      <p className="text-sm text-foreground-muted">{t('body')}</p>

      {/*
        While a new secret is on the screen, copying it is the one thing left to do, so the button
        that would make another is put away until the panel is opened again.
      */}
      {siteRole === 'viewer' ? (
        <p className="text-sm text-foreground-muted">{t('viewer', { site: siteDomain })}</p>
      ) : issued ? null : (
        <div>
          <Button
            tone={latest ? 'secondary' : 'primary'}
            busy={creating.isPending}
            disabled={keys.isPending}
            onClick={create}
            className="w-full sm:w-auto"
          >
            {creating.isPending ? t('creating') : label}
          </Button>
        </div>
      )}

      {keys.isError ? <FailureNotice error={keys.error} /> : null}
      {creating.isError ? <FailureNotice error={creating.error} /> : null}

      {issued ? (
        <div ref={issuedArea} tabIndex={-1} className="flex flex-col gap-3 outline-none">
          <IssuedKey secret={issued} />
          <p className="text-sm text-foreground-muted">{t.rich('paste', { ui: interfaceLabel })}</p>
        </div>
      ) : null}

      {latest && !issued ? (
        <p className="text-sm text-foreground-muted">
          {latest.lastUsedAt
            ? t('lastUsed', {
                preview: latest.preview,
                date: format.dateTime(new Date(latest.lastUsedAt), {
                  dateStyle: 'medium',
                  timeZone: timeZoneId,
                }),
              })
            : t('neverUsed', { preview: latest.preview })}
        </p>
      ) : null}
    </Step>
  );
}

/**
 * The most recently created of a website's keys with a name, or nothing when it has none.
 *
 * @param keys The website's keys, when they have been read.
 * @param name The name.
 */
function latestNamed(keys: readonly ServerKey[] | undefined, name: string): ServerKey | undefined {
  return keys
    ?.filter((key) => key.name === name)
    .reduce<ServerKey | undefined>(
      (latest, key) => (latest && latest.createdAt >= key.createdAt ? latest : key),
      undefined,
    );
}

/**
 * The name of something on the WordPress screen, set apart from the words around it so that the
 * one thing to look for there is the thing that stands out here.
 */
function interfaceLabel(words: ReactNode) {
  return <span className="font-medium text-foreground">{words}</span>;
}

interface StepProps {
  /** Its place in the list, drawn for the eye; the list itself says it to a screen reader. */
  readonly number: number;
  readonly title: string;
  readonly children: ReactNode;
}

/**
 * One step, numbered, with what it takes beneath its name.
 *
 * On a phone what it takes runs the full width under the number and the name, since a column kept
 * free beside it would leave a website's identifier too narrow a line to sit on. From a tablet up
 * it lines up under the name, and a rule joins each number to the next.
 */
function Step({ number, title, children }: StepProps) {
  return (
    <li className="group relative grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-2 pb-6 last:pb-0 sm:gap-x-4">
      <span
        aria-hidden
        className="absolute top-10 bottom-3 left-3.5 hidden w-px bg-border group-last:hidden sm:block"
      />
      <span
        aria-hidden
        className={cn(
          'flex size-7 items-center justify-center rounded-full bg-accent-soft',
          'text-sm font-semibold text-accent-strong tabular-nums ring-1 ring-accent/25',
        )}
      >
        {number}
      </span>
      <h3 className="self-center text-sm font-semibold text-foreground">{title}</h3>
      <div className="col-span-2 flex min-w-0 flex-col gap-3 sm:col-span-1 sm:col-start-2">
        {children}
      </div>
    </li>
  );
}

interface CopyableValueProps {
  /** The name of the box it is pasted into on the other side, which is printed above it here. */
  readonly name: string;
  readonly value: string;
}

/**
 * One value somebody pastes into a box of the same name in WordPress.
 *
 * The name and the button share a line and the value has the whole width beneath them, so a
 * website's identifier sits on one line even on a phone rather than breaking part-way through.
 * A single press selects all of it, for a browser that refuses to copy.
 */
function CopyableValue({ name, value }: CopyableValueProps) {
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-center justify-between gap-3">
        <span className="text-sm font-medium text-foreground">{name}</span>
        <CopyButton value={value} what={name} />
      </div>
      <code
        className={cn(
          'block rounded-md border border-border bg-surface-muted px-3 py-2',
          'font-mono text-xs break-all text-foreground select-all',
        )}
      >
        {value}
      </code>
    </div>
  );
}
