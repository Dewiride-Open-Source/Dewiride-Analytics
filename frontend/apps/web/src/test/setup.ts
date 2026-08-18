import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

// Rendered screens are taken down between tests. The automatic version of this only happens when
// the test globals are injected, and they are not: without it every test after the first searches
// a document containing every screen rendered so far, and finds each control several times over.
afterEach(cleanup);

// The document this runs against implements almost all of a browser, but not the part that reports
// which appearance the device is set to. Anything that reads it would otherwise fail on a missing
// function rather than on anything to do with the test.
//
// Defined as an ordinary property rather than as a stubbed one, because stubs are put back after
// every test and this has to survive for the whole run.
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  configurable: true,
  value: (query: string): MediaQueryList =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    }) as unknown as MediaQueryList,
});

// The same document implements the dialog element's markup but none of its behaviour: opening and
// closing one is simply missing. The real thing is what supplies the focus trap, the inert page
// behind it and the Escape key, so it stays in the product and the two calls are supplied here.
// What a dialog actually does is checked in a real browser instead.
for (const [name, open] of [
  ['show', true],
  ['showModal', true],
  ['close', false],
] as const) {
  Object.defineProperty(HTMLDialogElement.prototype, name, {
    configurable: true,
    value(this: HTMLDialogElement) {
      this.open = open;

      if (!open) {
        this.dispatchEvent(new Event('close'));
      }
    },
  });
}
