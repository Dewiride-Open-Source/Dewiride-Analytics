import { within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LiveActivity } from '@/components/live/live-activity';
import type { LiveNarrowing } from '@/lib/analytics/live';
import type { LiveMinute } from '@/lib/api/schemas';
import { drawn } from '@/test/drawing';
import { renderScreen } from '@/test/harness';

/**
 * Stands in for the drawing surface and runs the builder it is handed, so that what the chart
 * would be told to draw can be read as an object instead of as pixels on a canvas.
 */
vi.mock('@/components/charts/chart', async () => ({ ...(await import('@/test/drawing')) }));

/** The moment the reading was taken, part-way through the last minute it carries. */
const AT = '2026-08-29T10:30:20.000Z';

/**
 * Five minutes running up to the reading.
 *
 * One of them silent, so a minute nothing happened in can be seen to be drawn rather than left
 * out; and the last one lower than the one before it, which is what a minute still filling looks
 * like and is exactly the shape that must not be read as traffic falling away.
 */
const MINUTES: readonly LiveMinute[] = [
  { start: '2026-08-29T10:26:00.000Z', pageViews: 4, sentBy: sent(3, 0) },
  { start: '2026-08-29T10:27:00.000Z', pageViews: 0, sentBy: sent(0, 0) },
  { start: '2026-08-29T10:28:00.000Z', pageViews: 9, sentBy: sent(1, 2) },
  { start: '2026-08-29T10:29:00.000Z', pageViews: 7, sentBy: sent(0, 0) },
  { start: '2026-08-29T10:30:00.000Z', pageViews: 2, sentBy: sent(1, 0) },
];

/** How many of a minute's pages went to visitors a search engine and an AI assistant sent. */
function sent(search: number, assistant: number): LiveMinute['sentBy'] {
  return { search, assistant, social: 0, link: 0 };
}

/** Kolkata, so that a minute written on the site's clock cannot pass for one written on UTC. */
const ZONE = 'Asia/Kolkata';

function render(
  minutes: readonly LiveMinute[] = MINUTES,
  at = AT,
  narrowing: LiveNarrowing = null,
) {
  return renderScreen(
    <LiveActivity
      minutes={minutes}
      at={at}
      timeZoneId={ZONE}
      siteName="My Blog"
      narrowing={narrowing}
    />,
  );
}

/** The columns the chart would have drawn, as the charting engine would have received them. */
function columns(): readonly unknown[] {
  const series = drawn.option?.series as readonly Record<string, unknown>[] | undefined;

  return (series?.[0]?.data as readonly unknown[] | undefined) ?? [];
}

describe('the half hour, drawn', () => {
  it('draws one column a minute, in the order the minutes ran', () => {
    render();

    expect(columns()).toStrictEqual([4, 0, 9, 7, expect.objectContaining({ value: 2 })]);
  });

  /**
   * The newest column is a fraction of a minute old. Drawn at full strength beside four finished
   * minutes it reads as a website that has just gone quiet.
   */
  it('fades the minute that has not finished yet', () => {
    render();

    expect(columns().at(-1)).toStrictEqual({ value: 2, itemStyle: { opacity: 0.35 } });
  });

  it('writes the minutes on the clock where the website is', () => {
    const { getByText } = render();

    expect(getByText('Times are the clock in Kolkata.', { exact: false })).toBeInTheDocument();
  });

  /** A fade is invisible to anybody reading rather than looking, so it is also said. */
  it('says in words that the last column is still filling', () => {
    const { getByText } = render();

    // Said in the same line as the clock the minutes are written on, so it is matched inside it.
    expect(getByText(/The last column is still filling\./)).toBeInTheDocument();
  });

  /**
   * A half hour that has gone quiet ends on a minute that is still running and also empty. There
   * is no faded column to explain, and the clause would read as an excuse for the silence.
   */
  it('says nothing about filling when the minute still running is empty', () => {
    const quiet = MINUTES.map((minute, bucket) =>
      bucket === MINUTES.length - 1 ? { ...minute, pageViews: 0 } : minute,
    );

    const { queryByText } = render(quiet);

    expect(queryByText(/The last column is still filling\./)).not.toBeInTheDocument();
  });

  it('names what the columns count, since a colour on its own means nothing', () => {
    const { getAllByText } = render();

    expect(getAllByText('Pages read').length).toBeGreaterThan(0);
  });

  it('announces what the drawing shows, since a drawing tells a screen reader nothing', () => {
    const { getByRole } = render();

    expect(
      getByRole('img', { name: 'Pages read on My Blog each minute over the last half hour.' }),
    ).toBeInTheDocument();
  });
});

describe('the half hour, as figures', () => {
  it('publishes the same figures the drawing was built from', async () => {
    const { getByText, findByRole } = render();

    getByText('Show these figures as a table').click();

    const table = await findByRole('table');

    expect(table).toHaveTextContent('3:56 PM');
    expect(table).toHaveTextContent('9');
  });

  /** The same qualifier the fade carries, for the readers the fade cannot reach. */
  it('marks the minute still filling in the table as well', async () => {
    const { getByText, findByText } = render();

    getByText('Show these figures as a table').click();

    expect(await findByText('still filling')).toBeInTheDocument();
  });
});

describe('the half hour, kept to how visitors arrived', () => {
  const SEARCH = { by: 'sourceKind', kind: 'search' } as const;

  /** The columns and the list beside them describe the same people while it is kept to them. */
  it('draws only the pages that went to visitors a search engine sent', () => {
    render(MINUTES, AT, SEARCH);

    expect(columns()).toStrictEqual([3, 0, 1, 0, expect.objectContaining({ value: 1 })]);
  });

  it('names whose pages the columns count', () => {
    const { getAllByText, getByRole } = render(MINUTES, AT, SEARCH);

    expect(getAllByText('Pages read by visitors from a search engine').length).toBeGreaterThan(0);
    expect(
      getByRole('img', {
        name: 'Pages read on My Blog each minute over the last half hour by visitors from a search engine.',
      }),
    ).toBeInTheDocument();
  });

  it('publishes the same kept figures in the table', async () => {
    const { getByText, findByRole } = render(MINUTES, AT, { by: 'sourceKind', kind: 'assistant' });

    getByText('Show these figures as a table').click();

    const table = await findByRole('table');

    expect(table).toHaveTextContent('Pages read by visitors from an AI assistant');
    expect(
      within(table)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['0', '0', '2', '0', '0']);
  });

  /**
   * What has been recognised is named only among the visitors a reading carries, so no minute can be
   * kept to it: the columns stay everybody's, and say so by their name.
   */
  it("keeps everybody's columns, and their name, while the list is kept to something recognised", () => {
    const { getAllByText } = render(MINUTES, AT, { by: 'category', category: 'known-ai-crawler' });

    expect(columns()).toStrictEqual([4, 0, 9, 7, expect.objectContaining({ value: 2 })]);
    expect(getAllByText('Pages read').length).toBeGreaterThan(0);
  });

  it('says nothing was read by those visitors where none of their pages fall in the half hour', () => {
    const { getByText, queryByRole } = render(MINUTES, AT, { by: 'sourceKind', kind: 'social' });

    expect(
      getByText('Nothing has been read in the last half hour by visitors from a social network.'),
    ).toBeInTheDocument();
    expect(queryByRole('img')).not.toBeInTheDocument();
  });
});

describe('a half hour nobody read', () => {
  const SILENT = MINUTES.map((minute) => ({ ...minute, pageViews: 0 }));

  it('says so rather than drawing a row of nothing', () => {
    const { getByText, queryByRole } = render(SILENT);

    expect(getByText('Nothing has been read in the last half hour.')).toBeInTheDocument();
    expect(queryByRole('img')).not.toBeInTheDocument();
  });

  /** The way out of a quiet website is on the screen around this card, and belongs there once. */
  it('offers no way out of its own', () => {
    const { queryByRole } = render(SILENT);

    expect(queryByRole('button')).not.toBeInTheDocument();
  });
});
