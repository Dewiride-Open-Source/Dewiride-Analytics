import { AlertTriangle, Info } from 'lucide-react';
import type { ReactNode } from 'react';
import { cn } from '@/lib/styling';

interface NoticeProps {
  readonly tone?: 'problem' | 'information';
  readonly title: string;
  readonly children?: ReactNode;
  readonly className?: string;
}

/**
 * Something the person needs to read before carrying on.
 *
 * Announced as it appears rather than only being visible, because the commonest place this is
 * used is after a form has been refused, and the refusal must reach somebody whose attention is
 * still on the control they just left.
 */
export function Notice({ tone = 'problem', title, children, className }: NoticeProps) {
  const problem = tone === 'problem';
  const Icon = problem ? AlertTriangle : Info;

  return (
    <div
      role={problem ? 'alert' : 'status'}
      className={cn(
        'flex gap-3 rounded-md border p-3 text-sm',
        problem
          ? 'border-danger/35 bg-danger-soft text-foreground'
          : 'border-border bg-surface-muted text-foreground',
        className,
      )}
    >
      <Icon
        aria-hidden
        className={cn('mt-0.5 size-4 shrink-0', problem ? 'text-danger' : 'text-accent-strong')}
      />
      <div className="flex flex-col gap-1">
        <p className="font-medium">{title}</p>
        {children ? <div className="text-foreground-muted">{children}</div> : null}
      </div>
    </div>
  );
}
