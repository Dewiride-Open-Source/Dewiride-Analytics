// Lint rules for the whole workspace.
//
// Formatting is Biome's job, not ESLint's, so nothing here decides where a line breaks. What is
// configured is correctness: the rules of hooks, the accessibility rules, TypeScript's own, and
// the Next.js rules that catch mistakes which otherwise only show up as a slow page in production.
//
// The plugins are composed directly rather than through `eslint-config-next`, which is the second
// arrangement Next.js documents. It is used here because that bundle pins `eslint-plugin-react`,
// whose newest release calls an ESLint API removed in version 10 and throws while loading its
// first rule. Everything that bundle contributes and this codebase needs — hooks, accessibility,
// TypeScript, the Next.js rules — is installed here at a version that supports ESLint 10. What is
// left out is the part of `eslint-plugin-react` that predates TypeScript and checks prop shapes
// the compiler already checks.

import nextPlugin from '@next/eslint-plugin-next';
import { defineConfig, globalIgnores } from 'eslint/config';
import jsxAccessibility from 'eslint-plugin-jsx-a11y';
import reactHooks from 'eslint-plugin-react-hooks';
import typescript from 'typescript-eslint';

export default defineConfig([
  globalIgnores([
    '**/.next/**',
    '**/out/**',
    '**/build/**',
    '**/dist/**',
    '**/coverage/**',
    // Compiled output, served as it is. Linting it reports on the compiler's choices.
    'frontend/apps/web/public/**',
    '**/next-env.d.ts',
    // A separate repository with its own tooling, checked out here to build the Cloud
    // edition. Absent from an ordinary clone, so linting it would pass or fail by luck.
    'ee/**',
  ]),

  ...typescript.configs.recommended,
  reactHooks.configs.flat['recommended-latest'],
  jsxAccessibility.flatConfigs.recommended,

  {
    files: ['**/*.{js,jsx,mjs,ts,tsx}'],
    plugins: { '@next/next': nextPlugin },
    // The application does not sit at the root of the workspace, and without being told where it
    // is the Next.js rules look for it beside this file, fail to find it, and report that as a
    // warning on every run.
    settings: { next: { rootDir: 'frontend/apps/web/' } },
    rules: {
      ...nextPlugin.configs.recommended.rules,
      ...nextPlugin.configs['core-web-vitals'].rules,
    },
  },

  {
    files: ['**/*.{js,jsx,mjs,ts,tsx}'],
    rules: {
      // An unused symbol is dead code. Treated the way the engine treats it — an error, not a
      // hint to tidy up later. An underscore prefix is the one way to say "deliberately ignored",
      // which is the only case where an unused binding is honest.
      '@typescript-eslint/no-unused-vars': [
        'error',
        {
          argsIgnorePattern: '^_',
          varsIgnorePattern: '^_',
          caughtErrorsIgnorePattern: '^_',
        },
      ],
      '@typescript-eslint/consistent-type-imports': [
        'error',
        { prefer: 'type-imports', fixStyle: 'inline-type-imports' },
      ],
      'no-console': ['error', { allow: ['warn', 'error'] }],
      eqeqeq: ['error', 'always', { null: 'ignore' }],
    },
  },

  {
    // Command-line scripts exist to print something and set an exit code. Writing to the console
    // is their whole output, not a leftover from debugging.
    files: ['scripts/**/*.mjs', 'tracker/build.mjs'],
    rules: { 'no-console': 'off' },
  },
]);
