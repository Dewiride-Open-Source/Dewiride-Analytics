'use client';

import { Check, Copy, KeyRound } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, TextInput } from '@/components/ui/field';
import type { ServerKey } from '@/lib/api/schemas';
import { useCreateServerKey, useRevokeServerKey, useServerKeys } from '@/lib/queries/server-keys';
import { cn } from '@/lib/styling';

interface ServerKeysProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly siteId: string;
  readonly siteDomain: string;
  /** IANA zone the website’s days are counted in, so dates read the same everywhere. */
  readonly timeZoneId: string;
}

/** How long the button keeps saying it worked. */
const CONFIRMATION_MS = 2000;

/** Where somebody gets, and takes away, the keys their own server reports with. */
export function ServerKeys({ open, onClose, siteId, siteDomain, timeZoneId }: ServerKeysProps) {
  const t = useTranslations('serverKeys');
  const [name, setName] = useState('');
  const [issued, setIssued] = useState<string | null>(null);

  const keys = useServerKeys(siteId, open);
  const creating = useCreateServerKey(siteId);
  const revoking = useRevokeServerKey(siteId);

  /**
   * Closes the panel, taking the one secret it ever holds with it.
   *
   * Cleared as the panel is dismissed rather than in response to having been dismissed, so that
   * re-opening it cannot show a key somebody has already walked away from.
   */
  function close() {
    setIssued(null);
    setName('');
    onClose();
  }

  /**
   * Asks for a key without producing a promise nobody is waiting on.
   *
   * A refusal is already on the screen through the notice below, so awaiting the attempt here
   * would leave a rejection with nothing to catch it — which surfaces as an unhandled error in
   * the browser rather than as anything a person can act on.
   */
  function create() {
    creating.mutate(name.trim(), {
      onSuccess: (created) => {
        setIssued(created.secret);
        setName('');
      },
    });
  }

  return (
    <Dialog open={open} onClose={close} title={t('title')} closeLabel={t('close')}>
      <div className="flex flex-col gap-5">
        <p className="text-sm text-foreground-muted">{t('body', { site: siteDomain })}</p>

        <form
          className="flex flex-col gap-3 sm:flex-row sm:items-end"
          onSubmit={(event) => {
            event.preventDefault();
            create();
          }}
        >
          <div className="flex-1">
            <Field label={t('name.label')}>
              {(attributes) => (
                <TextInput
                  {...attributes}
                  value={name}
                  maxLength={60}
                  placeholder={t('name.placeholder')}
                  onChange={(event) => setName(event.target.value)}
                />
              )}
            </Field>
          </div>
          <Button
            type="submit"
            busy={creating.isPending}
            disabled={name.trim().length === 0}
            className="w-full sm:w-auto"
          >
            {creating.isPending ? t('creating') : t('create')}
          </Button>
        </form>

        {creating.isError ? <FailureNotice error={creating.error} /> : null}
        {issued ? <IssuedKey secret={issued} /> : null}

        <section className="flex flex-col gap-3 border-t border-border pt-5">
          <h3 className="text-sm font-semibold text-foreground">{t('list.title')}</h3>

          {keys.isError ? <FailureNotice error={keys.error} /> : null}
          {revoking.isError ? <FailureNotice error={revoking.error} /> : null}

          <KeyList
            keys={keys.data}
            loading={keys.isPending}
            timeZoneId={timeZoneId}
            busyWith={revoking.isPending ? revoking.variables : undefined}
            onRemove={(keyId) => revoking.mutate(keyId)}
          />
        </section>
      </div>
    </Dialog>
  );
}

/**
 * The secret, at the only moment it exists outside whatever the customer puts it into.
 *
 * Given its own surface rather than dropped into the list, because it will never appear again and
 * somebody scanning the panel has to be unable to miss it.
 */
function IssuedKey({ secret }: { readonly secret: string }) {
  const t = useTranslations('serverKeys');
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) {
      return;
    }

    const timer = setTimeout(() => setCopied(false), CONFIRMATION_MS);

    return () => clearTimeout(timer);
  }, [copied]);

  async function copy() {
    try {
      await navigator.clipboard.writeText(secret);
      setCopied(true);
    } catch {
      // Refused, which happens when the page is not on a secure address. The key is on the screen
      // and selectable, so there is still a way through and nothing to announce.
      setCopied(false);
    }
  }

  return (
    <div className="flex flex-col gap-3 rounded-lg border border-accent-strong bg-accent-soft p-4">
      <p className="text-sm font-medium text-foreground">{t('issued.title')}</p>
      <code className="rounded-md bg-surface px-3 py-2 font-mono text-xs break-all text-foreground">
        {secret}
      </code>
      <div className="flex flex-col items-start gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-sm text-foreground-muted">{t('issued.body')}</p>
        <Button tone="secondary" size="sm" onClick={copy} className="w-full sm:w-auto">
          {copied ? (
            <Check aria-hidden className="size-4 text-positive" />
          ) : (
            <Copy aria-hidden className="size-4" />
          )}
          {copied ? t('copied') : t('copy')}
        </Button>
      </div>
    </div>
  );
}

interface KeyListProps {
  readonly keys: readonly ServerKey[] | undefined;
  readonly loading: boolean;
  readonly timeZoneId: string;
  readonly busyWith: string | undefined;
  readonly onRemove: (keyId: string) => void;
}

function KeyList({ keys, loading, timeZoneId, busyWith, onRemove }: KeyListProps) {
  const t = useTranslations('serverKeys');

  if (loading) {
    return <div className="h-16 animate-pulse rounded-lg border border-border bg-surface-muted" />;
  }

  /*
    Nothing at all where the list could not be read. "There are none" and "we could not find out"
    are different answers, and printing the first under a notice that says the second tells
    somebody their keys are gone when they may be perfectly fine.
  */
  if (keys === undefined) {
    return null;
  }

  if (keys.length === 0) {
    return <p className="text-sm text-foreground-muted">{t('list.empty')}</p>;
  }

  return (
    <ul className="flex flex-col gap-2">
      {keys.map((key) => (
        <KeyRow
          key={key.id}
          entry={key}
          timeZoneId={timeZoneId}
          busy={busyWith === key.id}
          onRemove={() => onRemove(key.id)}
        />
      ))}
    </ul>
  );
}

interface KeyRowProps {
  readonly entry: ServerKey;
  readonly timeZoneId: string;
  readonly busy: boolean;
  readonly onRemove: () => void;
}

/**
 * One key, and the two presses it takes to withdraw it.
 *
 * Confirmed in place rather than in a second dialog. Removing a key silently stops whatever was
 * reporting with it, so it is worth a deliberate second press — but a dialog on top of a dialog
 * takes the list out of view at the moment somebody is checking which one they meant.
 */
function KeyRow({ entry, timeZoneId, busy, onRemove }: KeyRowProps) {
  const t = useTranslations('serverKeys');
  const format = useFormatter();
  const [confirming, setConfirming] = useState(false);

  // Counted in the website's own zone, as every other date on the dashboard is. A key created
  // late one evening must not appear to have been created the day before.
  const on = (moment: string) =>
    format.dateTime(new Date(moment), { dateStyle: 'medium', timeZone: timeZoneId });

  const added = t('added', { date: on(entry.createdAt) });
  const used = entry.lastUsedAt ? t('lastUsed', { date: on(entry.lastUsedAt) }) : t('neverUsed');

  return (
    <li
      className={cn(
        'flex flex-col gap-3 rounded-lg border border-border bg-surface-muted px-4 py-3',
        'sm:flex-row sm:items-center sm:justify-between',
      )}
    >
      <div className="flex min-w-0 items-center gap-3">
        <KeyRound aria-hidden className="size-4 shrink-0 text-foreground-subtle" />
        <div className="min-w-0">
          <p className="truncate text-sm font-medium text-foreground">{entry.name}</p>
          <p className="text-xs text-foreground-muted">
            {added} · {used}
          </p>
        </div>
      </div>

      {confirming ? (
        <div className="flex items-center gap-2">
          <span className="text-sm text-foreground-muted">{t('confirm.question')}</span>
          <Button tone="danger" size="sm" busy={busy} onClick={onRemove}>
            {t('confirm.yes')}
          </Button>
          <Button tone="quiet" size="sm" onClick={() => setConfirming(false)}>
            {t('confirm.no')}
          </Button>
        </div>
      ) : (
        <Button
          tone="secondary"
          size="sm"
          onClick={() => setConfirming(true)}
          className="w-full sm:w-auto"
        >
          {t('remove')}
        </Button>
      )}
    </li>
  );
}
