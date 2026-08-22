import { edition } from '@edition';
import { redirect } from 'next/navigation';
import { SETTINGS } from '@/lib/routes';

/**
 * What an account is entitled to, and how much of it has been used.
 *
 * The route is written once and publicly; what fills it comes from the compiled edition. An
 * installation somebody runs themselves measures whatever they point at it, so the open-source
 * edition offers nothing here and this address puts them back on the account rather than showing
 * them a screen about an allowance that does not exist.
 */
export default function PlanPage() {
  const Plan = edition.plan;

  if (!Plan) {
    redirect(SETTINGS);
  }

  return <Plan />;
}
