import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { stampedIdentifier } from './beacon';
import { goTo, only, openPage, press, type Sent, stampResponse, visibility } from './harness';

let page: ReturnType<typeof openPage>;

beforeEach(() => {
  window.history.replaceState({}, '', '/posts/hello');
  visibility('visible');
  page = openPage();
});

afterEach(() => {
  page.close();
});

describe('what the beacon reports when a page is opened', () => {
  it('names the website and the page, and nothing about the person reading it', () => {
    page.open();

    const view = only(page.sent, 'pageview');

    expect(view['siteId']).toBe('0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c77');
    expect(view['url']).toBe(window.location.href);
    expect(Object.keys(view).join(' ')).not.toMatch(/cookie|id$|fingerprint|screen/i);
  });

  /**
   * The store keeps "nobody touched this page" and "we were not watching yet" apart, and a page
   * view is sent in the first instant of the second one. Reporting nought for engagement there
   * would assert a measurement that has not been taken.
   */
  it('leaves out every measurement it has not taken yet', () => {
    page.open();

    const view = only(page.sent, 'pageview');

    expect(view).not.toHaveProperty('engagedMs');
    expect(view).not.toHaveProperty('scrollDepthPercent');
    expect(view).not.toHaveProperty('pointerInteraction');
    expect(view).not.toHaveProperty('keyboardInteraction');
  });

  it('passes on the browser admitting it is being driven by automation', () => {
    Object.defineProperty(navigator, 'webdriver', { value: true, configurable: true });

    page.open();

    expect(only(page.sent, 'pageview')['webDriver']).toBe(true);
  });

  it('echoes back the identifier the page was served with, so the two can be matched', () => {
    page.open({ correlationId: 'e6c1f0' });

    expect(only(page.sent, 'pageview')['correlationId']).toBe('e6c1f0');
  });

  /**
   * A site built once and served from a cache has no per-request markup to carry an identifier,
   * but its response still has headers — so that is where the identifier is looked for. Without
   * this, matching the two halves of the measurement would work only on sites that render every
   * page as it is asked for, which is a minority of the ones this product is for.
   */
  it('finds the identifier on the response when the page itself carries none', () => {
    stampResponse([{ name: 'dw', description: '7b21ae' }]);

    expect(stampedIdentifier()).toBe('7b21ae');
  });

  it('reports no identifier when the site put none on the response', () => {
    stampResponse([{ name: 'cache', description: 'hit' }]);

    expect(stampedIdentifier()).toBeUndefined();
  });

  it('reports no identifier when the browser kept no timings at all', () => {
    stampResponse([]);

    expect(stampedIdentifier()).toBeUndefined();
  });

  /**
   * The identifier names the one page the site's server handed over. Pages reached from it without
   * a fresh request were never delivered by anybody, so repeating it would make several readings
   * look like one and lose every page after the first.
   */
  it('sends the identifier for the delivered page only, not for the ones reached from it', () => {
    page.open({ correlationId: 'e6c1f0' });

    goTo('/posts/second');

    const views = page.sent.filter((report) => report['kind'] === 'pageview');

    expect(views).toHaveLength(2);
    expect(views[0]?.['correlationId']).toBe('e6c1f0');
    expect(views[1]).not.toHaveProperty('correlationId');
  });

  /**
   * Two copies on one page — a theme that embeds the tag and an owner who pastes it as well —
   * would double every number on the dashboard, which is worse than measuring nothing.
   */
  it('measures once however many times it is started', () => {
    page.open();
    page.open();

    expect(page.sent.filter((report) => report['kind'] === 'pageview')).toHaveLength(1);
  });
});

describe('how the beacon sends what it has measured', () => {
  /**
   * Plain text is one of the few kinds a browser will post to another address without asking
   * permission first. Anything else has to be cleared in advance, and a refusal loses the report
   * silently.
   */
  it('sends plain text, so no permission has to be asked for', () => {
    page.open();

    expect(page.bodies[0]).toBeTypeOf('string');
    expect(page.bodies[0]).not.toBeInstanceOf(Blob);
  });

  it('sends it another way when the browser will not take it', () => {
    page.refuseTransport();

    page.open();

    expect(fetch).toHaveBeenCalledWith(
      'https://analytics.example.com/collect',
      expect.objectContaining({ method: 'POST', mode: 'no-cors', keepalive: true }),
    );
    expect(only(page.sent, 'pageview')).toBeDefined();
  });
});

describe('what the beacon reports when a reading ends', () => {
  it('reports the reading once the page is no longer in front of anybody', () => {
    page.open();

    visibility('hidden');

    expect(only(page.sent, 'exit')).toHaveProperty('engagedMs');
  });

  /**
   * Closing a tab raises both of the events this listens for. Reporting on each would file every
   * departure twice.
   */
  it('reports it once, however many ways the browser announces the departure', () => {
    page.open();

    visibility('hidden');
    window.dispatchEvent(new Event('pagehide'));

    expect(only(page.sent, 'exit')).toBeDefined();
  });

  it('reports again once the reader has come back and left a second time', () => {
    page.open();

    visibility('hidden');
    visibility('visible');
    visibility('hidden');

    expect(page.sent.filter((report) => report['kind'] === 'exit')).toHaveLength(2);
  });

  it('carries whether the reader touched the page, and whether they typed', () => {
    page.open();

    window.dispatchEvent(new Event('pointerdown'));
    window.dispatchEvent(new Event('keydown'));
    visibility('hidden');

    const exit = only(page.sent, 'exit');

    expect(exit['pointerInteraction']).toBe(true);
    expect(exit['keyboardInteraction']).toBe(true);
  });

  it('says plainly that nobody touched the page when nobody did', () => {
    page.open();

    visibility('hidden');

    const exit = only(page.sent, 'exit');

    expect(exit['pointerInteraction']).toBe(false);
    expect(exit['keyboardInteraction']).toBe(false);
  });
});

describe('a page that moves itself to another address', () => {
  it('closes the reading of the old page and opens one for the new', () => {
    page.open();

    goTo('/posts/second');

    const views = page.sent.filter((report) => report['kind'] === 'pageview');

    expect(views).toHaveLength(2);
    expect(page.sent.filter((report) => report['kind'] === 'exit')).toHaveLength(1);
    expect(views[1]?.['url']).toContain('/posts/second');
  });

  it('names the page that was left as where the reader came from', () => {
    page.open();

    goTo('/posts/second');

    const views = page.sent.filter((report) => report['kind'] === 'pageview') as Sent[];

    expect(views[1]?.['referrer']).toContain('/posts/hello');
  });

  it('says nothing when a site rewrites the address it is already on', () => {
    page.open();

    window.history.replaceState({}, '', '/posts/hello');

    expect(page.sent.filter((report) => report['kind'] === 'pageview')).toHaveLength(1);
  });
});

describe('a page the browser rendered before anybody asked for it', () => {
  afterEach(() => {
    Object.defineProperty(document, 'prerendering', { value: undefined, configurable: true });
  });

  /**
   * A page rendered in advance reports itself as hidden for the whole of that life. Measuring it
   * there would invent a page view and file a departure for a reading that never happened.
   */
  it('is not measured until it is genuinely shown', () => {
    Object.defineProperty(document, 'prerendering', { value: true, configurable: true });

    page.open();

    expect(page.sent).toHaveLength(0);

    Object.defineProperty(document, 'prerendering', { value: false, configurable: true });
    document.dispatchEvent(new Event('prerenderingchange'));

    expect(only(page.sent, 'pageview')).toBeDefined();
  });
});

describe('a page the browser kept rather than threw away', () => {
  /**
   * Coming back to a page held in the browser's own store is the reading resuming. Counting it as
   * a new one would invent a page view that nobody made.
   */
  it('resumes the reading rather than starting a new one', () => {
    page.open();

    window.dispatchEvent(new Event('pagehide'));

    const restored = new Event('pageshow') as Event & { persisted: boolean };

    Object.defineProperty(restored, 'persisted', { value: true });
    window.dispatchEvent(restored);
    visibility('hidden');

    expect(page.sent.filter((report) => report['kind'] === 'pageview')).toHaveLength(1);
    expect(page.sent.filter((report) => report['kind'] === 'exit')).toHaveLength(2);
  });
});

describe('how far down the page the reader got', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('is reported as a percentage of the page, never past the end of it', () => {
    Object.defineProperty(document.documentElement, 'scrollHeight', {
      value: 2000,
      configurable: true,
    });
    Object.defineProperty(window, 'innerHeight', { value: 800, configurable: true });
    Object.defineProperty(window, 'scrollY', { value: 1600, configurable: true });

    page.open();
    window.dispatchEvent(new Event('scroll'));
    visibility('hidden');

    expect(only(page.sent, 'exit')['scrollDepthPercent']).toBe(100);
  });

  it('reports the furthest point reached, not wherever the reader stopped', () => {
    Object.defineProperty(document.documentElement, 'scrollHeight', {
      value: 2000,
      configurable: true,
    });
    Object.defineProperty(window, 'innerHeight', { value: 500, configurable: true });

    page.open();

    Object.defineProperty(window, 'scrollY', { value: 1000, configurable: true });
    window.dispatchEvent(new Event('scroll'));

    Object.defineProperty(window, 'scrollY', { value: 0, configurable: true });
    window.dispatchEvent(new Event('scroll'));

    visibility('hidden');

    expect(only(page.sent, 'exit')['scrollDepthPercent']).toBe(75);
  });
});

describe('saying how a reading is going before it has ended', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  /**
   * A phone that dismisses the tab, a machine put to sleep, a process shut down to free memory:
   * none of them raises anything the beacon can listen for. Without a report on the way through,
   * the readers who stayed longest would be exactly the ones who counted for nothing.
   */
  it('reports what a page is worth so far, without waiting for the reader to leave', () => {
    vi.useFakeTimers();
    page.open();

    vi.advanceTimersByTime(15000);

    expect(only(page.sent, 'engagement')).toHaveProperty('engagedMs');
  });

  it('carries how far down the reader got and whether they touched the page', () => {
    vi.useFakeTimers();
    page.open();

    window.dispatchEvent(new Event('pointerdown'));
    vi.advanceTimersByTime(15000);

    const report = only(page.sent, 'engagement');

    expect(report['pointerInteraction']).toBe(true);
    expect(report['keyboardInteraction']).toBe(false);
    expect(report).toHaveProperty('scrollDepthPercent');
  });

  /** Reports are running totals, so the store can take the largest and lose nothing to a gap. */
  it('reports totals that only ever grow', () => {
    vi.useFakeTimers();
    page.open();

    vi.advanceTimersByTime(15000);
    vi.advanceTimersByTime(30000);

    const [first, second] = page.sent
      .filter((report) => report['kind'] === 'engagement')
      .map((report) => report['engagedMs'] as number);

    expect(second).toBeGreaterThanOrEqual(first as number);
  });

  it('says so less often the longer somebody stays, rather than at a fixed drumbeat', () => {
    vi.useFakeTimers();
    page.open();

    const reported = () => page.sent.filter((report) => report['kind'] === 'engagement').length;

    vi.advanceTimersByTime(15000);
    expect(reported()).toBe(1);

    vi.advanceTimersByTime(15000);
    expect(reported(), 'the second report came as quickly as the first').toBe(1);

    vi.advanceTimersByTime(15000);
    expect(reported()).toBe(2);
  });

  it('says nothing about a page that is no longer in front of anybody', () => {
    vi.useFakeTimers();
    page.open();

    visibility('hidden');
    vi.advanceTimersByTime(600000);

    expect(page.sent.filter((report) => report['kind'] === 'engagement')).toHaveLength(0);
  });

  it('takes it up again when the reader comes back', () => {
    vi.useFakeTimers();
    page.open();

    visibility('hidden');
    visibility('visible');
    vi.advanceTimersByTime(15000);

    expect(only(page.sent, 'engagement')).toBeDefined();
  });

  /** A page reached without a reload is a new reading, and is measured from the beginning. */
  it('begins again for a page the site moved itself to', () => {
    vi.useFakeTimers();
    page.open();

    vi.advanceTimersByTime(15000);
    vi.advanceTimersByTime(30000);

    goTo('/posts/second');
    vi.advanceTimersByTime(15000);

    const addresses = page.sent
      .filter((report) => report['kind'] === 'engagement')
      .map((report) => report['url'] as string);

    expect(addresses[addresses.length - 1]).toContain('/posts/second');
    expect(addresses).toHaveLength(3);
  });
});

describe('what somebody pressed on the page', () => {
  /** Every press of a control, without the site having to name one in advance. */
  function pressed(): Sent[] {
    return page.sent.filter((report) => report['kind'] === 'action');
  }

  it('names the control, what it said, and the page it was on', () => {
    page.open();

    press('<button data-press>Subscribe</button>');

    const click = only(page.sent, 'action');

    expect(click['element']).toBe('button');
    expect(click['label']).toBe('Subscribe');
    expect(click['url']).toContain('/posts/hello');
  });

  /**
   * A press lands on whatever is under the finger, which is usually a fragment of the thing
   * somebody meant to press. Reporting the fragment would answer a question nobody asked.
   */
  it('attributes a press to the control rather than to the scrap of it that was hit', () => {
    page.open();

    press('<button><span data-press>Subscribe</span></button>');

    expect(only(page.sent, 'action')['element']).toBe('button');
  });

  it('keeps a page on the same site in full, so which page they went to can be answered', () => {
    page.open();

    press('<a data-press href="/pricing">Pricing</a>');

    const click = only(page.sent, 'action');

    expect(click['target']).toBe('/pricing');
    expect(click['targetKind']).toBe('internal');
  });

  /**
   * The rest of an address off the site is written by whoever wrote the link and can carry
   * anything at all, including who followed it. Where they went is the host and nothing more.
   */
  it('keeps only the host of somewhere off the site', () => {
    page.open();

    press('<a data-press href="https://github.com/dewiride/analytics?from=jane">Source</a>');

    const click = only(page.sent, 'action');

    expect(click['target']).toBe('github.com');
    expect(click['targetKind']).toBe('external');
  });

  it('records that an address to write to was used, and never the address itself', () => {
    page.open();

    press('<a data-press href="mailto:jane@example.com">Email me</a>');

    const click = only(page.sent, 'action');

    expect(click['targetKind']).toBe('contact');
    expect(click).not.toHaveProperty('target');
    expect(JSON.stringify(click)).not.toContain('jane@example.com');
  });

  it('prefers the name a control is given to the text inside it', () => {
    page.open();

    press('<button data-press aria-label="Close" title="Dismiss"><span>x</span></button>');

    expect(only(page.sent, 'action')['label']).toBe('Close');
  });

  /** What somebody typed is theirs. The name of the field is the site's own writing. */
  it('never reads what somebody typed into a field', () => {
    page.open();

    press('<input data-press type="text" value="jane@example.com" aria-label="Email address">');

    const click = only(page.sent, 'action');

    expect(click['element']).toBe('input');
    expect(click['label']).toBe('Email address');
    expect(JSON.stringify(click)).not.toContain('jane@example.com');
  });

  it('says nothing at all about a part of the page the site marked as private', () => {
    page.open();

    press('<div data-dw-ignore><button data-press>Delete everything Jane wrote</button></div>');

    expect(pressed()).toHaveLength(0);
  });

  it('says nothing about a press that landed on the page rather than on anything in it', () => {
    page.open();

    press('<p data-press>Just some words.</p>');

    expect(pressed()).toHaveLength(0);
  });

  /**
   * A browser answers a press on a field's own name by raising a second press on the field. Both
   * reach the same watcher, and counting both would report one tick of a box as two.
   */
  it('counts one tick of a labelled box once', () => {
    page.open();

    press(
      '<label data-press for="letters">Send me letters</label><input id="letters" type="checkbox">',
    );

    expect(pressed()).toHaveLength(1);
  });

  it('keeps a long label down to something that is still a name', () => {
    page.open();

    press(`<button data-press>${'first '.repeat(40)}</button>`);

    expect((only(page.sent, 'action')['label'] as string).length).toBe(64);
  });

  it('measures a page that handles its own presses and stops them going any further', () => {
    page.open();

    document.body.innerHTML = '<div><button data-press>Subscribe</button></div>';
    document.body.firstElementChild?.addEventListener('click', (event) => event.stopPropagation());
    document.body
      .querySelector('[data-press]')
      ?.dispatchEvent(new MouseEvent('click', { bubbles: true }));

    expect(only(page.sent, 'action')['label']).toBe('Subscribe');
  });

  it('leaves out where a press pointed when it pointed nowhere', () => {
    page.open();

    press('<button data-press>Subscribe</button>');

    const click = only(page.sent, 'action');

    expect(click).not.toHaveProperty('target');
    expect(click).not.toHaveProperty('targetKind');
  });

  it('says nothing about an address the browser itself cannot read', () => {
    page.open();

    press('<a data-press href="http://[">Broken</a>');

    const click = only(page.sent, 'action');

    expect(click['element']).toBe('a');
    expect(click).not.toHaveProperty('targetKind');
  });

  /**
   * The whole of what one press says, pinned.
   *
   * Where on the screen it landed, how hard, how long it was held and which button was used all
   * describe the reader rather than the reading, and none of them is here. Written as the complete
   * set rather than as a list of absences, so a field added later has to be argued for.
   */
  it('says what was pressed and nothing whatever about the person pressing it', () => {
    page.open();

    press('<button data-press>Subscribe</button>');

    expect(Object.keys(only(page.sent, 'action')).sort()).toStrictEqual([
      'clientTimestamp',
      'element',
      'kind',
      'label',
      'siteId',
      'url',
    ]);
  });
});
