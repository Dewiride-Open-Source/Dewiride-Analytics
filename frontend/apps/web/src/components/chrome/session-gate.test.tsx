import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SessionGate } from '@/components/chrome/session-gate';
import type { ClosedAccount } from '@/lib/api/schemas';
import { engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

const replace = vi.fn();
const pathname = vi.fn(() => '/app');

vi.mock('@/i18n/navigation', () => ({
  usePathname: () => pathname(),
  useRouter: () => ({ replace }),
}));

beforeEach(() => {
  pathname.mockReturnValue('/app');
});

afterEach(() => {
  vi.unstubAllGlobals();
});

const CLOSED_ACCOUNT: ClosedAccount = {
  name: 'Acme',
  closedAt: '2026-09-21T09:30:00+00:00',
  deletionDue: '2026-10-21T09:30:00+00:00',
  closedBy: null,
  closedByYou: true,
  canRestore: true,
  hasOpenAccount: false,
};

function session(setupCompleted: boolean, signedIn: boolean, closure: ClosedAccount | null = null) {
  return {
    setupCompleted,
    user: signedIn
      ? {
          id: '0195f7e0-0000-7000-8000-000000000000',
          emailAddress: 'owner@example.com',
          displayName: 'Owner',
        }
      : null,
    token: 'proof-value',
    closure,
  };
}

describe('landing on the right screen', () => {
  it('shows nothing but a quiet wait until the engine has answered', () => {
    engineDoing(() => new Promise<Response>(() => {}));

    renderScreen(
      <SessionGate>
        <p>The dashboard</p>
      </SessionGate>,
      { sessionAlreadyRead: false },
    );

    expect(screen.getByRole('status')).toHaveTextContent('Loading');
    expect(screen.queryByText('The dashboard')).not.toBeInTheDocument();
  });

  it('sends somebody to the setup screen while the install has no owner', async () => {
    engineDoing(async () => respondWith(200, session(false, false)));

    renderScreen(
      <SessionGate>
        <p>The dashboard</p>
      </SessionGate>,
      { sessionAlreadyRead: false },
    );

    await waitFor(() => expect(replace).toHaveBeenCalledWith('/app/set-up'));
    expect(screen.queryByText('The dashboard')).not.toBeInTheDocument();
  });

  it('shows the screen once somebody is signed in and already in the right place', async () => {
    engineDoing(async () => respondWith(200, session(true, true)));

    renderScreen(
      <SessionGate>
        <p>The dashboard</p>
      </SessionGate>,
      { sessionAlreadyRead: false },
    );

    expect(await screen.findByText('The dashboard')).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });

  /**
   * Somebody whose only account is closed has one screen, and the numbers are not it: nothing
   * behind the gate is theirs to look at until the account is brought back.
   */
  it('sends somebody whose only account is closed to the screen that says so', async () => {
    engineDoing(async () => respondWith(200, session(true, true, CLOSED_ACCOUNT)));

    renderScreen(
      <SessionGate>
        <p>The dashboard</p>
      </SessionGate>,
      { sessionAlreadyRead: false },
    );

    await waitFor(() => expect(replace).toHaveBeenCalledWith('/app/closed'));
    expect(screen.queryByText('The dashboard')).not.toBeInTheDocument();
  });

  it('shows the closed screen to somebody whose only account is closed', async () => {
    pathname.mockReturnValue('/app/closed');
    engineDoing(async () => respondWith(200, session(true, true, CLOSED_ACCOUNT)));

    renderScreen(
      <SessionGate>
        <p>The closed account</p>
      </SessionGate>,
      { sessionAlreadyRead: false },
    );

    expect(await screen.findByText('The closed account')).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });

  /**
   * Somebody who also belongs to an open account still has a dashboard to use, so a closed one
   * is something the bar mentions rather than a wall in front of every screen.
   */
  it('shows the screen to somebody who still has an open account', async () => {
    const stillOpen = { ...CLOSED_ACCOUNT, hasOpenAccount: true };
    engineDoing(async () => respondWith(200, session(true, true, stillOpen)));

    renderScreen(
      <SessionGate>
        <p>The dashboard</p>
      </SessionGate>,
      { sessionAlreadyRead: false },
    );

    expect(await screen.findByText('The dashboard')).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });

  it('offers a way to try again when the engine cannot be reached', async () => {
    const engine = engineStopped();

    renderScreen(
      <SessionGate>
        <p>The dashboard</p>
      </SessionGate>,
      { sessionAlreadyRead: false },
    );

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(engine.count).toBeGreaterThan(1));
  });
});
