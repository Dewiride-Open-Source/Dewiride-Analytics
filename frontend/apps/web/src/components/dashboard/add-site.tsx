'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, SelectInput, TextInput } from '@/components/ui/field';
import { useAddSite } from '@/lib/queries/sites';
import { offeredZone, timeZoneGroups, withZone } from '@/lib/time-zones';

/** The longest an address runs to, so a hostname is stopped here rather than refused there. */
const LONGEST_ADDRESS = 253;

interface AddSiteProps {
  readonly open: boolean;
  readonly onClose: () => void;
  /**
   * The zone to start on: the one the website already on screen counts its days in.
   *
   * A better guess than the zone of the machine somebody happens to be sitting at. Somebody adding
   * a second website usually runs it for the same audience as the first, and whoever administers
   * it may be nowhere near either.
   */
  readonly likelyTimeZoneId?: string;
  /** Called with the new website once it exists, so the screen can move to it. */
  readonly onAdded: (siteId: string) => void;
}

/**
 * Starting to measure another website.
 *
 * Two things are asked for, because neither can be worked out from the other: the address, and the
 * zone its days are cut in. The zone opens on the one the website already on screen is counted in,
 * falling back to this machine's where there is none to borrow.
 *
 * The zone the websites are counted in is borrowed the moment it arrives, and the choices are
 * widened to include it. Two things make that necessary rather than tidy. Platforms disagree about
 * zone names — the same place is `Asia/Calcutta` on one and `Asia/Kolkata` on another — so a
 * website's own zone is not always among the ones this browser offers. And this panel belongs to
 * the bar across the top, which is drawn before any website has been read, so whatever it opened on
 * was chosen with nothing to go on. Both end the same way if the picker is left to its own devices:
 * a website measured in a country nobody involved has ever been to, chosen for them, silently.
 */
export function AddSite({ open, onClose, likelyTimeZoneId, onAdded }: AddSiteProps) {
  const t = useTranslations('addSite');
  const [zones, setZones] = useState(() => withZone(timeZoneGroups(), likelyTimeZoneId));
  const [borrowed, setBorrowed] = useState(likelyTimeZoneId);
  const [zone, setZone] = useState(() => offeredZone(zones, likelyTimeZoneId));
  const [domain, setDomain] = useState('');
  const adding = useAddSite();

  if (borrowed !== likelyTimeZoneId) {
    const widened = withZone(timeZoneGroups(), likelyTimeZoneId);

    setBorrowed(likelyTimeZoneId);
    setZones(widened);
    setZone(offeredZone(widened, likelyTimeZoneId));
  }

  function close() {
    setDomain('');
    adding.reset();
    onClose();
  }

  /**
   * Adds it without producing a promise nobody is waiting on.
   *
   * A refusal is already on the screen through the notice below, so awaiting the attempt here
   * would leave a rejection with nothing to catch it.
   */
  function add() {
    adding.mutate(
      { domain: domain.trim(), timeZoneId: zone },
      {
        onSuccess: (site) => {
          setDomain('');
          adding.reset();
          onAdded(site.id);
        },
      },
    );
  }

  return (
    <Dialog
      open={open}
      onClose={close}
      title={t('title')}
      closeLabel={t('close')}
      className="max-w-lg"
    >
      <form
        className="flex flex-col gap-5"
        onSubmit={(event) => {
          event.preventDefault();
          add();
        }}
      >
        <Field label={t('domain.label')}>
          {(attributes) => (
            <TextInput
              {...attributes}
              value={domain}
              maxLength={LONGEST_ADDRESS}
              autoComplete="off"
              spellCheck={false}
              placeholder={t('domain.placeholder')}
              onChange={(event) => setDomain(event.target.value)}
              /*
                The first field of a panel that exists only to be filled in, which is where focus
                belongs the moment it opens. The rule guards against seizing focus on a page
                somebody was already reading; nobody was reading this until they asked for it.
              */
              // eslint-disable-next-line jsx-a11y/no-autofocus
              autoFocus
              required
            />
          )}
        </Field>

        <Field label={t('timeZone.label')}>
          {(attributes) => (
            <SelectInput
              {...attributes}
              value={zone}
              onChange={(event) => setZone(event.target.value)}
              required
            >
              {zones.map((group) => (
                <optgroup key={group.area} label={group.area}>
                  {group.zones.map((zone) => (
                    <option key={zone.id} value={zone.id}>
                      {zone.label}
                    </option>
                  ))}
                </optgroup>
              ))}
            </SelectInput>
          )}
        </Field>

        {adding.isError ? <FailureNotice error={adding.error} /> : null}

        <div className="flex justify-end">
          <Button
            type="submit"
            busy={adding.isPending}
            disabled={domain.trim().length === 0}
            className="w-full sm:w-auto"
          >
            {adding.isPending ? t('adding') : t('add')}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
