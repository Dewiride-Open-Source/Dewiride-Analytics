import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SiteSettings } from '@/components/dashboard/site-settings';
import { type Engine, engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const SITE_ID = '01a013fa-49d6-77be-b65d-20ec86e9df78';

/** Answers with what the website records, and remembers what it is asked to change it to. */
function engineWith(captureClicks = true): Engine {
  let recording = captureClicks;

  return engineDoing(async (_path, init) => {
    if (init.method === 'PUT') {
      const asked = JSON.parse(String(init.body)) as { captureClicks?: boolean };

      recording = asked.captureClicks ?? recording;
    }

    return respondWith(200, { captureClicks: recording });
  });
}

function show(open = true) {
  renderScreen(
    <SiteSettings open={open} onClose={() => {}} siteId={SITE_ID} siteDomain="example.com" />,
  );
}

describe('what a website measures', () => {
  it('shows whether clicks are being recorded', async () => {
    engineWith(true);

    show();

    expect(await screen.findByRole('switch', { name: /Record what people click/ })).toBeChecked();
  });

  it('shows when they are not', async () => {
    engineWith(false);

    show();

    expect(
      await screen.findByRole('switch', { name: /Record what people click/ }),
    ).not.toBeChecked();
  });

  it('asks for nothing until the panel is opened', async () => {
    const engine = engineWith();

    show(false);

    expect(engine.count).toBe(0);
  });

  it('turns recording off and says so', async () => {
    engineWith(true);

    show();

    await userEvent.click(await screen.findByRole('switch'));

    expect(await screen.findByRole('switch')).not.toBeChecked();
  });

  /**
   * A setting left out is left as it was, so a panel that has never heard of a setting cannot
   * switch it off by saving.
   */
  it('sends only the setting that is being changed', async () => {
    const engine = engineWith(true);

    show();

    await userEvent.click(await screen.findByRole('switch'));

    const change = engine.all().find((sent) => sent.init.method === 'PUT');

    expect(change).toBeDefined();
    expect(JSON.parse(String(change?.init.body))).toStrictEqual({ captureClicks: false });
  });

  /**
   * A cookie the browser returns on its own is not proof that this page meant to send the change,
   * so the pair the engine issued travels with it.
   */
  it('proves where the change came from', async () => {
    const engine = engineWith(true);

    show();

    await userEvent.click(await screen.findByRole('switch'));

    const change = engine.all().find((sent) => sent.init.method === 'PUT');
    const headers = change?.init.headers as Record<string, string> | undefined;

    expect(headers?.['X-Csrf-Token']).toBe('proof-value');
  });

  it('says the settings could not be read rather than showing a guess', async () => {
    engineStopped();

    show();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('switch')).not.toBeInTheDocument();
  });

  it('says a change could not be saved rather than pretending it was', async () => {
    let asked = 0;

    engineDoing(async (_path, init) => {
      asked += 1;

      if (init.method === 'PUT') {
        throw new TypeError('Failed to fetch');
      }

      return respondWith(200, { captureClicks: true });
    });

    show();

    await userEvent.click(await screen.findByRole('switch'));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.getByRole('switch')).toBeChecked();
    expect(asked).toBeGreaterThan(1);
  });
});
