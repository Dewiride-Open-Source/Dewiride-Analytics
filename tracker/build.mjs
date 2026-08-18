/**
 * Compiles the beacon.
 *
 * Two outputs from one build: `dist/`, which is what the size budget is measured against, and the
 * dashboard's own served files, because the address people paste into their website is the address
 * they read the dashboard on and the file has to be there.
 *
 * Run it directly to build. Import `bundle` to build in memory, which is how the size test
 * measures the real artefact rather than one somebody remembered to compile first.
 */

import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'esbuild';

const here = path.dirname(fileURLToPath(import.meta.url));

/** Where the compiled beacon is written for measuring and for publishing. */
export const DIST = path.join(here, 'dist');

/** Where the dashboard serves it from. */
export const SERVED = path.join(here, '..', 'frontend', 'apps', 'web', 'public');

/** The name it is served under, which is written into other people's pages and never changes. */
export const FILE_NAME = 'dw.js';

/**
 * The oldest browsers the beacon is compiled to run in.
 *
 * Stated as versions rather than left at the compiler's default, which emits whatever syntax the
 * source happened to use. Syntax a browser cannot parse fails before any check inside the file can
 * run, so a floor set too high does not degrade — it reports nothing, from a whole class of
 * visitor, without ever saying so, and quiet undercounting is the one failure this product cannot
 * have.
 *
 * These are the oldest releases that both carry the beacon interface and can be compiled for: the
 * compiler cannot rewrite block-scoped declarations or iteration for anything older, so a floor
 * below this is not a smaller audience, it is a build that does not run.
 */
export const OLDEST_BROWSERS = ['chrome55', 'edge15', 'firefox53', 'safari11.1'];

/**
 * Compiles the beacon and returns it.
 *
 * @returns The compiled script and its map, as text.
 */
export async function bundle() {
  const result = await build({
    entryPoints: [path.join(here, 'src', 'tracker.ts')],
    // Named but not written. The name is what lets the map be produced as a separate file; the
    // writing is done below, to more than one place.
    outfile: path.join(DIST, FILE_NAME),
    bundle: true,
    format: 'iife',
    target: OLDEST_BROWSERS,
    minify: true,
    charset: 'utf8',
    // Kept out of the file rather than dropped, so the compiled beacon can still be read back to
    // its source without the served file carrying a reference to something that is not published.
    sourcemap: 'external',
    // Bundling keeps licence comments at the end of the file by default. There are no dependencies
    // to attribute, and the one notice that is owed is put back by hand below.
    legalComments: 'none',
    banner: { js: '/*! Dewiride Analytics tracker | MIT | github.com/Dewiride-Open-Source */' },
    write: false,
  });

  const code = result.outputFiles.find((file) => file.path.endsWith('.js'));
  const map = result.outputFiles.find((file) => file.path.endsWith('.map'));

  if (!code || !map) {
    throw new Error('The build produced no script.');
  }

  return { code: code.text, map: map.text };
}

/**
 * Compiles the beacon and writes it everywhere it is served from.
 */
export async function compile() {
  const { code, map } = await bundle();

  await mkdir(DIST, { recursive: true });
  await mkdir(SERVED, { recursive: true });

  await Promise.all([
    writeFile(path.join(DIST, FILE_NAME), code, 'utf8'),
    writeFile(path.join(DIST, `${FILE_NAME}.map`), map, 'utf8'),
    writeFile(path.join(SERVED, FILE_NAME), code, 'utf8'),
  ]);

  return code;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const code = await compile();
  const { gzipSync } = await import('node:zlib');

  console.log(
    `${FILE_NAME}: ${code.length} bytes, ${gzipSync(code).length} bytes compressed for transport`,
  );
}
