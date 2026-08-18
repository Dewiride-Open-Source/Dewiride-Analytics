'use client';

import { Eye, EyeOff } from 'lucide-react';
import { type ComponentProps, type ReactNode, useId, useState } from 'react';
import { cn } from '@/lib/styling';

interface FieldProps {
  readonly label: string;
  /** Sentence under the control explaining what to put in it. */
  readonly hint?: string;
  /** Shown instead of the hint once the entry has been refused, and announced when it appears. */
  readonly problem?: string;
  /** Marks the field as one that may be left empty. */
  readonly optionalLabel?: string;
  /**
   * The control itself. It is handed the identifiers it must carry so that the label, the hint
   * and the refusal are all attached to it rather than merely sitting near it.
   */
  readonly children: (attributes: ControlAttributes) => ReactNode;
}

/** What a control has to carry for its label and its description to reach a screen reader. */
export interface ControlAttributes {
  readonly id: string;
  readonly 'aria-describedby': string | undefined;
  readonly 'aria-invalid': boolean;
}

/**
 * One labelled entry in a form, with its explanation and its refusal.
 *
 * The control is supplied as a function rather than as children so that the identifiers cannot be
 * forgotten: there is no way to render the field without receiving them, and no way to receive
 * them without putting them somewhere.
 */
export function Field({ label, hint, problem, optionalLabel, children }: FieldProps) {
  const id = useId();
  const hintId = `${id}-hint`;
  const problemId = `${id}-problem`;
  const description = problem ? problemId : hint ? hintId : undefined;

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-baseline justify-between gap-3">
        <label htmlFor={id} className="text-sm font-medium text-foreground">
          {label}
        </label>
        {optionalLabel ? (
          <span className="text-xs text-foreground-subtle">{optionalLabel}</span>
        ) : null}
      </div>

      {children({ id, 'aria-describedby': description, 'aria-invalid': Boolean(problem) })}

      {problem ? (
        <p id={problemId} role="alert" className="text-sm text-danger">
          {problem}
        </p>
      ) : hint ? (
        <p id={hintId} className="text-sm text-foreground-muted">
          {hint}
        </p>
      ) : null}
    </div>
  );
}

const control =
  'w-full rounded-md border border-border bg-surface px-3 text-sm text-foreground ' +
  'placeholder:text-foreground-subtle disabled:opacity-60 aria-[invalid=true]:border-danger';

export function TextInput({ className, ...rest }: ComponentProps<'input'>) {
  return <input className={cn('glow-control h-11', control, className)} {...rest} />;
}

export function SelectInput({ className, children, ...rest }: ComponentProps<'select'>) {
  return (
    <select className={cn('select-trigger h-11 appearance-none', control, className)} {...rest}>
      {children}
    </select>
  );
}

/**
 * A password box with a way to see what has been typed.
 *
 * Hiding a password by default is right; giving somebody no way to check it before submitting is
 * how a long passphrase becomes three failed attempts and a lockout. The control announces which
 * state pressing it will produce, and reverts to hidden whenever the screen is left.
 */
export function PasswordInput({
  showLabel,
  hideLabel,
  className,
  ...rest
}: ComponentProps<'input'> & { readonly showLabel: string; readonly hideLabel: string }) {
  const [visible, setVisible] = useState(false);

  return (
    <div className="relative">
      <input
        type={visible ? 'text' : 'password'}
        className={cn('glow-control h-11 pr-12', control, className)}
        {...rest}
      />
      <button
        type="button"
        onClick={() => setVisible((was) => !was)}
        aria-pressed={visible}
        className="absolute inset-y-0 right-0 flex items-center rounded-r-md px-3 text-foreground-subtle transition-colors hover:text-foreground focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-accent-strong"
      >
        {visible ? (
          <EyeOff aria-hidden className="size-4" />
        ) : (
          <Eye aria-hidden className="size-4" />
        )}
        <span className="sr-only">{visible ? hideLabel : showLabel}</span>
      </button>
    </div>
  );
}
