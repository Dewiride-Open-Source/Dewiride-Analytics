import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ClosedAccount } from '@/components/account/closed-account';
import { CloseAccount } from '@/components/settings/close-account';
import type * as Navigation from '@/i18n/navigation';
import type { ClosedAccount as Closure } from '@/lib/api/schemas';
import { renderScreen } from '@/test/harness';

/**
 * Stands in for an edition with something to say about money when an account is closed.
 *
 * The open-source edition says nothing, so the rule for where that something is shown can only be
 * proved with an edition that has it — which is what the commercial one will be.
 */
vi.mock('@edition', () => ({
  edition: {
    name: 'community',
    signUp: null,
    plan: null,
    notice: null,
    closure: () => <p>Your plan will not renew.</p>,
    settingsSections: [],
    messages: {},
  },
}));

vi.mock('@/i18n/navigation', async (original) => ({
  ...(await original<typeof Navigation>()),
  useRouter: () => ({ replace: vi.fn() }),
}));

const SAID = 'Your plan will not renew.';

const OWNER = {
  id: '0195f7e0-0000-7000-8000-000000000001',
  emailAddress: 'ada@example.com',
  displayName: 'Ada Lovelace',
};

const CLOSURE: Closure = {
  name: 'Acme Inc.',
  closedAt: '2026-09-21T09:30:00+00:00',
  deletionDue: '2026-10-21T09:30:00+00:00',
  closedBy: 'Ada Lovelace',
  closedByYou: true,
  canRestore: true,
  hasOpenAccount: false,
};

describe('what the edition says about money', () => {
  /**
   * Before the confirmation rather than inside it, so that somebody paying for a plan reads what
   * closing does to it before they are asked to type anything.
   */
  it('is on the card that closes an account, before and during the confirmation', async () => {
    renderScreen(<CloseAccount name="Acme Inc." />, { signedInAs: OWNER });

    expect(screen.getByText(SAID)).toBeInTheDocument();
    expect(screen.getByText(SAID).compareDocumentPosition(screen.getByRole('button'))).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Close account' }));

    expect(screen.getByText(SAID)).toBeInTheDocument();
  });

  it('is on the closed screen for an owner with nowhere else to be', () => {
    renderScreen(<ClosedAccount />, { signedInAs: OWNER, closure: CLOSURE });

    expect(screen.getByText(SAID)).toBeInTheDocument();
  });

  /** What happens to the account's arrangement is not something a member can act on. */
  it('is kept from somebody who cannot bring the account back', () => {
    renderScreen(<ClosedAccount />, {
      signedInAs: OWNER,
      closure: { ...CLOSURE, canRestore: false },
    });

    expect(screen.queryByText(SAID)).not.toBeInTheDocument();
  });

  /** The edition describes the account somebody is in, and for them that is the open one. */
  it('is kept from somebody who also has an open account', () => {
    renderScreen(<ClosedAccount />, {
      signedInAs: OWNER,
      closure: { ...CLOSURE, hasOpenAccount: true },
    });

    expect(screen.queryByText(SAID)).not.toBeInTheDocument();
  });
});
