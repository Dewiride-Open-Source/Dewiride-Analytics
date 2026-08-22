import type { ReactNode } from 'react';
import { SettingsNav } from '@/components/settings/settings-nav';

/**
 * The frame every screen inside the account is drawn in.
 *
 * It carries the way between them and nothing else. Each screen writes its own heading, because
 * what it is about — the account, you, what you are paying for — is not something one shared title
 * could say honestly.
 */
export default function SettingsLayout({ children }: { readonly children: ReactNode }) {
  return (
    <div className="mx-auto flex w-full max-w-4xl flex-col gap-6 px-4 py-8 sm:px-6 sm:py-10">
      <SettingsNav />
      {children}
    </div>
  );
}
