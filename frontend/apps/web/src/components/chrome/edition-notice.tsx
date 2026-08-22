'use client';

import { edition } from '@edition';

/**
 * A place above every screen for the one thing an account has to be told first.
 *
 * The slot is public and the thing that fills it is not. An installation somebody runs themselves
 * has nothing to say here — there is no allowance to run out of — so the open-source edition
 * contributes nothing and this renders nothing at all rather than an empty strip that pushes every
 * screen down by the height of a message nobody wrote.
 */
export function EditionNotice() {
  const Notice = edition.notice;

  return Notice ? <Notice /> : null;
}
