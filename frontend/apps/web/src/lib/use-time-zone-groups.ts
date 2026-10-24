'use client';

import { useState } from 'react';
import { useHydrated } from '@/lib/hydrated';
import { type TimeZoneGroup, timeZoneGroups } from '@/lib/time-zones';

/**
 * Every zone this browser offers, once the screen is running in one, and nothing until then.
 *
 * The list is the platform's — which zones it knows, and where each stands today — so a list drawn
 * while a page is rendered on the server is the server's, from its copy of the world's time zone
 * rules, which need not be the browser's. A page offering the server's list and then the browser's
 * hands React two first paints that disagree wherever the two copies do, and React answers that by
 * throwing away everything the server drew. The bar across the top holds a picker on every screen,
 * so it would be every screen. Where a picker starts is worked out from the list, so it waits for
 * the list too, and the server's own zone — UTC, in a container — never reaches a page either.
 *
 * So the server and the browser's first paint both offer nothing, and the browser's own list
 * arrives in the render straight after. A screen first drawn in the browser — one somebody moves
 * to later in a visit — is not taking anything over, and has its list from its first paint.
 *
 * Drawn up once for each screen that asks and kept for as long as that screen is, for the reason
 * the list itself is never kept at module scope. The bar across the top stays for the whole of a
 * visit, so its picker keeps the list it drew when the visit's first page was taken over.
 */
export function useTimeZoneGroups(): readonly TimeZoneGroup[] | null {
  const hydrated = useHydrated();
  const [groups, setGroups] = useState<readonly TimeZoneGroup[] | null>(null);

  if (hydrated && groups === null) {
    setGroups(timeZoneGroups());
  }

  return groups;
}
