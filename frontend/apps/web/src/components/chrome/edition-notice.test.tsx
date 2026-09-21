import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { EditionNotice } from '@/components/chrome/edition-notice';
import type { ClosedAccount } from '@/lib/api/schemas';
import { renderScreen } from '@/test/harness';

/**
 * The open-source edition has nothing to say above the screens, so against the real seam this
 * would render nothing in every case and prove nothing about the rule. The rule — that whatever
 * an edition says is withheld from somebody whose only account is closed — exists for the edition
 * that does have something to say, so one that does stands in here.
 */
vi.mock('@edition', () => ({
  edition: {
    name: 'community',
    signUp: null,
    plan: null,
    notice: () => <p>Something the edition says</p>,
    closure: null,
    settingsSections: [],
    messages: {},
  },
}));

const OWNER = {
  id: '0195f7e0-0000-7000-8000-000000000001',
  emailAddress: 'ada@example.com',
  displayName: 'Ada Lovelace',
};

const CLOSED: ClosedAccount = {
  name: 'Acme Inc.',
  closedAt: '2026-09-21T09:30:00+00:00',
  deletionDue: '2026-10-21T09:30:00+00:00',
  closedBy: null,
  closedByYou: true,
  canRestore: true,
  hasOpenAccount: false,
};

function show(closure: ClosedAccount | null) {
  return renderScreen(<EditionNotice />, { signedInAs: OWNER, closure });
}

describe('what the edition says above every screen', () => {
  it('is shown to somebody whose account is open', () => {
    show(null);

    expect(screen.getByText('Something the edition says')).toBeInTheDocument();
  });

  /**
   * Whatever it says is about an account they cannot open, and the one screen they can still see
   * already says everything there is to say about it.
   */
  it('is withheld from somebody whose only account is closed', () => {
    show(CLOSED);

    expect(screen.queryByText('Something the edition says')).not.toBeInTheDocument();
  });

  it('is still shown to somebody who has an open account beside the closed one', () => {
    show({ ...CLOSED, hasOpenAccount: true });

    expect(screen.getByText('Something the edition says')).toBeInTheDocument();
  });
});
