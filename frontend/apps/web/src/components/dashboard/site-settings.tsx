'use client';

import {
  Check,
  CircleCheck,
  CirclePause,
  Info,
  LoaderCircle,
  type LucideIcon,
  Newspaper,
  TriangleAlert,
} from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { type ReactNode, type RefObject, useEffect, useMemo, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, TextInput } from '@/components/ui/field';
import { Switch } from '@/components/ui/switch';
import { TimeZoneSelect } from '@/components/ui/time-zone-select';
import { couldBeASitemap, LONGEST_SITEMAP_ADDRESS, SITEMAP_REFUSED } from '@/lib/analytics/sitemap';
import { ApiError } from '@/lib/api/problem';
import type { Site, SitemapStatus, SiteSettings as StoredSettings } from '@/lib/api/schemas';
import {
  useRemoveSite,
  useSitemapStatus,
  useSiteSettings,
  useUpdateSiteSettings,
} from '@/lib/queries/site-settings';
import { cn } from '@/lib/styling';
import { currentZoneName, withZone } from '@/lib/time-zones';
import { useTimeZoneGroups } from '@/lib/use-time-zone-groups';

interface SiteSettingsProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly site: Site;
  /** Called once the website is gone, so the screen can put this away. */
  readonly onRemoved: () => void;
  /**
   * Where focus goes on closing when whatever opened the panel has gone — as the clicks card's own
   * way in does once recording is switched on here.
   */
  readonly returnFocusTo?: RefObject<HTMLElement | null>;
}

/** The longest name the engine will keep, so a name is stopped here rather than refused there. */
const LONGEST_NAME = 253;

/** The longest an address runs to, so the box that confirms one is never shorter than the answer. */
const LONGEST_ADDRESS = 253;

/** The least that is ever said to have passed since a reading, in milliseconds. */
const A_MINUTE = 60_000;

/**
 * Everything about one website its owner decides, and the way to stop measuring it altogether.
 *
 * The panel is mounted whether or not it is open, so what it holds is drawn only while it is: a
 * name typed and then abandoned is gone by the time somebody looks again, and the confirmation
 * that removes a website is never found already armed.
 */
export function SiteSettings({ open, onClose, site, onRemoved, returnFocusTo }: SiteSettingsProps) {
  const t = useTranslations('siteSettings');
  const settings = useSiteSettings(site.id, open);
  const saving = useUpdateSiteSettings(site.id);

  function close() {
    saving.reset();
    onClose();
  }

  /**
   * Saves without producing a promise nobody is waiting on.
   *
   * A refusal is already on the screen through the notice below, so awaiting the attempt here
   * would leave a rejection with nothing to catch it.
   */
  function set(captureClicks: boolean) {
    saving.mutate({ captureClicks });
  }

  return (
    <Dialog
      open={open}
      onClose={close}
      title={t('title')}
      closeLabel={t('close')}
      returnFocusTo={returnFocusTo}
      className="max-w-lg"
    >
      <div className="flex flex-col gap-5">
        <p className="text-sm text-foreground-muted">{t('body', { site: site.domain })}</p>

        {open && settings.isError ? <FailureNotice error={settings.error} /> : null}

        {open && settings.data ? (
          <>
            <WebsiteDetails siteId={site.id} stored={settings.data} />

            <section className="flex flex-col gap-3 border-t border-border pt-5">
              {saving.isError ? <FailureNotice error={saving.error} /> : null}

              <Switch
                label={t('presses.label')}
                hint={t('presses.hint')}
                checked={settings.data.captureClicks}
                busy={saving.isPending}
                onChange={set}
              />
            </section>

            <SitemapDetails site={site} stored={settings.data} />
          </>
        ) : null}

        {/*
          Only while an answer is genuinely on its way. Placeholders left under a refusal say two
          opposite things at once — that something is still coming, and that it is not — and the
          one somebody watches is the one that is still moving.
        */}
        {open && settings.isPending ? (
          <div className="flex flex-col gap-4">
            <div className="h-11 animate-pulse rounded-md bg-surface-muted" />
            <div className="h-11 animate-pulse rounded-md bg-surface-muted" />
            <div className="h-20 animate-pulse rounded-lg bg-surface-muted" />
          </div>
        ) : null}

        {open && site.role === 'owner' ? <Removal site={site} onRemoved={onRemoved} /> : null}
      </div>
    </Dialog>
  );
}

interface WebsiteDetailsProps {
  readonly siteId: string;
  readonly stored: StoredSettings;
}

/**
 * What a website is called, and the zone its days are cut in.
 *
 * The two are saved together because they are one thought: this is the website, and this is the
 * day it is counted in. Only what actually differs is sent, so somebody who came to rename a
 * website cannot move the boundary of its day by passing through the field below.
 *
 * The starting values are the stored ones rather than the ones the screen above is already
 * showing, and the zones offered are widened to include the stored one: a picker that cannot offer
 * the zone a website is counted in would open on a fall-back and save that instead the moment
 * anything else on this form was changed. A zone stored under an earlier name is held, offered and
 * compared under the name it goes by now, so the picker shows the place it is and leaving it alone
 * sends nothing.
 *
 * They are taken again whenever the stored ones move under the form, which is what a save and a
 * re-read both do. Seeded once and left, the boxes would go on holding what was stored a moment
 * ago while the button beside them compares against what is stored now — so the button would arm
 * itself with nobody having typed anything, and pressing it would put the older name back.
 *
 * What is watched is the two values this form is about, not the settings they arrive in. The switch
 * below shares those settings, so anybody who typed a new name and then turned recording off would
 * have had the name taken out from under them by a save that had nothing to do with it.
 *
 * The zones offered are this browser's own, so they can arrive after the form does. The zone the
 * form holds needs none of them to be right: it is the stored one, under the name it goes by now,
 * from the start.
 */
function WebsiteDetails({ siteId, stored }: WebsiteDetailsProps) {
  const t = useTranslations('siteSettings');
  const groups = useTimeZoneGroups();
  const zones = useMemo(
    () => (groups === null ? null : withZone(groups, stored.timeZoneId)),
    [groups, stored.timeZoneId],
  );
  const [seeded, setSeeded] = useState({ name: stored.displayName, zone: stored.timeZoneId });
  const [name, setName] = useState(stored.displayName);
  const [zone, setZone] = useState(() => currentZoneName(stored.timeZoneId));
  const saving = useUpdateSiteSettings(siteId);

  if (seeded.name !== stored.displayName || seeded.zone !== stored.timeZoneId) {
    setSeeded({ name: stored.displayName, zone: stored.timeZoneId });
    setName(stored.displayName);
    setZone(currentZoneName(stored.timeZoneId));
  }

  const trimmed = name.trim();
  const renamed = trimmed.length > 0 && trimmed !== stored.displayName;
  const moved = zone !== currentZoneName(stored.timeZoneId);
  const changed = renamed || moved;

  /**
   * Saves without producing a promise nobody is waiting on.
   *
   * A refusal is already on the screen through the notice below, so awaiting the attempt here
   * would leave a rejection with nothing to catch it.
   */
  function save() {
    saving.mutate({
      ...(renamed ? { displayName: trimmed } : {}),
      ...(moved ? { timeZoneId: zone } : {}),
    });
  }

  return (
    <form
      className="flex flex-col gap-5"
      onSubmit={(event) => {
        event.preventDefault();
        save();
      }}
    >
      <Field label={t('name.label')}>
        {(attributes) => (
          <TextInput
            {...attributes}
            value={name}
            maxLength={LONGEST_NAME}
            autoComplete="off"
            spellCheck={false}
            onChange={(event) => setName(event.target.value)}
            required
          />
        )}
      </Field>

      <Field label={t('timeZone.label')} hint={t('timeZone.hint')}>
        {(attributes) => (
          <TimeZoneSelect
            {...attributes}
            zones={zones}
            value={zone}
            onChange={(event) => setZone(event.target.value)}
            required
          />
        )}
      </Field>

      {saving.isError ? <FailureNotice error={saving.error} /> : null}

      <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-center sm:justify-end">
        {saving.isSuccess && !changed ? (
          <p role="status" className="flex items-center gap-1.5 text-sm text-positive">
            <Check aria-hidden className="size-4" />
            {t('saved')}
          </p>
        ) : null}

        <Button
          type="submit"
          busy={saving.isPending}
          disabled={!changed}
          className="w-full sm:w-auto"
        >
          {saving.isPending ? t('saving') : t('save')}
        </Button>
      </div>
    </form>
  );
}

interface SitemapDetailsProps {
  readonly site: Site;
  readonly stored: StoredSettings;
}

/**
 * Where a website lists its posts, and how reading them has gone.
 *
 * Somebody who may only read the numbers sees the same answer about the same address, and nothing
 * they could press: how a website is set up is theirs to check, and changing what this engine goes
 * and fetches for it is not.
 */
function SitemapDetails({ site, stored }: SitemapDetailsProps) {
  const t = useTranslations('siteSettings.sitemap');
  const mayChange = site.role !== 'viewer';
  const reading = (
    <SitemapReading siteId={site.id} address={stored.sitemapAddress} mayChange={mayChange} />
  );

  return (
    <section className="flex flex-col gap-3 border-t border-border pt-5">
      <h3 className="text-sm font-semibold text-foreground">{t('title')}</h3>

      {mayChange ? (
        <SitemapForm site={site} stored={stored.sitemapAddress}>
          {reading}
        </SitemapForm>
      ) : (
        <>
          <SitemapAddress address={stored.sitemapAddress} />
          {reading}
        </>
      )}
    </section>
  );
}

interface SitemapFormProps {
  readonly site: Site;
  /** The address in force, in the spelling the engine keeps it in. */
  readonly stored: string | null;
  /** How reading it is going, which sits between the box and the button that changes it. */
  readonly children: ReactNode;
}

/**
 * The address a website's posts are read from.
 *
 * Saved by itself rather than with the name and the zone above it. Naming a sitemap sends this engine
 * off to read somebody's website, which is a different act from renaming it and is not one to carry
 * out by accident on the way through to something else.
 *
 * The box shows what somebody typed for as long as they are typing, and otherwise the stored address,
 * so there is no second copy to fall out of step. It follows a change made anywhere else with nobody
 * having to tell it, it settles on the engine's own spelling once a save goes through, and a save
 * from the switch above leaves a half-typed address where it is.
 *
 * The browser's own check of a web address is turned off, because it would stop the form in words
 * this product did not write. Only an address nothing could make right is stopped here; whether it
 * is one of this website's is the engine's to say. The two are one refusal to whoever typed it, so
 * they read the same and sit in the same place, under the box. Anything else that goes wrong with a
 * save has nothing to do with the address, and is reported as it is everywhere else.
 */
function SitemapForm({ site, stored, children }: SitemapFormProps) {
  const t = useTranslations('siteSettings');
  const reasons = useTranslations('problems');
  const [typed, setTyped] = useState<string | null>(null);
  const [refused, setRefused] = useState(false);
  const saving = useUpdateSiteSettings(site.id);

  const inForce = stored ?? '';
  const held = typed ?? inForce;

  // A browser keeps the space somebody typed or pasted round an address, even in a box meant for one.
  const address = held.trim();
  const changed = address !== inForce;
  const turnedDown = isRefusalOfAddress(saving.error);

  function type(what: string) {
    setTyped(what);
    setRefused(false);
    saving.reset();
  }

  /**
   * Saves without producing a promise nobody is waiting on.
   *
   * A refusal is already on the screen, under the box or in the notice beneath it, so awaiting the
   * attempt here would leave a rejection with nothing to catch it.
   */
  function save() {
    if (address.length > 0 && !couldBeASitemap(address)) {
      setRefused(true);

      return;
    }

    saving.mutate({ sitemapAddress: address }, { onSuccess: () => setTyped(null) });
  }

  return (
    <form
      noValidate
      className="flex flex-col gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        save();
      }}
    >
      <Field
        label={t('sitemap.label')}
        problem={refused || turnedDown ? reasons(SITEMAP_REFUSED) : undefined}
      >
        {(attributes) => (
          <TextInput
            {...attributes}
            type="url"
            inputMode="url"
            dir="ltr"
            value={held}
            maxLength={LONGEST_SITEMAP_ADDRESS}
            placeholder={t('sitemap.placeholder', { site: site.domain })}
            autoComplete="off"
            spellCheck={false}
            autoCapitalize="none"
            autoCorrect="off"
            readOnly={saving.isPending}
            onChange={(event) => type(event.target.value)}
          />
        )}
      </Field>

      {saving.isError && !turnedDown ? <FailureNotice error={saving.error} /> : null}

      {children}

      <SitemapSaving
        saving={saving.isPending}
        saved={saving.isSuccess}
        changed={changed}
        removing={changed && address.length === 0}
      />
    </form>
  );
}

interface SitemapSavingProps {
  /** Whether a save is on its way. */
  readonly saving: boolean;
  /** Whether the last save went through, with nothing typed since. */
  readonly saved: boolean;
  /** Whether the box holds anything other than the address in force. */
  readonly changed: boolean;
  /** Whether what it holds takes the address out altogether. */
  readonly removing: boolean;
}

/** The button under the box, and the word that a save went through. */
function SitemapSaving({ saving, saved, changed, removing }: SitemapSavingProps) {
  const t = useTranslations('siteSettings');

  return (
    <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-center sm:justify-end">
      {saved ? (
        <p role="status" className="flex items-center gap-1.5 text-sm text-positive">
          <Check aria-hidden className="size-4" />
          {t('saved')}
        </p>
      ) : null}

      <Button
        type="submit"
        tone={removing ? 'secondary' : 'primary'}
        busy={saving}
        disabled={!changed}
        className={cn('w-full sm:w-auto', removing && 'text-danger')}
      >
        {saveLabel(t, saving, removing)}
      </Button>
    </div>
  );
}

/** Whether what went wrong with a save was the engine saying the address cannot be used. */
function isRefusalOfAddress(error: Error | null): boolean {
  return (
    error instanceof ApiError && error.reasons.some((reason) => reason.code === SITEMAP_REFUSED)
  );
}

/** What the button under the box does, which is not saving at all once an address is taken out. */
function saveLabel(
  t: ReturnType<typeof useTranslations<'siteSettings'>>,
  saving: boolean,
  removing: boolean,
): string {
  if (saving) {
    return t('saving');
  }

  return removing ? t('sitemap.remove') : t('sitemap.save');
}

/**
 * The address, for somebody who may not change it.
 *
 * Written out and never linked. It came from a website's own settings, so it is only ever text, and
 * however long it runs it stays in its two lines with the whole of it a hover away.
 */
function SitemapAddress({ address }: { readonly address: string | null }) {
  const t = useTranslations('siteSettings.sitemap');

  if (address === null) {
    return null;
  }

  return (
    <div className="flex flex-col gap-1.5">
      <p className="text-sm font-medium text-foreground">{t('label')}</p>
      <p
        dir="ltr"
        title={address}
        className="line-clamp-2 text-sm text-foreground-muted wrap-anywhere"
      >
        {address}
      </p>
    </div>
  );
}

interface SitemapReadingProps {
  readonly siteId: string;
  /** The address in force, which an answer has to be about for it to be worth showing. */
  readonly address: string | null;
  readonly mayChange: boolean;
}

/**
 * How reading the address in force is going.
 *
 * An answer about any other address is about to be replaced, so it is treated as no answer yet: right
 * after a new address is saved, the verdict on the old one would otherwise sit under the new one for
 * as long as the question took. A failed read is only worth saying so when there is nothing to show
 * instead, because an answer already on the screen is still the best there is and the next beat will
 * ask again.
 *
 * The region stays in place while what is in it changes. That is what gets a change read out, and
 * React writes nothing at all when a fresh answer says the same, so a long wait is not read aloud
 * every few seconds.
 */
function SitemapReading({ siteId, address, mayChange }: SitemapReadingProps) {
  const reading = useSitemapStatus(siteId);
  const current = reading.data?.address === address ? reading.data : undefined;

  if (current === undefined && reading.isError) {
    return <FailureNotice error={reading.error} />;
  }

  return (
    <div role="status">
      {current === undefined ? (
        <div aria-hidden className="h-11 animate-pulse rounded-md bg-surface-muted" />
      ) : (
        <SitemapStanding status={current} asOf={reading.dataUpdatedAt} mayChange={mayChange} />
      )}
    </div>
  );
}

/**
 * The ways a reading can stand, which are its states and two that one of those divides into: a
 * sitemap read with its pages still to go, and one read to the end that listed no posts.
 */
type Look = SitemapStatus['state'] | 'pages' | 'empty';

/** Which of them an answer is. */
function lookOf({ state, posts, reading }: SitemapStatus): Look {
  if (state !== 'read') {
    return state;
  }

  if (reading > 0) {
    return 'pages';
  }

  return posts > 0 ? 'read' : 'empty';
}

const MARKED = 'border-border bg-surface-muted';

/** How each is drawn. Going wrong is the only one that asks to be noticed. */
const LOOKS: Readonly<Record<Look, { icon: LucideIcon; mark: string; box: string }>> = {
  none: { icon: Newspaper, mark: 'text-accent-strong', box: MARKED },
  off: { icon: CirclePause, mark: 'text-foreground-muted', box: MARKED },
  waiting: { icon: LoaderCircle, mark: 'animate-spin text-accent', box: MARKED },
  pages: { icon: LoaderCircle, mark: 'animate-spin text-accent', box: MARKED },
  read: { icon: CircleCheck, mark: 'text-positive', box: MARKED },
  empty: { icon: Info, mark: 'text-accent-strong', box: MARKED },
  'partly-read': { icon: TriangleAlert, mark: 'text-danger', box: MARKED },
  failed: { icon: TriangleAlert, mark: 'text-danger', box: 'border-danger/35 bg-danger-soft' },
};

interface SitemapStandingProps {
  readonly status: SitemapStatus;
  /** When the answer was read, which is what its "ago" is measured from. */
  readonly asOf: number;
  readonly mayChange: boolean;
}

/**
 * One answer about a reading, written out.
 *
 * "Ago" is measured from when the answer was read and not from a clock that keeps moving, so an open
 * panel does not read itself out again every minute to say nothing has happened. It comes back
 * correct each time the answer is asked for, which is every opening and every return to the tab.
 */
function SitemapStanding({ status, asOf, mayChange }: SitemapStandingProps) {
  const t = useTranslations('siteSettings.sitemap');
  const format = useFormatter();
  const look = lookOf(status);
  const { icon: Icon, mark, box } = LOOKS[look];

  const ago = (instant: string) => passedSince(format, instant, asOf);

  return (
    <div className={cn('flex gap-2.5 rounded-md border p-3 text-sm', box)}>
      <Icon aria-hidden className={cn('mt-0.5 size-4 shrink-0', mark)} />
      <div className="flex min-w-0 flex-col gap-1">
        <p className="font-medium text-foreground">{headline(t, status, look, mayChange)}</p>
        {details(t, status, ago)[look].map((sentence) => (
          <p key={sentence} className="text-foreground-muted">
            {sentence}
          </p>
        ))}
      </div>
    </div>
  );
}

type SitemapWords = ReturnType<typeof useTranslations<'siteSettings.sitemap'>>;

/** What a reading came to, in one line. */
function headline(
  t: SitemapWords,
  { state, posts }: SitemapStatus,
  look: Look,
  mayChange: boolean,
): string {
  if (look === 'pages') {
    return t('underway', { posts });
  }

  return state === 'none' && !mayChange ? t('nobodyAdded') : t(`state.${state}`, { posts });
}

/**
 * What else each way of standing has to say, in the order it is worth reading.
 *
 * Written once for all of them rather than chosen between, because several say the same things and
 * none of it costs anything to work out. A reading that got part of the way had the address answer,
 * so what went wrong is said about the files it lists and never about the address in the box. Pages
 * that turned the reader away are said in place of the advice about an empty sitemap, which would
 * send somebody to check the wrong thing.
 */
function details(
  t: SitemapWords,
  { problem, readAt, succeededAt, posts, dated, reading, turnedAway }: SitemapStatus,
  ago: (instant: string) => string,
): Readonly<Record<Look, readonly string[]>> {
  const at = (sentence: string, instant: string | null) =>
    instant === null ? [] : [t(sentence, { when: ago(instant) })];
  const wrong = problem === null ? [] : [t(`problem.${problem}`)];
  const wrongInPart = problem === null ? [] : [t(`partlyProblem.${problem}`)];
  const refused = turnedAway > 0 ? [t('turnedAway', { pages: turnedAway })] : [];
  const left = reading > 0 ? [t('left', { reading })] : [];
  const lastRead = at('lastRead', readAt);
  const lastGood =
    succeededAt === null
      ? t('soFar', { posts })
      : t('soFarRead', { posts, when: ago(succeededAt) });
  const kept = posts > 0 ? [lastGood] : [];

  return {
    none: [],
    off: [t('switchOn'), ...kept],
    waiting: [],
    pages: [...left, ...refused],
    read: [...(dated < posts ? [t('dated', { dated })] : []), ...refused, ...lastRead],
    empty: [...(turnedAway > 0 ? refused : [t('noPosts')]), ...lastRead],
    'partly-read': [...wrongInPart, ...refused, ...left, ...at('lastReadAgain', readAt)],
    failed: [...wrong, ...kept, ...at('lastTried', readAt)],
  };
}

/**
 * How long ago something happened, as of when an answer was read.
 *
 * Never less than a minute. A browser's clock can be running behind the engine's, and a reading that
 * finished moments ago would then be said to finish in a few seconds' time; the seconds would be read
 * by nobody in any case.
 */
function passedSince(
  format: ReturnType<typeof useFormatter>,
  instant: string,
  asOf: number,
): string {
  const at = Date.parse(instant);

  return format.relativeTime(at, Math.max(asOf, at + A_MINUTE));
}

interface RemovalProps {
  readonly site: Site;
  readonly onRemoved: () => void;
}

/**
 * Stopping altogether, and the deliberate act it takes.
 *
 * Confirmed in place rather than in a second dialog, which would take the website's own name out
 * of view at the moment somebody is checking which one they are about to lose. What is asked for
 * is the address itself: nothing here can be undone, so a press that lands on the wrong website
 * has to be something that cannot happen by reflex.
 */
function Removal({ site, onRemoved }: RemovalProps) {
  const t = useTranslations('siteSettings.removal');
  const [confirming, setConfirming] = useState(false);
  const [typed, setTyped] = useState('');
  const confirmation = useRef<HTMLFormElement>(null);
  const removing = useRemoveSite(site.id);

  /*
    Brought into view as it appears. This panel is already taller than a phone screen, so the
    confirmation opens below the fold, and somebody who pressed a button and saw nothing move
    would reasonably decide that nothing had happened.

    Keyed on the confirmation appearing rather than attached to the form as a ref callback, which
    is handed the element again on every render and so on every keystroke — pulling the view about
    while somebody is part way through typing an address on a phone with the keyboard up.
  */
  useEffect(() => {
    if (confirming) {
      confirmation.current?.scrollIntoView({ block: 'nearest' });
    }
  }, [confirming]);

  function stop() {
    setConfirming(false);
    setTyped('');
    removing.reset();
  }

  /**
   * Removes it without producing a promise nobody is waiting on.
   *
   * A refusal is already on the screen through the notice above, so awaiting the attempt here
   * would leave a rejection with nothing to catch it.
   */
  function remove() {
    removing.mutate(undefined, { onSuccess: onRemoved });
  }

  return (
    <section className="flex flex-col gap-3 border-t border-border pt-5">
      <h3 className="text-sm font-semibold text-danger">{t('title')}</h3>
      <p className="text-sm text-foreground-muted">{t('body', { site: site.domain })}</p>

      {removing.isError ? <FailureNotice error={removing.error} /> : null}

      {confirming ? (
        <form
          ref={confirmation}
          className="flex flex-col gap-4 rounded-lg border border-danger/35 bg-danger-soft p-4"
          onSubmit={(event) => {
            event.preventDefault();
            remove();
          }}
        >
          <Field label={t('confirm.label', { site: site.domain })}>
            {(attributes) => (
              <TextInput
                {...attributes}
                value={typed}
                maxLength={LONGEST_ADDRESS}
                autoComplete="off"
                spellCheck={false}
                /*
                  A phone keyboard capitalises the first letter of a box by default and offers to
                  correct what looks to it like a misspelt word. Either would quietly turn a
                  correctly typed address into one that does not match, leaving the button dead
                  and nothing on the screen saying why.
                */
                autoCapitalize="none"
                autoCorrect="off"
                onChange={(event) => setTyped(event.target.value)}
                /*
                  This box takes the place of the button that was just pressed, so focus has
                  nowhere left to go unless it is sent here. The rule guards against seizing focus
                  on a page somebody was already reading; this appeared because they asked for it
                  a moment ago, and it is the one thing left to do.
                */
                // eslint-disable-next-line jsx-a11y/no-autofocus
                autoFocus
              />
            )}
          </Field>

          {/*
            Stacked in the order they are reached, with the irreversible one last and furthest
            from the box that was just typed into. A reflex press after typing lands on the way
            out, never on the way through.
          */}
          <div className="flex flex-col gap-3 sm:flex-row sm:justify-end">
            <Button tone="quiet" onClick={stop}>
              {t('confirm.no')}
            </Button>
            <Button
              type="submit"
              tone="danger"
              busy={removing.isPending}
              /*
                Matched without regard to case, against an address the engine already stores in
                lower case. Somebody who types their own address in capitals has confirmed which
                website they mean just as plainly as somebody who did not.
              */
              disabled={typed.trim().toLowerCase() !== site.domain}
            >
              {removing.isPending ? t('removing') : t('confirm.yes')}
            </Button>
          </div>
        </form>
      ) : (
        <Button
          tone="secondary"
          onClick={() => setConfirming(true)}
          className="w-full text-danger sm:w-auto sm:self-start"
        >
          {t('action')}
        </Button>
      )}
    </section>
  );
}
