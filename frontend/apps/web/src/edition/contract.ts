import type { ComponentType } from 'react';

/**
 * The seam the two editions of the dashboard meet on.
 *
 * It mirrors the one the engine has. Which edition a build is comes from which module the bundler
 * resolved `@edition` to, decided once at build time — never from a flag read while the product is
 * running, and never from a branch in a component. Neither edition's screens are in the other's
 * bundle, which is the whole point: one of them is not free software.
 *
 * A screen an edition does not offer is `null` rather than absent, so the route that would show it
 * can be written once, publicly, and send somebody somewhere sensible instead.
 */
export interface EditionModule {
  /** Which edition this is. Nothing branches on it; a test reads it to prove the seam resolved. */
  readonly name: EditionName;

  /**
   * The screen somebody uses to create an account of their own.
   *
   * Nothing in the open-source edition: an installation is claimed once by whoever set it up, and
   * everybody else is added by them. Self-service sign-up only means anything where somebody else
   * is running the service.
   */
  readonly signUp: ComponentType | null;

  /**
   * The screen showing what an account is entitled to and how much of it has been used.
   *
   * Nothing in the open-source edition. A self-hosted installation measures whatever its owner
   * points at it, so there is no allowance to show and nothing that could run out.
   */
  readonly plan: ComponentType | null;

  /**
   * A strip above every screen, for the one thing an account needs to be told before it reads
   * anything else.
   *
   * Rendered when the edition has something to say and absent otherwise, which is almost always.
   * It decides for itself: a component that appears on every screen must be able to return to
   * nothing without the screens knowing about it.
   */
  readonly notice: ComponentType | null;

  /**
   * Screens this edition adds inside the account.
   *
   * Appended after the product's own, so the ones the whole product has stay first and in the
   * order they are written. Inside the account rather than in the bar across the top: what an
   * edition adds here is something somebody settles once and comes back to occasionally, and the
   * bar has room for the screens people open every day.
   */
  readonly settingsSections: readonly EditionSection[];

  /**
   * Wording for this edition's own screens, by language.
   *
   * Kept apart from the product's catalogue rather than mixed into it, so that the open-source
   * repository does not carry the copy for screens it does not have — and so that adding a language
   * to the commercial edition stays a translation job in the repository that owns those screens.
   * The open-source edition contributes none, because it contributes no screens.
   */
  readonly messages: Readonly<Record<string, Record<string, unknown>>>;
}

/** One screen an edition adds inside the account. */
export interface EditionSection {
  /** The address it lives at, which is a route the open-source repository already declares. */
  readonly path: string;

  /** Key into the merged catalogue for what the bar calls it. */
  readonly label: string;
}

/** The two editions this product is built as. */
export type EditionName = 'community' | 'cloud';
