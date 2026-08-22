/**
 * Where the website in front of the product answers, if there is one.
 *
 * The product deliberately occupies one segment of its address and leaves the root free, so that a
 * deployment may put something else there. On the hosted service that something is the public
 * website; on an installation somebody runs themselves there is usually nothing, and the root goes
 * on leading to the dashboard as it always has.
 *
 * Unlike the engine's address there is **no error when this is unset**, and that is the whole
 * difference between the two settings. A dashboard with no engine cannot answer a single screen; a
 * dashboard with nothing in front of it is the ordinary case. So this answers nothing rather than
 * throwing, and the proxy treats nothing as "there is no website here".
 *
 * Read at the moment a request is forwarded rather than when the dashboard is built, for the same
 * reason the engine's is: one built image is pointed at whatever it is deployed beside.
 */

const VARIABLE = 'DEWIRIDE_SITE_ORIGIN';

export function siteOrigin(): string | null {
  const configured = process.env[VARIABLE]?.trim();

  return configured ? configured.replace(/\/+$/, '') : null;
}
