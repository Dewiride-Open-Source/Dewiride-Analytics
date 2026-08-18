import path from 'node:path';
import type { NextConfig } from 'next';
import createNextIntlPlugin from 'next-intl/plugin';

const config: NextConfig = {
  // Produces a self-contained server directory, which is what the container image copies. Without
  // it the image needs the whole workspace's installed packages.
  output: 'standalone',

  // Traced from the workspace root, or the packages linked from outside this directory are left
  // out of the image and the server starts and then fails on its first import.
  outputFileTracingRoot: path.join(import.meta.dirname, '..', '..'),

  // One package the trace does not follow far enough on its own. Its compiled-module half is
  // reached through an entry map the tracer resolves to a link it then does not walk, so only its
  // other half is copied and the server dies on its first import of a file that is on the disk it
  // was built from. Named here so the whole package travels with the build.
  outputFileTracingIncludes: {
    '/**': ['../../node_modules/.pnpm/@swc+helpers@*/node_modules/@swc/helpers/**'],
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
    ];
  },
};

export default createNextIntlPlugin()(config);
