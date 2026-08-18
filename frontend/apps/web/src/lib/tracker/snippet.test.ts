import { describe, expect, it } from 'vitest';
import { pixelTag, scriptTag, trackingSnippet } from '@/lib/tracker/snippet';

const SITE = '0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c77';

describe('the lines somebody pastes into their website', () => {
  it('points at the address the dashboard is being read on', () => {
    expect(scriptTag('https://analytics.example.com', SITE)).toBe(
      `<script defer src="https://analytics.example.com/dw.js" data-site="${SITE}"></script>`,
    );
  });

  /**
   * A browser sends only the site's name, and not the page, when an image on another address asks
   * for it. Set on the tag, the policy overrides that — and without it every reader with scripting
   * turned off is recorded as having read the front page.
   */
  it('asks the browser to name the page when it fetches the fallback image', () => {
    expect(pixelTag('https://analytics.example.com', SITE)).toContain(
      'referrerpolicy="no-referrer-when-downgrade"',
    );
  });

  it('carries the website it belongs to in both lines', () => {
    const snippet = trackingSnippet('https://analytics.example.com', SITE);

    expect(snippet.match(new RegExp(SITE, 'g'))).toHaveLength(2);
  });

  /**
   * The address comes from the browser's own bar, which on some deployments ends in a slash. Left
   * alone it produces a double slash — which works, and then reads as a mistake to whoever pastes
   * it into their site.
   */
  it('does not double the slash when the address it was given ends in one', () => {
    expect(trackingSnippet('https://analytics.example.com/', SITE)).not.toContain('.com//');
  });

  it('serves the fallback image from the same address as everything else', () => {
    expect(trackingSnippet('http://localhost:3000', SITE)).toContain(
      `http://localhost:3000/collect/pixel.gif?site=${SITE}`,
    );
  });
});
