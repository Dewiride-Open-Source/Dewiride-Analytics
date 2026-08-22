import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { JoinForm } from '@/components/account/join-form';
import { type Engine, engineDoing, respondWith, type Sent } from '@/test/engine';
import { renderScreen } from '@/test/harness';

const asked = vi.fn(() => new URLSearchParams());

vi.mock('next/navigation', async (importOriginal) => ({
  ...(await importOriginal<Record<string, unknown>>()),
  useSearchParams: () => asked(),
}));

const TOKEN = 'dwi_a-secret-from-the-link';
const PASSPHRASE = 'cardamom lantern rowboat';

const JOINED = {
  signedIn: true,
  user: {
    id: '0195f7e0-0000-7000-8000-000000000009',
    emailAddress: 'alan@example.com',
    displayName: 'Alan Turing',
  },
  token: 'a-fresh-proof-value',
};

beforeEach(() => {
  asked.mockReturnValue(new URLSearchParams({ token: TOKEN }));
});

afterEach(() => {
  vi.unstubAllGlobals();
});

/**
 * Answers what the engine answers: what the invitation is for, and then what came of taking it up.
 */
function engineOffering(needsAccount: boolean, joined: unknown = JOINED): Engine {
  return engineDoing(async (path) =>
    path.endsWith('/preview')
      ? respondWith(200, {
          organizationName: 'Acme Inc.',
          emailAddress: 'alan@example.com',
          needsAccount,
        })
      : respondWith(200, joined),
  );
}

/** What the screen sent after reading the invitation back, which is the act being tested. */
function accepted(engine: Engine): Sent {
  const sent = engine.all()[1];

  if (!sent) {
    throw new Error('The invitation was never taken up.');
  }

  return sent;
}

describe('taking up an invitation', () => {
  it('says whose account it is for, and to which address', async () => {
    engineOffering(true);

    renderScreen(<JoinForm />);

    expect(await screen.findByText('Join Acme Inc.')).toBeInTheDocument();
    expect(screen.getByText('Invited to alan@example.com.')).toBeInTheDocument();
  });

  it('sends the secret in the body rather than putting it in the address', async () => {
    const engine = engineOffering(true);

    renderScreen(<JoinForm />);

    await screen.findByText('Join Acme Inc.');

    expect(engine.first().path).toBe('/api/invitations/preview');
    expect(engine.first().path).not.toContain(TOKEN);
    expect(engine.body()).toEqual({ token: TOKEN });
  });

  it('asks somebody with no account here to choose a password', async () => {
    const engine = engineOffering(true);

    renderScreen(<JoinForm />);

    await userEvent.type(await screen.findByLabelText('Your name'), 'Alan Turing');
    await userEvent.type(screen.getByLabelText('Choose a password'), PASSPHRASE);
    await userEvent.click(screen.getByRole('button', { name: 'Join and start' }));

    await waitFor(() => expect(engine.count).toBe(2));

    const sent = accepted(engine);

    expect(sent.path).toBe('/api/invitations/accept');
    expect(JSON.parse(String(sent.init.body))).toEqual({
      token: TOKEN,
      displayName: 'Alan Turing',
      password: PASSPHRASE,
    });
  });

  it('checks the password before sending anything', async () => {
    const engine = engineOffering(true);

    renderScreen(<JoinForm />);

    await userEvent.type(await screen.findByLabelText('Choose a password'), 'short');
    await userEvent.click(screen.getByRole('button', { name: 'Join and start' }));

    expect(await screen.findByText('Use at least 15 characters.')).toBeInTheDocument();
    expect(engine.count).toBe(1);
  });

  it('opens the numbers for somebody who has just been signed in', async () => {
    engineOffering(true);

    renderScreen(<JoinForm />);

    await userEvent.type(await screen.findByLabelText('Choose a password'), PASSPHRASE);
    await userEvent.click(screen.getByRole('button', { name: 'Join and start' }));

    expect(await screen.findByText('You have joined Acme Inc.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open my numbers' })).toHaveAttribute('href', '/app');
  });

  /**
   * Somebody who already has an account here is not asked for anything. Holding the link proves
   * the mailbox, and what it buys is a standing in one account rather than a way into their own.
   */
  it('asks somebody who already has an account for nothing at all', async () => {
    const engine = engineOffering(false, { ...JOINED, signedIn: false, user: null });

    renderScreen(<JoinForm />);

    await userEvent.click(await screen.findByRole('button', { name: 'Join' }));

    await waitFor(() => expect(engine.count).toBe(2));

    expect(JSON.parse(String(accepted(engine).init.body))).toEqual({
      token: TOKEN,
      displayName: null,
      password: null,
    });
  });

  it('sends somebody who already has an account to sign in with the password they have', async () => {
    engineOffering(false, { ...JOINED, signedIn: false, user: null });

    renderScreen(<JoinForm />);

    await userEvent.click(await screen.findByRole('button', { name: 'Join' }));

    expect(await screen.findByText('You have joined Acme Inc.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/app/sign-in');
  });

  it('says plainly when the link is missing its secret', () => {
    engineOffering(true);
    asked.mockReturnValue(new URLSearchParams());

    renderScreen(<JoinForm />);

    expect(screen.getByText('This link is incomplete')).toBeInTheDocument();
  });

  /**
   * A spent link, a withdrawn one and one that was never issued are one screen. Telling them apart
   * would say whether somebody else had already used it.
   */
  it('says the same thing about every link that will not do', async () => {
    engineDoing(async () =>
      respondWith(400, {
        title: 'That invitation cannot be used.',
        problems: [{ code: 'InvitationLinkNotUsable', description: 'Ask for a new one.' }],
      }),
    );

    renderScreen(<JoinForm />);

    expect(await screen.findByText('This invitation cannot be used')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go to sign in' })).toHaveAttribute(
      'href',
      '/app/sign-in',
    );
  });
});
