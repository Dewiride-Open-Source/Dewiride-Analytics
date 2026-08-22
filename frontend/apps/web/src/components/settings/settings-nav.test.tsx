import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { SettingsNav } from '@/components/settings/settings-nav';
import type * as Navigation from '@/i18n/navigation';
import { renderScreen } from '@/test/harness';

/**
 * Which screen the browser is on comes from the framework's own router, and there is no router in
 * a document. The rail is told it is on the account, which is the state the marker has to be right
 * about.
 */
vi.mock('@/i18n/navigation', async (original) => ({
  ...(await original<typeof Navigation>()),
  usePathname: () => '/app/settings',
}));

describe('the way between the screens inside the account', () => {
  it('leads to the screens the open-source product has', () => {
    renderScreen(<SettingsNav />);

    expect(screen.getByRole('link', { name: 'Account' })).toHaveAttribute('href', '/app/settings');
    expect(screen.getByRole('link', { name: 'Your details' })).toHaveAttribute(
      'href',
      '/app/settings/you',
    );
  });

  /**
   * An allowance only means anything where somebody else is running the service, so the
   * open-source edition contributes nothing here and the rail is two entries rather than three.
   */
  it('offers no plan, because a self-hosted installation has none', () => {
    renderScreen(<SettingsNav />);

    expect(screen.getAllByRole('link')).toHaveLength(2);
    expect(screen.queryByRole('link', { name: 'Plan' })).not.toBeInTheDocument();
  });

  it('marks the screen being read, and only that one', () => {
    renderScreen(<SettingsNav />);

    expect(screen.getByRole('link', { name: 'Account' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: 'Your details' })).not.toHaveAttribute('aria-current');
  });
});
