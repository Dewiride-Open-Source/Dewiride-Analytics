/**
 * The checks a form makes before anything is sent.
 *
 * Each one answers with the name of a sentence in the message catalogue rather than the sentence
 * itself, so the rules stay in one place and the wording stays in another and neither has to know
 * about the other. Nothing here replaces the engine's own checks: this exists to save somebody a
 * round trip, not to be the authority on what is allowed.
 */

/** Names of the sentences in the `validation` catalogue. */
export type ValidationKey =
  | 'emailRequired'
  | 'emailInvalid'
  | 'passwordRequired'
  | 'passwordTooShort'
  | 'organisationRequired'
  | 'websiteRequired'
  | 'websiteInvalid'
  | 'timeZoneRequired';

/** Matches the shortest thing that could be an address, and rejects the obvious mistakes. */
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

const HOST_LABEL = /^[a-z0-9]([a-z0-9-]*[a-z0-9])?$/;
const TOP_LEVEL = /^[a-z]{2,63}$/;

const LONGEST_HOSTNAME = 253;
const LONGEST_LABEL = 63;

/**
 * The shortest password the engine accepts.
 *
 * Stated once here so the hint under the box and the check beside it cannot disagree. The engine
 * decides in the end, and it also refuses long passwords that are easy to guess — which is a
 * judgement this side has no way to make.
 */
export const SHORTEST_PASSWORD = 15;

export function checkEmail(value: string): ValidationKey | null {
  if (value.length === 0) {
    return 'emailRequired';
  }

  return EMAIL.test(value) ? null : 'emailInvalid';
}

export function checkPassword(value: string): ValidationKey | null {
  if (value.length === 0) {
    return 'passwordRequired';
  }

  return value.length < SHORTEST_PASSWORD ? 'passwordTooShort' : null;
}

export function checkPresent(value: string, whenMissing: ValidationKey): ValidationKey | null {
  return value.trim().length === 0 ? whenMissing : null;
}

/**
 * Checks a hostname the way the engine will read it: labels separated by dots, ending in letters.
 *
 * Written as a walk over the labels rather than as one expression, because the expression that
 * describes a hostname correctly is also the kind that can be made to run for a very long time on
 * a string somebody chose carefully.
 */
export function checkHostname(value: string): ValidationKey | null {
  if (value.length === 0) {
    return 'websiteRequired';
  }

  if (value.length > LONGEST_HOSTNAME) {
    return 'websiteInvalid';
  }

  const labels = value.split('.');
  const top = labels.at(-1) ?? '';

  if (labels.length < 2 || !TOP_LEVEL.test(top)) {
    return 'websiteInvalid';
  }

  const usable = labels.every(
    (label) => label.length > 0 && label.length <= LONGEST_LABEL && HOST_LABEL.test(label),
  );

  return usable ? null : 'websiteInvalid';
}

/**
 * Reduces whatever somebody pasted into the box to the hostname underneath it.
 *
 * People copy the address out of their browser, so what arrives is as likely to be
 * `https://example.com/blog/` as `example.com`. Accepting both and storing one is friendlier than
 * refusing the version they actually had to hand.
 */
export function tidyHostname(value: string): string {
  const trimmed = value.trim().toLowerCase();
  const withoutScheme = trimmed.replace(/^[a-z][a-z0-9+.-]*:\/\//, '');

  // The authority is taken before anything else is looked at, so an `@` further along the path
  // cannot be mistaken for the end of a username.
  const authority = withoutScheme.split(/[/?#]/)[0] ?? '';
  const host = authority.slice(authority.indexOf('@') + 1);

  return host.replace(/:\d+$/, '').replace(/\.+$/, '');
}
