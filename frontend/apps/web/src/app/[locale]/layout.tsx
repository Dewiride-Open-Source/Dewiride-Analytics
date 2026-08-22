import type { Metadata } from 'next';
import { Inter } from 'next/font/google';
import { notFound } from 'next/navigation';
import { hasLocale, NextIntlClientProvider } from 'next-intl';
import { getTranslations } from 'next-intl/server';
import type { ReactNode } from 'react';
import { AppHeader } from '@/components/chrome/app-header';
import { EditionNotice } from '@/components/chrome/edition-notice';
import { SessionGate } from '@/components/chrome/session-gate';
import { Providers } from '@/components/providers';
import { routing } from '@/i18n/routing';
import '../globals.css';

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-inter',
  display: 'swap',
});

export function generateStaticParams() {
  return routing.locales.map((locale) => ({ locale }));
}

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('app');

  return {
    title: t('name'),
    description: t('description'),
    // Nothing here is meant to be found by a search engine: every screen is behind a sign-in, and
    // an install reachable from the internet should not be advertising its own address.
    robots: { index: false, follow: false },
  };
}

/**
 * The frame every screen is drawn in.
 *
 * There is no footer, and that is a decision rather than an omission: these screens are an
 * application, not a website, and a strip of links under a sign-in form is one more thing between
 * somebody and what they came to do.
 */
export default async function LocaleLayout({
  children,
  params,
}: {
  readonly children: ReactNode;
  readonly params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;

  if (!hasLocale(routing.locales, locale)) {
    notFound();
  }

  return (
    <html lang={locale} className={inter.variable} suppressHydrationWarning>
      <body>
        <NextIntlClientProvider>
          <Providers>
            <div className="flex min-h-dvh flex-col">
              <AppHeader />
              <EditionNotice />
              <main className="flex-1">
                <SessionGate>{children}</SessionGate>
              </main>
            </div>
          </Providers>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
