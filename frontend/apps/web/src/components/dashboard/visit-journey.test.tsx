import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { VisitJourney } from '@/components/dashboard/visit-journey';
import { engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const SITE_ID = '01a013fa-49d6-77be-b65d-20ec86e9df78';
const VISIT = '2f8a1c0b4d6e7f905a1b2c3d4e5f6071:1787113786255';
const ZONE = 'Etc/UTC';

const READ = [
  {
    at: '2026-08-19T04:29:46.000+00:00',
    path: '/posts/hello',
    statusCode: 200,
    engagedMs: 74_000,
    depthPercent: 82,
    press: null,
  },
  {
    at: '2026-08-19T04:31:12.000+00:00',
    path: '/pricing',
    statusCode: 200,
    engagedMs: 9000,
    depthPercent: 30,
    press: null,
  },
];

/** A reader who worked through a page, followed a link away, and came back to another page. */
const READ_AND_PRESSED = [
  {
    at: '2026-08-19T04:29:46.000+00:00',
    path: '/posts/hello',
    statusCode: 200,
    engagedMs: 74_000,
    depthPercent: 82,
    press: null,
  },
  {
    at: '2026-08-19T04:30:12.000+00:00',
    path: '/posts/hello',
    statusCode: null,
    engagedMs: null,
    depthPercent: null,
    press: {
      name: 'Read the source',
      control: 'link',
      target: 'github.com',
      targetKind: 'external',
    },
  },
  {
    at: '2026-08-19T04:31:12.000+00:00',
    path: '/pricing',
    statusCode: 200,
    engagedMs: 9000,
    depthPercent: 30,
    press: null,
  },
];

const SWEPT = [
  {
    at: '2026-08-19T04:29:46.000+00:00',
    path: '/.env',
    statusCode: 404,
    engagedMs: null,
    depthPercent: null,
    press: null,
  },
  {
    at: '2026-08-19T04:29:46.000+00:00',
    path: '/wp-login.php',
    statusCode: 404,
    engagedMs: null,
    depthPercent: null,
    press: null,
  },
];

function engineWith(steps: readonly unknown[], asked: { count: number } = { count: 0 }) {
  return engineDoing(async () => {
    asked.count += 1;

    return respondWith(200, { visit: VISIT, steps });
  });
}

function show(pageCount: number, open = true) {
  renderScreen(
    <VisitJourney
      siteId={SITE_ID}
      visit={VISIT}
      pageCount={pageCount}
      timeZoneId={ZONE}
      open={open}
    />,
  );
}

describe('the pages one visit went through', () => {
  it('asks for nothing until the visit has been opened', async () => {
    const asked = { count: 0 };
    engineWith(READ, asked);

    show(2, false);

    await screen.findByText('What happened in this visit');

    expect(asked.count).toBe(0);
  });

  it('lists the pages in the order they were asked for', async () => {
    engineWith(READ);

    show(2);

    const addresses = await screen.findAllByTitle(/^\//);

    expect(addresses.map((address) => address.textContent)).toEqual(['/posts/hello', '/pricing']);
  });

  it('says how long each page held somebody and how far down they got', async () => {
    engineWith(READ);

    show(2);

    expect(await screen.findByText('1m 14s')).toBeInTheDocument();
    expect(screen.getByText('82% down')).toBeInTheDocument();
    expect(screen.getByText('9s')).toBeInTheDocument();
    expect(screen.getByText('30% down')).toBeInTheDocument();

    // Stamped with the time it happened where the website is.
    expect(screen.getByText('4:29:46 AM')).toBeInTheDocument();
  });

  /**
   * The distinction the whole product rests on, one step at a time — and said once rather than
   * beside every address, which would turn one honest fact into a column of noise.
   */
  it('says once that a visit nothing watched could not be measured', async () => {
    engineWith(SWEPT);

    show(2);

    expect(
      await screen.findByText(
        'Nothing here ran the code on your pages, so how long each one held anybody is unknown.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText('Not measured')).not.toBeInTheDocument();
  });

  it('marks a page the website could not deliver', async () => {
    engineWith(SWEPT);

    show(2);

    expect(await screen.findAllByText('Not found')).toHaveLength(2);
  });

  it('says nothing beside a page that was delivered', async () => {
    engineWith(READ);

    show(2);

    await screen.findByText('/posts/hello');

    expect(screen.queryByText('Not found')).not.toBeInTheDocument();
    expect(screen.queryByText('200')).not.toBeInTheDocument();
  });

  /**
   * A sweep's journey is thousands of pages long. The visit's own page count is exact, so a journey
   * that was cut short says which of the two the reader is looking at.
   */
  it('says when it is showing only the opening of a long visit', async () => {
    engineWith(READ);

    show(900);

    expect(await screen.findByText('Showing the first 2 of 900 pages.')).toBeInTheDocument();
  });

  it('says nothing about being shortened when the whole visit is on screen', async () => {
    engineWith(READ);

    show(2);

    await screen.findByText('/posts/hello');

    expect(screen.queryByText(/Showing the first/)).not.toBeInTheDocument();
  });

  it('says the pages could not be loaded rather than showing none', async () => {
    engineStopped();

    show(2);

    expect(await screen.findByText('This visit could not be loaded.')).toBeInTheDocument();
  });

  it('says plainly when a visit recorded no pages', async () => {
    engineWith([]);

    show(0);

    expect(await screen.findByText('Nothing was recorded for this visit.')).toBeInTheDocument();
  });

  /**
   * The concrete half of the whole feature: a visit reads as a story rather than as a list of
   * addresses. What somebody pressed sits between the page they were on and the page it took them
   * to, in the order it happened.
   */
  it('shows what was clicked, in place, between the pages it happened between', async () => {
    engineWith(READ_AND_PRESSED);

    show(2);

    const trail = await screen.findByRole('list');
    const said = Array.from(trail.querySelectorAll('li')).map((step) =>
      step.textContent?.replace(/\s+/g, ' ').trim(),
    );

    expect(said[0]).toContain('/posts/hello');
    expect(said[1]).toContain('Clicked Read the source');
    expect(said[2]).toContain('/pricing');
  });

  it('says what sort of thing was clicked, in its own words', async () => {
    engineWith(READ_AND_PRESSED);

    show(2);

    expect(await screen.findByText('Link')).toBeInTheDocument();
  });

  /**
   * Where a click led on the site is the next page down the rail, so saying it again would be
   * noise. Where it led away is the only place that fact exists.
   */
  it('says where a click led away and stays quiet where it led onwards', async () => {
    engineWith(READ_AND_PRESSED);

    show(2);

    expect(await screen.findByText('went to github.com')).toBeInTheDocument();
    expect(screen.queryByText(/went to \/pricing/)).not.toBeInTheDocument();
  });

  it('says plainly when a site gave the thing that was clicked no name', async () => {
    engineWith([
      {
        at: '2026-08-19T04:29:50.000+00:00',
        path: '/posts/hello',
        statusCode: null,
        engagedMs: null,
        depthPercent: null,
        press: { name: '', control: 'button', target: null, targetKind: 'none' },
      },
    ]);

    show(1);

    expect(await screen.findByText('Clicked something unnamed')).toBeInTheDocument();
  });

  /**
   * The visit's own page count is exact and counts pages. Counting clicks against it would make a
   * visit whose every page is on screen announce itself as shortened.
   */
  it('counts only pages when it says whether the visit was cut short', async () => {
    engineWith(READ_AND_PRESSED);

    show(2);

    await screen.findByText('/pricing');

    expect(screen.queryByText(/Showing the first/)).not.toBeInTheDocument();
  });

  /**
   * The address a click led to is written by whoever wrote the page. It reaches the screen as text
   * and nothing else.
   */
  it('never turns what was clicked into something that can be followed', async () => {
    engineWith([
      {
        at: '2026-08-19T04:29:50.000+00:00',
        path: '/posts/hello',
        statusCode: null,
        engagedMs: null,
        depthPercent: null,
        press: {
          name: '<img src=x onerror=alert(1)>',
          control: 'link',
          target: 'evil.example',
          targetKind: 'external',
        },
      },
    ]);

    show(1);

    expect(await screen.findByText(/<img src=x onerror=alert\(1\)>/)).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  /**
   * An address is written by whoever asked for it. It reaches the screen as text and nothing else,
   * so it is never a link and never markup.
   */
  it('never turns a visited address into something that can be followed', async () => {
    engineWith([
      {
        at: '2026-08-19T04:29:46.000+00:00',
        path: '/<img src=x onerror=alert(1)>',
        statusCode: 404,
        engagedMs: null,
        depthPercent: null,
        press: null,
      },
    ]);

    show(1);

    expect(await screen.findByText('/<img src=x onerror=alert(1)>')).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
});
