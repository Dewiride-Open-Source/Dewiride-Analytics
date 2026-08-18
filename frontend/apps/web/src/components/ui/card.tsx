import type { ReactNode } from 'react';
import { cn } from '@/lib/styling';

interface CardProps {
  readonly children: ReactNode;
  readonly className?: string;
  /**
   * Whether this card is the thing the screen is built around.
   *
   * A focal card keeps the accent ring and its bloom permanently, because there is nothing beside
   * it to compete with. An ordinary card rests with a softer halo and sharpens when it is pointed
   * at, so a page of several cards does not glow all over.
   */
  readonly focal?: boolean;
}

export function Card({ children, className, focal = false }: CardProps) {
  return (
    <section
      className={cn(
        'rounded-lg border border-border bg-surface',
        focal ? 'glow-modal' : 'glow-card',
        className,
      )}
    >
      {children}
    </section>
  );
}
