using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Extensibility;
using Dewiride.Analytics.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Taking up an invitation to join an account.
/// </summary>
/// <remarks>
/// <para>
/// The two endpoints nobody is signed in for. Whoever is holding the link is the person it was
/// addressed to, and until they use it they have no account here to sign in with — so the secret
/// they present is the whole of what authorises this.
/// </para>
/// <para>
/// The secret is sent in the body rather than in the address. What is in an address is written to
/// access logs, kept in browser history and passed on in a referrer header, and this one is a way
/// into somebody's account for as long as it lasts.
/// </para>
/// <para>
/// Every link that will not do is answered identically. Spent, withdrawn, expired and never issued
/// need the same thing done about them — being asked again — and telling them apart would say
/// whether somebody else had already used it.
/// </para>
/// </remarks>
internal static class InvitationEndpoints
{
    /// <summary>Names the reason an invitation link will not do.</summary>
    public const string LinkNotUsableCode = "InvitationLinkNotUsable";

    /// <summary>Names the reason an account could not be created from an invitation.</summary>
    public const string DetailsMissingCode = "JoinDetailsMissing";

    /// <summary>
    /// Maps reading an invitation and taking it up.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapInvitations(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost("/api/invitations/preview", PreviewAsync)
            .WithName("PreviewInvitation")
            .WithSummary("Says what an invitation is for, to whoever is holding it.")
            .WithDescription(
                "Answers the name of the account and whether the address already has an account "
                + "here, which is what decides whether a password has to be chosen.")
            .RequireRateLimiting(RateLimitPolicies.Accounts)
            .RequireProofOfOrigin()
            .AllowAnonymous();

        routes.MapPost("/api/invitations/accept", AcceptAsync)
            .WithName("AcceptInvitation")
            .WithSummary("Joins the account the invitation was sent from.")
            .WithDescription(
                "Creates an account where the address has none, and signs the caller in. Where it "
                + "already has one, the standing is granted and they sign in as they always do.")
            .RequireRateLimiting(RateLimitPolicies.Accounts)
            .RequireProofOfOrigin()
            .AllowAnonymous();
    }

    private static async Task<Results<Ok<InvitationPreviewResponse>, ProblemHttpResult>> PreviewAsync(
        InvitationTokenRequest? request,
        HttpContext context,
        IInvitations invitations,
        CancellationToken cancellationToken)
    {
        DoNotStore(context);

        var preview = string.IsNullOrWhiteSpace(request?.Token)
            ? null
            : await invitations.PreviewAsync(request.Token, cancellationToken).ConfigureAwait(false);

        return preview is null
            ? LinkNotUsable()
            : TypedResults.Ok(
                new InvitationPreviewResponse(
                    preview.Value.OrganizationName,
                    preview.Value.EmailAddress,
                    preview.Value.NeedsAccount));
    }

    private static async Task<Results<Ok<JoinResponse>, ProblemHttpResult>> AcceptAsync(
        AcceptInvitationRequest? request,
        HttpContext context,
        IInvitations invitations,
        SignInManager<ApplicationUser> sessions,
        UserManager<ApplicationUser> accounts,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        DoNotStore(context);

        if (string.IsNullOrWhiteSpace(request?.Token))
        {
            return LinkNotUsable();
        }

        var outcome = await invitations
            .AcceptAsync(
                new AcceptanceRequest(request.Token, request.DisplayName, request.Password),
                cancellationToken)
            .ConfigureAwait(false);

        switch (outcome.Status)
        {
            case AcceptanceStatus.Joined:
                return await SignInAsync(context, outcome, sessions, accounts, antiforgery).ConfigureAwait(false);

            case AcceptanceStatus.JoinedExisting:
                return TypedResults.Ok(
                    new JoinResponse(false, null, AntiforgeryGuard.IssueToken(antiforgery, context)));

            case AcceptanceStatus.DetailsMissing:
                return DetailsMissing();

            case AcceptanceStatus.PasswordRejected:
                return Unusable(outcome.Problems);

            default:
                return LinkNotUsable();
        }
    }

    private static async Task<Results<Ok<JoinResponse>, ProblemHttpResult>> SignInAsync(
        HttpContext context,
        Acceptance outcome,
        SignInManager<ApplicationUser> sessions,
        UserManager<ApplicationUser> accounts,
        IAntiforgery antiforgery)
    {
        var user = await accounts.FindByIdAsync(outcome.UserId!.Value.ToString()).ConfigureAwait(false);

        if (user is null)
        {
            return LinkNotUsable();
        }

        await sessions.SignInAsync(user, isPersistent: false).ConfigureAwait(false);

        // The token issued below is tied to whoever is signed in, and signing in does not change
        // the principal on the request that did it. Setting it here means the token handed back is
        // the one that will still be accepted on the caller's next write.
        context.User = await sessions.CreateUserPrincipalAsync(user).ConfigureAwait(false);

        return TypedResults.Ok(
            new JoinResponse(
                true,
                SignedInUsers.Describe(user),
                AntiforgeryGuard.IssueToken(antiforgery, context)));
    }

    /// <summary>
    /// Keeps answers about an invitation out of every cache between here and the browser.
    /// </summary>
    private static void DoNotStore(HttpContext context) =>
        context.Response.Headers.CacheControl = "no-store";

    private static ProblemHttpResult LinkNotUsable() =>
        Refused(
            "That invitation cannot be used.",
            new RefusedReason(
                LinkNotUsableCode,
                "Invitations stop working after 7 days, and once they have been used. Ask "
                + "whoever invited you to send another."),
            StatusCodes.Status400BadRequest);

    private static ProblemHttpResult DetailsMissing() =>
        Refused(
            "A password is needed.",
            new RefusedReason(DetailsMissingCode, "Choose a password to finish setting up your account."),
            StatusCodes.Status400BadRequest);

    private static ProblemHttpResult Unusable(IReadOnlyList<AccountProblem> problems) =>
        TypedResults.Problem(
            title: "That password cannot be used.",
            detail: problems.Count > 0 ? problems[0].Description : null,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["problems"] = problems,
            });

    private static ProblemHttpResult Refused(string title, RefusedReason reason, int status) =>
        TypedResults.Problem(
            title: title,
            detail: reason.Description,
            statusCode: status,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["problems"] = new[] { reason },
            });
}
