/** One entry in the key above a drawing. */
export interface ChartKey {
  /** The class that paints the mark beside the words. */
  readonly fill: string;
  readonly label: string;
  /** Whether it stands for the period before, which is drawn as a dashed line. */
  readonly dashed?: boolean;
}

/**
 * What each colour on a drawing means.
 *
 * A drawing is announced to anybody reading rather than looking as a single sentence, so the
 * colours in it are only ever legible to somebody who can see them. The key is where they are said
 * in words, which makes it part of the drawing rather than decoration above it.
 */
export function Keys({ items }: { readonly items: readonly ChartKey[] }) {
  return (
    <ul className="flex flex-wrap items-center gap-x-4 gap-y-1.5 text-xs text-foreground-muted">
      {items.map((item) => (
        <li key={item.label} className="flex items-center gap-1.5">
          {item.dashed ? (
            <span aria-hidden className="flex shrink-0 items-center gap-0.5 opacity-60">
              <span className={`h-0.5 w-1.5 rounded-full ${item.fill}`} />
              <span className={`h-0.5 w-1.5 rounded-full ${item.fill}`} />
            </span>
          ) : (
            <span aria-hidden className={`size-2 shrink-0 rounded-full ${item.fill}`} />
          )}
          {item.label}
        </li>
      ))}
    </ul>
  );
}
