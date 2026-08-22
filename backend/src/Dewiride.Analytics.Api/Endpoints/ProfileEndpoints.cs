using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Extensibility;
using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// The two things somebody may change about their own account.
/// </summary>
/// <remarks>
/// Nothing here names an account. It is always the caller's own, so these cannot be pointed at
/// somebody else's by changing an identifier.
/// </remarks>
internal static class ProfileEndpoints
{
    /// <summary>Names the reason a name is not one an account can be shown under.</summary>
    public const string NameRejectedCode = "AccountNameRejected";

    /// <summary>Names the reason a password change was refused before it began.</summary>
    public const string CurrentPasswordWrongCode = "CurrentPasswordWrong";

    /// <summary>
    /// Maps changing your own name and your own password.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapProfile(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPatch("/api/account", RenameAsync)
            .WithName("RenameAccount")
            .WithSummary("Changes the name the caller is shown under.")
            .RequireProofOfOrigin();

        routes.MapPut("/api/account/password", ChangePasswordAsync)
            .WithName("ChangePassword")
            .WithSummary("Replaces the caller's password.")
            .WithDescription(
                "The current password is required. Every other sign-in the account has open stops "
                + "working within a few minutes, and the one making the change is renewed.")
            .RequireRateLimiting(RateLimitPolicies.Accounts)
            .RequireProofOfOrigin();
    }

    private static async Task<Results<Ok<SignedInUser>, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> RenameAsync(
        RenameAccountRequest? request,
        ICurrentPrincipalAccessor caller,
        IAccountProfile profile,
        UserManager<ApplicationUser> accounts,
        CancellationToken cancellationToken)
    {
        var userId = caller.GetUserId();

        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var outcome = await profile
            .RenameAsync(userId.Value, request?.DisplayName ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Status == ProfileStatus.NameRejected)
        {
            // The store's own reasons where it had any, and this product's sentence where the name
            // was simply not one an account can be shown under.
            return outcome.Problems.Count > 0 ? Unusable(outcome.Problems) : NameRejected();
        }

        if (outcome.Status != ProfileStatus.Changed)
        {
            return TypedResults.NotFound();
        }

        var described = SignedInUsers.Describe(
            await accounts.FindByIdAsync(userId.Value.ToString()).ConfigureAwait(false));

        return described is null ? TypedResults.NotFound() : TypedResults.Ok(described);
    }

    /// <summary>
    /// Replaces a password and keeps the caller signed in on the device that did it.
    /// </summary>
    /// <remarks>
    /// Setting a password rotates the stamp every cookie is validated against, which is what stops
    /// the sessions elsewhere working and is the whole point. Renewing this one afterwards means the
    /// person who deliberately changed their own password is not thrown out of the screen they did
    /// it on.
    /// </remarks>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, NotFound, ProblemHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest? request,
        ICurrentPrincipalAccessor caller,
        IAccountProfile profile,
        SignInManager<ApplicationUser> sessions,
        UserManager<ApplicationUser> accounts,
        CancellationToken cancellationToken)
    {
        var userId = caller.GetUserId();

        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        if (request is null
            || string.IsNullOrEmpty(request.CurrentPassword)
            || string.IsNullOrEmpty(request.NewPassword))
        {
            return CurrentPasswordWrong();
        }

        var outcome = await profile
            .ChangePasswordAsync(userId.Value, request.CurrentPassword, request.NewPassword, cancellationToken)
            .ConfigureAwait(false);

        switch (outcome.Status)
        {
            case ProfileStatus.CurrentPasswordWrong:
                return CurrentPasswordWrong();

            case ProfileStatus.PasswordRejected:
                return Unusable(outcome.Problems);

            case ProfileStatus.Changed:
                break;

            default:
                return TypedResults.NotFound();
        }

        var user = await accounts.FindByIdAsync(userId.Value.ToString()).ConfigureAwait(false);

        if (user is not null)
        {
            await sessions.RefreshSignInAsync(user).ConfigureAwait(false);
        }

        return TypedResults.NoContent();
    }

    private static ProblemHttpResult NameRejected() =>
        Refused(
            "That name could not be saved.",
            new RefusedReason(NameRejectedCode, "Give a name of up to 100 characters."));

    /// <summary>
    /// The single answer to a password change that never got as far as the new password.
    /// </summary>
    /// <remarks>
    /// A blank field and a wrong password are answered identically. There is nothing to gain from
    /// telling them apart — the caller is already signed in — and one answer is one sentence to
    /// write and one thing for a reader to do about it.
    /// </remarks>
    private static ProblemHttpResult CurrentPasswordWrong() =>
        Refused(
            "That password did not match.",
            new RefusedReason(
                CurrentPasswordWrongCode,
                "Enter the password you sign in with now, then the one you would like instead."));

    private static ProblemHttpResult Unusable(IReadOnlyList<AccountProblem> problems) =>
        TypedResults.Problem(
            title: "Those details cannot be used.",
            detail: problems.Count > 0 ? problems[0].Description : null,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["problems"] = problems,
            });

    private static ProblemHttpResult Refused(string title, RefusedReason reason) =>
        TypedResults.Problem(
            title: title,
            detail: reason.Description,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["problems"] = new[] { reason },
            });
}
