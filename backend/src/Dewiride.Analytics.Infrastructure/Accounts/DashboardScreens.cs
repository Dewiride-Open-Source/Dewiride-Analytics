namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// The screens this product sends somebody to, named once.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard keeps the same list in <c>frontend/apps/web/src/lib/routes.ts</c> and is the
/// authority on it: a screen exists because it has a file and an entry in <c>SCREENS</c>. What is
/// here is the engine's half of the same fact, and the two halves are held together by a test
/// rather than by anybody remembering to change both.
/// </para>
/// <para>
/// Named once because a link to a screen that has moved fails in the quietest way this product
/// has. The address is well formed, the proxy answers it, and the person who followed it is shown
/// a page saying there is nothing there. Nothing is logged, because from the engine's side nothing
/// went wrong. Moving the plan screen under the account settings left three copies of its old path
/// behind — in the address somebody is returned to after paying, the one after cancelling, and the
/// one in every message about a failing card — and all three were found by a customer buying a
/// plan and landing nowhere.
/// </para>
/// <para>
/// No leading slash on any of them: <see cref="AccountLinks"/> resolves each against the address
/// the installation is published on, and a leading slash would discard that address's own path.
/// </para>
/// </remarks>
public static class DashboardScreens
{
    /// <summary>Where somebody signs in.</summary>
    public const string SignIn = "app/sign-in";

    /// <summary>Where somebody creates an account, and where a confirmation link lands.</summary>
    public const string SignUp = "app/sign-up";

    /// <summary>Where somebody asks for a way back into an account.</summary>
    public const string ForgotPassword = "app/forgot-password";

    /// <summary>Where a link for setting a new password lands.</summary>
    public const string ResetPassword = "app/reset-password";

    /// <summary>Where an invitation to an account lands.</summary>
    public const string Join = "app/join";

    /// <summary>Where an account's plan, its allowance and what it has used are shown.</summary>
    public const string Plan = "app/settings/plan";

    /// <summary>
    /// Every screen named here.
    /// </summary>
    /// <remarks>
    /// For the test that holds this list to the dashboard's own. A screen added above and left out
    /// of this is a screen the test cannot check, which is the failure it exists to prevent.
    /// </remarks>
    public static readonly IReadOnlyList<string> All =
        [SignIn, SignUp, ForgotPassword, ResetPassword, Join, Plan];
}
