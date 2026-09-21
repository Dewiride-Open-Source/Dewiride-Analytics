import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ClosedAccount } from '@/components/account/closed-account';
import type * as Navigation from '@/i18n/navigation';
import type { ClosedAccount as Closure, Session, Site } from '@/lib/api/schemas';
import { sitesKey } from '@/lib/queries/keys';
import { sessionKey } from '@/lib/queries/session';
import {
  type Engine,
  engineAnswering,
  engineDoing,
  engineStopped,
  respondWith,
  type Sent,
} from '@/test/engine';
import { renderScreen } from '@/test/harness';

const replace = vi.fn();

/**
 * The screen moves the reader itself once the account is back, and there is no router in a
 * document. The links keep the real component so their addresses can be read.
 */
vi.mock('@/i18n/navigation', async (original) => ({
  ...(await original<typeof Navigation>()),
  useRouter: () => ({ replace }),
}));

beforeEach(() => {
  replace.mockReset();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

const OWNER = {
  id: '0195f7e0-0000-7000-8000-000000000001',
  emailAddress: 'ada@example.com',
  displayName: 'Ada Lovelace',
};

/** An account its owner closed a month before it is due to be deleted, read by that owner. */
const CLOSURE: Closure = {
  name: 'Acme Inc.',
  closedAt: '2026-09-21T09:30:00+00:00',
  deletionDue: '2026-10-21T09:30:00+00:00',
  closedBy: 'Ada Lovelace',
  closedByYou: true,
  canRestore: true,
  hasOpenAccount: false,
};

/** The session as the engine describes it once the account is open again. */
const OPEN: Session = {
  setupCompleted: true,
  user: OWNER,
  token: 'a-fresh-proof-value',
  closure: null,
};

/** A website of the account, as it was read before the closure. */
const SITE: Site = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df78',
  domain: 'example.com',
  displayName: 'My Blog',
  timeZoneId: 'Europe/London',
  role: 'owner',
};

/** The session as the engine describes it once nobody is signed in. */
const SIGNED_OUT: Session = {
  setupCompleted: true,
  user: null,
  token: 'x',
  closure: null,
};

function show(closure: Partial<Closure> = {}) {
  return renderScreen(<ClosedAccount />, {
    signedInAs: OWNER,
    closure: { ...CLOSURE, ...closure },
  });
}

/** The one thing the screen sent that changes something, or a failed test where nothing was. */
function change(engine: Engine): Sent {
  const sent = engine.all().find((request) => request.init.method === 'DELETE');

  if (!sent) {
    throw new Error('Nothing that changes anything reached the engine.');
  }

  return sent;
}

/** The proof-of-origin value a request travelled with. */
function proofOn(request: Sent): string | undefined {
  return (request.init.headers as Record<string, string> | undefined)?.['X-Csrf-Token'];
}

describe('the closed account screen', () => {
  it('tells the owner who closed it what happened and what they can do about it', () => {
    show();

    expect(screen.getByRole('heading', { name: 'Acme Inc. is closed' })).toBeInTheDocument();
    expect(screen.getByText('You closed it on September 21, 2026.')).toBeInTheDocument();
    expect(
      screen.getByText(
        'Everything it measured is kept in case you want it back, and is deleted on October 21, 2026.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Bring it back' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Back to your dashboard' })).not.toBeInTheDocument();
    expect(screen.queryByText('Only an owner can bring it back.')).not.toBeInTheDocument();
  });

  it('names the person who closed it when it was somebody else', () => {
    show({ closedBy: 'Grace Hopper', closedByYou: false });

    expect(screen.getByText('Grace Hopper closed it on September 21, 2026.')).toBeInTheDocument();
  });

  /**
   * The person who closed it may since have been taken off the account, and then the screen is
   * not told their name. It says when rather than guessing who.
   */
  it('says only when it was closed where nobody can be named', () => {
    show({ closedBy: null, closedByYou: false });

    expect(screen.getByText('It was closed on September 21, 2026.')).toBeInTheDocument();
  });

  it('offers a member no way to bring it back, and says why', () => {
    show({ canRestore: false, closedByYou: false });

    expect(screen.getByText('Only an owner can bring it back.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Bring it back' })).not.toBeInTheDocument();
  });

  it('gives somebody with an open account the way back to their dashboard', () => {
    show({ hasOpenAccount: true });

    expect(screen.getByRole('link', { name: 'Back to your dashboard' })).toHaveAttribute(
      'href',
      '/app',
    );
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument();
  });

  /**
   * The open-source edition has nothing to say about money when an account is closed, so the
   * card carries exactly what the account's own record says and not a word more.
   */
  it('adds nothing of its own in this edition', () => {
    const { container } = show();

    expect(container.textContent).toBe(
      'Acme Inc. is closed' +
        'You closed it on September 21, 2026.' +
        'Everything it measured is kept in case you want it back, and is deleted on October 21, 2026.' +
        'Bring it back' +
        'Sign out',
    );
  });

  it('brings it back with proof of where the request came from', async () => {
    const engine = engineAnswering(200, OPEN);
    const { cache } = show();

    await userEvent.click(screen.getByRole('button', { name: 'Bring it back' }));

    await waitFor(() => expect(cache.getQueryData<Session>(sessionKey)?.closure).toBeNull());

    const sent = change(engine);

    expect(sent.path).toBe('/api/organization/closure');
    expect(proofOn(sent)).toBe('proof-value');
  });

  it('goes to the dashboard once it is back, and reads the websites again on the way', async () => {
    engineAnswering(200, OPEN);
    const { cache } = show();
    cache.setQueryData(sitesKey, [SITE]);

    await userEvent.click(screen.getByRole('button', { name: 'Bring it back' }));

    await waitFor(() => expect(replace).toHaveBeenCalledWith('/app'));
    expect(cache.getQueryState(sitesKey)?.isInvalidated).toBe(true);
  });

  /**
   * An edition's plan says what bringing the account back did to it, and only a fresh answer can.
   * The session is left alone: it was just written from the engine's own answer.
   */
  it('reads everything else about the account again, but not the session it just wrote', async () => {
    engineAnswering(200, OPEN);
    const { cache } = show();
    cache.setQueryData(['edition', 'plan'], { plan: 'growth' });

    await userEvent.click(screen.getByRole('button', { name: 'Bring it back' }));

    await waitFor(() => expect(replace).toHaveBeenCalledWith('/app'));
    expect(cache.getQueryState(['edition', 'plan'])?.isInvalidated).toBe(true);
    expect(cache.getQueryState(sessionKey)?.isInvalidated).toBe(false);
  });

  /**
   * Somebody who belongs to two closed accounts is still described as having one closed once the
   * first is back, so the gate alone would leave them here reading about the other. They are
   * moved to the dashboard regardless, where the bar says what is still closed.
   */
  it('goes to the dashboard even when another account of theirs is still closed', async () => {
    engineAnswering(200, {
      ...OPEN,
      closure: { ...CLOSURE, name: 'Other Inc.', hasOpenAccount: true },
    });
    show();

    await userEvent.click(screen.getByRole('button', { name: 'Bring it back' }));

    await waitFor(() => expect(replace).toHaveBeenCalledWith('/app'));
  });

  /**
   * A refusal because the account is already open means somebody else brought it back first. The
   * screen says so and reads the session again, which is what moves the reader on. The engine's
   * second answer is held back until the refusal has been read, because once it arrives there is
   * no closed account left to draw.
   */
  it('reads the session again when somebody else has already brought it back', async () => {
    let release: (session: Session) => void = () => {};
    const reread = new Promise<Session>((resolve) => {
      release = resolve;
    });
    const engine = engineDoing(async (_path, init) =>
      init.method === 'DELETE'
        ? respondWith(409, {
            title: 'The account is not closed.',
            problems: [{ code: 'AccountNotClosed', description: 'It is open.' }],
          })
        : respondWith(200, await reread),
    );
    const { cache } = show();

    await userEvent.click(screen.getByRole('button', { name: 'Bring it back' }));

    expect(await screen.findByText('This account is already open.')).toBeInTheDocument();
    await waitFor(() =>
      expect(engine.all().map((request) => request.path)).toContain('/api/session'),
    );

    release(OPEN);

    await waitFor(() => expect(cache.getQueryData<Session>(sessionKey)?.closure).toBeNull());
    expect(replace).not.toHaveBeenCalled();
  });

  it('says when the engine cannot be reached, and lets them try again', async () => {
    engineStopped();
    show();

    await userEvent.click(screen.getByRole('button', { name: 'Bring it back' }));

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Bring it back' })).toBeEnabled();
  });

  it('ends the sign-in for somebody with nowhere else to be', async () => {
    const engine = engineAnswering(200, SIGNED_OUT);
    const { cache } = show();

    await userEvent.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => expect(cache.getQueryData<Session>(sessionKey)?.user).toBeNull());

    const sent = change(engine);

    expect(sent.path).toBe('/api/session');
    expect(proofOn(sent)).toBe('proof-value');
  });

  it('draws nothing when no account is closed', () => {
    const { container } = renderScreen(<ClosedAccount />, { signedInAs: OWNER });

    expect(container).toBeEmptyDOMElement();
  });
});
