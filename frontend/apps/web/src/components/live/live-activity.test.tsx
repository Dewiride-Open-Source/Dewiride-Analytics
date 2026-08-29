import { describe, expect, it, vi } from 'vitest';
import { LiveActivity } from '@/components/live/live-activity';
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
  { start: '2026-08-29T10:26:00.000Z', pageViews: 4 },
  { start: '2026-08-29T10:27:00.000Z', pageViews: 0 },
  { start: '2026-08-29T10:28:00.000Z', pageViews: 9 },
  { start: '2026-08-29T10:29:00.000Z', pageViews: 7 },
  { start: '2026-08-29T10:30:00.000Z', pageViews: 2 },
];

/** Kolkata, so that a minute written on the site's clock cannot pass for one written on UTC. */
const ZONE = 'Asia/Kolkata';

function render(minutes: readonly LiveMinute[] = MINUTES, at = AT) {
  return renderScreen(
    <LiveActivity minutes={minutes} at={at} timeZoneId={ZONE} siteName="My Blog" />,
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
