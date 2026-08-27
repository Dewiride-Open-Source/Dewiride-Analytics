'use client';

import { useCallback } from 'react';
import { Chart } from '@/components/charts/chart';
import type { ChartPalette } from '@/lib/charts/palette';
import { type RingSlice, ringOption } from '@/lib/charts/ring';

interface RingProps {
  /**
   * Builds the parts from the palette in force.
   *
   * Taken as a function for the same reason a chart's option is: the ring is redrawn when the
   * theme changes, without the card around it having to watch for that. Memoise it.
   */
  readonly slices: (palette: ChartPalette) => readonly RingSlice[];
  /**
   * What the ring shows, in a sentence.
   *
   * The whole of what a screen reader is told about the drawing itself. The figures behind it are
   * not in here: they are in the list beside the ring, in words and numbers.
   */
  readonly label: string;
}

/**
 * A set whose parts add up to one thing, drawn as one thing.
 *
 * Sized rather than stretched, because a ring drawn into whatever space is left comes out as an
 * oval on one screen and a thumbnail on another, and the answer it gives is the proportion between
 * its parts — which only reads if the shape is a circle.
 */
export function Ring({ slices, label }: RingProps) {
  const option = useCallback(
    (palette: ChartPalette) => ringOption(slices(palette), palette),
    [slices],
  );

  return (
    <div className="size-40 shrink-0">
      <Chart option={option} label={label} />
    </div>
  );
}
