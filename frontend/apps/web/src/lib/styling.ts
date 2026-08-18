import { type ClassValue, clsx } from 'clsx';
import { twMerge } from 'tailwind-merge';

/**
 * Joins class names, letting a later one win over an earlier one that sets the same property.
 *
 * Without the merge, a component that accepts an override ends up with both its own padding and
 * the caller's in the attribute, and which one applies is decided by the order Tailwind happened
 * to emit them in.
 */
export function cn(...values: ClassValue[]): string {
  return twMerge(clsx(values));
}
