import { cn } from '@/lib/styling';

interface BrandMarkProps {
  readonly name: string;
  readonly className?: string;
  /** Hides the wordmark on narrow screens, where the glyph alone is enough. */
  readonly compactOnMobile?: boolean;
}

/**
 * The product's glyph and name.
 *
 * The glyph is drawn inline rather than loaded as an image so that it inherits the accent colour
 * and is correct in both themes without a second file to keep in step.
 */
export function BrandMark({ name, className, compactOnMobile = false }: BrandMarkProps) {
  return (
    <span className={cn('flex items-center gap-2.5', className)}>
      <span
        aria-hidden
        className="grid size-9 shrink-0 place-items-center rounded-md bg-accent text-accent-foreground shadow-[var(--glow-bloom-soft)]"
      >
        <svg viewBox="0 0 24 24" className="size-5" fill="none" role="presentation">
          <path
            d="M4 17.5V13m5 4.5V7.5m5 10V10.5m5 7V5"
            stroke="currentColor"
            strokeWidth="2.25"
            strokeLinecap="round"
          />
        </svg>
      </span>
      <span
        className={cn(
          'text-[0.9375rem] font-semibold tracking-tight text-foreground',
          compactOnMobile && 'hidden sm:inline',
        )}
      >
        {name}
      </span>
    </span>
  );
}
