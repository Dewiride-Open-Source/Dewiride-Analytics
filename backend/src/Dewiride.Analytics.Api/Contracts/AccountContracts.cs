namespace Dewiride.Analytics.Api.Contracts;

/// <summary>
/// Details somebody types to sign in.
/// </summary>
public sealed record SignInRequest
{
    /// <summary>Address the account signs in with.</summary>
    public string? EmailAddress { get; init; }

    /// <summary>The account's password.</summary>
    public string? Password { get; init; }

    /// <summary>
    /// Whether to stay signed in after the browser is closed.
    /// </summary>
    /// <remarks>
    /// Off unless asked for, so that a sign-in on a borrowed machine ends when the window does.
    /// </remarks>
    public bool StaySignedIn { get; init; }
}

/// <summary>
/// Details typed on the setup screen the very first time the product is opened.
/// </summary>
public sealed record SetupRequest
{
    /// <summary>Address the first account will sign in with.</summary>
    public string? EmailAddress { get; init; }

    /// <summary>Password for the first account.</summary>
    public string? Password { get; init; }

    /// <summary>Name shown in the interface. The address is used when this is left out.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Name of the organisation that will own the sites.</summary>
    public string? OrganizationName { get; init; }

    /// <summary>Hostname of the first website to measure.</summary>
    public string? SiteDomain { get; init; }

    /// <summary>IANA time zone the first site's days are counted in.</summary>
    public string? TimeZoneId { get; init; }
}

/// <summary>
/// Everything the interface needs to decide what to show before anything else is loaded.
/// </summary>
/// <param name="SetupCompleted">
/// Whether anybody has claimed this install yet. When false the only thing the interface can
/// usefully show is the setup screen.
/// </param>
/// <param name="User">Who is signed in, or nothing when nobody is.</param>
/// <param name="Token">
/// Value to send back in the <c>X-Csrf-Token</c> header on anything that changes something. It is
/// tied to the identity it was issued to, so a fresh one arrives with every answer that changes
/// who is signed in.
/// </param>
public sealed record SessionResponse(bool SetupCompleted, SignedInUser? User, string Token);

/// <summary>
/// The person currently signed in.
/// </summary>
/// <param name="Id">Their identifier.</param>
/// <param name="EmailAddress">The address they sign in with.</param>
/// <param name="DisplayName">The name shown in the interface.</param>
public sealed record SignedInUser(Guid Id, string EmailAddress, string DisplayName);

/// <summary>
/// What claiming an install produced.
/// </summary>
/// <param name="SiteId">
/// Identifier of the first site, which is the value that goes into its tracking snippet.
/// </param>
/// <param name="User">The owner account that was created, now signed in.</param>
/// <param name="Token">Value to send back in the <c>X-Csrf-Token</c> header from now on.</param>
public sealed record SetupResponse(Guid SiteId, SignedInUser User, string Token);

/// <summary>
/// The address somebody types when they cannot remember their password.
/// </summary>
public sealed record ForgotPasswordRequest
{
    /// <summary>Address to send a way back in to.</summary>
    public string? EmailAddress { get; init; }
}

/// <summary>
/// What somebody following a reset link sends back.
/// </summary>
/// <remarks>
/// The address travels with the link rather than being typed again, so that somebody halfway
/// through getting back into their account is not asked to remember which of their addresses they
/// registered with.
/// </remarks>
public sealed record ResetPasswordRequest
{
    /// <summary>Address the link was sent to, carried in the link itself.</summary>
    public string? EmailAddress { get; init; }

    /// <summary>The token from the link, exactly as it arrived.</summary>
    public string? Token { get; init; }

    /// <summary>The password to set.</summary>
    public string? Password { get; init; }
}
