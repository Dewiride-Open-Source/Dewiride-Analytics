import { getTranslations } from 'next-intl/server';
import { Suspense } from 'react';
import { ResetPasswordForm } from '@/components/account/reset-password-form';
import { Waiting } from '@/components/ui/waiting';

/**
 * Choosing a new password from an emailed link.
 *
 * The form reads the link's two values out of the address, which the framework will only hand over
 * once it knows what was actually asked for — so the screen is drawn around a boundary rather than
 * waiting as a whole for something the rest of it does not need.
 */
export default async function ResetPasswordPage() {
  const t = await getTranslations('resetPassword');

  return (
    <div className="flex min-h-[calc(100dvh-4rem)] items-center justify-center px-4 py-10 sm:py-16">
      <Suspense fallback={<Waiting label={t('loading')} />}>
        <ResetPasswordForm />
      </Suspense>
    </div>
  );
}
