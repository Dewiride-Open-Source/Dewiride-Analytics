'use client';

import { type RefObject, useEffect, useRef, useState } from 'react';

/**
 * Whether a box's contents are wider than the box, watched for as long as it is on screen.
 *
 * A region that scrolls sideways has to be reachable from the keyboard, or somebody without a pointer
 * cannot see the end of it; one with nothing to scroll is one more stop on the way to everything else.
 * The box is watched and so is whatever it holds, since either can change which of the two it is.
 *
 * @returns Where to attach the box, and whether its contents are wider than it.
 */
export function useOverflowing<TElement extends HTMLElement>(): readonly [
  RefObject<TElement | null>,
  boolean,
] {
  const box = useRef<TElement>(null);
  const [overflowing, setOverflowing] = useState(false);

  useEffect(() => {
    const watched = box.current;

    if (watched === null) {
      return;
    }

    const measure = () => setOverflowing(watched.scrollWidth > watched.clientWidth);
    const watcher = new ResizeObserver(measure);

    watcher.observe(watched);

    if (watched.firstElementChild !== null) {
      watcher.observe(watched.firstElementChild);
    }

    measure();

    return () => watcher.disconnect();
  }, []);

  return [box, overflowing];
}
