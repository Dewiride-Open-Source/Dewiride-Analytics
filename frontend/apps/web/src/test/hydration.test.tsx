import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { timeZoneGroups } from '@/lib/time-zones';
import { CURRENT_BROWSER, hydrated, STALE_SERVER } from '@/test/hydration';

/** A screen that reads the platform while it is drawn: the mistake these checks look for. */
function ReadsThePlatform() {
  const first = timeZoneGroups()[0]?.zones[0];

  return <p>{first?.label}</p>;
}

describe('a page taken over by a browser it was not drawn on', () => {
  /**
   * The check every screen's own test leans on. A harness that could not see the difference
   * between the two machines would pass every one of them, including the ones it exists to fail.
   */
  it('is caught when the browser would have drawn it differently', () => {
    const page = hydrated(<ReadsThePlatform />, {
      server: STALE_SERVER,
      browser: CURRENT_BROWSER,
    });

    expect(page.html).toContain('Casablanca (GMT+1:00)');
    expect(page.recovered).toHaveBeenCalled();
  });

  it('passes quietly when both sides draw the same thing', () => {
    const page = hydrated(<p>Nothing here depends on the machine.</p>, {
      server: STALE_SERVER,
      browser: CURRENT_BROWSER,
    });

    expect(screen.getByText('Nothing here depends on the machine.')).toBeInTheDocument();
    expect(page.recovered).not.toHaveBeenCalled();
    expect(page.complaints).not.toHaveBeenCalled();
  });
});
