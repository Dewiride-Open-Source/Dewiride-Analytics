using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Extensibility;
using Dewiride.Analytics.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Composes the answer that says who is signed in and where they stand.
/// </summary>
/// <remarks>
/// Written once because four answers carry it — reading the session, signing in, closing an account
/// and bringing one back — and each has to say the same thing about a closed account, which is the
/// one fact the dashboard routes on before it draws anything.
/// </remarks>
internal static class SessionResponses
{
    /// <summary>
    /// Describes the session as it now stands.
    /// </summary>
    /// <param name="context">The request, whose principal the proof-of-origin token is tied to.</param>
    /// <param name="antiforgery">Issues that token.</param>
    /// <param name="claimed">Whether the install has been claimed.</param>
    /// <param name="user">Who is signed in, or nothing when nobody is.</param>
    /// <param name="closure">Describes a closed account to somebody in it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The answer.</returns>
    public static async Task<SessionResponse> DescribeAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        bool claimed,
        ApplicationUser? user,
        IAccountClosure closure,
        CancellationToken cancellationToken)
    {
        var closed = user is null
            ? null
            : await closure.DescribeForAsync(user.Id, cancellationToken).ConfigureAwait(false);

        return new SessionResponse(
            claimed,
            SignedInUsers.Describe(user),
            AntiforgeryGuard.IssueToken(antiforgery, context),
            Describe(closed));
    }

    private static ClosedAccountSummary? Describe(ClosedAccount? closed) =>
        closed is null
            ? null
            : new ClosedAccountSummary(
                closed.Value.Name,
                closed.Value.ClosedAt,
                closed.Value.DeletionDue,
                closed.Value.ClosedBy,
                closed.Value.ClosedByYou,
                closed.Value.CanRestore,
                closed.Value.HasOpenAccount);
}
