import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { YourDetails } from '@/components/settings/your-details';
import { type Engine, engineDoing, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const ME = {
  id: '0195f7e0-0000-7000-8000-000000000001',
  emailAddress: 'ada@example.com',
  displayName: 'Ada Lovelace',
};

const PASSPHRASE = 'sequoia harbour lantern';

function show() {
  return renderScreen(<YourDetails />, { signedInAs: ME });
}

/** Takes everything and answers as the engine does: a name back, and nothing for a password. */
function engineTaking(): Engine {
  return engineDoing(async (path) =>
    path.endsWith('/password') ? respondWith(204, null) : respondWith(200, ME),
  );
}

describe('your own details', () => {
  it('says which address the person signs in with, and does not offer to change it', () => {
    engineTaking();

    show();

    expect(screen.getByText('You sign in as ada@example.com.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Email address')).not.toBeInTheDocument();
  });

  it('will not send a name that has not changed', async () => {
    const engine = engineTaking();

    show();

    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    expect(engine.count).toBe(0);
  });

  it('sends a new name, trimmed of whatever was typed around it', async () => {
    const engine = engineTaking();

    show();

    const name = screen.getByLabelText('Your name');
    await userEvent.clear(name);
    await userEvent.type(name, '  Grace Hopper  ');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(engine.count).toBe(1));

    expect(engine.first().path).toBe('/api/account');
    expect(engine.body()).toEqual({ displayName: 'Grace Hopper' });
  });

  it('checks the new password before sending anything to the engine', async () => {
    const engine = engineTaking();

    show();

    await userEvent.type(screen.getByLabelText('Current password'), 'whatever they use now');
    await userEvent.type(screen.getByLabelText('New password'), 'short');
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByText('Use at least 15 characters.')).toBeInTheDocument();
    expect(engine.count).toBe(0);
  });

  it('sends both passwords together, because one without the other proves nothing', async () => {
    const engine = engineTaking();

    show();

    await userEvent.type(screen.getByLabelText('Current password'), 'the one they use now');
    await userEvent.type(screen.getByLabelText('New password'), PASSPHRASE);
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }));

    await waitFor(() => expect(engine.count).toBe(1));

    expect(engine.first().path).toBe('/api/account/password');
    expect(engine.first().init.method).toBe('PUT');
    expect(engine.body()).toEqual({
      currentPassword: 'the one they use now',
      newPassword: PASSPHRASE,
    });
  });

  it('empties both boxes once the password has been changed', async () => {
    engineTaking();

    show();

    await userEvent.type(screen.getByLabelText('Current password'), 'the one they use now');
    await userEvent.type(screen.getByLabelText('New password'), PASSPHRASE);
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByText('Password changed')).toBeInTheDocument();
    expect(screen.getByLabelText('Current password')).toHaveValue('');
    expect(screen.getByLabelText('New password')).toHaveValue('');
  });

  it('explains a password that did not match in the words the reader knows', async () => {
    engineDoing(async () =>
      respondWith(400, {
        title: 'That password did not match.',
        problems: [{ code: 'CurrentPasswordWrong', description: 'Enter the one you use now.' }],
      }),
    );

    show();

    await userEvent.type(screen.getByLabelText('Current password'), 'not the one');
    await userEvent.type(screen.getByLabelText('New password'), PASSPHRASE);
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }));

    expect(
      await screen.findByText("That password didn't match. Enter the one you sign in with now."),
    ).toBeInTheDocument();
  });
});
