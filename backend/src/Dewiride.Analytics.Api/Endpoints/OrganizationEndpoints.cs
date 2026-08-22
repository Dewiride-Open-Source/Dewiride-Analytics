using System.Collections.Frozen;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Extensibility;
using Dewiride.Analytics.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// The account somebody belongs to, and the people in it.
/// </summary>
/// <remarks>
/// <para>
/// Which account is answered about is never in the request. It is the one the caller belongs to,
/// established from their standing, so nothing here can be pointed at somebody else's by changing
/// an identifier.
/// </para>
/// <para>
/// Reading is open to everybody in the account: who else can see the numbers is something anybody
/// looking at them ought to be able to check. Changing anything belongs to an owner, because
/// adding and removing people decides who can read a whole account's traffic.
/// </para>
/// </remarks>
internal static class OrganizationEndpoints
{
    /// <summary>Names the reason a name is not one an account can be shown under.</summary>
    public const string NameRejectedCode = "OrganizationNameRejected";

    /// <summary>Names the reason a change would leave the account with nobody to manage it.</summary>
    public const string LastOwnerCode = "LastOwnerRemains";

    /// <summary>Names the reason an invitation could not be addressed.</summary>
    public const string AddressUnusableCode = "InvitationAddressUnusable";

    /// <summary>Names the reason somebody cannot be invited.</summary>
    public const string AlreadyHereCode = "InvitationAlreadyHere";

    /// <summary>Names the reason a standing was not recognised.</summary>
    public const string RoleUnknownCode = "StandingNotRecognised";

    /// <summary>What each standing is called on the wire.</summary>
    private static readonly FrozenDictionary<OrganizationRole, string> StandingNames =
        new Dictionary<OrganizationRole, string>
        {
            [OrganizationRole.Member] = "member",
            [OrganizationRole.Admin] = "admin",
            [OrganizationRole.Owner] = "owner",
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, OrganizationRole> StandingsByName =
        StandingNames.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>
    /// Maps reading and changing the account and its people.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapOrganization(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/organization", DescribeAsync)
            .WithName("Organization")
            .WithSummary("Returns the account the caller belongs to and everybody in it.");

        routes.MapPatch("/api/organization", RenameAsync)
            .WithName("RenameOrganization")
            .WithSummary("Changes what the account is called.")
            .RequireProofOfOrigin();

        routes.MapPatch("/api/organization/people/{userId:guid}", ChangeStandingAsync)
            .WithName("ChangeStanding")
            .WithSummary("Changes what somebody may do in the account.")
            .WithDescription(
                "The last owner cannot be moved to anything else. An account with no owner is one "
                + "nobody can manage.")
            .RequireProofOfOrigin();

        routes.MapDelete("/api/organization/people/{userId:guid}", RemovePersonAsync)
            .WithName("RemovePerson")
            .WithSummary("Takes somebody out of the account.")
            .WithDescription(
                "Their grants on the account's websites go with them. Their own account is left "
                + "alone.")
            .RequireProofOfOrigin();

        routes.MapPost("/api/organization/invitations", InviteAsync)
            .WithName("InvitePerson")
            .WithSummary("Asks somebody to join the account, and sends them a link.")
            .WithDescription(
                "Nothing is created in their name until they open it. Sending a second invitation "
                + "to the same address replaces the first, which is also how one is sent again. "
                + "Read the account back to see it.")
            .RequireRateLimiting(RateLimitPolicies.Accounts)
            .RequireProofOfOrigin();

        routes.MapDelete("/api/organization/invitations/{invitationId:guid}", RevokeAsync)
            .WithName("RevokeInvitation")
            .WithSummary("Withdraws an invitation, so its link stops working.")
            .RequireProofOfOrigin();
    }

    private static async Task<Results<Ok<OrganizationResponse>, UnauthorizedHttpResult, NotFound>> DescribeAsync(
        ICurrentPrincipalAccessor caller,
        IOrganizationDirectory organizations,
        IInvitations invitations,
        CancellationToken cancellationToken)
    {
        var userId = caller.GetUserId();

        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var standing = await organizations
            .StandingForAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (standing is null)
        {
            return TypedResults.NotFound();
        }

        var account = await organizations
            .DescribeAsync(standing.Value.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return TypedResults.NotFound();
        }

        var waiting = standing.Value.Role == OrganizationRole.Owner
            ? await invitations.ListAsync(standing.Value.OrganizationId, cancellationToken).ConfigureAwait(false)
            : [];

        return TypedResults.Ok(Describe(account.Value, standing.Value.Role, waiting));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound, ProblemHttpResult>> RenameAsync(
        RenameOrganizationRequest? request,
        ICurrentPrincipalAccessor caller,
        IOrganizationDirectory organizations,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedAsync(caller, organizations, cancellationToken).ConfigureAwait(false);

        if (owned.Refusal is not null)
        {
            return owned.Refusal;
        }

        var outcome = await organizations
            .RenameAsync(owned.OrganizationId, request?.Name ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            OrganizationRenameOutcome.Renamed => TypedResults.NoContent(),
            OrganizationRenameOutcome.NameRejected => NameRejected(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound, ProblemHttpResult>> ChangeStandingAsync(
        Guid userId,
        ChangeStandingRequest? request,
        ICurrentPrincipalAccessor caller,
        IOrganizationDirectory organizations,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedAsync(caller, organizations, cancellationToken).ConfigureAwait(false);

        if (owned.Refusal is not null)
        {
            return owned.Refusal;
        }

        if (!TryReadStanding(request?.Role, out var role))
        {
            return StandingNotRecognised();
        }

        var outcome = await organizations
            .ChangeStandingAsync(owned.OrganizationId, userId, role, cancellationToken)
            .ConfigureAwait(false);

        return Answer(outcome);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound, ProblemHttpResult>> RemovePersonAsync(
        Guid userId,
        ICurrentPrincipalAccessor caller,
        IOrganizationDirectory organizations,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedAsync(caller, organizations, cancellationToken).ConfigureAwait(false);

        if (owned.Refusal is not null)
        {
            return owned.Refusal;
        }

        var outcome = await organizations
            .RemovePersonAsync(owned.OrganizationId, userId, cancellationToken)
            .ConfigureAwait(false);

        return Answer(outcome);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound, ProblemHttpResult>> InviteAsync(
        InvitePersonRequest? request,
        ICurrentPrincipalAccessor caller,
        IOrganizationDirectory organizations,
        IInvitations invitations,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedAsync(caller, organizations, cancellationToken).ConfigureAwait(false);

        if (owned.Refusal is not null)
        {
            return owned.Refusal;
        }

        if (!TryReadStanding(request?.Role, out var role))
        {
            return StandingNotRecognised();
        }

        var outcome = await invitations
            .InviteAsync(
                new InvitationRequest(
                    owned.OrganizationId,
                    owned.UserId,
                    request!.EmailAddress ?? string.Empty,
                    role),
                cancellationToken)
            .ConfigureAwait(false);

        return outcome.Status switch
        {
            InvitationStatus.Invited => TypedResults.NoContent(),
            InvitationStatus.AlreadyHere => AlreadyHere(),
            _ => AddressUnusable(),
        };
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound, ProblemHttpResult>> RevokeAsync(
        Guid invitationId,
        ICurrentPrincipalAccessor caller,
        IOrganizationDirectory organizations,
        IInvitations invitations,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedAsync(caller, organizations, cancellationToken).ConfigureAwait(false);

        if (owned.Refusal is not null)
        {
            return owned.Refusal;
        }

        var outcome = await invitations
            .RevokeAsync(owned.OrganizationId, invitationId, cancellationToken)
            .ConfigureAwait(false);

        return outcome == InvitationWithdrawal.Revoked
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    /// <summary>
    /// Establishes the account the caller owns, or the answer to give instead.
    /// </summary>
    /// <remarks>
    /// Every change here is one an owner makes, and the account it is made to is theirs by
    /// definition. Asking for both in one place is what keeps an identifier in a request from ever
    /// being the thing that decides which account is changed.
    /// </remarks>
    private static async Task<OwnedAccount> OwnedAsync(
        ICurrentPrincipalAccessor caller,
        IOrganizationDirectory organizations,
        CancellationToken cancellationToken)
    {
        var userId = caller.GetUserId();

        if (userId is null)
        {
            return new OwnedAccount(Guid.Empty, Guid.Empty, TypedResults.Unauthorized());
        }

        var standing = await organizations
            .StandingForAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (standing is null)
        {
            return new OwnedAccount(Guid.Empty, Guid.Empty, TypedResults.NotFound());
        }

        return standing.Value.Role == OrganizationRole.Owner
            ? new OwnedAccount(standing.Value.OrganizationId, userId.Value, null)
            : new OwnedAccount(Guid.Empty, Guid.Empty, TypedResults.Forbid());
    }

    private static Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound, ProblemHttpResult> Answer(
        PersonChangeOutcome outcome) => outcome switch
        {
            PersonChangeOutcome.Changed => TypedResults.NoContent(),
            PersonChangeOutcome.LastOwner => LastOwner(),
            _ => TypedResults.NotFound(),
        };

    private static OrganizationResponse Describe(
        OrganizationAccount account,
        OrganizationRole role,
        IReadOnlyList<PendingInvitation> waiting) =>
        new(
            account.Id,
            account.Name,
            StandingNames[role],
            [
                .. account.People.Select(person => new PersonSummary(
                    person.UserId,
                    person.EmailAddress,
                    person.DisplayName,
                    StandingNames[person.Role],
                    person.JoinedAt)),
            ],
            [.. waiting.Select(Describe)]);

    private static InvitationSummary Describe(PendingInvitation invitation) =>
        new(
            invitation.Id,
            invitation.EmailAddress,
            StandingNames[invitation.Role],
            invitation.InvitedAt,
            invitation.ExpiresAt);

    private static bool TryReadStanding(string? name, out OrganizationRole role)
    {
        role = OrganizationRole.Member;

        return name is not null && StandingsByName.TryGetValue(name, out role);
    }

    private static ProblemHttpResult NameRejected() =>
        Refused(
            "That name could not be saved.",
            new RefusedReason(NameRejectedCode, "Give the account a name of up to 200 characters."),
            StatusCodes.Status400BadRequest);

    private static ProblemHttpResult StandingNotRecognised() =>
        Refused(
            "That is not something somebody can be.",
            new RefusedReason(
                RoleUnknownCode,
                "Choose whether they can read the numbers, manage the websites, or run the account."),
            StatusCodes.Status400BadRequest);

    private static ProblemHttpResult LastOwner() =>
        Refused(
            "Somebody has to run this account.",
            new RefusedReason(
                LastOwnerCode,
                "Make somebody else an owner first, then come back to this."),
            StatusCodes.Status409Conflict);

    private static ProblemHttpResult AlreadyHere() =>
        Refused(
            "They are already on this account.",
            new RefusedReason(
                AlreadyHereCode,
                "Change what they can do from the list of people instead."),
            StatusCodes.Status409Conflict);

    private static ProblemHttpResult AddressUnusable() =>
        Refused(
            "That invitation could not be sent.",
            new RefusedReason(AddressUnusableCode, "Check the email address and try again."),
            StatusCodes.Status400BadRequest);

    private static ProblemHttpResult Refused(string title, RefusedReason reason, int status) =>
        TypedResults.Problem(
            title: title,
            detail: reason.Description,
            statusCode: status,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["problems"] = new[] { reason },
            });

    /// <summary>
    /// The account a change is being made to, or the answer to give instead of making it.
    /// </summary>
    /// <param name="OrganizationId">The account, where the caller owns one.</param>
    /// <param name="UserId">The caller, where there is one.</param>
    /// <param name="Refusal">The answer to give, where the change must not be made.</param>
    private readonly record struct OwnedAccount(
        Guid OrganizationId,
        Guid UserId,
        Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound, ProblemHttpResult>? Refusal);
}
