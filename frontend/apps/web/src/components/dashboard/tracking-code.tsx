'use client';

import { Check, Copy } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useEffect, useMemo, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { cn } from '@/lib/styling';
import { trackingSnippet } from '@/lib/tracker/snippet';

interface TrackingCodeProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly siteId: string;
  readonly siteDomain: string;
}

/** How long the button keeps saying it worked. */
const CONFIRMATION_MS = 2000;

/** What somebody pastes into their website, ready to be taken away. */
export function TrackingCode({ open, onClose, siteId, siteDomain }: TrackingCodeProps) {
  const t = useTranslations('install');
  const [copied, setCopied] = useState(false);

  // The dashboard's own address, which is the one address a reader is certain can be reached from
  // outside — the engine may not be published at all.
  const snippet = useMemo(
    () => (typeof window === 'undefined' ? '' : trackingSnippet(window.location.origin, siteId)),
    [siteId],
  );

  useEffect(() => {
    if (!copied) {
      return;
    }

    const timer = setTimeout(() => setCopied(false), CONFIRMATION_MS);

    return () => clearTimeout(timer);
  }, [copied]);

  async function copy() {
    try {
      await navigator.clipboard.writeText(snippet);
      setCopied(true);
    } catch {
      // Refused, which happens when the page is not on a secure address. The code is on the
      // screen and selectable, so there is still a way through and nothing to announce.
      setCopied(false);
    }
  }

  return (
    <Dialog open={open} onClose={onClose} title={t('title')} closeLabel={t('close')}>
      <div className="flex flex-col gap-4">
        <p className="text-sm text-foreground-muted">{t('body', { site: siteDomain })}</p>

        {/*
          Wrapped rather than scrolled sideways, and the button sits below rather than over it.
          These are lines somebody checks before pasting them into their own website, and a copy
          button laid across the end of one hides the very part they are checking.
        */}
        <pre
          className={cn(
            'max-h-64 overflow-y-auto rounded-lg border border-border bg-surface-muted p-4',
            'text-xs leading-relaxed whitespace-pre-wrap text-foreground wrap-anywhere',
          )}
        >
          <code>{snippet}</code>
        </pre>

        <div className="flex justify-end">
          <Button tone="secondary" size="sm" onClick={copy} className="w-full sm:w-auto">
            {copied ? (
              <Check aria-hidden className="size-4 text-positive" />
            ) : (
              <Copy aria-hidden className="size-4" />
            )}
            {copied ? t('copied') : t('copy')}
          </Button>
        </div>

        <p className="text-sm text-foreground-muted">{t('next')}</p>
      </div>
    </Dialog>
  );
}
