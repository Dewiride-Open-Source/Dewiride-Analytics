import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ThemeProvider } from 'next-themes';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AppHeader } from '@/components/chrome/app-header';
import type * as Navigation from '@/i18n/navigation';
import type { Site } from '@/lib/api/schemas';
import { engineDoing, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

/**
 * Which screen the browser is on comes from the framework's own router, and there is no router in
 * a document. The bar is told it is on the overview, which is the state the marker has to be right
 * about.
 */
vi.mock('@/i18n/navigation', async (original) => ({
  ...(await original<typeof Navigation>()),
  usePathname: () => '/',
}));

afterEach(() => {
  vi.unstubAllGlobals();
});

const OWNER = {
  id: '0195f7e0-0000-7000-8000-000000000000',
  emailAddress: 'owner@example.com',
  displayName: 'Ada Lovelace',
};

const SITE: Site = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df78',
  domain: 'example.com',
  displayName: 'My Blog',
  timeZoneId: 'Europe/London',
  role: 'owner',
};

const SHOP: Site = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df99',
  domain: 'shop.example.com',
  displayName: 'The Shop',
  timeZoneId: 'Europe/London',
  role: 'owner',
};

/** Answers with a signed-in person and their websites, and remembers one that is added. */
function engineWith(sites: readonly Site[]) {
  const known = [...sites];

  return engineDoing(async (path, init) => {
    if (init.method === 'POST' && path.endsWith('/api/sites')) {
      known.push(SHOP);

      return respondWith(200, SHOP);
    }

    if (path.endsWith('/api/sites')) {
      return respondWith(200, known);
    }

    return respondWith(200, { setupCompleted: true, user: OWNER, token: 'p' });
  });
}

function withTheme(ui: React.ReactElement) {
  return renderScreen(<ThemeProvider attribute="class">{ui}</ThemeProvider>, {
    sessionAlreadyRead: false,
  });
}

describe('the bar across the top', () => {
  it('names the product whether or not anybody is signed in', async () => {
    engineDoing(async () => respondWith(200, { setupCompleted: false, user: null, token: 'p' }));

    withTheme(<AppHeader />);

    expect(await screen.findByText('Dewiride Analytics')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Sign out/ })).not.toBeInTheDocument();
  });

  it('shows who is signed in and offers the way out', async () => {
    engineDoing(async () => respondWith(200, { setupCompleted: true, user: OWNER, token: 'p' }));

    withTheme(<AppHeader />);

    expect(await screen.findByText('Signed in as Ada Lovelace')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Sign out/ })).toBeInTheDocument();
  });

  it('ends the sign-in when asked, and stops showing who was here', async () => {
    let signedIn = true;
    const engine = engineDoing(async (_path, init) => {
      if (init.method === 'DELETE') {
        signedIn = false;

        return respondWith(200, { setupCompleted: true, user: null, token: 'a-fresh-proof' });
      }

      return respondWith(200, {
        setupCompleted: true,
        user: signedIn ? OWNER : null,
        token: 'p',
      });
    });

    withTheme(<AppHeader />);

    await userEvent.click(await screen.findByRole('button', { name: /Sign out/ }));

    await waitFor(() =>
      expect(screen.queryByText('Signed in as Ada Lovelace')).not.toBeInTheDocument(),
    );
    expect(engine.count).toBeGreaterThan(1);
  });

  /**
   * The two screens are two views of the same website, so moving between them is part of the
   * chrome rather than a link buried at the foot of one of them.
   */
  it('offers the way between the screens once somebody is signed in', async () => {
    engineWith([SITE]);

    withTheme(<AppHeader />);

    const sections = await screen.findByRole('navigation', { name: 'Sections' });

    expect(within(sections).getByRole('link', { name: 'Overview' })).toHaveAttribute('href', '/');
    expect(within(sections).getByRole('link', { name: 'User journey' })).toHaveAttribute(
      'href',
      '/journeys',
    );
  });

  it('marks the screen being looked at', async () => {
    engineWith([SITE]);

    withTheme(<AppHeader />);

    const sections = await screen.findByRole('navigation', { name: 'Sections' });

    expect(within(sections).getByRole('link', { name: 'Overview' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(within(sections).getByRole('link', { name: 'User journey' })).not.toHaveAttribute(
      'aria-current',
    );
  });

  it('offers no way between screens before anybody is signed in', async () => {
    engineDoing(async () => respondWith(200, { setupCompleted: false, user: null, token: 'p' }));

    withTheme(<AppHeader />);

    await screen.findByText('Dewiride Analytics');

    expect(screen.queryByRole('navigation', { name: 'Sections' })).not.toBeInTheDocument();
  });

  it('offers no website picker before anybody is signed in', async () => {
    engineDoing(async () => respondWith(200, { setupCompleted: false, user: null, token: 'p' }));

    withTheme(<AppHeader />);

    await screen.findByText('Dewiride Analytics');

    expect(screen.queryByRole('combobox', { name: 'Website' })).not.toBeInTheDocument();
  });

  it('names the website being looked at, and lets it be swapped for another', async () => {
    engineWith([SITE, SHOP]);

    withTheme(<AppHeader />);

    const picker = await screen.findByRole('combobox', { name: 'Website' });

    expect(picker).toHaveValue(SITE.id);

    await userEvent.selectOptions(picker, SHOP.id);

    await waitFor(() => expect(picker).toHaveValue(SHOP.id));
  });

  /**
   * An installation with one website is exactly the one that needs somewhere to add a second, so
   * the picker is there whether or not there is anything to pick between.
   */
  it('offers the picker with one website, because that is where another is added', async () => {
    engineWith([SITE]);

    withTheme(<AppHeader />);

    const picker = await screen.findByRole('combobox', { name: 'Website' });

    expect(picker).toBeInTheDocument();
    expect(screen.getByRole('option', { name: '+ Add a website' })).toBeInTheDocument();
  });

  it('opens the way to add a website from the end of the same list', async () => {
    engineWith([SITE]);

    withTheme(<AppHeader />);

    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Website' }), 'add');

    expect(await screen.findByRole('dialog', { name: 'Add a website' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Website address' })).toBeInTheDocument();
  });

  /**
   * The panel is mounted before the websites have arrived, so a starting value chosen at that
   * moment would be the zone of the machine somebody is sitting at rather than the one the website
   * they are already measuring counts its days in.
   */
  it('starts on the time zone the website already on screen counts its days in', async () => {
    engineWith([SITE]);

    withTheme(<AppHeader />);

    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Website' }), 'add');

    expect(await screen.findByRole('combobox', { name: 'Count its days in' })).toHaveValue(
      'Europe/London',
    );
  });

  /**
   * The same place goes by two names — `Asia/Calcutta` on one platform and `Asia/Kolkata` on
   * another — so a website's own zone is not always among the ones a particular browser lists.
   * A picker that cannot offer it settles on whichever zone happens to sort first, which is how a
   * second website ends up counted in a country nobody involved has ever been to.
   */
  it('starts on that zone even where this browser spells it the other way', async () => {
    vi.spyOn(Intl, 'supportedValuesOf').mockReturnValue([
      'Africa/Abidjan',
      'Asia/Calcutta',
      'Europe/London',
    ]);
    engineWith([{ ...SITE, timeZoneId: 'Asia/Kolkata' }]);

    withTheme(<AppHeader />);

    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Website' }), 'add');

    expect(await screen.findByRole('combobox', { name: 'Count its days in' })).toHaveValue(
      'Asia/Kolkata',
    );
  });

  it('adds a website and moves to it', async () => {
    const engine = engineWith([SITE]);

    withTheme(<AppHeader />);

    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Website' }), 'add');
    await userEvent.type(
      await screen.findByRole('textbox', { name: 'Website address' }),
      'shop.example.com',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Add website' }));

    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Website' })).toHaveValue(SHOP.id),
    );

    const added = engine.all().find((sent) => sent.init.method === 'POST');

    expect(added).toBeDefined();
    expect(JSON.parse(String(added?.init.body))).toMatchObject({ domain: 'shop.example.com' });
  });

  /**
   * A cookie the browser returns on its own is not proof that this page meant to send the request,
   * so the pair the engine issued travels with it.
   */
  it('proves where a new website came from', async () => {
    const engine = engineWith([SITE]);

    withTheme(<AppHeader />);

    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Website' }), 'add');
    await userEvent.type(
      await screen.findByRole('textbox', { name: 'Website address' }),
      'shop.example.com',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Add website' }));

    await waitFor(() =>
      expect(engine.all().some((sent) => sent.init.method === 'POST')).toBe(true),
    );

    const added = engine.all().find((sent) => sent.init.method === 'POST');
    const headers = added?.init.headers as Record<string, string> | undefined;

    expect(headers?.['X-Csrf-Token']).toBe('p');
  });

  it('says why a website could not be added rather than closing as though it had been', async () => {
    engineDoing(async (path, init) => {
      if (init.method === 'POST') {
        return respondWith(409, {
          title: 'That website is already here.',
          problems: [
            {
              code: 'SiteAlreadyMeasured',
              description: 'It is already in the list of websites you can switch between.',
            },
          ],
        });
      }

      if (path.endsWith('/api/sites')) {
        return respondWith(200, [SITE]);
      }

      return respondWith(200, { setupCompleted: true, user: OWNER, token: 'p' });
    });

    withTheme(<AppHeader />);

    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Website' }), 'add');
    await userEvent.type(
      await screen.findByRole('textbox', { name: 'Website address' }),
      'example.com',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Add website' }));

    expect(await screen.findByText(/That website is already in your list/)).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Add a website' })).toBeInTheDocument();
  });

  it('offers all three ways of choosing how the product looks', async () => {
    engineDoing(async () => respondWith(200, { setupCompleted: true, user: OWNER, token: 'p' }));

    withTheme(<AppHeader />);

    const appearance = await screen.findByRole('radiogroup', { name: 'Appearance' });

    expect(appearance).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Light' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Dark' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Match my device' })).toBeInTheDocument();
  });

  it('marks the chosen appearance as the one in use', async () => {
    engineDoing(async () => respondWith(200, { setupCompleted: true, user: OWNER, token: 'p' }));

    withTheme(<AppHeader />);

    await userEvent.click(await screen.findByRole('radio', { name: 'Dark' }));

    await waitFor(() =>
      expect(screen.getByRole('radio', { name: 'Dark' })).toHaveAttribute('aria-checked', 'true'),
    );
    expect(screen.getByRole('radio', { name: 'Light' })).toHaveAttribute('aria-checked', 'false');
  });
});
