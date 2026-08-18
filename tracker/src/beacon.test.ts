import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { goTo, only, openPage, type Sent, visibility } from './harness';

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
