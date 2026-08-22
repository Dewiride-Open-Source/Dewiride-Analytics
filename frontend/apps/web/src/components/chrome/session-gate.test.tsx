import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SessionGate } from '@/components/chrome/session-gate';
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
  replace.mockClear();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function session(setupCompleted: boolean, signedIn: boolean) {
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
