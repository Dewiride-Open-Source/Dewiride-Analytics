'use client';

import { useTranslations } from 'next-intl';
import { Notice } from '@/components/ui/notice';
import { ApiError } from '@/lib/api/problem';

interface FailureNoticeProps {
  readonly error: unknown;
  readonly className?: string;
}

/**
 * Turns whatever went wrong into something worth reading.
 *
 * Screens hand it the failure and nothing else. The mapping from a refusal to a sentence lives
 * here so that "we could not reach the engine" reads the same on every screen, and so that a
 * status code never reaches a person.
 */
export function FailureNotice({ error, className }: FailureNoticeProps) {
  const t = useTranslations('errors');
  const reasons = useTranslations('problems');

  if (error instanceof ApiError && error.unreachable) {
    return (
      <Notice title={t('unreachableTitle')} className={className}>
        {t('unreachableBody')}
      </Notice>
    );
  }

  if (error instanceof ApiError && error.throttled) {
    return (
      <Notice title={t('tooManyTitle')} className={className}>
        {t('tooManyBody')}
      </Notice>
    );
  }

  if (error instanceof ApiError && error.reasons.length > 0) {
    return (
      <Notice title={reasons('genericTitle')} className={className}>
        <ul className="flex list-disc flex-col gap-1 pl-4">
          {error.reasons.map((reason) => (
            <li key={reason.code}>
              {/*
                A reason we have written words for is shown in the reader's own language. One we
                have not is shown as the engine described it: an unfamiliar code is a gap in this
                catalogue, and hiding the only explanation behind a generic sentence would leave
                somebody unable to fix a password they are perfectly capable of fixing.
              */}
              {reasons.has(reason.code) ? reasons(reason.code) : reason.description}
            </li>
          ))}
        </ul>
      </Notice>
    );
  }

  return (
    <Notice title={t('unexpectedTitle')} className={className}>
      {t('unexpectedBody')}
    </Notice>
  );
}
