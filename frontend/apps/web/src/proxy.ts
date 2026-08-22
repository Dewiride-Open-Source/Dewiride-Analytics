import { type NextRequest, NextResponse } from 'next/server';
import createMiddleware from 'next-intl/middleware';
import { routing } from '@/i18n/routing';
import { engineOrigin } from '@/lib/engine-origin';
import { DASHBOARD, isEngineAddress, isScreen, isSiteFile } from '@/lib/routes';
import { siteOrigin } from '@/lib/site-origin';

const localise = createMiddleware(routing);

/**
 * Everything that happens before a screen is drawn.
 *
 * Four jobs, tried in order, and they never overlap.
 *
 * Anything the engine answers is forwarded to it untouched, which is what lets the browser see a
 * single address for both halves of the product: sign-in is held in a cookie the browser only
 * returns to the address that set it, so a dashboard calling the engine directly would arrive
 * signed out. The same forwarding is what lets somebody paste one address into their own website
 * and have the beacon, the image fallback and the collector all reach it.
 *
 * Everything that names a screen gets the language prefix its address implies.
 *
 * Everything else belongs to whatever a deployment has put in front of the product. The screens
 * deliberately occupy one segment and leave the root free; where a website is deployed there, it
 * is forwarded the same way the engine is, so that it too shares the one address. That includes
 * its compiled files and the two files a crawler expects at the root — which is why they are named
 * in the matcher below, since the screen pattern leaves anything shaped like a file alone.
 *
 * And where there is nothing in front of the product — an installation somebody runs themselves,
 * which is the ordinary case — an address that names no screen is answered here, before anything
 * is rendered. This is an application rather than a website, so the answer is to put the person on
 * the dashboard rather than to show them a dead end. Doing it here rather than in a page is what
 * makes it a real redirect: once a screen has begun to be sent its answer is already decided, and
 * anything the page does afterwards can only be patched over the top. The one exception is an
 * address shaped like a file, which is answered as missing instead.
 */
export default function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;

  if (isEngineAddress(pathname)) {
    return NextResponse.rewrite(new URL(`${pathname}${search}`, engineOrigin()));
  }

  if (isScreen(pathname, routing.locales)) {
    return localise(request);
  }

  const website = siteOrigin();

  if (website) {
    return NextResponse.rewrite(new URL(`${pathname}${search}`, website));
  }

  // A file-shaped address with no website behind it is answered as missing rather than sent to a
  // screen. A redirect would hand back a page of markup to something that asked for a stylesheet
  // or read a crawler's robots file, which is a stranger failure than the one that actually
  // happened: there is nothing here.
  if (isSiteFile(pathname)) {
    return NextResponse.next();
  }

  return NextResponse.redirect(new URL(DASHBOARD, request.url));
}

export const config = {
  matcher: [
    '/api/:path*',
    // Named in full because the image fallback's address ends in a file extension, and the rule
    // below deliberately leaves anything shaped like a file to be served as one.
    '/collect',
    '/collect/:path*',
    // The website's own compiled files and the two files a crawler looks for at the root. Named
    // for the same reason: all three are shaped like files.
    '/site-assets/:path*',
    '/robots.txt',
    '/sitemap.xml',
    // Everything that is a screen: not the engine's addresses, not the framework's own files, and
    // nothing with a full stop in it, which is how a request for a file is told from a page.
    '/((?!api|collect|_next|_vercel|.*\\..*).*)',
  ],
};
