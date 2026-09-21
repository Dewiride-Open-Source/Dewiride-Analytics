import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CloseAccount } from '@/components/settings/close-account';
import type { ClosedAccount, Organization, Session, Site } from '@/lib/api/schemas';
import { organizationKey, sitesKey } from '@/lib/queries/keys';
import { sessionKey, useSession } from '@/lib/queries/session';
import {
  type Engine,
  engineAnswering,
  engineDoing,
  engineStopped,
  respondWith,
} from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const NAME = 'Acme Inc.';

const OWNER = {
  id: '0195f7e0-0000-7000-8000-000000000001',
  emailAddress: 'ada@example.com',
  displayName: 'Ada Lovelace',
};

/** What the engine says about the account once it has been closed. */
const CLOSED: ClosedAccount = {
  name: NAME,
  closedAt: '2026-09-21T09:30:00+00:00',
  deletionDue: '2026-10-21T09:30:00+00:00',
  closedBy: OWNER.displayName,
  closedByYou: true,
  canRestore: true,
  hasOpenAccount: false,
};

/** The session the engine answers with once the account is closed. */
const CLOSED_SESSION: Session = {
  setupCompleted: true,
  user: OWNER,
  token: 'a-fresh-proof',
  closure: CLOSED,
};

/** A website of the account, as the bar across the top would have read it before the closure. */
const SITE: Site = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df78',
  domain: 'example.com',
  displayName: 'My Blog',
  timeZoneId: 'Europe/London',
  role: 'owner',
};

/** The account, as this screen read it before the closure. */
const ACCOUNT: Organization = {
  id: '0195f7e0-0000-7000-8000-0000000000aa',
  name: NAME,
  role: 'owner',
  people: [],
  invitations: [],
};

/**
 * Watches the session the way the gate above every screen does.
 *
 * The card never reads the session itself, and the cache only asks a question again while
 * somebody is waiting on the answer. A test of whether a refusal has the session read again
 * therefore needs something on the page that is.
 */
function Gate() {
  useSession();

  return null;
}

function show({ watched = false } = {}) {
  return renderScreen(
    <>
      {watched ? <Gate /> : null}
      <CloseAccount name={NAME} />
    </>,
    { signedInAs: OWNER },
  );
}

function closeButton(): HTMLElement {
  return screen.getByRole('button', { name: 'Close account' });
}

function confirmationBox(): HTMLElement | null {
  return screen.queryByRole('textbox', { name: `Type ${NAME} to confirm` });
}

/** Opens the confirmation and types whatever is given into it. */
async function askToClose(typed: string): Promise<void> {
  await userEvent.click(closeButton());
  await userEvent.type(
    await screen.findByRole('textbox', { name: `Type ${NAME} to confirm` }),
    typed,
  );
}

/** Opens the confirmation, types the account's name, and presses the button that closes it. */
async function confirm(): Promise<void> {
  await askToClose(NAME);
  await userEvent.click(closeButton());
}

/** An engine that refuses the closure with a named reason and answers everything else normally. */
function refusing(
  status: number,
  reason: { readonly code: string; readonly description: string },
): Engine {
  return engineDoing(async (_path, init) =>
    init.method === 'POST'
      ? respondWith(status, { title: 'That could not be done.', problems: [reason] })
      : respondWith(200, CLOSED_SESSION),
  );
}

describe('closing the account', () => {
  it('explains what closing does and offers one button to begin', () => {
    engineAnswering(200, CLOSED_SESSION);

    show();

    expect(screen.getByRole('heading', { name: 'Close this account' })).toBeInTheDocument();
    expect(screen.getByText(/30 days/)).toBeInTheDocument();
    expect(closeButton()).toBeEnabled();
    expect(confirmationBox()).not.toBeInTheDocument();
  });

  it('asks for the name to be typed before anything can go', async () => {
    const engine = engineAnswering(200, CLOSED_SESSION);

    show();
    await userEvent.click(closeButton());

    const box = await screen.findByRole('textbox', { name: `Type ${NAME} to confirm` });

    expect(box).toHaveFocus();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(closeButton()).toHaveAttribute('type', 'submit');
    expect(closeButton()).toBeDisabled();
    expect(engine.count).toBe(0);
  });

  it('closes nothing while what was typed is not the name', async () => {
    const engine = engineAnswering(200, CLOSED_SESSION);

    show();
    await askToClose('Acme Ltd{Enter}');

    expect(closeButton()).toBeDisabled();
    expect(engine.count).toBe(0);
  });

  /**
   * A phone capitalises the first letter of a box on its own and adds a space after a full stop,
   * so a name typed correctly on one arrives looking different. Refusing that would leave the
   * button dead with nothing on the screen saying why.
   */
  it('takes the name however it was capitalised and spaced', async () => {
    engineAnswering(200, CLOSED_SESSION);

    show();
    await askToClose('acme inc. ');

    expect(closeButton()).toBeEnabled();
  });

  it('puts the card back as it was when the confirmation is cancelled', async () => {
    engineAnswering(200, CLOSED_SESSION);

    show();
    await askToClose('Acme');
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(confirmationBox()).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    expect(closeButton()).toHaveAttribute('type', 'button');

    await userEvent.click(closeButton());

    expect(confirmationBox()).toHaveValue('');
  });

  it('takes a refusal off the screen with the confirmation it belonged to', async () => {
    engineStopped();

    show();
    await confirm();

    expect(await screen.findByRole('alert')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  /**
   * A cookie the browser returns on its own is not proof that this page meant to send the change,
   * so the pair the engine issued travels with it. There is nothing else to send: which account is
   * being closed is the one the person runs, and the engine knows that from who is signed in.
   */
  it('asks the engine to close it, proving where the request came from and carrying nothing else', async () => {
    const engine = engineAnswering(200, CLOSED_SESSION);

    show();
    await confirm();

    await waitFor(() => expect(engine.count).toBe(1));
    expect(engine.first().path).toBe('/api/organization/closure');
    expect(engine.first().init.method).toBe('POST');
    expect(engine.header('X-Csrf-Token')).toBe('proof-value');
    expect(engine.first().init.body).toBeUndefined();
  });

  /**
   * The engine answers with the session as it now stands, and that answer is what the gate above
   * every screen reads to move the person to the one that explains what has happened.
   */
  it('keeps the closed account the engine answered with, ready for the gate to act on', async () => {
    engineAnswering(200, CLOSED_SESSION);

    const { cache } = show();
    await confirm();

    await waitFor(() => expect(cache.getQueryData<Session>(sessionKey)?.closure).toEqual(CLOSED));
    expect(cache.getQueryData<Session>(sessionKey)?.token).toBe('a-fresh-proof');
  });

  /**
   * Nothing about a closed account can be read, so what was read before it closed is taken out
   * of memory rather than left to be shown for a moment while an empty answer is on its way.
   */
  it('forgets the websites and the account the moment it is closed', async () => {
    engineAnswering(200, CLOSED_SESSION);

    const { cache } = show();
    cache.setQueryData(sitesKey, [SITE]);
    cache.setQueryData(organizationKey, ACCOUNT);
    await confirm();

    await waitFor(() => expect(cache.getQueryData(sitesKey)).toBeUndefined());
    expect(cache.getQueryData(organizationKey)).toBeUndefined();
  });

  /**
   * Whatever else the dashboard has read about the account — an edition's plan, say — is no
   * longer true of a closed one, and is read again before it is shown again. The session is left
   * alone: it was just written from the answer, and asking again would only ask the same question.
   */
  it('marks everything else read about the account stale, but not the session it just wrote', async () => {
    engineAnswering(200, CLOSED_SESSION);

    const { cache } = show();
    cache.setQueryData(['edition', 'plan'], { plan: 'growth' });
    await confirm();

    await waitFor(() => expect(cache.getQueryState(['edition', 'plan'])?.isInvalidated).toBe(true));
    expect(cache.getQueryState(sessionKey)?.isInvalidated).toBe(false);
  });

  /**
   * Two owners pressing the button within moments of each other is one closed account, not a
   * failure. The second is told what happened, and the session is read again so that the gate
   * moves them to the closed screen exactly as it moved the first.
   */
  it('says when somebody else has just closed it, and reads the session again', async () => {
    const engine = refusing(409, {
      code: 'AccountAlreadyClosed',
      description: 'The account was closed a moment ago.',
    });

    const { cache } = show({ watched: true });
    cache.setQueryData(sitesKey, [SITE]);
    cache.setQueryData(organizationKey, ACCOUNT);
    await confirm();

    expect(await screen.findByText('This account is already closed.')).toBeInTheDocument();

    await waitFor(() =>
      expect(
        engine.all().filter((sent) => sent.init.method === 'GET' && sent.path === '/api/session'),
      ).toHaveLength(1),
    );
    await waitFor(() => expect(cache.getQueryData<Session>(sessionKey)?.closure).toEqual(CLOSED));
    // Settled exactly as a press that did the closing would have settled it.
    expect(cache.getQueryData(sitesKey)).toBeUndefined();
    expect(cache.getQueryData(organizationKey)).toBeUndefined();
  });

  it('says so when the engine cannot be reached, and keeps the confirmation open to try again', async () => {
    engineStopped();

    show();
    await confirm();

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
    expect(confirmationBox()).toHaveValue(NAME);
    expect(closeButton()).toBeEnabled();
  });
});
