import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type RenderResult, render } from '@testing-library/react';
import { NextIntlClientProvider } from 'next-intl';
import { NuqsTestingAdapter, type OnUrlUpdateFunction } from 'nuqs/adapters/testing';
import type { ReactElement } from 'react';
import type { SignedInUser } from '@/lib/api/schemas';
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

  /**
   * Who is signed in, for the screens that are about the person reading them.
   *
   * Nobody by default, because most screens are about a website rather than about whoever is
   * looking at it and would only be given somebody to ignore.
   */
  readonly signedInAs?: SignedInUser | null;

  /**
   * What the address is asking for.
   *
   * Nothing by default, which is the address every screen opens on. Setting it is how a test asks
   * what somebody sees when they follow a link somebody else sent them.
   */
  readonly searchParams?: string | Record<string, string>;

  /**
   * Told whenever the screen writes to the address.
   *
   * How a test asks the other half of the same question: that what somebody arrived at is a link
   * they could send back.
   */
  readonly watchingAddress?: OnUrlUpdateFunction;
}

/**
 * Renders a screen with the three things every screen assumes: somewhere to keep answers, the
 * English catalogue, and an address it can read from and write to.
 *
 * The real catalogue is used rather than a stub, so a test that looks for a sentence is also
 * checking that the sentence exists and reads the way it is supposed to.
 */
export function renderScreen(
  ui: ReactElement,
  { sessionAlreadyRead = true, signedInAs = null, searchParams, watchingAddress }: Options = {},
): RenderResult & { readonly cache: QueryClient } {
  const cache = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  if (sessionAlreadyRead) {
    cache.setQueryData(sessionKey, {
      setupCompleted: true,
      user: signedInAs,
      token: 'proof-value',
    });
  }

  const result = render(
    // The address remembers what is written to it, the way the browser's own does. Left frozen on
    // what it started with, a screen would read back the period it opened on however many times
    // somebody changed it, and every test of a choice would be a test of nothing.
    <NuqsTestingAdapter searchParams={searchParams} onUrlUpdate={watchingAddress} hasMemory>
      <NextIntlClientProvider locale="en" messages={messages}>
        <QueryClientProvider client={cache}>{ui}</QueryClientProvider>
      </NextIntlClientProvider>
    </NuqsTestingAdapter>,
  );

  return { ...result, cache };
}
