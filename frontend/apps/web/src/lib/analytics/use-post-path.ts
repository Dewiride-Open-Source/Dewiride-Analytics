'use client';

import { parseAsString, useQueryState } from 'nuqs';
import { LIST_KEY, POST_KEY, readPostPath } from '@/lib/analytics/post';

/**
 * The post the screen's address names, or nothing where it names none.
 *
 * Read and never written. A post is chosen on Insights, and the address naming it is the link that
 * was followed or sent; nothing on the screen about it chooses another.
 */
export function usePostPath(): string | null {
  const [written] = useQueryState(POST_KEY, parseAsString);

  return readPostPath(written);
}

/**
 * The page of the list of every post the screen was opened from, as the screen's address names it,
 * or nothing where it was opened from anywhere else.
 *
 * Read and never written, like the post itself: it is only where the screen leads back to.
 */
export function useOpenedFromList(): string | null {
  const [written] = useQueryState(LIST_KEY, parseAsString);

  return written;
}
