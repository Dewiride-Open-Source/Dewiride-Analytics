import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SetupForm } from '@/components/account/setup-form';
import { engineAnswering, engineDoing, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const PASSPHRASE = 'vermilion tractor almanac';

const CLAIMED = {
  siteId: '01a013fa-49d6-77be-b65d-20ec86e9df78',
  user: {
    id: '0195f7e0-0000-7000-8000-000000000000',
    emailAddress: 'owner@example.com',
    displayName: 'Owner',
  },
  token: 'a-fresh-proof',
};

async function fillIn(overrides: { readonly website?: string; readonly password?: string } = {}) {
  await userEvent.type(screen.getByLabelText('Email address'), 'owner@example.com');
  await userEvent.type(screen.getByLabelText('Password'), overrides.password ?? PASSPHRASE);
  await userEvent.type(screen.getByLabelText('Organisation'), 'My Blog');
  await userEvent.type(
    screen.getByLabelText('Website address'),
    overrides.website ?? 'example.com',
  );
}

function create() {
  return screen.getByRole('button', { name: 'Create my account' });
}

describe('the setup screen', () => {
  it('asks for everything it needs before sending anything', async () => {
    const engine = engineDoing(async () => respondWith(200, {}));

    renderScreen(<SetupForm />);
    await userEvent.click(create());

    expect(await screen.findByText('Enter your email address.')).toBeInTheDocument();
    expect(screen.getByText('Enter a password.')).toBeInTheDocument();
    expect(screen.getByText('Enter a name for your organisation.')).toBeInTheDocument();
    expect(screen.getByText('Enter your website address.')).toBeInTheDocument();
    expect(engine.count).toBe(0);
  });

  it('refuses a password shorter than the engine will accept', async () => {
    const engine = engineDoing(async () => respondWith(200, {}));

    renderScreen(<SetupForm />);
    await fillIn({ password: 'short' });
    await userEvent.click(create());

    expect(await screen.findByText('Use at least 15 characters.')).toBeInTheDocument();
    expect(engine.count).toBe(0);
  });

  /**
   * People copy the address out of the browser, so what arrives is a whole web address. Storing
   * that verbatim would leave the site recorded under a name no report could match.
   */
  it('reduces a pasted web address to the site it names', async () => {
    const engine = engineAnswering(200, CLAIMED);

    renderScreen(<SetupForm />);
    await fillIn({ website: 'https://Blog.Example.COM/posts/' });
    await userEvent.click(create());

    await waitFor(() => expect(engine.count).toBe(1));
    expect(engine.body()).toMatchObject({ siteDomain: 'blog.example.com' });
  });

  it('sends the whole account, the organisation and the site in one go', async () => {
    const engine = engineAnswering(200, CLAIMED);

    renderScreen(<SetupForm />);
    await userEvent.type(screen.getByLabelText(/Your name/), 'Ada');
    await fillIn();
    await userEvent.click(create());

    await waitFor(() => expect(engine.count).toBe(1));

    expect(engine.first().path).toBe('/api/setup');
    expect(engine.header('X-Csrf-Token')).toBe('proof-value');
    expect(engine.body()).toMatchObject({
      emailAddress: 'owner@example.com',
      password: PASSPHRASE,
      displayName: 'Ada',
      organizationName: 'My Blog',
      siteDomain: 'example.com',
    });
  });

  it('leaves the name out rather than sending an empty one', async () => {
    const engine = engineAnswering(200, CLAIMED);

    renderScreen(<SetupForm />);
    await fillIn();
    await userEvent.click(create());

    await waitFor(() => expect(engine.count).toBe(1));
    expect(engine.body()).toMatchObject({ displayName: null });
  });

  it('offers the time zone this device is set to', async () => {
    engineAnswering(200, CLAIMED);

    renderScreen(<SetupForm />);

    const chosen = screen.getByLabelText('Reporting time zone') as HTMLSelectElement;

    expect(chosen.value).toBe(Intl.DateTimeFormat().resolvedOptions().timeZone);
    expect(chosen.options.length).toBeGreaterThan(100);
  });

  it('explains a refused password in words rather than repeating a code', async () => {
    engineAnswering(400, {
      title: 'Those details cannot be used.',
      problems: [{ code: 'PasswordIsPredictable', description: 'Anything.' }],
    });

    renderScreen(<SetupForm />);
    await fillIn();
    await userEvent.click(create());

    expect(
      await screen.findByText('That password is easy to guess. Try a few unrelated words instead.'),
    ).toBeInTheDocument();
  });

  /**
   * A reason nobody has written words for yet is still shown, because hiding the only explanation
   * would leave somebody unable to fix a password they are perfectly able to fix.
   */
  it('passes on a reason it has no words of its own for', async () => {
    engineAnswering(400, {
      title: 'Those details cannot be used.',
      problems: [{ code: 'SomethingNobodyHasSeenYet', description: 'A very specific problem.' }],
    });

    renderScreen(<SetupForm />);
    await fillIn();
    await userEvent.click(create());

    expect(await screen.findByText('A very specific problem.')).toBeInTheDocument();
  });

  it('points at the way in when somebody else has already claimed the install', async () => {
    engineAnswering(409, { title: 'This installation has already been set up.' });

    renderScreen(<SetupForm />);
    await fillIn();
    await userEvent.click(create());

    expect(await screen.findByText('This installation already has an owner')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go to sign in' })).toHaveAttribute(
      'href',
      '/app/sign-in',
    );
  });
});
