import { cva, type VariantProps } from 'class-variance-authority';
import { Loader2 } from 'lucide-react';
import type { ComponentProps, ReactNode } from 'react';
import { cn } from '@/lib/styling';

const button = cva(
  'glow-control inline-flex items-center justify-center gap-2 rounded-md border font-medium ' +
    'whitespace-nowrap select-none disabled:pointer-events-none disabled:opacity-55',
  {
    variants: {
      tone: {
        primary:
          'border-transparent bg-accent text-accent-foreground hover:brightness-105 ' +
          'active:brightness-95',
        secondary: 'border-border bg-surface text-foreground hover:bg-surface-muted',
        quiet: 'border-transparent bg-transparent text-foreground-muted hover:text-foreground',
        danger: 'border-transparent bg-danger text-danger-foreground hover:brightness-110',
      },
      size: {
        sm: 'h-9 px-3 text-sm',
        md: 'h-11 px-4 text-sm',
        lg: 'h-12 px-5 text-base',
        icon: 'size-10 p-0',
      },
      block: {
        true: 'w-full',
        false: '',
      },
    },
    defaultVariants: { tone: 'primary', size: 'md', block: false },
  },
);

interface ButtonProps extends ComponentProps<'button'>, VariantProps<typeof button> {
  /**
   * Whether the action this button starts is still running.
   *
   * Shows a turning marker and stops a second press, so an impatient double-click cannot submit
   * the same form twice.
   */
  readonly busy?: boolean;
  readonly children: ReactNode;
}

export function Button({
  tone,
  size,
  block,
  busy = false,
  disabled,
  className,
  children,
  ...rest
}: ButtonProps) {
  return (
    <button
      type="button"
      aria-busy={busy}
      disabled={disabled === true || busy}
      className={cn(button({ tone, size, block }), className)}
      {...rest}
    >
      {busy ? <Loader2 aria-hidden className="size-4 animate-spin" /> : null}
      {children}
    </button>
  );
}
