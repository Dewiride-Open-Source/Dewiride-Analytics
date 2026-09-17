import { fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PeriodPicker } from '@/components/dashboard/period-picker';
import { DEFAULT_PERIOD, type Period, PRESETS } from '@/lib/analytics/period';
import { renderScreen } from '@/test/harness';

/** Mid-afternoon in Kolkata, and nowhere near a day boundary. */
const NOW = new Date('2026-08-18T09:37:12Z');

const ZONE = 'Asia/Kolkata';

/**
 * Only the clock is stood still, not the timers beneath it. Everything here works out the days a
 * period covers from the moment it is asked, and a test that let that moment move would pass or
 * fail depending on when it ran — while pressing things needs real timers to keep working.
 */
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(NOW);
});

afterEach(() => {
  vi.useRealTimers();
});

function show(value: Period = DEFAULT_PERIOD) {
  const chose = vi.fn();

  renderScreen(<PeriodPicker value={value} onChange={chose} timeZoneId={ZONE} />);

  return { chose, list: screen.getByRole('combobox', { name: 'Period' }) };
}

/**
 * A date box is filled by a picker rather than by keystrokes, and setting it outright is what that
 * actually does. Typed a character at a time it is never a date until the last one, and the
 * browser throws away every value along the way.
 */
function fill(box: HTMLElement, day: string) {
  fireEvent.change(box, { target: { value: day } });
}

describe('choosing how far back to look', () => {
  /**
   * Also the guard on the groups below it: every named period has to appear exactly once, so one
   * added to the list and forgotten under a heading fails here rather than going unoffered.
   */
  it('offers every named period, and nothing that is not one', () => {
    show();

    const offered = screen
      .getAllByRole('option')
      .map((option) => (option as HTMLOptionElement).value);

    expect(offered).toStrictEqual([...PRESETS, 'choose']);
  });

  it('cannot confuse its own two entries with a named period', () => {
    expect(PRESETS as readonly string[]).not.toContain('choose');
    expect(PRESETS as readonly string[]).not.toContain('chosen');
  });

  it('files them under headings rather than as one list of eight', () => {
    show();

    const groups = screen.getByRole('combobox').querySelectorAll('optgroup');

    expect([...groups].map((group) => group.label)).toStrictEqual([
      'Recent',
      'Calendar',
      'Your own',
    ]);
  });

  it('shows the one being looked at', () => {
    const { list } = show({ kind: 'preset', preset: 'last-30-days' });

    expect(list).toHaveValue('last-30-days');
    expect(screen.getByRole('option', { name: 'Last 30 days' })).toBeInTheDocument();
  });

  it('reports the period that was picked', async () => {
    const { chose, list } = show();

    await userEvent.selectOptions(list, 'yesterday');

    expect(chose).toHaveBeenCalledWith({ kind: 'preset', preset: 'yesterday' });
  });

  /**
   * The one thing a list of names cannot show, and the difference between reading "Last 30 days"
   * and knowing whether today is in it.
   */
  it('prints the days the choice works out to', () => {
    show();

    expect(screen.getByText('Aug 12 – 18, 2026')).toBeInTheDocument();
  });

  it('counts those days where the site is, not where the reader is', () => {
    show({ kind: 'preset', preset: 'today' });

    expect(screen.getByText('Aug 18, 2026')).toBeInTheDocument();
  });

  /** The dates are the answer to "which days", and a screen reader hears them with the control. */
  it('reads those days out with the control', () => {
    show();

    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveAccessibleDescription(
      /^Aug 12\s–\s18, 2026$/,
    );
  });
});

describe('choosing exact dates', () => {
  async function open() {
    const shown = show();

    await userEvent.selectOptions(shown.list, 'choose');

    return { ...shown, panel: await screen.findByRole('dialog') };
  }

  it('opens on the days already being looked at rather than empty', async () => {
    const { panel } = await open();

    expect(within(panel).getByLabelText('First day')).toHaveValue('2026-08-12');
    expect(within(panel).getByLabelText('Last day')).toHaveValue('2026-08-18');
  });

  it('says how long the stretch is as it is chosen', async () => {
    const { panel } = await open();

    fill(within(panel).getByLabelText('First day'), '2026-08-18');

    expect(within(panel).getByText('1 day')).toBeInTheDocument();

    fill(within(panel).getByLabelText('First day'), '2026-08-16');

    expect(within(panel).getByText('3 days')).toBeInTheDocument();
  });

  it('reports the stretch that was chosen', async () => {
    const { chose, panel } = await open();

    fill(within(panel).getByLabelText('First day'), '2026-08-01');
    await userEvent.click(within(panel).getByRole('button', { name: 'Show this period' }));

    expect(chose).toHaveBeenCalledWith({
      kind: 'chosen',
      first: '2026-08-01',
      last: '2026-08-18',
    });
  });

  it('refuses a stretch that ends before it starts, and says which way round it is', async () => {
    const { panel } = await open();

    fill(within(panel).getByLabelText('Last day'), '2026-08-01');

    expect(within(panel).getByRole('alert')).toHaveTextContent(
      'The last day comes before the first one.',
    );
    expect(within(panel).getByRole('button', { name: 'Show this period' })).toBeDisabled();
  });

  it('refuses a stretch longer than a year', async () => {
    const { panel } = await open();

    fill(within(panel).getByLabelText('First day'), '2024-08-18');

    expect(within(panel).getByRole('alert')).toHaveTextContent('Pick a stretch of a year or less.');
  });

  /**
   * A half-filled pair of boxes is somebody part-way through rather than somebody who got it
   * wrong, and a form that starts refusing before it has been answered is a form that shouts.
   */
  it('says nothing at all while a box is still empty', async () => {
    const { panel } = await open();

    fill(within(panel).getByLabelText('Last day'), '');

    expect(within(panel).queryByRole('alert')).not.toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Show this period' })).toBeDisabled();
  });

  it('offers no day that has not happened yet', async () => {
    const { panel } = await open();

    expect(within(panel).getByLabelText('First day')).toHaveAttribute('max', '2026-08-18');
    expect(within(panel).getByLabelText('Last day')).toHaveAttribute('max', '2026-08-18');
  });
});

/**
 * A list gives nothing back when the entry already showing is picked again. A single entry that
 * was both the chosen stretch and the way into the chooser would therefore be one nobody could
 * reopen to correct a date.
 */
describe('a stretch already chosen', () => {
  const CHOSEN: Period = { kind: 'chosen', first: '2026-07-01', last: '2026-07-07' };

  it('sits in the list beside the way back into the chooser', async () => {
    const { list } = show(CHOSEN);

    expect(list).toHaveValue('chosen');
    expect(screen.getByRole('option', { name: 'Your dates' })).toBeInTheDocument();

    await userEvent.selectOptions(list, 'choose');

    expect(await screen.findByRole('dialog')).toBeInTheDocument();
  });

  it('prints the days it covers', () => {
    show(CHOSEN);

    expect(screen.getByText('Jul 1 – 7, 2026')).toBeInTheDocument();
  });

  it('opens the chooser on those days', async () => {
    const { list } = show(CHOSEN);

    await userEvent.selectOptions(list, 'choose');

    const panel = await screen.findByRole('dialog');

    expect(within(panel).getByLabelText('First day')).toHaveValue('2026-07-01');
    expect(within(panel).getByLabelText('Last day')).toHaveValue('2026-07-07');
  });
});
