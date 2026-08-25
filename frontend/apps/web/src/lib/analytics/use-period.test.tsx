import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { type OnUrlUpdateFunction, withNuqsTestingAdapter } from 'nuqs/adapters/testing';
import { describe, expect, it, vi } from 'vitest';
import { type Period, writePeriod } from '@/lib/analytics/period';
import { usePeriod } from '@/lib/analytics/use-period';

const WEEK: Period = { kind: 'preset', preset: 'last-7-days' };
const YESTERDAY: Period = { kind: 'preset', preset: 'yesterday' };

interface ProbeProps {
  readonly pick: Period;
}

/** Prints what the address resolved to, and writes a period back into it when pressed. */
function Probe({ pick }: ProbeProps) {
  const { period, choose } = usePeriod();

  return (
    <button type="button" onClick={() => choose(pick)}>
      {writePeriod(period)}
    </button>
  );
}

interface Arriving {
  readonly at?: string;
  readonly picking?: Period;
  readonly watching?: OnUrlUpdateFunction;
}

function arrive({ at, picking = YESTERDAY, watching }: Arriving = {}) {
  return render(<Probe pick={picking} />, {
    wrapper: withNuqsTestingAdapter({
      searchParams: at,
      onUrlUpdate: watching,
      // The address remembers what is written to it, the way the browser's own does.
      hasMemory: true,
    }),
  });
}

/** What the screen currently believes it is looking at. */
function showing() {
  return screen.getByRole('button').textContent;
}

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
});
