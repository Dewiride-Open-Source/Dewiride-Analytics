import type { ChartPalette } from '@/lib/charts/palette';

/**
 * What is drawn over what, where the order they are declared in is not enough.
 *
 * A chart draws its series in the order it is handed them, but only among those left at the same
 * depth. Anything given a depth of its own is therefore placed against the rest here rather than
 * by where it happens to sit in a list.
 */
export const LAYERS = {
  /**
   * The shade a hover lays across the bucket it points at, beneath the columns the engine draws a
   * step above it. Laid over them it fades the columns it picks out, until the bucket being
   * pointed at looks like one still being judged.
   */
  pointerShade: 1,
  /** The wash over the buckets that have not finished being judged, which covers the bands. */
  unsettled: 5,
  /** The period before, which has been judged in full and is not what the wash is quietening. */
  earlier: 6,
  /**
   * The line a hover draws through the bucket it points at, over everything else — a line is thin
   * enough to leave the shape beneath it whole. Stated rather than left to the engine, which keeps
   * a depth once given until another replaces it, so a picture switched from columns to a line
   * would otherwise draw its line at the shade's depth, beneath the bands.
   */
  pointerLine: 50,
} as const;

/**
 * How much of its colour a bucket keeps while what it counts is not finished.
 *
 * Faded rather than left out. The newest bucket on a chart is a real count that will still grow,
 * so drawing it at full strength invites a reader to compare it with the finished ones beside it,
 * and dropping it takes the most recent thing that happened off the picture of what is happening.
 */
export const UNSETTLED = 0.35;

/**
 * How the card a hover opens writes one figure of a measure, as the engine hands it over with the
 * place of its bucket.
 */
export type FigureWriter = (value: unknown, at?: number) => string;

/**
 * Writes a measure's figures in the card a hover opens: in the reader's own way, and a bucket with
 * no figure in the words the table uses for it, which say why it has none. Left to itself the
 * engine writes a dash there, which reads as a figure of its own.
 *
 * Given to the measures a picture is drawn from and never to the period before, whose buckets past
 * its own end are nothing because that period had none, not because a month went uncounted.
 *
 * @param written How the reader writes a number.
 * @param nothing What a bucket with no figure is called, by its place — or where the engine does
 * not say which bucket it is.
 */
export function figureWriter(
  written: (value: number) => string,
  nothing: (at: number | undefined) => string,
): FigureWriter {
  return (value, at) => (typeof value === 'number' ? written(value) : nothing(at));
}

/** What the engine hands the heading of the card a hover opens. */
interface Pointed {
  /** The bucket's name as the axis writes it. */
  readonly value: string;
  /** One entry for each series drawn at that bucket, each carrying where the bucket sits. */
  readonly seriesData: readonly { readonly dataIndex: number }[];
}

/**
 * The heading of the card a hover opens: the full name of the bucket under the pointer.
 *
 * Found by where the bucket sits rather than by its name on the axis. An hour is written twice on
 * the day the clocks go back, and its name alone cannot say which of the two is meant. Where no
 * series is drawn at the bucket there is nothing to say where it sits, and the name on the axis is
 * the heading.
 */
function heading(titles: readonly string[]): (pointed: Pointed) => string {
  return ({ value, seriesData }) => {
    const at = seriesData[0]?.dataIndex;

    return at === undefined ? value : (titles[at] ?? value);
  };
}

/** How the buckets along the axis are named. */
interface Naming {
  /**
   * Whether every bucket is named rather than as many as fit. A picture of years holds only a
   * handful, each a year of a website's history, and a year left unnamed on a phone is one a reader
   * cannot place.
   */
  readonly every?: boolean;
}

/** Names every bucket along the axis, handed to the engine where it asks which buckets to name. */
const EVERY_BUCKET = () => true;

/**
 * The frame both pictures on the overview are drawn inside.
 *
 * The grid, the two axes and the tooltip are the same whichever question is being answered, and
 * written once so that switching between them changes what is drawn and nothing around it. A
 * chart whose axes shifted as the view changed would read as the period having changed too.
 *
 * @param labels What each bucket is called, already written in the website's own zone.
 * @param columns Whether the buckets are drawn as columns, which sit in a band of the axis rather
 * than at a point on it.
 * @param titles What each bucket is called over the card a hover opens, in the order of the
 * labels; the label itself where left out.
 * @param naming How the buckets along the axis are named: as many as fit where left out.
 */
export function chartFrame(
  palette: ChartPalette,
  labels: readonly string[],
  columns: boolean,
  titles: readonly string[] = labels,
  { every = false }: Naming = {},
) {
  const pointer = columns
    ? {
        type: 'shadow' as const,
        z: LAYERS.pointerShade,
        shadowStyle: { color: palette.line, opacity: 0.5 },
      }
    : { type: 'line' as const, z: LAYERS.pointerLine, lineStyle: { color: palette.border } };

  return {
    // Laid out so the axes' labels fall inside the picture: the setting ECharts 6 gives for it,
    // rather than the older one it only honours through a compatibility module this bundle leaves
    // out.
    grid: {
      top: 16,
      right: 4,
      bottom: 4,
      left: 4,
      outerBoundsMode: 'same' as const,
      outerBoundsContain: 'axisLabel' as const,
    },
    tooltip: {
      trigger: 'axis' as const,
      // Kept inside the picture. Left to itself the card jumps to the far side of the pointer when
      // it would run off the right, and a picture a phone wide is narrow enough that the far side
      // is often off the left of the screen.
      confine: true,
      backgroundColor: palette.surface,
      borderColor: palette.border,
      textStyle: { color: palette.text, fontSize: 12 },
      // The card takes its heading from the label the pointer would carry on the axis. With `show`
      // unset the engine keeps that label off the axis, which goes on reading the short names.
      axisPointer: { ...pointer, label: { formatter: heading(titles) } },
    },
    xAxis: {
      type: 'category' as const,
      data: [...labels],
      boundaryGap: columns,
      axisTick: { show: false },
      axisLine: { lineStyle: { color: palette.line } },
      // The last bucket is always named, ending at its own position rather than centred on it, and
      // a name that would collide with it gives way. Which buckets are named is settled after the
      // picture is fitted to its labels, so on a phone the last name chosen can otherwise sit a
      // column from the edge and be cut off part-way through — "3:58 PM" drawn as "3:58 P".
      //
      // Where every bucket is named, none is left out to make room, and the last name drawn as a
      // column is centred on it instead: ending at the middle of its column it reaches back over
      // the one before, and a column's middle is far enough from the edge for the whole name. Both
      // are stated either way, since the engine keeps a setting of an axis until another replaces
      // it, and a picture of months drawn after one of years would otherwise name every month.
      axisLabel: {
        color: palette.label,
        fontSize: 11,
        hideOverlap: true,
        interval: every ? EVERY_BUCKET : ('auto' as const),
        showMaxLabel: true,
        alignMaxLabel: every && columns ? ('center' as const) : ('right' as const),
      },
    },
    yAxis: {
      type: 'value' as const,
      minInterval: 1,
      axisLabel: { color: palette.label, fontSize: 11 },
      splitLine: { lineStyle: { color: palette.line } },
    },
  };
}
