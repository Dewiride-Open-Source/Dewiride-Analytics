using Microsoft.AspNetCore.Identity;

namespace Dewiride.Analytics.Infrastructure.Identity;

/// <summary>
/// A person who signs in.
/// </summary>
/// <remarks>
/// Keyed by <see cref="Guid"/> rather than the default string so that identity keys match the
/// key type used everywhere else, and so a user identifier can be carried into telemetry-side
/// structures without a conversion at every boundary.
/// </remarks>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Name shown in the interface, distinct from the login identifier.</summary>
    public string? DisplayName { get; set; }

    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A role a user can hold.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
}
