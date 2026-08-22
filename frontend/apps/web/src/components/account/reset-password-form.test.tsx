import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ResetPasswordForm } from '@/components/account/reset-password-form';
import { engineAnswering, engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

const asked = vi.fn(() => new URLSearchParams());

vi.mock('next/navigation', async (importOriginal) => ({
  ...(await importOriginal<Record<string, unknown>>()),
  useSearchParams: () => asked(),
}));

const PASSPHRASE = 'cardamom lantern rowboat';

const LINK = new URLSearchParams({
  address: 'nobody@example.com',
  token: 'a-token-from-the-link',
});

beforeEach(() => {
  asked.mockReturnValue(LINK);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

async function choose(password: string) {
  await userEvent.type(screen.getByLabelText('New password'), password);
  await userEvent.click(screen.getByRole('button', { name: 'Save my new password' }));
}

describe('choosing a new password from a link', () => {
  it('names the account the link was sent for', () => {
    engineDoing(async () => respondWith(204, null));

    renderScreen(<ResetPasswordForm />);

    expect(screen.getByText('For nobody@example.com.')).toBeInTheDocument();
  });

  it('checks the password before sending anything to the engine', async () => {
    const engine = engineDoing(async () => respondWith(204, null));

    renderScreen(<ResetPasswordForm />);
    await choose('short');

    expect(await screen.findByText('Use at least 15 characters.')).toBeInTheDocument();
    expect(engine.count).toBe(0);
  });

  it('sends the link back with the password and the proof of origin', async () => {
    const engine = engineDoing(async () => respondWith(204, null));

    renderScreen(<ResetPasswordForm />);
    await choose(PASSPHRASE);

    await waitFor(() => expect(engine.count).toBe(1));

    expect(engine.first().path).toBe('/api/password-reset/complete');
    expect(engine.header('X-Csrf-Token')).toBe('proof-value');
    expect(engine.body()).toEqual({
      emailAddress: 'nobody@example.com',
      token: 'a-token-from-the-link',
      password: PASSPHRASE,
    });
  });

  it('sends somebody to sign in once the password is saved', async () => {
    engineDoing(async () => respondWith(204, null));

    renderScreen(<ResetPasswordForm />);
    await choose(PASSPHRASE);

    expect(await screen.findByText('Your new password is saved')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go to sign in' })).toHaveAttribute(
      'href',
      '/app/sign-in',
    );
  });

  it('shows the reason a password was refused and keeps the form', async () => {
    engineAnswering(400, {
      title: "We couldn't use those details",
      problems: [{ code: 'PasswordIsPredictable', description: 'That password is easy to guess.' }],
    });

    renderScreen(<ResetPasswordForm />);
    await choose('aaaaaaaaaaaaaaaaaa');

    expect(await screen.findByText(/easy to guess/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save my new password' })).toBeInTheDocument();
  });

  it('reports an engine that cannot be reached as exactly that', async () => {
    engineStopped();

    renderScreen(<ResetPasswordForm />);
    await choose(PASSPHRASE);

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
  });
});

describe('a link that leads nowhere', () => {
  it('says so when the address is missing half of it', () => {
    asked.mockReturnValue(new URLSearchParams({ address: 'nobody@example.com' }));

    renderScreen(<ResetPasswordForm />);

    expect(screen.getByText('This link is incomplete')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Send me a new one' })).toHaveAttribute(
      'href',
      '/app/forgot-password',
    );
  });

  /**
   * Expired, already used and never valid are one answer from the engine on purpose. The screen
   * repeats that answer rather than guessing which of the three it was.
   */
  it('offers another one when the engine will not take it', async () => {
    engineAnswering(400, {
      title: 'That link cannot be used.',
      problems: [
        { code: 'ResetLinkNotUsable', description: 'Reset links stop working after 24 hours.' },
      ],
    });

    renderScreen(<ResetPasswordForm />);
    await choose(PASSPHRASE);

    expect(await screen.findByText('That link no longer works')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Send me a new one' })).toHaveAttribute(
      'href',
      '/app/forgot-password',
    );
    expect(screen.queryByLabelText('New password')).not.toBeInTheDocument();
  });
});
