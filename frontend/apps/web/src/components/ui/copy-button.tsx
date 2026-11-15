'use client';

import { Check, Copy } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';

/** How long the button keeps saying it worked. */
const CONFIRMATION_MS = 2000;

interface CopyButtonProps {
  /** What is put on the clipboard. */
  readonly value: string;
  /**
   * The name of what is copied, where a panel holds more than one thing to copy.
   *
   * Heard rather than printed. Beside the value it belongs to, "Copy" says enough to the eye, but
   * a panel whose buttons are read out one after another as "Copy, Copy" says nothing at all.
   */
  readonly what?: string;
  readonly className?: string;
}

/**
 * Puts one value on the clipboard, and says so for a moment.
 *
 * A refused copy says nothing. Browsers refuse on an address they do not consider secure, and
 * whatever this copies is on the screen beside it and can be selected by hand, so there is still a
 * way through and nothing worth interrupting anybody for.
 *
 * The confirmation is also written where a screen reader announces it. The button's own words
 * change as well, but not every screen reader reads out a change to the control it is already on.
 */
export function CopyButton({ value, what, className }: CopyButtonProps) {
  const t = useTranslations('copy');
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) {
      return;
    }

    const timer = setTimeout(() => setCopied(false), CONFIRMATION_MS);

    return () => clearTimeout(timer);
  }, [copied]);

  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
    } catch {
      // Refused. The value is on the screen and selectable, so there is nothing to announce.
      setCopied(false);
    }
  }

  const shown = copied ? t('done') : t('action');
  const heard = spokenName(what, copied, t);

  return (
    <>
      <Button tone="secondary" size="sm" onClick={copy} aria-label={heard} className={className}>
        {copied ? (
          <Check aria-hidden className="size-4 text-positive" />
        ) : (
          <Copy aria-hidden className="size-4" />
        )}
        {shown}
      </Button>
      <span role="status" className="sr-only">
        {copied ? (heard ?? shown) : null}
      </span>
    </>
  );
}

/**
 * What the button is called out loud, where that is more than the words on it.
 *
 * Nothing where it copies the only thing in sight, so its name is simply what it says. Otherwise
 * the words on the button are kept inside what is heard, so that somebody who works a screen by
 * speaking the name of what they can see is still understood.
 */
function spokenName(
  what: string | undefined,
  copied: boolean,
  t: ReturnType<typeof useTranslations<'copy'>>,
): string | undefined {
  if (what === undefined) {
    return undefined;
  }

  return copied ? t('doneNamed', { what }) : t('actionNamed', { what });
}
