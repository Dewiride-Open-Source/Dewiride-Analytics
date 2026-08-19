'use client';

import { Check } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useEffect, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, SelectInput, TextInput } from '@/components/ui/field';
import { Switch } from '@/components/ui/switch';
import type { Site, SiteSettings as StoredSettings } from '@/lib/api/schemas';
import { useRemoveSite, useSiteSettings, useUpdateSiteSettings } from '@/lib/queries/site-settings';
import { timeZoneGroups, withZone } from '@/lib/time-zones';

interface SiteSettingsProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly site: Site;
  /** Called once the website is gone, so the screen can put this away. */
  readonly onRemoved: () => void;
}

/** The longest name the engine will keep, so a name is stopped here rather than refused there. */
const LONGEST_NAME = 253;

/** The longest an address runs to, so the box that confirms one is never shorter than the answer. */
const LONGEST_ADDRESS = 253;

/**
 * Everything about one website its owner decides, and the way to stop measuring it altogether.
 *
 * The panel is mounted whether or not it is open, so what it holds is drawn only while it is: a
 * name typed and then abandoned is gone by the time somebody looks again, and the confirmation
 * that removes a website is never found already armed.
 */
export function SiteSettings({ open, onClose, site, onRemoved }: SiteSettingsProps) {
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
      className="max-w-lg"
    >
      <div className="flex flex-col gap-5">
        <p className="text-sm text-foreground-muted">{t('body', { site: site.domain })}</p>

        {settings.isError ? <FailureNotice error={settings.error} /> : null}

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
 * showing, and the zones offered are widened to include the stored one. Platforms disagree about
 * zone names, and a picker that cannot offer the zone a website is counted in would open on a
 * fall-back and save that instead the moment anything else on this form was changed.
 *
 * They are taken again whenever the stored ones move under the form, which is what a save and a
 * re-read both do. Seeded once and left, the boxes would go on holding what was stored a moment
 * ago while the button beside them compares against what is stored now — so the button would arm
 * itself with nobody having typed anything, and pressing it would put the older name back.
 */
function WebsiteDetails({ siteId, stored }: WebsiteDetailsProps) {
  const t = useTranslations('siteSettings');
  const [seeded, setSeeded] = useState(stored);
  const [zones, setZones] = useState(() => withZone(timeZoneGroups(), stored.timeZoneId));
  const [name, setName] = useState(stored.displayName);
  const [zone, setZone] = useState(stored.timeZoneId);
  const saving = useUpdateSiteSettings(siteId);

  if (seeded !== stored) {
    setSeeded(stored);
    setZones(withZone(timeZoneGroups(), stored.timeZoneId));
    setName(stored.displayName);
    setZone(stored.timeZoneId);
  }

  const trimmed = name.trim();
  const renamed = trimmed.length > 0 && trimmed !== stored.displayName;
  const moved = zone !== stored.timeZoneId;
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
          <SelectInput
            {...attributes}
            value={zone}
            onChange={(event) => setZone(event.target.value)}
            required
          >
            {zones.map((group) => (
              <optgroup key={group.area} label={group.area}>
                {group.zones.map((choice) => (
                  <option key={choice.id} value={choice.id}>
                    {choice.label}
                  </option>
                ))}
              </optgroup>
            ))}
          </SelectInput>
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
