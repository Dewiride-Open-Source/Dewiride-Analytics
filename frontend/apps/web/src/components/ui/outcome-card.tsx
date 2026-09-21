import { Archive, TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { buttonStyle } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Link } from '@/i18n/navigation';

/**
 * What the card is about, which decides the mark drawn above its title.
 *
 * Nothing for a card somebody is filling in; a warning for a link that leads nowhere; and a box
 * for something that has been put away and kept — an account that is closed but not gone.
 */
export type OutcomeTone = 'plain' | 'problem' | 'kept';

interface OutcomeCardProps {
  readonly title: string;
  readonly subtitle?: string;
  readonly tone?: OutcomeTone;
  readonly children: ReactNode;
}

/**
 * The one card a screen is built around, when the screen is about one thing.
 *
 * Choosing a new password, taking up an invitation and reading that an account is closed are all
 * one card in the middle of an otherwise empty screen, so they share its shape here rather than
 * each drawing their own and drifting apart by a pixel at a time.
 */
export function OutcomeCard({ title, subtitle, tone = 'plain', children }: OutcomeCardProps) {
  return (
    <Card focal className="w-full max-w-md p-6 sm:p-8">
      <header className="mb-6 flex flex-col gap-1">
        {tone === 'problem' ? (
          <span
            aria-hidden
            className="mb-3 grid size-10 place-items-center rounded-full bg-danger-soft text-danger"
          >
            <TriangleAlert className="size-5" />
          </span>
        ) : null}
        {tone === 'kept' ? (
          <span
            aria-hidden
            className="mb-3 grid size-10 place-items-center rounded-full bg-accent-soft text-accent-strong"
          >
            <Archive className="size-5" />
          </span>
        ) : null}
        {/* Wrapped anywhere: an account can be named with one long word, and a title that ran
            off the card would take the page with it on a phone. */}
        <h1 className="break-words text-2xl font-semibold tracking-tight text-foreground">
          {title}
        </h1>
        {subtitle ? <p className="text-sm text-foreground-muted">{subtitle}</p> : null}
      </header>
      {children}
    </Card>
  );
}

interface DeadEndProps {
  readonly title: string;
  readonly body: string;
  /** What the one action is called. */
  readonly action: string;
  /** Where it leads. */
  readonly href: string;
}

/**
 * A link that leads nowhere, and the one thing that would put it right.
 *
 * An expired link, an incomplete one and one that was never issued are the same shape of screen:
 * nothing can be done on it, and the way out takes a single press. Which way out depends on what
 * the link was for, so the screen that shows this says where it leads.
 */
export function DeadEnd({ title, body, action, href }: DeadEndProps) {
  return (
    <OutcomeCard title={title} subtitle={body} tone="problem">
      <Link href={href} className={buttonStyle({ size: 'lg', block: true })}>
        {action}
      </Link>
    </OutcomeCard>
  );
}
