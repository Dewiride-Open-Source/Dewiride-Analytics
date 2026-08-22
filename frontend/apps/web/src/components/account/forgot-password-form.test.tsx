import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ForgotPasswordForm } from '@/components/account/forgot-password-form';
import { engineAnswering, engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

async function ask(address: string) {
  await userEvent.type(screen.getByLabelText('Email address'), address);
  await userEvent.click(screen.getByRole('button', { name: 'Send me a link' }));
}

describe('asking for a way back in', () => {
  it('checks the address before sending anything to the engine', async () => {
    const engine = engineDoing(async () => respondWith(202, null));

    renderScreen(<ForgotPasswordForm />);
    await userEvent.click(screen.getByRole('button', { name: 'Send me a link' }));

    expect(await screen.findByText('Enter your email address.')).toBeInTheDocument();
    expect(engine.count).toBe(0);
  });

  it('refuses an address that is plainly not one', async () => {
    engineDoing(async () => respondWith(202, null));

    renderScreen(<ForgotPasswordForm />);
    await ask('nobody');

    expect(await screen.findByText("That doesn't look like an email address.")).toBeInTheDocument();
  });

  it('sends the address with the proof of origin the engine issued', async () => {
    const engine = engineDoing(async () => respondWith(202, null));

    renderScreen(<ForgotPasswordForm />);
    await ask('  nobody@example.com  ');

    await waitFor(() => expect(engine.count).toBe(1));

    expect(engine.first().path).toBe('/api/password-reset');
    expect(engine.header('X-Csrf-Token')).toBe('proof-value');
    expect(engine.body()).toEqual({ emailAddress: 'nobody@example.com' });
  });

  /**
   * The engine says nothing about whether the address belongs to an account, and the screen must
   * not invent a difference: what is shown afterwards is one sentence that is true either way.
   */
  it('says only that a link is on its way, and never whether there is an account', async () => {
    engineDoing(async () => respondWith(202, null));

    renderScreen(<ForgotPasswordForm />);
    await ask('nobody@example.com');

    const answered = await screen.findByRole('heading', { name: 'Check your inbox' });

    expect(answered).toBeInTheDocument();
    expect(screen.getByText(/If that address has an account/)).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(/no account|does not exist|not registered/i);
    expect(screen.queryByRole('button', { name: 'Send me a link' })).not.toBeInTheDocument();
  });

  it('reports an engine that cannot be reached as exactly that', async () => {
    engineStopped();

    renderScreen(<ForgotPasswordForm />);
    await ask('nobody@example.com');

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
  });

  it('reports having asked too often as something to wait out', async () => {
    engineAnswering(429, { title: 'Too many attempts.' });

    renderScreen(<ForgotPasswordForm />);
    await ask('nobody@example.com');

    expect(await screen.findByText('Too many attempts')).toBeInTheDocument();
  });
});
