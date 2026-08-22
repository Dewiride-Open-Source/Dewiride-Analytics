import type { EditionModule } from '@/edition/contract';

/**
 * The open-source edition's contributions to the dashboard.
 *
 * There is nothing here, and that is the edition's answer rather than a gap in it. There is no
 * sign-up screen because a self-hosted installation is claimed once by the person who put it
 * there and everybody else is added by them — a form that let anyone passing create an account on
 * somebody's own server would be a way in, not a feature. There is no plan screen and no notice
 * above the screens because an installation somebody runs themselves measures whatever they point
 * at it: there is no allowance to show them, and nothing that could run out.
 */
export const edition: EditionModule = {
  name: 'community',
  signUp: null,
  plan: null,
  notice: null,
  settingsSections: [],
  messages: {},
};
