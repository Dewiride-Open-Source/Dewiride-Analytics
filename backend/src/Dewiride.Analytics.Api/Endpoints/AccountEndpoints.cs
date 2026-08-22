using System.Security.Claims;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Extensibility;
using Dewiride.Analytics.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Signing in, signing out, and claiming an install that nobody owns yet.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard is protected because a self-hosted install is reachable from the internet — the
/// collector has to be, so the screens beside it are too — and because the visitor addresses held
/// for three days are personal data sitting behind that door.
/// </para>
/// <para>
/// Nothing here says whether an address belongs to an account. A wrong password, an address
/// nobody has registered, and an account locked after too many attempts all produce the same
/// answer, so the endpoint cannot be used to find out who has an account on an install.
/// </para>
/// </remarks>
internal static class AccountEndpoints
{
    /// <summary>Problem code reported when a password-reset link will not do.</summary>
    public const string ResetLinkNotUsableCode = "ResetLinkNotUsable";

    /// <summary>
    /// Stands in for a real account when the address supplied does not match one.
    /// </summary>
    /// <remarks>
    /// Verifying a password is deliberately slow, so answering an unknown address immediately
    /// would make it measurably quicker than a known one and turn the sign-in form into a way of
    /// listing who has an account. Hashing against this instead spends the same time and throws
    /// the result away.
    /// </remarks>
    private static readonly ApplicationUser AbsentAccount = new()
    {
        Id = Guid.Empty,
        UserName = "absent",
        PasswordHash = null,
    };

    /// <summary>
    /// Maps the session and setup endpoints.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapAccount(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/session", DescribeAsync)
            .WithName("DescribeSession")
            .WithSummary("Reports whether this install has been set up and who is signed in.")
            .AllowAnonymous();

        routes.MapPost("/api/session", SignInAsync)
            .WithName("SignIn")
            .WithSummary("Signs somebody in with an address and a password.")
            .RequireRateLimiting(RateLimitPolicies.Accounts)
            .RequireProofOfOrigin()
            .AllowAnonymous();

        routes.MapDelete("/api/session", SignOutAsync)
            .WithName("SignOut")
            .WithSummary("Ends the current sign-in and issues a token for signing in again.")
            .RequireProofOfOrigin();

        routes.MapPost("/api/setup", ClaimAsync)
            .WithName("ClaimInstall")
            .WithSummary("Creates the first account, its organisation and its first site.")
            .WithDescription(
                "Works once. Every later call is refused, whoever makes it, because the install "
                + "already has an owner.")
            .RequireRateLimiting(RateLimitPolicies.Accounts)
            .RequireProofOfOrigin()
            .AllowAnonymous();

        routes.MapPost("/api/password-reset", BeginResetAsync)
            .WithName("BeginPasswordReset")
            .WithSummary("Sends a way back in to an address that has an account.")
            .WithDescription(
                "Answers the same whether or not the address belongs to an account. Anything "
                + "else would turn this into a way of finding out who has one.")
            .RequireRateLimiting(RateLimitPolicies.Accounts)
            .RequireProofOfOrigin()
            .AllowAnonymous();

        routes.MapPost("/api/password-reset/complete", CompleteResetAsync)
            .WithName("CompletePasswordReset")
            .WithSummary("Sets a new password for somebody holding a link that still works.")
            .RequireRateLimiting(RateLimitPolicies.Accounts)
            .RequireProofOfOrigin()
            .AllowAnonymous();
    }

    private static async Task<Ok<SessionResponse>> DescribeAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IInstallation installation,
        UserManager<ApplicationUser> accounts,
        CancellationToken cancellationToken)
    {
        DoNotStore(context);

        var claimed = await installation.IsClaimedAsync(cancellationToken).ConfigureAwait(false);

        var user = context.User.Identity?.IsAuthenticated == true
            ? await accounts.GetUserAsync(context.User).ConfigureAwait(false)
            : null;

        return TypedResults.Ok(
            new SessionResponse(claimed, SignedInUsers.Describe(user), AntiforgeryGuard.IssueToken(antiforgery, context)));
    }

    private static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> SignInAsync(
        SignInRequest request,
        HttpContext context,
        SignInManager<ApplicationUser> sessions,
        UserManager<ApplicationUser> accounts,
        IAntiforgery antiforgery)
    {
        DoNotStore(context);

        if (request is null
            || string.IsNullOrWhiteSpace(request.EmailAddress)
            || string.IsNullOrEmpty(request.Password))
        {
            return NotRecognised();
        }

        var user = await accounts.FindByEmailAsync(request.EmailAddress).ConfigureAwait(false);

        if (user is null)
        {
            accounts.PasswordHasher.HashPassword(AbsentAccount, request.Password);

            return NotRecognised();
        }

        var outcome = await sessions
            .PasswordSignInAsync(user, request.Password, request.StaySignedIn, lockoutOnFailure: true)
            .ConfigureAwait(false);

        if (!outcome.Succeeded)
        {
            return NotRecognised();
        }

        // The token issued below is tied to whoever is signed in, and signing in does not change
        // the principal on the request that did it. Setting it here means the token handed back is
        // the one that will still be accepted on the caller's next write.
        context.User = await sessions.CreateUserPrincipalAsync(user).ConfigureAwait(false);

        return TypedResults.Ok(
            new SessionResponse(true, SignedInUsers.Describe(user), AntiforgeryGuard.IssueToken(antiforgery, context)));
    }

    /// <summary>
    /// Ends the sign-in and hands back what the caller needs to sign in again.
    /// </summary>
    /// <remarks>
    /// A proof-of-origin token belongs to the identity it was issued to, so the one the caller
    /// arrived with stops working the moment they sign out. Answering with a fresh one means the
    /// sign-in form is usable immediately instead of refusing the first attempt.
    /// </remarks>
    private static async Task<Ok<SessionResponse>> SignOutAsync(
        HttpContext context,
        SignInManager<ApplicationUser> sessions,
        IAntiforgery antiforgery)
    {
        DoNotStore(context);

        await sessions.SignOutAsync().ConfigureAwait(false);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());

        return TypedResults.Ok(
            new SessionResponse(true, null, AntiforgeryGuard.IssueToken(antiforgery, context)));
    }

    private static async Task<Results<Ok<SetupResponse>, ProblemHttpResult>> ClaimAsync(
        SetupRequest request,
        HttpContext context,
        IInstallation installation,
        SignInManager<ApplicationUser> sessions,
        UserManager<ApplicationUser> accounts,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        DoNotStore(context);

        if (!TryRead(request, out var claim))
        {
            return Incomplete();
        }

        var outcome = await installation.ClaimAsync(claim, cancellationToken).ConfigureAwait(false);

        if (outcome.Status == InstallationStatus.AlreadyClaimed)
        {
            return AlreadyClaimed();
        }

        if (outcome.Status == InstallationStatus.Rejected)
        {
            return Unusable(outcome.Problems);
        }

        var owner = await accounts.FindByIdAsync(outcome.UserId!.Value.ToString()).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The account just created could not be read back.");

        await sessions.SignInAsync(owner, isPersistent: false).ConfigureAwait(false);
        context.User = await sessions.CreateUserPrincipalAsync(owner).ConfigureAwait(false);

        return TypedResults.Ok(
            new SetupResponse(
                outcome.SiteId!.Value,
                SignedInUsers.Describe(owner)!,
                AntiforgeryGuard.IssueToken(antiforgery, context)));
    }

    /// <summary>
    /// Takes a request for a way back in and says nothing about what came of it.
    /// </summary>
    /// <remarks>
    /// Accepted, always. Whether a message was sent, whether the address belongs to an account and
    /// whether the mail server took it are all invisible from here, because an answer that varied
    /// would be a way of asking this installation who has an account on it.
    /// </remarks>
    private static async Task<Results<Accepted, ProblemHttpResult>> BeginResetAsync(
        ForgotPasswordRequest request,
        HttpContext context,
        IPasswordReset resets,
        CancellationToken cancellationToken)
    {
        DoNotStore(context);

        if (request is null || string.IsNullOrWhiteSpace(request.EmailAddress))
        {
            return AddressMissing();
        }

        await resets.BeginAsync(request.EmailAddress.Trim(), cancellationToken).ConfigureAwait(false);

        return TypedResults.Accepted((string?)null);
    }

    /// <summary>
    /// Sets a new password for somebody who followed a link.
    /// </summary>
    /// <remarks>
    /// Nobody is signed in as a result. Possession of the link proves the mailbox and nothing
    /// more, and the password that was just set is what the next screen asks for.
    /// </remarks>
    private static async Task<Results<NoContent, ProblemHttpResult>> CompleteResetAsync(
        ResetPasswordRequest request,
        HttpContext context,
        IPasswordReset resets,
        CancellationToken cancellationToken)
    {
        DoNotStore(context);

        if (request is null
            || string.IsNullOrWhiteSpace(request.EmailAddress)
            || string.IsNullOrWhiteSpace(request.Token)
            || string.IsNullOrEmpty(request.Password))
        {
            return LinkNotUsable();
        }

        var outcome = await resets
            .CompleteAsync(
                new PasswordResetRequest(request.EmailAddress.Trim(), request.Token, request.Password),
                cancellationToken)
            .ConfigureAwait(false);

        return outcome.Status switch
        {
            PasswordResetStatus.Reset => TypedResults.NoContent(),
            PasswordResetStatus.PasswordRejected => Unusable(outcome.Problems),
            _ => LinkNotUsable(),
        };
    }

    /// <summary>
    /// Turns the posted setup form into a request, or reports that something is missing.
    /// </summary>
    /// <remarks>
    /// Only presence is checked here. Whether the hostname is usable and whether the time zone
    /// exists are the site's own rules, and asking twice is how the two answers drift apart.
    /// </remarks>
    private static bool TryRead(SetupRequest request, out InstallationRequest claim)
    {
        claim = null!;

        if (request is null
            || string.IsNullOrWhiteSpace(request.EmailAddress)
            || string.IsNullOrEmpty(request.Password)
            || string.IsNullOrWhiteSpace(request.OrganizationName)
            || string.IsNullOrWhiteSpace(request.SiteDomain)
            || string.IsNullOrWhiteSpace(request.TimeZoneId))
        {
            return false;
        }

        claim = new InstallationRequest(
            request.EmailAddress.Trim(),
            request.Password,
            request.DisplayName,
            request.OrganizationName,
            request.SiteDomain,
            request.TimeZoneId);

        return true;
    }

    /// <summary>
    /// Keeps answers about who is signed in out of every cache between here and the browser.
    /// </summary>
    private static void DoNotStore(HttpContext context) =>
        context.Response.Headers.CacheControl = "no-store";

    /// <summary>
    /// The single answer to every failed attempt to sign in.
    /// </summary>
    /// <remarks>
    /// A wrong password, an unknown address and a locked account are answered identically and
    /// with the same status. Distinguishing them would confirm which addresses have accounts, and
    /// naming a lockout would confirm it twice over — an attacker can cause one on demand. The
    /// wait is mentioned without saying whether it applies, so somebody genuinely locked out is
    /// still told what to do.
    /// </remarks>
    private static ProblemHttpResult NotRecognised() =>
        TypedResults.Problem(
            title: "Those details were not recognised.",
            detail: "Check the email address and password. After several failed attempts, "
                + "sign-in is paused for fifteen minutes.",
            statusCode: StatusCodes.Status401Unauthorized);

    private static ProblemHttpResult Incomplete() =>
        TypedResults.Problem(
            title: "Some details are missing.",
            detail: "An email address, a password, a name for your account, the address of the "
                + "website to measure and its time zone are all needed.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult AddressMissing() =>
        TypedResults.Problem(
            title: "An email address is needed.",
            detail: "Enter the address you sign in with.",
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// The single answer to every link that will not do.
    /// </summary>
    /// <remarks>
    /// Expired, already used, tampered with, and sent to an address that has no account are all
    /// answered identically and with the same code. Telling them apart would say which addresses
    /// have accounts here, and confirming that a link had been used would confirm that somebody
    /// had asked for one.
    /// </remarks>
    private static ProblemHttpResult LinkNotUsable() =>
        Refusal(
            "That link cannot be used.",
            new AccountProblem(
                ResetLinkNotUsableCode,
                "Reset links stop working after 24 hours, and once they have been used. Ask for a "
                + "new one."));

    private static ProblemHttpResult Refusal(string title, AccountProblem problem) =>
        TypedResults.Problem(
            title: title,
            detail: problem.Description,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["problems"] = new[] { problem },
            });

    private static ProblemHttpResult AlreadyClaimed() =>
        TypedResults.Problem(
            title: "This installation has already been set up.",
            detail: "Sign in with the account that was created, or ask whoever set it up to add "
                + "you.",
            statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult Unusable(IReadOnlyList<AccountProblem> problems) =>
        TypedResults.Problem(
            title: "Those details cannot be used.",
            detail: problems.Count > 0 ? problems[0].Description : null,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["problems"] = problems,
            });
}
