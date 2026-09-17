import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { type OnUrlUpdateFunction, withNuqsTestingAdapter } from 'nuqs/adapters/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { usePeopleOnly } from '@/lib/analytics/use-people-only';

interface ProbeProps {
  readonly pick: boolean;
  readonly seeding: boolean;
}

/** Prints whether only the people are counted, and asks for the other answer when pressed. */
function Probe({ pick, seeding }: ProbeProps) {
  const { peopleOnly, population, showOnlyPeople } = usePeopleOnly({ seeding });

  return (
    <button type="button" onClick={() => showOnlyPeople(pick)}>
      {peopleOnly ? 'on' : 'off'} {population}
    </button>
  );
}

interface Arriving {
  readonly at?: string;
  readonly picking?: boolean;
  /** Whether the screen is one the population is the subject of. */
  readonly seeding?: boolean;
  readonly watching?: OnUrlUpdateFunction;
}

function arrive({ at, picking = true, seeding = false, watching }: Arriving = {}) {
  const wrapper = withNuqsTestingAdapter({
    searchParams: at,
    onUrlUpdate: watching,
    hasMemory: true,
  });

  // The stand-in address renders once more the moment it is mounted, to take up what it was given,
  // and throws away whatever is waiting to be written every time it renders. A screen mounted in
  // that same moment would have what it seeds thrown away with it — which the browser's own
  // address never does — so the screen arrives one step after the address.
  const shown = render(<></>, { wrapper });

  shown.rerender(<Probe pick={picking} seeding={seeding} />);

  return shown;
}

/** Lets whatever the screen was going to write into the address go through, if anything was. */
async function settled() {
  await act(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
}

/** Whether the screen currently believes it is counting the people alone. */
function drawing() {
  return screen.getByRole('button').textContent?.split(' ')[0];
}

/** The word every question is asked with. */
function population() {
  return screen.getByRole('button').textContent?.split(' ')[1];
}

// The browser's storage outlives a test, so each one starts with nothing remembered.
beforeEach(() => {
  window.localStorage.clear();
});

describe('whether the screen is kept to people', () => {
  it('counts everybody when the address says nothing', () => {
    arrive();

    expect(drawing()).toBe('off');
    expect(population()).toBe('everybody');
  });

  it('counts people alone for a link sent that way', () => {
    arrive({ at: '?only=people' });

    expect(drawing()).toBe('on');
    expect(population()).toBe('people');
  });

  /** An address is written by whoever sent the link, so nothing in it is taken on trust. */
  it('counts everybody when a link asks for a population this product has never had', () => {
    arrive({ at: '?only=machines' });

    expect(drawing()).toBe('off');
  });

  /**
   * A word naming no population is an address that says nothing, and an address that says nothing
   * is given what the browser remembers — written in over the junk, on the entry already open.
   */
  it('treats a population this product has never had as an address that says nothing', async () => {
    const watching = vi.fn();

    window.localStorage.setItem('dewiride.population', 'people');

    arrive({ at: '?only=machines', seeding: true, watching });

    expect(drawing()).toBe('on');

    await waitFor(() => expect(watching).toHaveBeenCalled());
    expect(watching.mock.calls[0]?.[0].queryString).toContain('only=people');
    expect(watching.mock.calls[0]?.[0].queryString).not.toContain('machines');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('replace');
  });

  /**
   * Keeping the picture to people is a decision, and the way out of it should be the button
   * somebody already has.
   */
  it('travels in the address as a new entry in the history', async () => {
    const watching = vi.fn();

    arrive({ watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(drawing()).toBe('on'));
    expect(watching.mock.calls[0]?.[0].queryString).toContain('only=people');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('push');
  });

  it('leaves the address alone again once everybody is back', async () => {
    const watching = vi.fn();

    arrive({ at: '?only=people', picking: false, watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(drawing()).toBe('off'));
    expect(watching.mock.calls[0]?.[0].queryString).not.toContain('only');
  });

  it('is remembered', async () => {
    arrive();

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(drawing()).toBe('on'));
    expect(window.localStorage.getItem('dewiride.population')).toBe('people');
  });

  /**
   * The link in the bar should still say what is on the screen, and nothing anybody pressed put
   * the choice there, so it is written in over the entry somebody is already on.
   */
  it('opens on people alone when that was remembered, and says so in the address quietly', async () => {
    const watching = vi.fn();

    window.localStorage.setItem('dewiride.population', 'people');

    arrive({ seeding: true, watching });

    expect(drawing()).toBe('on');

    await waitFor(() => expect(watching).toHaveBeenCalled());
    expect(watching.mock.calls[0]?.[0].queryString).toContain('only=people');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('replace');
  });

  /**
   * The bar across the top reads the population on every screen so its links can carry it, and
   * must not write it into the address of a screen it means nothing on.
   */
  it('reads the remembered population without writing it anywhere when nobody asked it to', async () => {
    const watching = vi.fn();

    window.localStorage.setItem('dewiride.population', 'people');

    arrive({ watching });

    expect(drawing()).toBe('on');

    await settled();
    expect(watching).not.toHaveBeenCalled();
  });
});
