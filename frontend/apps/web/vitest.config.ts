import react from '@vitejs/plugin-react';

import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react()],
  // Vite resolves the '@/...' aliases from the TypeScript configuration itself, so there is no
  // second place where they have to be repeated.
  resolve: { tsconfigPaths: true },
  test: {
    environment: 'jsdom',
    // next-intl reaches for the framework's navigation helpers through an entry the framework
    // only publishes under conditions its own server sets. Processed here rather than left to the
    // runtime, the import resolves the same way it does in a build.
    server: { deps: { inline: [/next-intl/] } },
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    restoreMocks: true,
    coverage: {
      // The v8 provider rather than the instrumenting one: it is faster, and the defect that
      // produces unreadable branch records affects single-file components of another framework,
      // none of which exist here.
      provider: 'v8',
      reporter: ['text-summary', 'lcov'],
      reportsDirectory: './coverage',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/test/**'],
    },
  },
});
