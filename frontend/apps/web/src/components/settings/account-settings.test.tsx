import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AccountSettings } from '@/components/settings/account-settings';
import type { Organization } from '@/lib/api/schemas';
import { type Engine, engineDoing, respondWith, type Sent } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const OWNER = {
  id: '0195f7e0-0000-7000-8000-000000000001',
  emailAddress: 'ada@example.com',
  displayName: 'Ada Lovelace',
};

const ACCOUNT: Organization = {
  id: '0195f7e0-0000-7000-8000-0000000000aa',
  name: 'Acme Inc.',
  role: 'owner',
  people: [
    {
      id: OWNER.id,
      emailAddress: OWNER.emailAddress,
      displayName: OWNER.displayName,
      role: 'owner',
      joinedAt: '2026-06-01T09:00:00+00:00',
    },
    {
      id: '0195f7e0-0000-7000-8000-000000000002',
      emailAddress: 'grace@example.com',
      displayName: 'Grace Hopper',
      role: 'member',
      joinedAt: '2026-07-01T09:00:00+00:00',
    },
  ],
  invitations: [],
};

/** Answers with an account, and remembers everything it was asked to change about it. */
function engineHolding(account: Partial<Organization> = {}): Engine {
  const held: Organization = { ...ACCOUNT, ...account };

  return engineDoing(async (_path, init) =>
    init.method === undefined || init.method === 'GET'
      ? respondWith(200, held)
      : respondWith(204, null),
  );
}

/** Everything sent to the engine that was not simply reading the account back. */
function changes(engine: Engine): Sent[] {
  return engine
    .all()
    .filter((sent) => sent.init.method !== undefined && sent.init.method !== 'GET');
}

/** The one change a test expects, or a failed test where nothing was sent. */
function change(engine: Engine): Sent {
  const sent = changes(engine)[0];

  if (!sent) {
    throw new Error('Nothing that changes anything reached the engine.');
  }

  return sent;
}

function show(as = OWNER) {
  return renderScreen(<AccountSettings />, { signedInAs: as });
}

describe('the account screen', () => {
  it('names the account and everybody in it', async () => {
    engineHolding();

    show();

    expect(await screen.findByDisplayValue('Acme Inc.')).toBeInTheDocument();
    expect(screen.getByText('Grace Hopper')).toBeInTheDocument();
    expect(screen.getByText('grace@example.com')).toBeInTheDocument();
    expect(screen.getByText('2 people')).toBeInTheDocument();
  });

  it('marks the person reading it, so nobody wonders which row is theirs', async () => {
    engineHolding();

    show();

    const mine = (await screen.findByText('Ada Lovelace')).closest('li');

    expect(mine).not.toBeNull();
    expect(within(mine as HTMLElement).getByText('You')).toBeInTheDocument();
  });

  it('sends a new name for the account', async () => {
    const engine = engineHolding();

    show();

    const name = await screen.findByDisplayValue('Acme Inc.');
    await userEvent.clear(name);
    await userEvent.type(name, 'Acme Holdings');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(changes(engine)).toHaveLength(1));

    const sent = change(engine);

    expect(sent.path).toBe('/api/organization');
    expect(sent.init.method).toBe('PATCH');
    expect(JSON.parse(String(sent.init.body))).toEqual({ name: 'Acme Holdings' });
  });

  it('sends what somebody may do when it is changed', async () => {
    const engine = engineHolding();

    show();

    const grace = await screen.findByLabelText('What Grace Hopper can do');
    await userEvent.selectOptions(grace, 'admin');

    await waitFor(() => expect(changes(engine)).toHaveLength(1));

    const sent = change(engine);

    expect(sent.path).toBe('/api/organization/people/0195f7e0-0000-7000-8000-000000000002');
    expect(JSON.parse(String(sent.init.body))).toEqual({ role: 'admin' });
  });

  /**
   * Taking somebody's access away is not something to do by mis-clicking a row, and the panel says
   * plainly that nothing already collected is thrown away with them.
   */
  it('asks before taking somebody out of the account', async () => {
    const engine = engineHolding();

    show();

    await userEvent.click(await screen.findByRole('button', { name: 'Remove Grace Hopper' }));

    expect(await screen.findByText(/Grace Hopper will lose access/)).toBeInTheDocument();
    expect(changes(engine)).toHaveLength(0);

    await userEvent.click(screen.getByRole('button', { name: /^Remove$/ }));

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(change(engine).init.method).toBe('DELETE');
  });

  /**
   * Taking yourself out of the account you are reading would leave you signed in with nothing to
   * look at, and on an account with one owner it is the one change the engine always refuses.
   */
  it('does not offer to take the person reading it out of the account', async () => {
    engineHolding();

    show();

    expect(await screen.findByRole('button', { name: 'Remove Grace Hopper' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove Ada Lovelace' })).not.toBeInTheDocument();
  });

  it('sends an invitation with the address and what they would be able to do', async () => {
    const engine = engineHolding();

    show();

    await userEvent.type(await screen.findByLabelText('Email address'), 'alan@example.com');
    await userEvent.selectOptions(screen.getByLabelText('What they can do'), 'admin');
    await userEvent.click(screen.getByRole('button', { name: 'Send invitation' }));

    await waitFor(() => expect(changes(engine)).toHaveLength(1));

    const sent = change(engine);

    expect(sent.path).toBe('/api/organization/invitations');
    expect(JSON.parse(String(sent.init.body))).toEqual({
      emailAddress: 'alan@example.com',
      role: 'admin',
    });
  });

  it('checks the address before sending anything to the engine', async () => {
    const engine = engineHolding();

    show();

    await userEvent.type(await screen.findByLabelText('Email address'), 'not-an-address');
    await userEvent.click(screen.getByRole('button', { name: 'Send invitation' }));

    expect(await screen.findByText("That doesn't look like an email address.")).toBeInTheDocument();
    expect(changes(engine)).toHaveLength(0);
  });

  it('lists an invitation nobody has taken up yet, and offers to withdraw it', async () => {
    const engine = engineHolding({
      invitations: [
        {
          id: '0195f7e0-0000-7000-8000-0000000000bb',
          emailAddress: 'alan@example.com',
          role: 'member',
          invitedAt: '2026-08-20T09:00:00+00:00',
          expiresAt: '2026-08-27T09:00:00+00:00',
        },
      ],
    });

    show();

    expect(await screen.findByText('alan@example.com')).toBeInTheDocument();
    expect(screen.getByText('Can view · link works until August 27')).toBeInTheDocument();

    await userEvent.click(
      screen.getByRole('button', { name: 'Cancel the invitation to alan@example.com' }),
    );

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(change(engine).path).toBe(
      '/api/organization/invitations/0195f7e0-0000-7000-8000-0000000000bb',
    );
  });

  /**
   * Somebody who merely belongs to the account reads it. Offering them controls the engine would
   * refuse would be a screen that lies about what they can do.
   */
  it('offers a member nothing to change', async () => {
    engineHolding({ role: 'member' });

    show(ACCOUNT.people[1]);

    expect(await screen.findByText('Acme Inc.')).toBeInTheDocument();
    expect(screen.queryByLabelText('What Grace Hopper can do')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Send invitation' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Remove/ })).not.toBeInTheDocument();
  });

  it('explains a refusal in the words the reader knows', async () => {
    engineDoing(async (_path, init) =>
      init.method === undefined || init.method === 'GET'
        ? respondWith(200, ACCOUNT)
        : respondWith(409, {
            title: 'Somebody has to run this account.',
            problems: [{ code: 'LastOwnerRemains', description: 'Make somebody else an owner.' }],
          }),
    );

    show();

    const mine = await screen.findByLabelText('What Ada Lovelace can do');
    await userEvent.selectOptions(mine, 'member');

    expect(
      await screen.findByText('Someone has to run this account. Make someone else an owner first.'),
    ).toBeInTheDocument();
  });
});
