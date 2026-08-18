import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type RenderResult, render } from '@testing-library/react';
import { NextIntlClientProvider } from 'next-intl';
import type { ReactElement } from 'react';
import { sessionKey } from '@/lib/queries/session';
import messages from '../../messages/en.json';

interface Options {
  /**
   * Whether the session has already been read.
   *
   * On by default, because most screens are only reachable once it has and they need the
   * proof-of-origin value that came with it. Turn it off to test what a screen does while the
   * answer is still on its way, or when it never comes.
   */
  readonly sessionAlreadyRead?: boolean;
}

/**
 * Renders a screen with the two things every screen assumes: somewhere to keep answers, and the
 * English catalogue.
 *
 * The real catalogue is used rather than a stub, so a test that looks for a sentence is also
 * checking that the sentence exists and reads the way it is supposed to.
 */
export function renderScreen(
  ui: ReactElement,
  { sessionAlreadyRead = true }: Options = {},
): RenderResult & { readonly cache: QueryClient } {
  const cache = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  if (sessionAlreadyRead) {
    cache.setQueryData(sessionKey, { setupCompleted: true, user: null, token: 'proof-value' });
  }

  const result = render(
    <NextIntlClientProvider locale="en" messages={messages}>
      <QueryClientProvider client={cache}>{ui}</QueryClientProvider>
    </NextIntlClientProvider>,
  );

  return { ...result, cache };
}
