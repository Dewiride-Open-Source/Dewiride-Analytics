// Fails the build if a workspace member named in pnpm-workspace.yaml has no entry in pnpm-lock.yaml.
//
// Two of the members live in a separate private repository and are absent from an ordinary
// checkout. pnpm passes over a member that is not there, which is what lets the free product
// install on its own — and it also writes the lockfile from the members it can see, so an install
// that is not frozen, run where those two are missing, quietly drops their entries. The build that
// has them then refuses the lockfile as out of date. That rewrite is exactly what an automated
// dependency update produces, and what `pnpm add` produces on a machine without the private clone,
// so it is caught here, on the change itself, rather than by the commercial build after the merge.
//
// Both files are read with the shape pnpm gives them rather than through a YAML parser, which the
// workspace does not carry: the member list is a flat list of paths, one per line, and the
// lockfile's importers are keys two spaces in under a top-level `importers:`.

import { readFile } from 'node:fs/promises';

const WORKSPACE_FILE = 'pnpm-workspace.yaml';
const LOCKFILE = 'pnpm-lock.yaml';

// The block of lines belonging to one top-level key: everything after it up to the next line that
// starts in the first column, with comments and blank lines left out.
function blockOf(text, key) {
  const lines = text.split('\n');
  const start = lines.findIndex((line) => line === `${key}:`);
  if (start === -1) {
    return [];
  }
  const block = [];
  for (const line of lines.slice(start + 1)) {
    if (/^\S/.test(line)) {
      break;
    }
    if (line.trim() === '' || line.trim().startsWith('#')) {
      continue;
    }
    block.push(line);
  }
  return block;
}

function unquoted(value) {
  return value.replace(/^(['"])(.*)\1$/, '$2');
}

// The paths listed under `packages:`. A pattern cannot be checked by name and is passed over;
// this repository lists its members one by one, and says why in the workspace file itself.
function listedMembers(workspace) {
  return blockOf(workspace, 'packages')
    .map((line) => /^\s+-\s+(.+?)\s*$/.exec(line)?.[1])
    .filter((entry) => entry !== undefined)
    .map(unquoted)
    .filter((entry) => !/[*?[\]{}!]/.test(entry));
}

function lockedImporters(lockfile) {
  return new Set(
    blockOf(lockfile, 'importers')
      .map((line) => /^ {2}(\S.*?):\s*$/.exec(line)?.[1])
      .filter((entry) => entry !== undefined)
      .map(unquoted),
  );
}

const [workspace, lockfile] = await Promise.all([
  readFile(WORKSPACE_FILE, 'utf8'),
  readFile(LOCKFILE, 'utf8'),
]);

const members = listedMembers(workspace);
const importers = lockedImporters(lockfile);
const missing = members.filter((member) => !importers.has(member));

if (missing.length > 0) {
  console.error(
    `${LOCKFILE} no longer lists ${missing.join(', ')}, which ${WORKSPACE_FILE} names as ` +
      `${missing.length === 1 ? 'a member' : 'members'} of this workspace. It was rewritten on a ` +
      `checkout without ${missing.length === 1 ? 'that directory' : 'those directories'}. ` +
      `Regenerate it with every member present — clone the commercial edition into ee/ and run ` +
      `pnpm install --lockfile-only — and commit the result.`,
  );
  process.exit(1);
}

console.log(`${LOCKFILE} lists every workspace member (${members.length}).`);
