import path from 'node:path';
import type { NextConfig } from 'next';
import createNextIntlPlugin from 'next-intl/plugin';

/** The repository root, which is also the workspace root every package is installed from. */
const workspace = path.join(import.meta.dirname, '..', '..', '..');

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
