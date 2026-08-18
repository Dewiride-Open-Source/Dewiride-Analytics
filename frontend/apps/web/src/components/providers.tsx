'use client';

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ThemeProvider } from 'next-themes';
import { type ReactNode, useState } from 'react';

/**
 * The two things every screen needs: somewhere to keep answers, and the chosen appearance.
 *
 * The cache is built inside state rather than at module level so that each visitor gets their
 * own. A cache shared by every request on the server would hand one person's numbers to the next.
 */
export function Providers({ children }: { readonly children: ReactNode }) {
  const [cache] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            // Nothing here is retried automatically. Every question this dashboard asks is either
            // answered at once or worth telling somebody about, and silent retries only delay the
            // moment they find out the engine is not running.
            retry: false,
            refetchOnWindowFocus: false,
            staleTime: 30_000,
          },
        },
      }),
  );

  return (
    <QueryClientProvider client={cache}>
      <ThemeProvider attribute="class" defaultTheme="system" enableSystem disableTransitionOnChange>
        {children}
      </ThemeProvider>
    </QueryClientProvider>
  );
}
