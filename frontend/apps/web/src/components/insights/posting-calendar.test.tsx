import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { PostingCalendar } from '@/components/insights/posting-calendar';
import { renderScreen } from '@/test/harness';

/** Twelve months to a Saturday, with a busy Tuesday this week and a few days before it. */
const CALENDAR = {
  from: '2025-12-01',
  to: '2026-11-07',
  days: [
    { day: '2025-12-01', posts: 1 },
    { day: '2026-03-31', posts: 2 },
    { day: '2026-10-30', posts: 3 },
    { day: '2026-11-03', posts: 7 },
  ],
};

const TODAY = '1 post on Saturday, November 7, 2026';

/** The square of the day a sentence names, as a screen reader reaches it. */
function square(name: string): HTMLElement {
  return screen.getByRole('gridcell', { name });
}

/** Whatever is named by the one tip on the calendar, or nothing where none is shown. */
function tipped(): string | null {
  return document.querySelector('.glow-modal')?.textContent ?? null;
}

function show(calendar = CALENDAR) {
  return renderScreen(<PostingCalendar calendar={calendar} leftOut={null} />);
}

/** Takes the keyboard into the calendar, where it lands on the day it stops at. */
async function enter(): Promise<ReturnType<typeof userEvent.setup>> {
  const acting = userEvent.setup();

  await acting.tab();

  return acting;
}

describe('a posting calendar', () => {
  it('lays the last twelve months out a block to a month, newest last', () => {
    show();

    expect(screen.getAllByRole('grid').map((grid) => grid.getAttribute('aria-label'))).toEqual([
      'December 2025',
      'January 2026',
      'February 2026',
      'March 2026',
      'April 2026',
      'May 2026',
      'June 2026',
      'July 2026',
      'August 2026',
      'September 2026',
      'October 2026',
      'November 2026',
    ]);
  });

  it('says how many posts the calendar holds, on how many days', () => {
    show();

    expect(screen.getByText('13 posts on 4 days in the last 12 months.')).toBeInTheDocument();
  });

  it('says so where no post was published in the last twelve months', () => {
    show({ ...CALENDAR, days: [] });

    expect(screen.getByText('No posts in the last 12 months.')).toBeInTheDocument();
    expect(screen.queryByText('Show posting days as a list')).not.toBeInTheDocument();
  });

  it('shades each day by how many posts it saw, and gives five or more the darkest', () => {
    show();

    expect(square('No posts on Friday, November 6, 2026')).toHaveClass('bg-heat-0');
    expect(square('1 post on Monday, December 1, 2025')).toHaveClass('bg-heat-1');
    expect(square('2 posts on Tuesday, March 31, 2026')).toHaveClass('bg-heat-2');
    expect(square('7 posts on Tuesday, November 3, 2026')).toHaveClass('bg-heat-5');
  });

  it('stops at today', () => {
    show({ ...CALENDAR, days: [{ day: '2026-11-07', posts: 1 }] });

    const november = screen.getByRole('grid', { name: 'November 2026' });
    const days = within(november)
      .getAllByRole('gridcell')
      .map((cell) => cell.getAttribute('aria-label'))
      .filter((name) => name !== null);

    expect(days).toHaveLength(7);
    expect(days).toContain(TODAY);
  });

  it('is one stop on the way through the screen, on today', async () => {
    show({ ...CALENDAR, days: [{ day: '2026-11-07', posts: 1 }] });

    const stops = screen.getAllByRole('gridcell').filter((cell) => cell.tabIndex === 0);

    expect(stops).toStrictEqual([square(TODAY)]);

    await enter();

    expect(square(TODAY)).toHaveFocus();
  });

  it('moves a day with up and down, and a week with left and right', async () => {
    show();

    const acting = await enter();

    await acting.keyboard('{ArrowUp}');
    expect(square('No posts on Friday, November 6, 2026')).toHaveFocus();

    await acting.keyboard('{ArrowLeft}');
    expect(square('3 posts on Friday, October 30, 2026')).toHaveFocus();

    await acting.keyboard('{ArrowRight}{ArrowDown}');
    expect(square('No posts on Saturday, November 7, 2026')).toHaveFocus();
  });

  it('moves a month with the keys that page', async () => {
    show();

    const acting = await enter();

    await acting.keyboard('{PageUp}');
    expect(square('No posts on Wednesday, October 7, 2026')).toHaveFocus();

    await acting.keyboard('{PageDown}');
    expect(square('No posts on Saturday, November 7, 2026')).toHaveFocus();
  });

  it('goes to either end with Home and End, and never past them', async () => {
    show();

    const acting = await enter();

    await acting.keyboard('{Home}');
    expect(square('1 post on Monday, December 1, 2025')).toHaveFocus();

    await acting.keyboard('{ArrowUp}{ArrowLeft}{PageUp}');
    expect(square('1 post on Monday, December 1, 2025')).toHaveFocus();

    await acting.keyboard('{End}');
    expect(square('No posts on Saturday, November 7, 2026')).toHaveFocus();

    await acting.keyboard('{ArrowDown}{ArrowRight}{PageDown}');
    expect(square('No posts on Saturday, November 7, 2026')).toHaveFocus();
  });

  it('stays one stop, on the day the keyboard was left on', async () => {
    show();

    const acting = await enter();

    await acting.keyboard('{ArrowUp}');
    await acting.tab({ shift: true });

    expect(screen.getAllByRole('gridcell').filter((cell) => cell.tabIndex === 0)).toStrictEqual([
      square('No posts on Friday, November 6, 2026'),
    ]);
  });

  it('says how many posts a day saw while it is reached with the keyboard', async () => {
    show();

    const acting = await enter();

    expect(tipped()).toBe('No posts on Saturday, November 7, 2026');

    await acting.keyboard('{ArrowUp}{ArrowLeft}');

    expect(tipped()).toBe('3 posts on Friday, October 30, 2026');
  });

  it('takes the tip away with Escape and leaves the keyboard where it was', async () => {
    show();

    const acting = await enter();

    await acting.keyboard('{Escape}');

    expect(tipped()).toBeNull();
    expect(square('No posts on Saturday, November 7, 2026')).toHaveFocus();
  });

  it('says how many posts a day saw while it is pointed at', async () => {
    show();

    const acting = userEvent.setup();

    await acting.hover(square('7 posts on Tuesday, November 3, 2026'));

    expect(tipped()).toBe('7 posts on Tuesday, November 3, 2026');

    await acting.unhover(square('7 posts on Tuesday, November 3, 2026'));

    expect(tipped()).toBeNull();
  });

  it('never moves the keyboard to a day pressed with a pointer', async () => {
    show();

    const acting = userEvent.setup();

    await acting.click(square('7 posts on Tuesday, November 3, 2026'));

    expect(square('7 posts on Tuesday, November 3, 2026')).not.toHaveFocus();
  });

  it('shows the tip on a touch, and takes it away on a second touch or one elsewhere', async () => {
    renderScreen(
      <>
        <PostingCalendar calendar={CALENDAR} leftOut={null} />
        <p>Elsewhere</p>
      </>,
    );

    const acting = userEvent.setup();
    const busy = square('7 posts on Tuesday, November 3, 2026');

    await acting.pointer({ keys: '[TouchA]', target: busy });
    expect(tipped()).toBe('7 posts on Tuesday, November 3, 2026');

    await acting.pointer({ keys: '[TouchA]', target: busy });
    expect(tipped()).toBeNull();

    await acting.pointer({
      keys: '[TouchA]',
      target: square('2 posts on Tuesday, March 31, 2026'),
    });
    expect(tipped()).toBe('2 posts on Tuesday, March 31, 2026');

    await acting.pointer({ keys: '[TouchA]', target: screen.getByText('Elsewhere') });
    expect(tipped()).toBeNull();
  });

  it('lists the days that saw a post, newest first, for anybody who reads rather than looks', async () => {
    show();

    await userEvent.click(screen.getByText('Show posting days as a list'));

    const rows = within(screen.getByRole('table'))
      .getAllByRole('row')
      .map((row) => row.textContent);

    expect(rows).toStrictEqual([
      'DayPosts',
      'Tuesday, November 3, 20267',
      'Friday, October 30, 20263',
      'Tuesday, March 31, 20262',
      'Monday, December 1, 20251',
    ]);
  });

  it('keeps the meaning of its shades from a screen reader, and out of forced colours', () => {
    show();

    const legend = screen.getByText('No posts').closest('[aria-hidden="true"]');

    expect(legend).not.toBeNull();
    expect(legend).toHaveClass('forced-colors:hidden');
    expect(screen.getByText('5 or more')).toBeInTheDocument();
  });
});
