import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SignInForm } from '@/components/account/sign-in-form';
import { engineAnswering, engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const PASSPHRASE = 'vermilion tractor almanac';

async function fillIn(address: string, password: string) {
  await userEvent.type(screen.getByLabelText('Email address'), address);
  await userEvent.type(screen.getByLabelText('Password'), password);
}

describe('the sign-in screen', () => {
  it('asks for the details before sending anything to the engine', async () => {
    const engine = engineDoing(async () => respondWith(200, {}));

    renderScreen(<SignInForm />);
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Enter your email address.')).toBeInTheDocument();
    expect(screen.getByText('Enter a password.')).toBeInTheDocument();
    expect(engine.count).toBe(0);
  });

  it('refuses an address that is plainly not one', async () => {
    engineDoing(async () => respondWith(200, {}));

    renderScreen(<SignInForm />);
    await fillIn('nobody', PASSPHRASE);
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText("That doesn't look like an email address.")).toBeInTheDocument();
  });

  it('sends the details with the proof of origin the engine issued', async () => {
    const engine = engineAnswering(200, {
      setupCompleted: true,
      user: {
        id: '0195f7e0-0000-7000-8000-000000000000',
        emailAddress: 'nobody@example.com',
        displayName: 'Nobody',
      },
      token: 'a-fresh-proof',
    });

    renderScreen(<SignInForm />);
    await fillIn('nobody@example.com', PASSPHRASE);
    await userEvent.click(screen.getByRole('checkbox', { name: /Keep me signed in/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(engine.count).toBe(1));

    expect(engine.first().path).toBe('/api/session');
    expect(engine.header('X-Csrf-Token')).toBe('proof-value');
    expect(engine.body()).toEqual({
      emailAddress: 'nobody@example.com',
      password: PASSPHRASE,
      staySignedIn: true,
    });
  });

  /**
   * The engine answers a wrong password, an unknown address and a paused account identically. The
   * screen must not undo that by saying something more specific.
   */
  it('says the same thing however the attempt failed', async () => {
    engineAnswering(401, {
      title: 'Those details were not recognised.',
      detail: 'Check the email address and password.',
    });

    renderScreen(<SignInForm />);
    await fillIn('nobody@example.com', 'the wrong passphrase');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    const refusal = await screen.findByRole('alert');

    expect(refusal).toHaveTextContent("We couldn't sign you in");
    expect(refusal).not.toHaveTextContent(/locked|does not exist|no account/i);
  });

  it('reports an engine that cannot be reached as exactly that', async () => {
    engineStopped();

    renderScreen(<SignInForm />);
    await fillIn('nobody@example.com', PASSPHRASE);
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
  });
});

describe('reading back what was typed', () => {
  /**
   * A long passphrase typed blind is three wrong attempts and a locked account. The reveal is
   * announced by what it will do next, so a screen reader hears "Show password" while it is
   * hidden rather than a label that describes the state it is already in.
   */
  it('can be shown, and then hidden again', async () => {
    renderScreen(<SignInForm />);

    const password = screen.getByLabelText('Password');

    expect(password).toHaveAttribute('type', 'password');

    await userEvent.click(screen.getByRole('button', { name: 'Show password' }));

    expect(password).toHaveAttribute('type', 'text');

    await userEvent.click(screen.getByRole('button', { name: 'Hide password' }));

    expect(password).toHaveAttribute('type', 'password');
  });
});
