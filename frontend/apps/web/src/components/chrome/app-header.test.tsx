import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ThemeProvider } from 'next-themes';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AppHeader } from '@/components/chrome/app-header';
import { engineDoing, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const OWNER = {
  id: '0195f7e0-0000-7000-8000-000000000000',
  emailAddress: 'owner@example.com',
  displayName: 'Ada Lovelace',
};

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
