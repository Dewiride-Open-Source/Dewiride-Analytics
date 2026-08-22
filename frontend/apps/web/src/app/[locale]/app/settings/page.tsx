import { AccountSettings } from '@/components/settings/account-settings';

/**
 * The account itself: what it is called, and who is in it.
 *
 * Everybody who belongs to the account can read this. Changing any of it belongs to an owner,
 * which the screen settles from what the engine says about the person reading it rather than by
 * hiding a button and hoping.
 */
export default function AccountPage() {
  return <AccountSettings />;
}
