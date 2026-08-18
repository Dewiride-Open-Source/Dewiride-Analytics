import { type NextRequest, NextResponse } from 'next/server';
import createMiddleware from 'next-intl/middleware';
import { routing } from '@/i18n/routing';
import { engineOrigin } from '@/lib/engine-origin';
import { DASHBOARD, isEngineAddress, isScreen } from '@/lib/routes';

const localise = createMiddleware(routing);

/**
 * Everything that happens before a screen is drawn.
 *
 * Three jobs, and they never overlap.
 *
 * Anything the engine answers is forwarded to it untouched, which is what lets the browser see a
 * single address for both halves of the product: sign-in is held in a cookie the browser only
 * returns to the address that set it, so a dashboard calling the engine directly would arrive
 * signed out. The same forwarding is what lets somebody paste one address into their own website
 * and have the beacon, the image fallback and the collector all reach it.
 *
 * An address that names no screen is answered here, before anything is rendered. This is an
 * application rather than a website — nobody arrives from a search result, and the only way to
 * reach an address that does not exist is to have typed one — so the answer is to put the person
 * back on the dashboard rather than to show them a dead end. Doing it here rather than in a page
 * is what makes it a real redirect: once a screen has begun to be sent, its answer is already
 * decided and anything the page does afterwards can only be patched over the top.
 *
 * Everything else is a screen, and gets the language prefix its address implies.
 */
export default function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;

  if (isEngineAddress(pathname)) {
    return NextResponse.rewrite(new URL(`${pathname}${search}`, engineOrigin()));
  }

  if (!isScreen(pathname, routing.locales)) {
    return NextResponse.redirect(new URL(DASHBOARD, request.url));
  }

  return localise(request);
}

export const config = {
  matcher: [
    '/api/:path*',
    // Named in full because the image fallback's address ends in a file extension, and the rule
    // below deliberately leaves anything shaped like a file to be served as one.
    '/collect',
    '/collect/:path*',
    // Everything that is a screen: not the engine's addresses, not the framework's own files, and
    // nothing with a full stop in it, which is how a request for a file is told from a page.
    '/((?!api|collect|_next|_vercel|.*\\..*).*)',
  ],
};
