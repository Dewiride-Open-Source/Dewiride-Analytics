import { describe, expect, it } from 'vitest';
import { LivePages } from '@/components/live/live-pages';
import type { LivePage } from '@/lib/api/schemas';
import { renderScreen } from '@/test/harness';

/** Three pages as the engine ranks them, busiest first. */
const PAGES: readonly LivePage[] = [
  { path: '/writing/how-we-measure', pageViews: 12, visitors: 9 },
  { path: '/', pageViews: 5, visitors: 5 },
  { path: '/%E0%A4%B2%E0%A5%87%E0%A4%96', pageViews: 1, visitors: 1 },
];

describe('what is being read right now', () => {
  it('lists the pages in the order the engine ranked them', () => {
    const { getAllByRole } = renderScreen(<LivePages pages={PAGES} />);

    const rows = getAllByRole('listitem').map((row) => row.textContent);

    expect(rows[0]).toContain('/writing/how-we-measure');
    expect(rows[2]).toContain('/लेख');
  });

  it('says how many visitors each page held', () => {
    const { getByText } = renderScreen(<LivePages pages={PAGES} />);

    expect(getByText('9 visitors')).toBeInTheDocument();
    expect(getByText('1 visitor')).toBeInTheDocument();
  });

  /**
   * Only the leading handful of pages are carried back, so a share would be taken against the
   * part of the half hour that fitted on the card and would read as a share of all of it.
   */
  it('ends a row in how many times the page was opened rather than in a share', () => {
    const { getByText, queryByText } = renderScreen(<LivePages pages={PAGES} />);

    expect(getByText('12')).toBeInTheDocument();
    expect(queryByText('67%')).not.toBeInTheDocument();
  });

  /** The address was written by whoever asked for the page, so nothing on this card follows it. */
  it('shows an address as text and never as something to follow', () => {
    const { queryByRole } = renderScreen(<LivePages pages={PAGES} />);

    expect(queryByRole('link')).not.toBeInTheDocument();
  });

  it('says plainly when nothing has been opened at all', () => {
    const { getByText, queryByRole } = renderScreen(<LivePages pages={[]} />);

    expect(getByText('Once somebody opens a page, it appears here.')).toBeInTheDocument();
    expect(queryByRole('listitem')).not.toBeInTheDocument();
  });
});
