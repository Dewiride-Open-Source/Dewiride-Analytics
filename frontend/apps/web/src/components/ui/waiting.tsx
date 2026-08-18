import { Loader2 } from 'lucide-react';

interface WaitingProps {
  /** Read out to anyone who cannot see the marker turning. */
  readonly label: string;
}

/**
 * The whole screen, while the first answer is still on its way.
 *
 * Deliberately quiet: this is on screen for a fraction of a second in the ordinary case, and
 * something eye-catching would flash rather than reassure.
 */
export function Waiting({ label }: WaitingProps) {
  return (
    <div className="grid min-h-[60vh] place-items-center px-6" role="status" aria-live="polite">
      <div className="flex flex-col items-center gap-3 text-foreground-subtle">
        <Loader2 aria-hidden className="size-6 animate-spin text-accent" />
        <span className="text-sm">{label}</span>
      </div>
    </div>
  );
}
