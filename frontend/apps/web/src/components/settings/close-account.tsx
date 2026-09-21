'use client';

import { edition } from '@edition';
import { useTranslations } from 'next-intl';
import { useEffect, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, TextInput } from '@/components/ui/field';
import { useCloseAccount } from '@/lib/queries/account';

interface CloseAccountProps {
  /** What the account is called, which is what somebody has to type to close it. */
  readonly name: string;
}

/** The longest name the engine keeps, so the box that confirms one is never shorter than it. */
const LONGEST_NAME = 200;

/**
 * Closing the whole account, and the deliberate act it takes.
 *
 * Shown to owners only, because only an owner may do it. It can be undone for thirty days, but
 * it stops measurement for every website and takes the whole team's screens away the moment it is
 * pressed, so it is confirmed in place by typing the account's own name — the same act that
 * removes a website, for the same reason: a press that lands here by reflex must not be able to
 * happen.
 *
 * What the compiled edition has to say about money sits between the explanation and the button,
 * so that somebody paying for a plan reads what closing does to it before they are asked to
 * confirm anything.
 */
export function CloseAccount({ name }: CloseAccountProps) {
  const t = useTranslations('settings.close');
  const [confirming, setConfirming] = useState(false);
  const [typed, setTyped] = useState('');
  const confirmation = useRef<HTMLFormElement>(null);
  const closing = useCloseAccount();
  const Edition = edition.closure;

  /*
    Brought into view as it appears. This is the last card on a screen that is already taller than
    a phone, so the confirmation opens below the fold, and somebody who pressed a button and saw
    nothing move would reasonably decide that nothing had happened.
  */
  useEffect(() => {
    if (confirming) {
      confirmation.current?.scrollIntoView({ block: 'nearest' });
    }
  }, [confirming]);

  function stop() {
    setConfirming(false);
    setTyped('');
    closing.reset();
  }

  /**
   * Closes it without producing a promise nobody is waiting on.
   *
   * A refusal is already on the screen through the notice above, so awaiting the attempt here
   * would leave a rejection with nothing to catch it. Success needs nothing from this card: the
   * session it writes is what moves the reader to the screen that explains what has happened.
   */
  function close() {
    closing.mutate();
  }

  return (
    <Card className="flex flex-col gap-3 p-5 sm:p-6">
      <h2 className="text-sm font-semibold text-danger">{t('title')}</h2>
      <p className="text-sm text-foreground-muted">{t('body')}</p>

      {Edition ? <Edition /> : null}

      {closing.isError ? <FailureNotice error={closing.error} /> : null}

      {confirming ? (
        <form
          ref={confirmation}
          className="flex flex-col gap-4 rounded-lg border border-danger/35 bg-danger-soft p-4"
          onSubmit={(event) => {
            event.preventDefault();
            close();
          }}
        >
          <Field label={t('confirm.label', { name })}>
            {(attributes) => (
              <TextInput
                {...attributes}
                value={typed}
                maxLength={LONGEST_NAME}
                autoComplete="off"
                spellCheck={false}
                /*
                  A phone keyboard capitalises the first letter of a box by default and offers to
                  correct what looks to it like a misspelt word. Either would quietly turn a
                  correctly typed name into one that does not match, leaving the button dead and
                  nothing on the screen saying why.
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
            Stacked in the order they are reached, with the one that closes the account last and
            furthest from the box that was just typed into. A reflex press after typing lands on
            the way out, never on the way through.
          */}
          <div className="flex flex-col gap-3 sm:flex-row sm:justify-end">
            <Button tone="quiet" onClick={stop}>
              {t('confirm.no')}
            </Button>
            <Button
              type="submit"
              tone="danger"
              busy={closing.isPending}
              disabled={!matches(typed, name)}
            >
              {closing.isPending ? t('closing') : t('confirm.yes')}
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
    </Card>
  );
}

/**
 * Whether what was typed is the account's name.
 *
 * Matched without regard to case or to the space around it. Somebody who types the name in
 * capitals, or with a trailing space their keyboard added, has confirmed which account they mean
 * just as plainly as somebody who did not.
 */
function matches(typed: string, name: string): boolean {
  return typed.trim().toLocaleLowerCase() === name.trim().toLocaleLowerCase();
}
