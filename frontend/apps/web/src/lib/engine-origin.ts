/**
 * Where the engine answers.
 *
 * Read at the moment a request is forwarded rather than when the dashboard is built, so one built
 * image can be pointed at whichever engine it is deployed beside. Reading it at build time would
 * bake an address into the image and make the setting a lie.
 *
 * There is no production default. A value that only works because somebody's machine is arranged
 * a certain way is the kind of setting that fails once, silently, in the one place it matters.
 * Development is allowed the obvious local address so that a fresh clone runs.
 */

const VARIABLE = 'DEWIRIDE_API_ORIGIN';
const WHILE_DEVELOPING = 'http://localhost:8080';

export function engineOrigin(): string {
  const configured = process.env[VARIABLE];

  if (configured) {
    return configured.replace(/\/+$/, '');
  }

  if (process.env.NODE_ENV === 'production') {
    throw new Error(
      `${VARIABLE} is not set. It must name the address the Dewiride Analytics engine answers ` +
        'on, for example http://api:8080.',
    );
  }

  return WHILE_DEVELOPING;
}
