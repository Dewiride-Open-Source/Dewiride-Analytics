import { ClosedAccount } from '@/components/account/closed-account';

/**
 * The screen somebody lands on when the account they belong to has been closed.
 *
 * Drawn the way the doors are — one card in the middle of an otherwise empty screen — because it
 * is the same kind of moment: there is one thing to take in, and at most two things to do.
 */
export default function ClosedPage() {
  return (
    <div className="flex flex-1 items-center justify-center px-4 py-10 sm:py-16">
      <ClosedAccount />
    </div>
  );
}
