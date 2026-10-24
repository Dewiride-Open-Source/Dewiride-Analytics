interface RowFactProps {
  /** The words, which are often somebody else's writing: the name of a site, a page's address. */
  readonly children: string;
}

/**
 * One fact said on a row's first line, after what the row was judged to be.
 *
 * The middot travels with the words it introduces rather than standing between two things as a
 * piece of its own, so a narrow screen wrapping the line carries it along instead of leaving it
 * hanging off the end of the line above. It is hidden from anybody listening, who hears the words
 * alone.
 *
 * Two lines at most. On a phone the words get a couple of dozen characters beside a badge, and one
 * line of "Landed on" cut short would say little more than that; a title says the rest to a
 * pointer and nothing at all under a finger. The words may break anywhere, because a hostname or
 * an address has nowhere else to break, and are isolated so that a name written right to left does
 * not rearrange the line around it.
 */
export function RowFact({ children }: RowFactProps) {
  return (
    <span className="flex min-w-0 items-baseline gap-x-1.5 text-xs text-foreground-muted">
      <span aria-hidden className="text-foreground-subtle">
        ·
      </span>
      <bdi className="line-clamp-2 wrap-anywhere" title={children}>
        {children}
      </bdi>
    </span>
  );
}
