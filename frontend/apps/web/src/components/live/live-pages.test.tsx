import { describe, expect, it } from 'vitest';
import { LivePages } from '@/components/live/live-pages';
import type { LivePage } from '@/lib/api/schemas';
import { renderScreen } from '@/test/harness';

/** Three pages as the engine ranks them, the one holding most visitors first. */
const PAGES: readonly LivePage[] = [
  { path: '/writing/how-we-measure', visitors: 9 },
  { path: '/', visitors: 5 },
  { path: '/%E0%A4%B2%E0%A5%87%E0%A4%96', visitors: 1 },
];

describe('what is being read right now', () => {
  it('lists the pages in the order the engine ranked them', () => {
    const { getAllByRole } = renderScreen(<LivePages pages={PAGES} visitorsSeen={15} />);

    const rows = getAllByRole('listitem').map((row) => row.textContent);

    expect(rows[0]).toContain('/writing/how-we-measure');
    expect(rows[2]).toContain('/लेख');
  });

  it('says how many visitors each page held', () => {
    const { getByText } = renderScreen(<LivePages pages={PAGES} visitorsSeen={15} />);

    expect(getByText('9 visitors')).toBeInTheDocument();
    expect(getByText('1 visitor')).toBeInTheDocument();
  });

  /**
   * Every visitor is on exactly one page, so a share is taken against everybody here and the rows
   * add up to the count above them.
   */
  it('ends a row in the share of everybody here who was last on it', () => {
    const { getAllByRole } = renderScreen(<LivePages pages={PAGES} visitorsSeen={15} />);

    const shares = getAllByRole('listitem').map((row) => /\d+%$/.exec(row.textContent ?? '')?.[0]);

    expect(shares).toEqual(['60%', '33%', '7%']);
  });

  it('says each visitor is counted once', () => {
    const { getByText } = renderScreen(<LivePages pages={PAGES} visitorsSeen={15} />);

    expect(
      getByText('Each visitor is counted once, on the page they were last on.'),
    ).toBeInTheDocument();
  });

  /**
   * Only the leading pages are carried back. Where the list was cut short, the visitors on the
   * pages it left out are said in one line, so the card and the count above it never disagree.
   */
  it('says how many are on pages it had no room for', () => {
    const { getByText } = renderScreen(<LivePages pages={PAGES} visitorsSeen={27} />);

    expect(getByText('12 more on other pages')).toBeInTheDocument();
  });

  it('says nothing about other pages when it lists everybody', () => {
    const { queryByText } = renderScreen(<LivePages pages={PAGES} visitorsSeen={15} />);

    expect(queryByText(/more on/)).not.toBeInTheDocument();
  });

  /** The address was written by whoever asked for the page, so nothing on this card follows it. */
  it('shows an address as text and never as something to follow', () => {
    const { queryByRole } = renderScreen(<LivePages pages={PAGES} visitorsSeen={15} />);

    expect(queryByRole('link')).not.toBeInTheDocument();
  });

  it('says plainly when nothing has been opened at all', () => {
    const { getByText, queryByRole } = renderScreen(<LivePages pages={[]} visitorsSeen={0} />);

    expect(getByText('Once somebody opens a page, it appears here.')).toBeInTheDocument();
    expect(queryByRole('listitem')).not.toBeInTheDocument();
  });
});
