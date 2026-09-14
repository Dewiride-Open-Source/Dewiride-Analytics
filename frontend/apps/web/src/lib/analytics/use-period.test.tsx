import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { type OnUrlUpdateFunction, withNuqsTestingAdapter } from 'nuqs/adapters/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { type Period, writePeriod } from '@/lib/analytics/period';
import { usePeriod } from '@/lib/analytics/use-period';

const WEEK: Period = { kind: 'preset', preset: 'last-7-days' };
const YESTERDAY: Period = { kind: 'preset', preset: 'yesterday' };

interface ProbeProps {
  readonly pick: Period;
  /** Whether this stands for a screen the period is the subject of, which seeds its own address. */
  readonly seeding: boolean;
}

/** Prints what the address resolved to, and writes a period back into it when pressed. */
function Probe({ pick, seeding }: ProbeProps) {
  const { period, choose } = usePeriod({ seeding });

  return (
    <button type="button" onClick={() => choose(pick)}>
      {writePeriod(period)}
    </button>
  );
}

interface Arriving {
  readonly at?: string;
  readonly picking?: Period;
  readonly seeding?: boolean;
  readonly watching?: OnUrlUpdateFunction;
}

function arrive({ at, picking = YESTERDAY, seeding = false, watching }: Arriving = {}) {
  const wrapper = withNuqsTestingAdapter({
    searchParams: at,
    onUrlUpdate: watching,
    // The address remembers what is written to it, the way the browser's own does.
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

/** What the screen currently believes it is looking at. */
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

describe('the period a screen is on', () => {
  it('is the one everything opens on when the address says nothing', () => {
    arrive();

    expect(showing()).toBe('last-7-days');
  });

  it('is the one the address names, so a link opens on what its sender was looking at', () => {
    arrive({ at: '?period=yesterday' });

    expect(showing()).toBe('yesterday');
  });

  it('is the stretch the address names when somebody chose their own days', () => {
    arrive({ at: '?period=2026-08-01..2026-08-14' });

    expect(showing()).toBe('2026-08-01..2026-08-14');
  });

  /**
   * An address is typed, edited and forwarded by people, so most of what can arrive in one is not
   * a period at all. Every one of those has to leave somebody on a working screen rather than on a
   * refusal, because there is nothing they could do about it from there.
   */
  it('falls back to that same period rather than refusing to draw', () => {
    arrive({ at: '?period=whenever' });

    expect(showing()).toBe('last-7-days');
  });

  it('is written into the address as soon as it is chosen', async () => {
    const written = vi.fn();

    arrive({ watching: written });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(written).toHaveBeenCalled());
    expect(written.mock.calls[0]?.[0].queryString).toContain('period=yesterday');
    expect(showing()).toBe('yesterday');
  });

  /** An address that says what it would have said anyway is one more thing in the bar for nothing. */
  it('leaves the address alone again when it goes back to the one everything opens on', async () => {
    const written = vi.fn();

    arrive({ at: '?period=yesterday', picking: WEEK, watching: written });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(written).toHaveBeenCalled());
    expect(written.mock.calls[0]?.[0].queryString).not.toContain('period');
  });

  /**
   * The way back from a period somebody landed on by mistake should be the button they already
   * reach for, rather than working out what they were on before and picking it again.
   */
  it('leaves an entry in the history, so the browser’s own way back undoes it', async () => {
    const written = vi.fn();

    arrive({ watching: written });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(written).toHaveBeenCalled());
    expect(written.mock.calls[0]?.[0].options.history).toBe('push');
  });

  /**
   * The stretch of days somebody reads their website over is a habit rather than a decision made
   * afresh each morning.
   */
  it('is remembered once chosen, so the next screen opens on it', async () => {
    arrive();

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(showing()).toBe('yesterday'));
    expect(window.localStorage.getItem('dewiride.period')).toBe('yesterday');
  });

  /**
   * The link in the bar should still say what is on the screen, and nothing anybody pressed put
   * the period there, so it is written in over the entry somebody is already on.
   */
  it('opens on the period last chosen when the address says nothing, and says so in the address quietly', async () => {
    const written = vi.fn();

    window.localStorage.setItem('dewiride.period', 'yesterday');

    arrive({ seeding: true, watching: written });

    expect(showing()).toBe('yesterday');

    await waitFor(() => expect(written).toHaveBeenCalled());
    expect(written.mock.calls[0]?.[0].queryString).toContain('period=yesterday');
    expect(written.mock.calls[0]?.[0].options.history).toBe('replace');
  });

  /** A link names what its sender was looking at, and the reader's own habit gives way to it. */
  it('lets a link name a period over the one remembered', async () => {
    const written = vi.fn();

    window.localStorage.setItem('dewiride.period', 'yesterday');

    arrive({ at: '?period=today', seeding: true, watching: written });

    await anyWriting();

    expect(showing()).toBe('today');
    expect(written).not.toHaveBeenCalled();
  });

  /**
   * The bar across the top reads the period on every screen, including the ones with no stretch
   * of days to ask about, and must not write one into their address.
   */
  it('reads the remembered period without writing it anywhere when nobody asked it to', async () => {
    const written = vi.fn();

    window.localStorage.setItem('dewiride.period', 'yesterday');

    arrive({ watching: written });

    await anyWriting();

    expect(showing()).toBe('yesterday');
    expect(written).not.toHaveBeenCalled();
  });
});
