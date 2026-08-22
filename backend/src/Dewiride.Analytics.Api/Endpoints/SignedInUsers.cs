using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Infrastructure.Identity;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Describes an account to the interface.
/// </summary>
/// <remarks>
/// Written once because three endpoints answer with one — signing in, changing your own name, and
/// joining an account from an invitation — and because the fallbacks matter: an account created
/// without a name is shown under the address it signs in with rather than under a blank space.
/// </remarks>
internal static class SignedInUsers
{
    /// <summary>
    /// Describes an account, or nothing where there is none.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <returns>What the interface is told about them.</returns>
    public static SignedInUser? Describe(ApplicationUser? user) =>
        user is null
            ? null
            : new SignedInUser(
                user.Id,
                user.Email ?? string.Empty,
                user.DisplayName ?? user.Email ?? string.Empty);
}
