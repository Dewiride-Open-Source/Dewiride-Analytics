using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Extensibility;
using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Closing the account somebody belongs to, and bringing it back.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from the rest of the account's endpoints because these are the two that must see a
/// closed account: everything else under <c>/api/organization</c> answers as though a closed
/// account were not there, and that is the right answer everywhere but here.
/// </para>
/// <para>
/// Which account is closed or restored is never in the request. It is established from the
/// caller's standing, under the same lock the sweep that deletes closed accounts takes, by the
/// component that does the closing — so nothing here can be pointed at somebody else's account,
/// and nothing here decides ownership on the endpoint's say-so. Both answer with the session as it
/// now stands, because that is what the dashboard routes on.
/// </para>
/// </remarks>
internal static class ClosureEndpoints
{
    /// <summary>Names the reason an account cannot be closed: it is closed already.</summary>
    public const string AlreadyClosedCode = "AccountAlreadyClosed";

    /// <summary>Names the reason an account cannot be brought back: it is open.</summary>
    public const string NotClosedCode = "AccountNotClosed";

    /// <summary>
    /// Maps closing and restoring the account.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapClosure(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost("/api/organization/closure", CloseAsync)
            .WithName("CloseAccount")
            .WithSummary("Closes the account the caller owns.")
            .WithDescription(
                "Measurement stops for every website in it and nobody in it can open the dashboard. "
                + "Everything is kept for thirty days, during which any owner can bring it back; "
                + "after that everything it measured and everyone in it is deleted.")
            .RequireProofOfOrigin();

        routes.MapDelete("/api/organization/closure", RestoreAsync)
            .WithName("RestoreAccount")
            .WithSummary("Brings back the closed account the caller owns, exactly as it was.")
            .RequireProofOfOrigin();
    }

    private static async Task<Results<Ok<SessionResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound, ProblemHttpResult>> CloseAsync(
        HttpContext context,
        ICurrentPrincipalAccessor caller,
        IAccountClosure closure,
        IInstallation installation,
        UserManager<ApplicationUser> accounts,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var userId = caller.GetUserId();

        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var outcome = await closure.CloseAsync(userId.Value, cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            ClosureOutcome.Closed => TypedResults.Ok(
                await SessionAsync(context, installation, accounts, antiforgery, closure, cancellationToken)
                    .ConfigureAwait(false)),
            ClosureOutcome.NotOwner => TypedResults.Forbid(),
            ClosureOutcome.AlreadyClosed => AlreadyClosed(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Ok<SessionResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound, ProblemHttpResult>> RestoreAsync(
        HttpContext context,
        ICurrentPrincipalAccessor caller,
        IAccountClosure closure,
        IInstallation installation,
        UserManager<ApplicationUser> accounts,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var userId = caller.GetUserId();

        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var outcome = await closure.RestoreAsync(userId.Value, cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            ClosureOutcome.Restored => TypedResults.Ok(
                await SessionAsync(context, installation, accounts, antiforgery, closure, cancellationToken)
                    .ConfigureAwait(false)),
            ClosureOutcome.NotOwner => TypedResults.Forbid(),
            ClosureOutcome.NotClosed => NotClosed(),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>
    /// The session as it stands once the act is done, which is what the dashboard needs next.
    /// </summary>
    private static async Task<SessionResponse> SessionAsync(
        HttpContext context,
        IInstallation installation,
        UserManager<ApplicationUser> accounts,
        IAntiforgery antiforgery,
        IAccountClosure closure,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";

        var claimed = await installation.IsClaimedAsync(cancellationToken).ConfigureAwait(false);
        var user = await accounts.GetUserAsync(context.User).ConfigureAwait(false);

        return await SessionResponses.DescribeAsync(context, antiforgery, claimed, user, closure, cancellationToken)
            .ConfigureAwait(false);
    }

    private static ProblemHttpResult AlreadyClosed() =>
        Refused(
            "This account is already closed.",
            new RefusedReason(AlreadyClosedCode, "Someone else has just closed this account."),
            StatusCodes.Status409Conflict);

    private static ProblemHttpResult NotClosed() =>
        Refused(
            "This account is open.",
            new RefusedReason(NotClosedCode, "This account is already open."),
            StatusCodes.Status409Conflict);

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
