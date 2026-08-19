'use client';

import { useTranslations } from 'next-intl';
import { Dialog } from '@/components/ui/dialog';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Switch } from '@/components/ui/switch';
import { useSiteSettings, useUpdateSiteSettings } from '@/lib/queries/site-settings';

interface SiteSettingsProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly siteId: string;
  readonly siteDomain: string;
}

/** What somebody chooses to have measured on their own website. */
export function SiteSettings({ open, onClose, siteId, siteDomain }: SiteSettingsProps) {
  const t = useTranslations('siteSettings');
  const settings = useSiteSettings(siteId, open);
  const saving = useUpdateSiteSettings(siteId);

  /**
   * Saves without producing a promise nobody is waiting on.
   *
   * A refusal is already on the screen through the notice below, so awaiting the attempt here
   * would leave a rejection with nothing to catch it.
   */
  function set(captureClicks: boolean) {
    saving.mutate({ captureClicks });
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={t('title')}
      closeLabel={t('close')}
      className="max-w-lg"
    >
      <div className="flex flex-col gap-5">
        <p className="text-sm text-foreground-muted">{t('body', { site: siteDomain })}</p>

        {settings.isError ? <FailureNotice error={settings.error} /> : null}
        {saving.isError ? <FailureNotice error={saving.error} /> : null}

        {settings.data ? (
          <Switch
            label={t('presses.label')}
            hint={t('presses.hint')}
            checked={settings.data.captureClicks}
            busy={saving.isPending}
            onChange={set}
          />
        ) : (
          <div className="h-20 animate-pulse rounded-lg bg-surface-muted" />
        )}
      </div>
    </Dialog>
  );
}
