import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DeadEnd, OutcomeCard } from '@/components/ui/outcome-card';
import { renderScreen } from '@/test/harness';

/** The mark drawn above the title, which is kept out of what is read aloud. */
function mark(container: HTMLElement): Element | null {
  return container.querySelector('header span[aria-hidden]');
}

describe('the one card a screen is built around', () => {
  it('leads with its title as the heading of the screen, and what it is about beneath', () => {
    renderScreen(
      <OutcomeCard title="Choose a new password" subtitle="For ada@example.com.">
        <p>The form.</p>
      </OutcomeCard>,
    );

    expect(
      screen.getByRole('heading', { level: 1, name: 'Choose a new password' }),
    ).toBeInTheDocument();
    expect(screen.getByText('For ada@example.com.')).toBeInTheDocument();
    expect(screen.getByText('The form.')).toBeInTheDocument();
  });

  it('draws no mark above a card somebody is filling in', () => {
    const { container } = renderScreen(
      <OutcomeCard title="Choose a new password">
        <p>The form.</p>
      </OutcomeCard>,
    );

    expect(mark(container)).toBeNull();
  });

  /**
   * The colour is the meaning: red is for something that went wrong, and the accent is for
   * something put away on purpose. A closed account drawn in red would read as a failure.
   */
  it.each([
    { about: 'a problem', tone: 'problem', colour: 'text-danger' },
    { about: 'something kept', tone: 'kept', colour: 'text-accent-strong' },
  ] as const)('draws a mark above a card about $about, in its own colour', ({ tone, colour }) => {
    const { container } = renderScreen(
      <OutcomeCard title="Acme Inc. is closed" tone={tone}>
        <p>What happens next.</p>
      </OutcomeCard>,
    );

    expect(mark(container)).toHaveClass(colour);
  });
});

describe('a link that leads nowhere', () => {
  it('says what is wrong and offers the one way out as a link', () => {
    renderScreen(
      <DeadEnd
        title="This link is incomplete"
        body="Ask whoever sent it for a new one."
        action="Go to sign in"
        href="/app/sign-in"
      />,
    );

    expect(
      screen.getByRole('heading', { level: 1, name: 'This link is incomplete' }),
    ).toBeInTheDocument();
    expect(screen.getByText('Ask whoever sent it for a new one.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go to sign in' })).toHaveAttribute(
      'href',
      '/app/sign-in',
    );
  });

  it('is marked as a problem', () => {
    const { container } = renderScreen(
      <DeadEnd
        title="This link is incomplete"
        body="Ask whoever sent it for a new one."
        action="Go to sign in"
        href="/app/sign-in"
      />,
    );

    expect(mark(container)).toHaveClass('text-danger');
  });
});
