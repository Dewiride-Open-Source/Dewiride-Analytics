import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { parseAsStringLiteral } from 'nuqs';
import { NuqsTestingAdapter, type OnUrlUpdateFunction } from 'nuqs/adapters/testing';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { oneOf, remembered } from '@/lib/preferences/remembered';
import { useRememberedAddress } from '@/lib/preferences/use-remembered-address';

const WORDS = ['a', 'b'] as const;

type Word = (typeof WORDS)[number];

const WRITTEN = parseAsStringLiteral(WORDS).withOptions({ history: 'push' });
const REMEMBERED = remembered<Word>('test.pick', oneOf(WORDS), 'a');

interface ProbeProps {
  readonly pick: Word;
  readonly seeding: boolean;
}

/** Prints what the choice resolved to, and makes a given one when pressed. */
function Probe({ pick, seeding }: ProbeProps) {
  const [value, choose] = useRememberedAddress('pick', WRITTEN, REMEMBERED, seeding);

  return (
    <button type="button" onClick={() => choose(pick)}>
      {value}
    </button>
  );
}

interface Arriving {
  readonly at?: string;
  readonly picking?: Word;
  readonly seeding?: boolean;
  readonly watching?: OnUrlUpdateFunction;
}

/**
 * The silent address, spelt so that the stand-in address takes it up as a change.
 *
 * The stand-in only takes up a new address when its spelling differs from the one before, and a
 * screen is opened on the silent address spelt as nothing at all. A question mark alone is the
 * same silent address spelt differently.
 */
const SILENT_AGAIN = '?';

interface Arrived {
  /** Stands for the browser's own way back to the silent address the screen was opened on. */
  readonly back: () => void;
}

function arrive({ at, picking = 'b', seeding = true, watching }: Arriving = {}): Arrived {
  const address = (searchParams: string | undefined, children: ReactNode) => (
    // The address remembers what is written to it, the way the browser's own does.
    <NuqsTestingAdapter searchParams={searchParams} onUrlUpdate={watching} hasMemory>
      {children}
    </NuqsTestingAdapter>
  );
  const probe = <Probe pick={picking} seeding={seeding} />;

  // The stand-in address renders once more the moment it is mounted, to take up what it was given,
  // and throws away whatever is waiting to be written every time it renders. A screen mounted in
  // that same moment would have what it seeds thrown away with it — which the browser's own
  // address never does — so the screen arrives one step after the address.
  const shown = render(address(at, null));

  shown.rerender(address(at, probe));

  return { back: () => shown.rerender(address(SILENT_AGAIN, probe)) };
}

/** What the screen currently believes was chosen. */
function showing() {
  return screen.getByRole('button').textContent;
}

/**
 * Waits out the moment the address takes to write anything it was handed, so that its silence
 * afterwards means it was handed nothing. A write is gathered up and made a tick later rather
 * than at once.
 */
async function anyWriting(): Promise<void> {
  await act(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
}

// The browser's storage outlives a test, so each one starts with nothing remembered.
beforeEach(() => {
  window.localStorage.clear();
});

describe('a choice carried in the address and remembered by the browser', () => {
  it('is the fallback when neither the address nor the browser says anything', () => {
    arrive();

    expect(showing()).toBe('a');
  });

  it('is whatever the address names', () => {
    arrive({ at: '?pick=b' });

    expect(showing()).toBe('b');
  });

  it('is what was remembered when the address says nothing', () => {
    window.localStorage.setItem('test.pick', 'b');

    arrive();

    expect(showing()).toBe('b');
  });

  /**
   * The link in the bar should still say what is on the screen, and nothing anybody pressed put
   * the value there, so it is written in over the entry somebody is already on.
   */
  it('writes what was remembered into the address without adding to the history', async () => {
    const watching = vi.fn();

    window.localStorage.setItem('test.pick', 'b');

    arrive({ watching });

    await waitFor(() => expect(watching).toHaveBeenCalled());
    expect(watching.mock.calls[0]?.[0].queryString).toContain('pick=b');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('replace');
  });

  /** A link names what its sender was looking at, and the reader's own habit gives way to it. */
  it('lets the address win over what was remembered', async () => {
    const watching = vi.fn();

    window.localStorage.setItem('test.pick', 'b');

    arrive({ at: '?pick=a', watching });

    await anyWriting();

    expect(showing()).toBe('a');
    expect(watching).not.toHaveBeenCalled();
  });

  it('writes a choice to both the address and the browser', async () => {
    const watching = vi.fn();

    arrive({ watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(showing()).toBe('b'));
    expect(watching.mock.calls[0]?.[0].queryString).toContain('pick=b');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('push');
    expect(window.localStorage.getItem('test.pick')).toBe('b');
  });

  /** The plain address of a screen stays plain, while the browser still learns the habit. */
  it('leaves the address alone for the fallback and still remembers it', async () => {
    const watching = vi.fn();

    arrive({ at: '?pick=b', picking: 'a', watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(showing()).toBe('a'));
    expect(watching.mock.calls[0]?.[0].queryString).not.toContain('pick');
    expect(window.localStorage.getItem('test.pick')).toBe('a');
  });

  /** A screen that merely reads the choice must not write it into its own address. */
  it('seeds nothing where it was not asked to', async () => {
    const watching = vi.fn();

    window.localStorage.setItem('test.pick', 'b');

    arrive({ seeding: false, watching });

    await anyWriting();

    expect(showing()).toBe('b');
    expect(watching).not.toHaveBeenCalled();
  });

  /**
   * A choice made on the plain address of the screen leaves that address behind it in the
   * history, and the button somebody reaches for to undo the choice lands on it. The screen
   * shows what that address meant before the choice, and the browser is told so, rather than
   * the choice being written back in over the very entry they went back to.
   */
  it('the way back from a choice lands on what was there before', async () => {
    const watching = vi.fn();
    const { back } = arrive({ watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(watching).toHaveBeenCalledTimes(1));

    back();

    await waitFor(() => expect(showing()).toBe('a'));
    await anyWriting();

    expect(watching).toHaveBeenCalledTimes(1);
    expect(watching.mock.calls[0]?.[0].options.history).toBe('push');
    expect(window.localStorage.getItem('test.pick')).toBe('a');
  });

  it('seeds what was remembered once, and not every time the address falls silent', async () => {
    const watching = vi.fn();

    window.localStorage.setItem('test.pick', 'b');

    const { back } = arrive({ watching });

    await waitFor(() => expect(watching).toHaveBeenCalledTimes(1));

    back();

    await waitFor(() => expect(showing()).toBe('a'));
    await anyWriting();

    expect(watching).toHaveBeenCalledTimes(1);
    expect(watching.mock.calls[0]?.[0].options.history).toBe('replace');
  });
});
