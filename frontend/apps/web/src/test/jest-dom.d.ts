// The extra assertions about a document — `toBeInTheDocument`, `toHaveTextContent` and the rest —
// are registered with the runner by the setup file. The compiler learns about them here, through
// the interface the runner documents for adding assertions. The package carries a declaration of
// its own, but it names the runner's `Assertion` with one type parameter where the runner's has
// two, so it is left out: the setup file registers the matchers directly rather than through the
// entry that would bring that declaration in.
import 'vitest';
import type { TestingLibraryMatchers } from '@testing-library/jest-dom/matchers';

declare module 'vitest' {
  // The runner's interface names both parameters and a merge has to repeat them: R is what an
  // assertion returns — void, or a promise under `.resolves` and `.rejects` — and T is the value
  // under test, which none of these assertions read. The first argument is the slot for an
  // asymmetric matcher; the runner publishes no type for one, so anything is accepted there.
  interface Matchers<R, T> extends TestingLibraryMatchers<unknown, R> {}
}
