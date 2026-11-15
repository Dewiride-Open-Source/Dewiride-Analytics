/**
 * Packages the WordPress plugin into the archive the dashboard hands out.
 *
 * WordPress installs an uploaded plugin by unpacking the archive and keeping its one top folder as
 * the plugin's own, so that folder is named after the plugin's slug and holds exactly the files the
 * plugin runs on. They are named one at a time below rather than collected from the directory: the
 * plugin is kept beside its development tools, its tests and the packages those install, and every
 * file in the archive reaches every website that installs it, so each is there because somebody put
 * it on the list.
 *
 * No dependency. The format is small, and the archive is read back and compared with what went
 * into it before it is saved. Nothing in it depends on when or where it was made: one fixed time on
 * every entry, the entries in the order of the list, and forward slashes between folders whatever
 * separator the machine making it uses.
 *
 * Run it directly to package the plugin. Import `packagePlugin` to package one kept somewhere else,
 * which is how its refusals can be tried without disturbing the real one.
 */

import { lstatSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { crc32, deflateRawSync, inflateRawSync } from 'node:zlib';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');

/** The plugin's slug: the folder WordPress installs it into, and the archive's one top folder. */
export const SLUG = 'dewiride-analytics';

/** The archive's file name. */
export const ARCHIVE = `${SLUG}.zip`;

/** Where the dashboard serves the archive, and so where its download button has to point. */
export const ADDRESS = `/downloads/${ARCHIVE}`;

/** Where the plugin is kept. */
export const PLUGIN = path.join(root, 'integrations', 'wordpress-plugin');

/** The dashboard's description of the download: the address it links to, and its version. */
export const DOWNLOADS = path.join(root, 'frontend', 'apps', 'web', 'src', 'lib', 'downloads.ts');

/** The folder the dashboard serves `ADDRESS` from, as a file handed out as it is. */
export const SERVED = path.join(root, 'frontend', 'apps', 'web', 'public', 'downloads');

/** The plugin's main file, whose header is where WordPress reads the plugin's version. */
const MAIN_FILE = `${SLUG}.php`;

/** The plugin's description for the WordPress plugin directory; its stable tag names a version. */
const README = 'readme.txt';

/** Every file the plugin runs on, in the order the archive holds them. */
export const SHIPPED = Object.freeze([
  MAIN_FILE,
  'uninstall.php',
  README,
  'LICENSE',
  'includes/class-plugin.php',
  'includes/class-settings.php',
  'includes/class-settings-page.php',
  'includes/class-audience.php',
  'includes/class-tracker-tag.php',
  'includes/class-optimiser-exclusions.php',
  'includes/class-no-script-image.php',
  'includes/class-tag-check.php',
]);

/**
 * Where those files live. Anything found under these has to be on the list too: the plugin requires
 * each of its files by name, so one left out would break the plugin on every website that installed
 * it, where failing this build costs nobody anything.
 */
const SHIPPED_ROOTS = Object.freeze([MAIN_FILE, 'uninstall.php', README, 'LICENSE', 'includes']);

/**
 * The time every entry carries: midnight on 1 January 1980, the earliest the format can record. Any
 * real time would make two archives of the same files differ. A date is packed as the year counted
 * from 1980, then the month, then the day; a time as hours, minutes and half-seconds.
 */
const DOS_TIME = 0;
const DOS_DATE = (0 << 9) | (1 << 5) | 1;

/** Version 2.0 of the format, the first with folders and with deflate, which is all this uses. */
const VERSION_NEEDED = 20;

/**
 * Made on Unix, which is what tells an unpacking tool to read the attributes below as permissions:
 * files anybody may read and their owner may change, and folders anybody may open — with the folder
 * mark MS-DOS tools look for as well.
 */
const VERSION_MADE_BY = (3 << 8) | VERSION_NEEDED;
const FILE_ATTRIBUTES = (0o100644 << 16) >>> 0;
const FOLDER_ATTRIBUTES = ((0o040755 << 16) | 0x10) >>> 0;

/** Names are UTF-8. Every name is ASCII today; the flag keeps a later one from being misread. */
const UTF8_NAMES = 0x0800;

const STORED = 0;
const DEFLATED = 8;

const LOCAL_HEADER = 0x04034b50;
const CENTRAL_HEADER = 0x02014b50;
const END_OF_CENTRAL_DIRECTORY = 0x06054b50;

/** The fixed lengths of the three kinds of record, before the name each may carry. */
const LOCAL_HEADER_LENGTH = 30;
const CENTRAL_HEADER_LENGTH = 46;
const END_LENGTH = 22;

/** Field widths, in bytes. Every number in the format is little-endian and unsigned. */
const U16 = 2;
const U32 = 4;

const NOTHING = Buffer.alloc(0);

/** A reason not to package, in words. Anything else thrown from here is a defect in this script. */
export class PackagingError extends Error {
  name = 'PackagingError';
}

/**
 * Packages a plugin, refusing anything that would put the wrong files, or the wrong version, in
 * front of somebody installing it.
 *
 * @param {string} plugin The folder the plugin is kept in.
 * @param {string} downloads The dashboard module describing the download.
 * @returns {{ archive: Buffer, version: string }} The archive, already read back, and its version.
 */
export function packagePlugin(plugin, downloads) {
  checkNames(SHIPPED);
  checkShippedFiles(plugin);

  const version = agreedVersion(plugin, downloads);
  const entries = entriesOf(plugin);
  const archive = zip(entries);

  verify(archive, entries);

  return { archive, version };
}

/**
 * Reads back an archive this script wrote: every entry's name and contents in the order its central
 * directory lists them, each unpacked and checked against the length and checksum recorded for it.
 *
 * @param {Buffer} archive The archive.
 * @returns {Array<{ name: string, data: Buffer }>} Its entries.
 */
export function readArchive(archive) {
  const end = archive.length - END_LENGTH;

  if (end < 0 || archive.readUInt32LE(end) !== END_OF_CENTRAL_DIRECTORY) {
    throw new PackagingError('The archive does not end the way an archive ends.');
  }

  const count = archive.readUInt16LE(end + 10);
  const start = archive.readUInt32LE(end + 16);
  const entries = [];
  let at = start;

  for (let index = 0; index < count; index += 1) {
    const { entry, next } = readEntry(archive, at);

    entries.push(entry);
    at = next;
  }

  if (at !== end || start + archive.readUInt32LE(end + 12) !== end) {
    throw new PackagingError(`The archive's directory does not list exactly its ${count} entries.`);
  }

  return entries;
}

/**
 * Refuses a name that could be read as anything other than a file inside the plugin's own folder.
 *
 * @param {readonly string[]} names The names on the list, relative to the plugin.
 */
function checkNames(names) {
  const seen = new Set();

  for (const name of names) {
    const plain =
      !name.includes('..') &&
      name.split('/').every((segment) => /^[\w.-]+$/.test(segment) && segment !== '.');

    if (!plain) {
      throw new PackagingError(
        `"${name}" cannot name a file in the archive: names are relative to the plugin, separate ` +
          String.raw`folders with "/", and never contain "\" or "..".`,
      );
    }

    if (seen.has(name)) {
      throw new PackagingError(`"${name}" is listed twice.`);
    }

    seen.add(name);
  }
}

/**
 * Refuses a listed file the plugin does not have, and a file the plugin has where shipped files
 * live but which is not on the list.
 *
 * @param {string} plugin The folder the plugin is kept in.
 */
function checkShippedFiles(plugin) {
  for (const name of SHIPPED) {
    const found = lstatSync(locate(plugin, name), { throwIfNoEntry: false });

    if (found === undefined) {
      throw new PackagingError(`${name} is listed to ship, but the plugin has no such file.`);
    }

    if (!found.isFile()) {
      throw new PackagingError(`${name} is listed to ship, but it is not an ordinary file.`);
    }
  }

  const listed = new Set(SHIPPED);
  const unlisted = SHIPPED_ROOTS.flatMap((name) => filesUnder(plugin, name)).filter(
    (name) => !listed.has(name),
  );

  if (unlisted.length > 0) {
    throw new PackagingError(
      "Found where the plugin's shipped files live, but missing from SHIPPED, the list of what " +
        `it ships: ${unlisted.join(', ')}. Put each on the list, or move it out of there.`,
    );
  }
}

/**
 * Every file at or under one place in the plugin, named the way the list names them.
 *
 * @param {string} plugin The folder the plugin is kept in.
 * @param {string} name A place in it, relative to the plugin.
 * @returns {string[]} The files found, or none where nothing is there.
 */
function filesUnder(plugin, name) {
  const found = lstatSync(locate(plugin, name), { throwIfNoEntry: false });

  if (found === undefined) {
    return [];
  }

  if (!found.isDirectory()) {
    return [name];
  }

  return readdirSync(locate(plugin, name)).flatMap((child) =>
    filesUnder(plugin, `${name}/${child}`),
  );
}

/**
 * The version the plugin is released as, once the four places that state it are seen to agree.
 *
 * WordPress reads it from the main file's header, the plugin from its own constant, the plugin
 * directory from the readme's stable tag, and the dashboard names it on its download button. All
 * four are written by hand, so they are compared here, where a disagreement costs a build instead
 * of a release whose button names one version and installs another.
 *
 * @param {string} plugin The folder the plugin is kept in.
 * @param {string} downloads The dashboard module describing the download.
 * @returns {string} The version.
 */
function agreedVersion(plugin, downloads) {
  const main = readFileSync(locate(plugin, MAIN_FILE));
  const download = dashboardDownload(downloads);

  if (download.href !== ADDRESS) {
    throw new PackagingError(
      `The dashboard's download points at ${download.href ?? 'nothing'}, but the archive is ` +
        `served at ${ADDRESS}.`,
    );
  }

  const stated = [
    [`the Version header of ${MAIN_FILE}`, headerVersion(main)],
    [`DEWIRIDE_ANALYTICS_VERSION in ${MAIN_FILE}`, constantVersion(main)],
    [`the Stable tag of ${README}`, stableTag(readFileSync(locate(plugin, README), 'utf8'))],
    [`WORDPRESS_PLUGIN in ${path.basename(downloads)}`, download.version],
  ];

  const unstated = stated.filter(([, version]) => !version).map(([where]) => where);

  if (unstated.length > 0) {
    throw new PackagingError(`No version could be read from ${unstated.join(', ')}.`);
  }

  if (new Set(stated.map(([, version]) => version)).size > 1) {
    const each = stated.map(([where, version]) => `${version} in ${where}`);

    throw new PackagingError(
      `The plugin is given different versions in the places that state one: ${each.join(', ')}.`,
    );
  }

  return stated[0][1];
}

/**
 * The version in a plugin's header, read the way WordPress reads it: from the first 8 KiB of the
 * file, on the first line holding nothing before `Version:` but spaces and comment marks, up to
 * whatever closes the comment.
 *
 * @param {Buffer} main The plugin's main file.
 * @returns {string | undefined} The version, or nothing where the header names none.
 */
function headerVersion(main) {
  const field = 'version:';

  for (const line of main
    .subarray(0, 8192)
    .toString('utf8')
    .split(/\r\n|\r|\n/)) {
    const text = line.replace(/^\s*(?:<\?php)?/, '').replace(/^[\s/*#@]*/, '');

    if (text.toLowerCase().startsWith(field)) {
      return text
        .slice(field.length)
        .split(/\*\/|\?>/)[0]
        .trim();
    }
  }

  return undefined;
}

/**
 * The version the plugin's own constant holds.
 *
 * @param {Buffer} main The plugin's main file.
 * @returns {string | undefined} The version, or nothing where the constant is not defined.
 */
function constantVersion(main) {
  const definitions = [
    ...main.toString('utf8').matchAll(/\bdefine\(\s*'DEWIRIDE_ANALYTICS_VERSION'\s*,\s*'([^']*)'/g),
  ];

  if (definitions.length > 1) {
    throw new PackagingError(
      `${MAIN_FILE} defines DEWIRIDE_ANALYTICS_VERSION ${definitions.length} times.`,
    );
  }

  return definitions[0]?.[1];
}

/**
 * The stable tag in a readme, read as the plugin directory reads it: only from the fields between
 * the plugin's name and the first line that is not one, so a mention further down cannot stand in
 * for it.
 *
 * @param {string} readme The readme.
 * @returns {string | undefined} The tag, or nothing where the readme's fields name none.
 */
function stableTag(readme) {
  const lines = readme.split(/\r\n|\r|\n/).map((line) => line.trim());
  const fields = lines.slice(lines.findIndex((line) => line !== '') + 1);

  for (const line of fields.filter((field) => field !== '')) {
    const colon = line.indexOf(':');

    if (colon === -1 || line.startsWith('#') || line.startsWith('=')) {
      return undefined;
    }

    if (
      line
        .slice(0, colon)
        .replace(/^[*\s-]*/, '')
        .trim()
        .toLowerCase() === 'stable tag'
    ) {
      return line.slice(colon + 1).trim();
    }
  }

  return undefined;
}

/**
 * The address and version the dashboard's download button is given.
 *
 * @param {string} downloads The dashboard module describing the download.
 * @returns {{ href: string | undefined, version: string | undefined }} What it states.
 */
function dashboardDownload(downloads) {
  const source = readFileSync(downloads, 'utf8');
  const declaration = /\bWORDPRESS_PLUGIN\s*=\s*\{([^}]*)\}/.exec(source)?.[1] ?? '';

  return {
    href: /\bhref:\s*'([^']*)'/.exec(declaration)?.[1],
    version: /\bversion:\s*'([^']*)'/.exec(declaration)?.[1],
  };
}

/**
 * The archive's entries in order: each folder just before the first file inside it, then the file.
 *
 * @param {string} plugin The folder the plugin is kept in.
 * @returns {Array<{ name: string, data: Buffer | null }>} The entries; a folder has no contents.
 */
function entriesOf(plugin) {
  const entries = [];
  const folders = new Set();

  for (const name of SHIPPED) {
    const segments = name.split('/');

    for (let depth = 0; depth < segments.length; depth += 1) {
      const folder = `${[SLUG, ...segments.slice(0, depth)].join('/')}/`;

      if (!folders.has(folder)) {
        folders.add(folder);
        entries.push({ name: folder, data: null });
      }
    }

    entries.push({ name: `${SLUG}/${name}`, data: readFileSync(locate(plugin, name)) });
  }

  return entries;
}

/**
 * Writes the archive: each entry's header, name and contents, then the central directory listing
 * them, then the record saying where that directory is.
 *
 * @param {ReadonlyArray<{ name: string, data: Buffer | null }>} entries The entries, in order.
 * @returns {Buffer} The archive.
 */
function zip(entries) {
  const records = [];
  const directory = [];
  let offset = 0;

  for (const { name, data } of entries) {
    const folder = data === null;
    const encoded = Buffer.from(name, 'utf8');
    const contents = data ?? NOTHING;
    const body = folder ? NOTHING : deflateRawSync(contents, { level: 9 });

    // The fields a file's own header and its line in the directory have in common, in order.
    const described = [
      [U16, VERSION_NEEDED],
      [U16, UTF8_NAMES],
      [U16, folder ? STORED : DEFLATED],
      [U16, DOS_TIME],
      [U16, DOS_DATE],
      [U32, crc32(contents)],
      [U32, body.length],
      [U32, contents.length],
      [U16, encoded.length],
      [U16, 0], // no extra fields
    ];

    const header = record([[U32, LOCAL_HEADER], ...described]);

    records.push(header, encoded, body);
    directory.push(
      record([
        [U32, CENTRAL_HEADER],
        [U16, VERSION_MADE_BY],
        ...described,
        [U16, 0], // no comment
        [U16, 0], // on the first and only disk
        [U16, 0], // no internal attributes
        [U32, folder ? FOLDER_ATTRIBUTES : FILE_ATTRIBUTES],
        [U32, offset],
      ]),
      encoded,
    );

    offset += header.length + encoded.length + body.length;
  }

  const listing = Buffer.concat(directory);
  const end = record([
    [U32, END_OF_CENTRAL_DIRECTORY],
    [U16, 0], // this disk
    [U16, 0], // the disk the directory starts on
    [U16, entries.length],
    [U16, entries.length],
    [U32, listing.length],
    [U32, offset],
    [U16, 0], // no comment
  ]);

  return Buffer.concat([...records, listing, end]);
}

/**
 * One fixed-length record, from its fields in the order the format lays them out.
 *
 * @param {ReadonlyArray<readonly [number, number]>} fields Each field's width and value.
 * @returns {Buffer} The record.
 */
function record(fields) {
  const bytes = Buffer.alloc(fields.reduce((total, [width]) => total + width, 0));
  let at = 0;

  for (const [width, value] of fields) {
    if (width === U16) {
      bytes.writeUInt16LE(value, at);
    } else {
      bytes.writeUInt32LE(value, at);
    }

    at += width;
  }

  return bytes;
}

/**
 * Reads one entry through its line in the central directory, then its own header and contents.
 *
 * @param {Buffer} archive The archive.
 * @param {number} at Where the entry's line in the central directory begins.
 * @returns {{ entry: { name: string, data: Buffer }, next: number }} The entry, and where the next
 *   line begins.
 */
function readEntry(archive, at) {
  expectRecord(archive, at, CENTRAL_HEADER, CENTRAL_HEADER_LENGTH);

  const method = archive.readUInt16LE(at + 10);
  const checksum = archive.readUInt32LE(at + 16);
  const packed = archive.readUInt32LE(at + 20);
  const length = archive.readUInt32LE(at + 24);
  const nameLength = archive.readUInt16LE(at + 28);
  const trailing = archive.readUInt16LE(at + 30) + archive.readUInt16LE(at + 32);
  const local = archive.readUInt32LE(at + 42);
  const name = archive.toString(
    'utf8',
    at + CENTRAL_HEADER_LENGTH,
    at + CENTRAL_HEADER_LENGTH + nameLength,
  );

  expectRecord(archive, local, LOCAL_HEADER, LOCAL_HEADER_LENGTH);

  const localName = local + LOCAL_HEADER_LENGTH;
  const localNameLength = archive.readUInt16LE(local + 26);
  const begins = localName + localNameLength + archive.readUInt16LE(local + 28);
  const data = unpacked(name, method, archive.subarray(begins, begins + packed));

  if (
    archive.toString('utf8', localName, localName + localNameLength) !== name ||
    data.length !== length ||
    crc32(data) !== checksum
  ) {
    throw new PackagingError(`${name} does not read back as it was written.`);
  }

  return { entry: { name, data }, next: at + CENTRAL_HEADER_LENGTH + nameLength + trailing };
}

/**
 * One entry's contents as they were before they were packed.
 *
 * @param {string} name The entry, for saying which one cannot be unpacked.
 * @param {number} method How its contents were packed.
 * @param {Buffer} body Its contents as the archive holds them.
 * @returns {Buffer} Its contents.
 */
function unpacked(name, method, body) {
  if (method === STORED) {
    return body;
  }

  if (method !== DEFLATED) {
    throw new PackagingError(`${name} is packed in a way this script never writes.`);
  }

  try {
    return inflateRawSync(body);
  } catch (error) {
    throw new PackagingError(`${name} cannot be unpacked: ${error.message}`);
  }
}

/**
 * Refuses an archive where a record of a given kind was expected and something else is there.
 *
 * @param {Buffer} archive The archive.
 * @param {number} at Where the record should begin.
 * @param {number} signature The kind of record expected there.
 * @param {number} length The record's fixed length, all of which has to be inside the archive.
 */
function expectRecord(archive, at, signature, length) {
  if (at + length > archive.length || archive.readUInt32LE(at) !== signature) {
    throw new PackagingError('The archive has no record where its directory says one begins.');
  }
}

/**
 * Refuses an archive that does not read back as exactly the entries written into it.
 *
 * @param {Buffer} archive The archive.
 * @param {ReadonlyArray<{ name: string, data: Buffer | null }>} entries What was written into it.
 */
function verify(archive, entries) {
  const read = readArchive(archive);

  if (read.length !== entries.length) {
    throw new PackagingError(
      `The archive reads back with ${read.length} entries where ${entries.length} were written.`,
    );
  }

  const differs = entries.find(
    ({ name, data }, index) =>
      read[index].name !== name || !read[index].data.equals(data ?? NOTHING),
  );

  if (differs) {
    throw new PackagingError(`${differs.name} does not read back as it was written.`);
  }
}

/**
 * Where a file named the way the list names it is kept on this machine.
 *
 * @param {string} plugin The folder the plugin is kept in.
 * @param {string} name The file, relative to the plugin, with "/" between folders.
 * @returns {string} Its path.
 */
function locate(plugin, name) {
  return path.join(plugin, ...name.split('/'));
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const { archive, version } = packagePlugin(PLUGIN, DOWNLOADS);

    mkdirSync(SERVED, { recursive: true });
    writeFileSync(path.join(SERVED, ARCHIVE), archive);

    console.log(`${ARCHIVE}: version ${version}, ${SHIPPED.length} files, ${archive.length} bytes`);
  } catch (error) {
    if (!(error instanceof PackagingError)) {
      throw error;
    }

    console.error(error.message);
    process.exitCode = 1;
  }
}
