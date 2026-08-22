import { SignInForm } from '@/components/account/sign-in-form';

export default function SignInPage() {
  return (
    <div className="flex min-h-[calc(100dvh-4rem)] items-center justify-center px-4 py-10 sm:py-16">
      <SignInForm />
    </div>
  );
}
