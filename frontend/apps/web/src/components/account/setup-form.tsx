'use client';

import { useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, PasswordInput, SelectInput, TextInput } from '@/components/ui/field';
import { Notice } from '@/components/ui/notice';
import { Link } from '@/i18n/navigation';
import { ApiError } from '@/lib/api/problem';
import {
  checkEmail,
  checkHostname,
  checkPassword,
  checkPresent,
  tidyHostname,
  type ValidationKey,
} from '@/lib/forms/validation';
import { useClaimInstall } from '@/lib/queries/session';
import { SIGN_IN } from '@/lib/routes';
import { thisDeviceTimeZone, timeZoneGroups } from '@/lib/time-zones';

interface Refusals {
  readonly emailAddress?: ValidationKey;
  readonly password?: ValidationKey;
  readonly organizationName?: ValidationKey;
  readonly siteDomain?: ValidationKey;
  readonly timeZoneId?: ValidationKey;
}

/**
 * The one and only time this installation is claimed.
 *
 * Everything needed to make the product usable is asked for in a single pass — an owner, an
 * organisation, and the first website — because a person who has just started a server wants one
 * screen, not a sequence of them. The engine treats the whole thing as a single act: if any part
 * of it is refused, nothing is created.
 */
export function SetupForm() {
  const t = useTranslations('setup');
  const validation = useTranslations('validation');
  const claim = useClaimInstall();
  const [refusals, setRefusals] = useState<Refusals>({});
  const [zones] = useState(timeZoneGroups);
  const [defaultZone] = useState(() => thisDeviceTimeZone(zones));

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const entered = new FormData(event.currentTarget);
    const emailAddress = String(entered.get('emailAddress') ?? '').trim();
    const password = String(entered.get('password') ?? '');
    const displayName = String(entered.get('displayName') ?? '').trim();
    const organizationName = String(entered.get('organizationName') ?? '').trim();
    const siteDomain = tidyHostname(String(entered.get('siteDomain') ?? ''));
    const timeZoneId = String(entered.get('timeZoneId') ?? '');

    const found: Refusals = {
      emailAddress: checkEmail(emailAddress) ?? undefined,
      password: checkPassword(password) ?? undefined,
      organizationName: checkPresent(organizationName, 'organisationRequired') ?? undefined,
      siteDomain: checkHostname(siteDomain) ?? undefined,
      timeZoneId: checkPresent(timeZoneId, 'timeZoneRequired') ?? undefined,
    };

    setRefusals(found);

    if (Object.values(found).some(Boolean)) {
      return;
    }

    claim.mutate({
      emailAddress,
      password,
      displayName: displayName.length > 0 ? displayName : null,
      organizationName,
      siteDomain,
      timeZoneId,
    });
  }

  const say = (refusal: ValidationKey | undefined) => (refusal ? validation(refusal) : undefined);

  // Somebody else claimed this installation between the screen opening and the form being sent.
  // Rare, but the only honest answer is to say so and point at the way in.
  const alreadyClaimed = claim.error instanceof ApiError && claim.error.alreadyDone;

  return (
    <Card focal className="w-full max-w-xl p-6 sm:p-8">
      <header className="mb-7 flex flex-col gap-1.5">
        <h1 className="text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
          {t('title')}
        </h1>
        <p className="text-sm text-foreground-muted">{t('subtitle')}</p>
      </header>

      {alreadyClaimed ? (
        <Notice tone="information" title={t('claimed.title')} className="mb-5">
          <p className="mb-3">{t('claimed.body')}</p>
          <Link
            href={SIGN_IN}
            className="font-medium text-accent-strong underline-offset-4 hover:underline"
          >
            {t('claimed.action')}
          </Link>
        </Notice>
      ) : claim.isError ? (
        <FailureNotice error={claim.error} className="mb-5" />
      ) : null}

      <form onSubmit={submit} noValidate className="flex flex-col gap-7">
        <Section title={t('accountSection')}>
          <Field label={t('name.label')} optionalLabel={t('name.optional')}>
            {(attributes) => (
              <TextInput
                {...attributes}
                name="displayName"
                autoComplete="name"
                placeholder={t('name.placeholder')}
              />
            )}
          </Field>

          <Field label={t('email.label')} problem={say(refusals.emailAddress)}>
            {(attributes) => (
              <TextInput
                {...attributes}
                name="emailAddress"
                type="email"
                inputMode="email"
                autoComplete="username"
                placeholder={t('email.placeholder')}
                required
              />
            )}
          </Field>

          <Field
            label={t('password.label')}
            hint={t('password.hint')}
            problem={say(refusals.password)}
          >
            {(attributes) => (
              <PasswordInput
                {...attributes}
                name="password"
                autoComplete="new-password"
                showLabel={t('password.show')}
                hideLabel={t('password.hide')}
                required
              />
            )}
          </Field>

          <Field
            label={t('organisation.label')}
            hint={t('organisation.hint')}
            problem={say(refusals.organizationName)}
          >
            {(attributes) => (
              <TextInput
                {...attributes}
                name="organizationName"
                autoComplete="organization"
                placeholder={t('organisation.placeholder')}
                required
              />
            )}
          </Field>
        </Section>

        <Section title={t('siteSection')}>
          <Field label={t('website.label')} problem={say(refusals.siteDomain)}>
            {(attributes) => (
              <TextInput
                {...attributes}
                name="siteDomain"
                inputMode="url"
                placeholder={t('website.placeholder')}
                required
              />
            )}
          </Field>

          <Field label={t('timeZone.label')} problem={say(refusals.timeZoneId)}>
            {(attributes) => (
              <SelectInput {...attributes} name="timeZoneId" defaultValue={defaultZone} required>
                {zones.map((group) => (
                  <optgroup key={group.area} label={group.area}>
                    {group.zones.map((zone) => (
                      <option key={zone.id} value={zone.id}>
                        {zone.label}
                      </option>
                    ))}
                  </optgroup>
                ))}
              </SelectInput>
            )}
          </Field>
        </Section>

        <Button type="submit" size="lg" block busy={claim.isPending}>
          {claim.isPending ? t('submitting') : t('submit')}
        </Button>
      </form>
    </Card>
  );
}

/**
 * One group of related questions, separated so the form reads as two short asks, not eight.
 *
 * The heading is set apart from the labels beneath it rather than merely sitting above them: at
 * the same weight and size, "Your account" and "Your name" read as two questions in a row, and
 * the grouping that makes eight boxes feel like two steps disappears entirely.
 */
function Section({
  title,
  children,
}: {
  readonly title: string;
  readonly children: React.ReactNode;
}) {
  return (
    <fieldset className="flex flex-col gap-5 border-0 p-0">
      <legend className="mb-4 w-full border-b border-border pb-2 text-xs font-semibold tracking-widest text-foreground-muted uppercase">
        {title}
      </legend>
      {children}
    </fieldset>
  );
}
