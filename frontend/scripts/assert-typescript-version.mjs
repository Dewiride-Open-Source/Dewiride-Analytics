// Fails the build if the resolved TypeScript is outside the supported range.
//
// This exists because `latest` on the registry is a major version ahead of what the toolchain
// accepts: typescript-eslint declares a peer range that stops below 6.1, so an unpinned install
// takes the newest release, lint stops working, and the failure appears as an unrelated resolver
// error. Asserting the resolved version turns that into one sentence at the top of the log.

import { createRequire } from 'node:module';

const SUPPORTED_MAJOR = 6;

const require = createRequire(import.meta.url);
const { version } = require('typescript/package.json');
const major = Number.parseInt(version.split('.')[0], 10);

if (major !== SUPPORTED_MAJOR) {
  console.error(
    `TypeScript ${version} is installed, but this workspace is built and linted against ` +
      `${SUPPORTED_MAJOR}.x. Check the pinned version in frontend/pnpm-workspace.yaml.`,
  );
  process.exit(1);
}

console.log(`TypeScript ${version} — supported.`);
