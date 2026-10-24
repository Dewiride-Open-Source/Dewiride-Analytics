import { renderHook, screen } from '@testing-library/react';
import { useLayoutEffect } from 'react';
import { renderToString } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { useTimeZoneGroups } from '@/lib/use-time-zone-groups';
import { CURRENT_BROWSER, hydrated, STALE_SERVER } from '@/test/hydration';

/** What a screen would offer, written out where a test can read it. */
function Offered() {
  const groups = useTimeZoneGroups();
  const labels = groups?.flatMap((group) => group.zones.map((zone) => zone.label));

  return <output>{labels === undefined ? 'nothing' : labels.join(', ')}</output>;
}

describe('the time zones a screen offers', () => {
  it('offers nothing while the page is drawn on the server', () => {
    expect(renderToString(<Offered />)).toBe('<output>nothing</output>');
  });

  /**
   * Nothing on the first paint in the browser either, because that paint has to match the page the
   * server sent. The browser's own zones arrive in the render straight after it.
   */
  it('offers this browser’s zones once the page the server sent is its own', () => {
    const page = hydrated(<Offered />, { server: STALE_SERVER, browser: CURRENT_BROWSER });

    expect(page.html).toContain('nothing');
    expect(screen.getByRole('status')).toHaveTextContent('Casablanca (GMT)');
    expect(page.recovered).not.toHaveBeenCalled();
  });

  /**
   * A screen somebody moves to later in a visit is not taking anything over from a server. What
   * its first paint held is recorded as that paint is laid out, because reading the answer after
   * the render has settled would pass just the same for a list that arrived a paint late.
   */
  it('offers them from the first paint of a screen drawn in the browser', () => {
    const painted: boolean[] = [];

    renderHook(() => {
      const groups = useTimeZoneGroups();

      useLayoutEffect(() => {
        painted.push(groups !== null);
      });

      return groups;
    });

    expect(painted[0]).toBe(true);
  });

  it('hands back the same list on every render, without asking the platform again', () => {
    const asked = vi.spyOn(Intl, 'supportedValuesOf');
    const { result, rerender } = renderHook(() => useTimeZoneGroups());
    const first = result.current;

    rerender();

    expect(first).not.toBeNull();
    expect(result.current).toBe(first);
    expect(asked).toHaveBeenCalledOnce();
  });

  /**
   * Kept for as long as the screen is and no longer, so the clocks changing between one screen and
   * the next show up on the next.
   */
  it('asks the platform afresh for every screen', () => {
    const asked = vi.spyOn(Intl, 'supportedValuesOf');
    const first = renderHook(() => useTimeZoneGroups()).result.current;
    const second = renderHook(() => useTimeZoneGroups()).result.current;

    expect(second).not.toBe(first);
    expect(asked).toHaveBeenCalledTimes(2);
  });
});
