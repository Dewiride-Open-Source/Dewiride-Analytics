'use client';

import { Monitor, Moon, Sun } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useTheme } from 'next-themes';
import { useHydrated } from '@/lib/hydrated';
import { cn } from '@/lib/styling';

const CHOICES = ['light', 'dark', 'system'] as const;

type Choice = (typeof CHOICES)[number];

const GLYPHS = { light: Sun, dark: Moon, system: Monitor } as const;

/**
 * Chooses between the light look, the dark look, and following the device.
 *
 * Rendered as a group of three rather than a single toggle because "follow my device" is a real
 * third answer, and a two-state switch has nowhere to put it.
 *
 * Nothing is drawn until the component is running in the browser: the chosen theme is only known
 * there, and marking the wrong option as selected during the first paint is both wrong and, for
 * anyone listening rather than looking, wrong out loud.
 */
export function ThemeSwitch() {
  const t = useTranslations('theme');
  const { theme, setTheme } = useTheme();
  const ready = useHydrated();

  if (!ready) {
    return <div className="h-9 w-[6.75rem]" aria-hidden />;
  }

  const current: Choice = CHOICES.find((choice) => choice === theme) ?? 'system';

  return (
    <div
      role="radiogroup"
      aria-label={t('label')}
      className="flex items-center gap-0.5 rounded-md border border-border bg-surface p-0.5"
    >
      {CHOICES.map((choice) => {
        const Glyph = GLYPHS[choice];
        const selected = choice === current;

        return (
          <button
            key={choice}
            type="button"
            role="radio"
            aria-checked={selected}
            aria-label={t(choice)}
            title={t(choice)}
            onClick={() => setTheme(choice)}
            className={cn(
              'grid size-8 place-items-center rounded-sm transition-colors',
              selected
                ? 'bg-accent-soft text-accent-strong'
                : 'text-foreground-subtle hover:text-foreground',
            )}
          >
            <Glyph aria-hidden className="size-4" />
          </button>
        );
      })}
    </div>
  );
}
