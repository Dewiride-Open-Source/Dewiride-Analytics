import path from 'node:path';
import type { NextConfig } from 'next';
import createNextIntlPlugin from 'next-intl/plugin';

/** The repository root, which is also the workspace root every package is installed from. */
const workspace = path.join(import.meta.dirname, '..', '..', '..');

/**
 * Which edition this build is, and where its contributions come from.
 *
 * The dashboard has the same seam the engine has, and it is settled here rather than while the
 * product is running: whichever module `@edition` resolves to is the only one bundled, so neither
 * edition's screens end up inside the other's. One of them is not free software, which is why this
 * is a build-time decision and not a flag.
 *
 * Anything other than `cloud` is the open-source edition, including the usual case of the variable
 * not being set at all — a checkout without `ee/` present must build, and it does.
 *
 * Both paths are written relative to this directory rather than resolved to absolute ones. The
 * bundler treats what is written here as something to import rather than as a place on the disk,
 * and refuses an absolute Windows path outright — which fails on one contributor's machine and
 * nobody else's.
 */
const cloud = process.env.DEWIRIDE_EDITION?.trim().toLowerCase() === 'cloud';

const editionModule = cloud
  ? '../../../ee/frontend/edition/index.ts'
  : './src/edition/community/index.ts';

const config: NextConfig = {
  // Produces a self-contained server directory, which is what the container image copies. Without
  // it the image needs the whole workspace's installed packages.
  output: 'standalone',

  // Traced from the workspace root, or the packages linked from outside this directory are left
  // out of the image and the server starts and then fails on its first import.
  outputFileTracingRoot: workspace,

  // One package the trace does not follow far enough on its own. Its compiled-module half is
  // reached through an entry map the tracer resolves to a link it then does not walk, so only its
  // other half is copied and the server dies on its first import of a file that is on the disk it
  // was built from. Named here so the whole package travels with the build.
  //
  // This path is resolved from this file's directory rather than from the tracing root above, so
  // the two are counted differently and moving the workspace changes one without the other.
  outputFileTracingIncludes: {
    '/**': ['../../../node_modules/.pnpm/@swc+helpers@*/node_modules/@swc/helpers/**'],
  },

  // Stated rather than inferred. Turbopack works out where a project begins from the position of
  // the lockfile, which is the repository root — and left to infer it, the development server
  // watches the engine, the migrations and every build output alongside the screens.
  turbopack: {
    root: workspace,

    // Stated for both editions rather than only the commercial one, so the seam is visible in
    // the build rather than being an alias that silently appears when a variable happens to be set.
    //
    // The short name for this application's own source is stated here as well as in the TypeScript
    // configuration. The configuration's version applies only to files inside this directory, and
    // the commercial edition's screens are not inside it — they are compiled from ee/, where the
    // same import would otherwise resolve to nothing.
    resolveAlias: {
      '@/*': './src/*',
      '@edition': editionModule,
    },
  },

  reactStrictMode: true,

  // Names the framework and its version to anything that connects, and buys nothing in return.
  poweredByHeader: false,

  async headers() {
    return [
      {
        source: '/:path*',
        headers: [
          { key: 'X-Content-Type-Options', value: 'nosniff' },
          { key: 'X-Frame-Options', value: 'DENY' },
          { key: 'Referrer-Policy', value: 'same-origin' },
          {
            key: 'Permissions-Policy',
            value: 'camera=(), microphone=(), geolocation=(), payment=(), usb=()',
          },
        ],
      },
      {
        // The beacon is fetched by every page of every measured website, so it is worth holding
        // on to — but its address is written into other people's pages and can never change, so
        // it must never be marked as never expiring. An hour, then ask again.
        source: '/dw.js',
        headers: [{ key: 'Cache-Control', value: 'public, max-age=3600, must-revalidate' }],
      },
    ];
  },
};

export default createNextIntlPlugin()(config);
