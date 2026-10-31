import { cleanup, fireEvent, screen, within } from '@testing-library/react';
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

function show(value: Period = DEFAULT_PERIOD, since?: string | null, nothingHeld?: boolean) {
  const chose = vi.fn();

  renderScreen(
    <PeriodPicker
      value={value}
      onChange={chose}
      timeZoneId={ZONE}
      since={since}
      nothingHeld={nothingHeld}
    />,
  );

  return { chose, list: screen.getByRole('combobox', { name: 'Period' }) };
}

/** All time, written the way a screen holds it. */
const ALL_TIME: Period = { kind: 'preset', preset: 'all-time' };

/**
 * A date box is filled by a picker rather than by keystrokes, and setting it outright is what that
 * actually does. Typed a character at a time it is never a date until the last one, and the
 * browser throws away every value along the way.
 */
function fill(box: HTMLElement, day: string) {
  fireEvent.change(box, { target: { value: day } });
}

/**
 * The control on its usual period, with the chooser opened from it — on a screen that reads only
 * the days still held, as the journeys screen does, unless the first day all time reaches back to
 * is given, as the overview gives it.
 */
async function open(since?: string | null) {
  const shown = show(DEFAULT_PERIOD, since);

  await userEvent.selectOptions(shown.list, 'choose');

  return { ...shown, panel: await screen.findByRole('dialog') };
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

  it('files them under headings rather than as one list of nine', () => {
    show();

    const groups = screen.getByRole('combobox').querySelectorAll('optgroup');

    expect([...groups].map((group) => group.label)).toStrictEqual([
      'Recent',
      'Calendar',
      'Your own',
    ]);
  });

  /** Every other period is part of all time, so it closes the calendar periods. */
  it('files all time with the calendar periods, after this year', () => {
    show();

    const calendar = screen.getByRole('group', { name: 'Calendar' });

    expect(
      within(calendar)
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toStrictEqual(['This month', 'Last month', 'This year', 'All time']);
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

  /**
   * All time is the website's whole record, so the day worth printing is its first: the first visit
   * where that is known, otherwise the first of its first month.
   */
  it('says since when all time runs', () => {
    show(ALL_TIME, '2025-03-14');

    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveAccessibleDescription(
      'Since Mar 14, 2025',
    );
  });

  it('says it runs since the first visit while that day is not known', () => {
    show(ALL_TIME, null);

    expect(screen.getByText('Since the first visit')).toBeInTheDocument();
  });

  /** A website that has never been measured has no first visit for all time to run from. */
  it('prints nothing under all time for a website never measured', () => {
    show(ALL_TIME, null, true);

    expect(screen.queryByText('Since the first visit')).not.toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Period' })).not.toHaveAccessibleDescription();
  });

  it('still prints the days of any other period for a website never measured', () => {
    show(DEFAULT_PERIOD, null, true);

    expect(screen.getByText('Aug 12 – 18, 2026')).toBeInTheDocument();
  });

  /**
   * A screen that reads only the days still held one by one, as the journeys screen does, is shown
   * the days it covers: every day still kept.
   */
  it('prints the days all time covers on a screen that reads only the days still held', () => {
    show(ALL_TIME);

    expect(screen.getByText('Aug 20, 2025 – Aug 18, 2026')).toBeInTheDocument();
  });
});

/**
 * A screen asks for what the next period will need once somebody reaches for this control, so it
 * hears when they do — and not when focus arrives on its own, as the overview sends it here after
 * a press on its picture.
 */
describe('somebody reaching for it', () => {
  function reached() {
    const reach = vi.fn();

    renderScreen(
      <PeriodPicker value={DEFAULT_PERIOD} onChange={vi.fn()} timeZoneId={ZONE} onReach={reach} />,
    );

    return { reach, list: screen.getByRole('combobox', { name: 'Period' }) };
  }

  it('says so when it is pressed', () => {
    const { reach, list } = reached();

    fireEvent.pointerDown(list);

    expect(reach).toHaveBeenCalled();
  });

  it('says so when it is worked from the keyboard', () => {
    const { reach, list } = reached();

    fireEvent.keyDown(list, { key: 'ArrowDown' });

    expect(reach).toHaveBeenCalled();
  });

  it('says so when the chooser is opened from it', () => {
    const { reach, list } = reached();

    fireEvent.change(list, { target: { value: 'choose' } });

    expect(reach).toHaveBeenCalled();
  });

  it('says so when it is opened from the keyboard', () => {
    for (const key of [
      { key: 'F4' },
      { key: 'ArrowDown', altKey: true },
      { key: 'ArrowUp', altKey: true },
      { key: 'Enter' },
      { key: ' ' },
    ]) {
      const { reach, list } = reached();

      fireEvent.keyDown(list, key);

      expect(reach).toHaveBeenCalled();

      cleanup();
    }
  });

  /** A letter typed into a closed list picks the period whose name begins with it. */
  it('says so when a letter is typed into it', () => {
    const { reach, list } = reached();

    fireEvent.keyDown(list, { key: 'a' });

    expect(reach).toHaveBeenCalled();
  });

  /**
   * A key that moves through the list on its own is mostly a browser shortcut with a modifier held:
   * back, forward, home or another tab. It is never counted then; where a browser does move the list
   * with it, the period it moves to asks for what it needs itself.
   */
  it('says nothing when a key that moves through it is pressed with a modifier', () => {
    const { reach, list } = reached();

    fireEvent.keyDown(list, { key: 'ArrowLeft', altKey: true });
    fireEvent.keyDown(list, { key: 'ArrowRight', altKey: true });
    fireEvent.keyDown(list, { key: 'Home', altKey: true });
    fireEvent.keyDown(list, { key: 'ArrowLeft', metaKey: true });
    fireEvent.keyDown(list, { key: 'ArrowRight', metaKey: true });
    fireEvent.keyDown(list, { key: 'PageUp', ctrlKey: true });
    fireEvent.keyDown(list, { key: 'PageDown', ctrlKey: true });

    expect(reach).not.toHaveBeenCalled();
  });

  /**
   * On one platform or another, a key that opens the list opens it with a modifier held: Shift or ⌘
   * with the arrows on a Mac, Shift with Space everywhere, Shift with Enter on Windows, Linux and
   * ChromeOS, Shift or ⌘ with F4 in Chrome.
   */
  it('says so when it is opened with a modifier held', () => {
    for (const key of [
      { key: ' ', shiftKey: true },
      { key: 'ArrowDown', metaKey: true },
      { key: 'ArrowUp', shiftKey: true },
      { key: 'Enter', shiftKey: true },
      { key: 'F4', shiftKey: true },
      { key: 'F4', metaKey: true },
    ]) {
      const { reach, list } = reached();

      fireEvent.keyDown(list, key);

      expect(reach).toHaveBeenCalled();

      cleanup();
    }
  });

  /** The browser's own shortcuts reach the control on their way, and work nothing in it. */
  it('says nothing when focus is sent from it to the browser', () => {
    const { reach, list } = reached();

    fireEvent.keyDown(list, { key: 'F6' });
    fireEvent.keyDown(list, { key: 'Control', ctrlKey: true });
    fireEvent.keyDown(list, { key: 'l', ctrlKey: true });
    fireEvent.keyDown(list, { key: 'f', ctrlKey: true });
    fireEvent.keyDown(list, { key: 'Alt', altKey: true });
    fireEvent.keyDown(list, { key: 'd', altKey: true });
    fireEvent.keyDown(list, { key: 'Escape' });

    expect(reach).not.toHaveBeenCalled();
  });

  it('says nothing when it is tabbed past', () => {
    const { reach, list } = reached();

    fireEvent.keyDown(list, { key: 'Tab' });
    fireEvent.keyDown(list, { key: 'Shift', shiftKey: true });
    fireEvent.keyDown(list, { key: 'Tab', shiftKey: true });

    expect(reach).not.toHaveBeenCalled();
  });

  it('says nothing when focus merely arrives on it', () => {
    const { reach, list } = reached();

    list.focus();

    expect(list).toHaveFocus();
    expect(reach).not.toHaveBeenCalled();
  });
});

describe('choosing exact dates', () => {
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

    fill(within(panel).getByLabelText('First day'), '2026-08-02');
    await userEvent.click(within(panel).getByRole('button', { name: 'Show this period' }));

    expect(chose).toHaveBeenCalledWith({
      kind: 'chosen',
      first: '2026-08-02',
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

  /**
   * Every stretch longer than a year also begins before the first day still kept. Told it was too
   * long, somebody shortening it to a year would be refused a second time; told how far back the
   * days go, they are given the first day that works.
   */
  it('answers a stretch longer than a year with the first day that would work', async () => {
    const { panel } = await open();

    fill(within(panel).getByLabelText('First day'), '2024-08-18');

    expect(within(panel).getByRole('alert')).toHaveTextContent(
      'Visits are kept for 12 months. Pick days on or after Aug 20, 2025.',
    );
  });

  /**
   * Reports are deleted a year after they arrive, so a stretch beginning before the first day still
   * kept would be drawn with nothing at its start. The refusal sits under the box that is at fault
   * and names the day that would be accepted.
   */
  it('refuses a first day older than what is kept, and says which day it can be', async () => {
    const { panel } = await open();
    const first = within(panel).getByLabelText('First day');

    fill(first, '2025-08-19');

    expect(within(panel).getByRole('alert')).toHaveTextContent(
      'Visits are kept for 12 months. Pick days on or after Aug 20, 2025.',
    );
    expect(first).toHaveAccessibleDescription(/^Visits are kept for 12 months/);
    expect(within(panel).getByRole('button', { name: 'Show this period' })).toBeDisabled();

    fill(first, '2025-08-20');

    expect(within(panel).queryByRole('alert')).not.toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Show this period' })).toBeEnabled();
  });

  /**
   * Both days older than what is kept are one mistake, and the sentence names the day both have to
   * move to: told about the first day alone, somebody moving only that one would turn the stretch
   * backwards and be refused a second time.
   */
  it('names the day both ends have to move to when the whole stretch is older than what is kept', async () => {
    const { panel } = await open();
    const first = within(panel).getByLabelText('First day');
    const last = within(panel).getByLabelText('Last day');

    fill(first, '2025-06-01');
    fill(last, '2025-06-30');

    expect(within(panel).getByRole('alert')).toHaveTextContent(
      'Visits are kept for 12 months. Pick days on or after Aug 20, 2025.',
    );

    fill(first, '2025-08-20');
    fill(last, '2025-08-20');

    expect(within(panel).queryByRole('alert')).not.toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Show this period' })).toBeEnabled();
  });

  /**
   * On the overview all time reaches back to the website's first month, from what is kept of each
   * month, so somebody reaching for older days is offered it. A screen that reads only the days
   * still held has no further back to offer.
   */
  it('offers all time for older days where all time reaches back further', async () => {
    const { panel } = await open('2025-03-14');

    fill(within(panel).getByLabelText('First day'), '2025-08-19');

    expect(within(panel).getByRole('alert')).toHaveTextContent(
      'Visits are kept for 12 months. Pick days on or after Aug 20, 2025, or choose All time.',
    );
  });

  /** A website first measured inside the days still kept has nothing older for all time to show. */
  it('offers no all time for older days where all time begins inside the days still kept', async () => {
    const { panel } = await open('2025-11-02');

    fill(within(panel).getByLabelText('First day'), '2025-06-01');

    expect(within(panel).getByRole('alert')).toHaveTextContent(
      'Visits are kept for 12 months. Pick days on or after Aug 20, 2025.',
    );
  });

  it('offers no all time for older days where all time begins on the first day still kept', async () => {
    const { panel } = await open('2025-08-20');

    fill(within(panel).getByLabelText('First day'), '2025-08-19');

    expect(within(panel).getByRole('alert')).toHaveTextContent(
      'Visits are kept for 12 months. Pick days on or after Aug 20, 2025.',
    );
  });

  /** Not known yet may be never measured, and all time would then have nothing at all to show. */
  it('offers no all time for older days while the first day it reaches back to is not known', async () => {
    const { panel } = await open(null);

    fill(within(panel).getByLabelText('First day'), '2025-06-01');

    expect(within(panel).getByRole('alert')).toHaveTextContent(
      'Visits are kept for 12 months. Pick days on or after Aug 20, 2025.',
    );
  });

  it('offers no day older than what is kept', async () => {
    const { panel } = await open();

    expect(within(panel).getByLabelText('First day')).toHaveAttribute('min', '2025-08-20');
    expect(within(panel).getByLabelText('Last day')).toHaveAttribute('min', '2025-08-20');
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

  /**
   * On the last day of the year its first of January is already partly gone, so this year is read
   * from the second — and the chooser opens on the days the screen is showing, with nothing in
   * them to refuse.
   */
  it('opens on this year without a refusal on the last day of the year', async () => {
    vi.setSystemTime(new Date('2026-12-31T12:00:00Z'));

    const { list } = show({ kind: 'preset', preset: 'this-year' });

    expect(screen.getByText('Jan 2 – Dec 31, 2026')).toBeInTheDocument();

    await userEvent.selectOptions(list, 'choose');

    const panel = await screen.findByRole('dialog');

    expect(within(panel).getByLabelText('First day')).toHaveValue('2026-01-02');
    expect(within(panel).getByLabelText('Last day')).toHaveValue('2026-12-31');
    expect(within(panel).queryByRole('alert')).not.toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Show this period' })).toBeEnabled();
  });
});

/**
 * A day typed into both boxes that the list already has a name for is the period of that name, so
 * the chooser and a day pressed on the picture never call the same day two different things.
 */
describe('a single day chosen that already has a name', () => {
  /** One day typed into both boxes and asked for, as somebody after a single day would. */
  async function chooseDay(day: string) {
    const { chose, panel } = await open();

    fill(within(panel).getByLabelText('First day'), day);
    fill(within(panel).getByLabelText('Last day'), day);
    await userEvent.click(within(panel).getByRole('button', { name: 'Show this period' }));

    return chose;
  }

  it('comes back as Today when it is today where the site is', async () => {
    const chose = await chooseDay('2026-08-18');

    expect(chose).toHaveBeenCalledWith({ kind: 'preset', preset: 'today' });
  });

  it('comes back as Yesterday when it is the day before', async () => {
    const chose = await chooseDay('2026-08-17');

    expect(chose).toHaveBeenCalledWith({ kind: 'preset', preset: 'yesterday' });
  });

  it('stays the dates somebody chose on any other day', async () => {
    const chose = await chooseDay('2026-08-16');

    expect(chose).toHaveBeenCalledWith({ kind: 'chosen', first: '2026-08-16', last: '2026-08-16' });
  });

  /**
   * Half an hour past midnight in Kolkata it is still the evening before where these tests run, so
   * the eighteenth is yesterday for the website and today for the reader. The website's calendar
   * is the one that names it.
   */
  it('is named by the site’s calendar rather than the reader’s', async () => {
    vi.setSystemTime(new Date('2026-08-18T19:00:00Z'));

    const chose = await chooseDay('2026-08-18');

    expect(chose).toHaveBeenCalledWith({ kind: 'preset', preset: 'yesterday' });
  });
});

/**
 * A whole month typed into the chooser is the month the list already has a name for, exactly as a
 * month pressed on the picture is — so the two ways of asking for it arrive under one name.
 */
describe('a stretch chosen that already has a name', () => {
  /** A stretch typed into both boxes and asked for. */
  async function chooseStretch(first: string, last: string) {
    const { chose, panel } = await open();

    fill(within(panel).getByLabelText('First day'), first);
    fill(within(panel).getByLabelText('Last day'), last);
    await userEvent.click(within(panel).getByRole('button', { name: 'Show this period' }));

    return chose;
  }

  it('comes back as Last month when the whole of it is typed', async () => {
    const chose = await chooseStretch('2026-07-01', '2026-07-31');

    expect(chose).toHaveBeenCalledWith({ kind: 'preset', preset: 'last-month' });
  });

  it('comes back as This month when its first day to today is typed', async () => {
    const chose = await chooseStretch('2026-08-01', '2026-08-18');

    expect(chose).toHaveBeenCalledWith({ kind: 'preset', preset: 'this-month' });
  });

  it('stays the dates somebody chose when they are only part of a month', async () => {
    const chose = await chooseStretch('2026-07-02', '2026-07-31');

    expect(chose).toHaveBeenCalledWith({ kind: 'chosen', first: '2026-07-02', last: '2026-07-31' });
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
