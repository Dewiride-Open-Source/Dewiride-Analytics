'use client';

import { edition } from '@edition';
import { useSession } from '@/lib/queries/session';

/**
 * A place above every screen for the one thing an account has to be told first.
 *
 * The slot is public and the thing that fills it is not. An installation somebody runs themselves
 * has nothing to say here — there is no allowance to run out of — so the open-source edition
 * contributes nothing and this renders nothing at all rather than an empty strip that pushes every
 * screen down by the height of a message nobody wrote.
 *
 * Nothing either for somebody whose only account is closed. Whatever the edition would say is
 * about an account they cannot open, and the one screen they can see already says everything
 * there is to say about it.
 */
export function EditionNotice() {
  const Notice = edition.notice;
  const session = useSession();
  const closure = session.data?.closure ?? null;
  const walled = closure !== null && !closure.hasOpenAccount;

  return Notice && !walled ? <Notice /> : null;
}
