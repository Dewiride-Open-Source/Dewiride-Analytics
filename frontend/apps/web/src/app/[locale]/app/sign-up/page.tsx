import { edition } from '@edition';
import { redirect } from 'next/navigation';
import { SIGN_IN } from '@/lib/routes';

/**
 * Creating an account of your own, where the edition running offers that.
 *
 * The route is written once and publicly; what fills it comes from the compiled edition. The
 * open-source edition offers nothing here — an installation is claimed by whoever set it up and
 * everybody else is added by them — so this address puts somebody on the sign-in screen instead of
 * showing them a dead end.
 */
export default function SignUpPage() {
  const SignUp = edition.signUp;

  if (!SignUp) {
    redirect(SIGN_IN);
  }

  return (
    <div className="flex min-h-[calc(100dvh-4rem)] items-center justify-center px-4 py-10 sm:py-16">
      <SignUp />
    </div>
  );
}
