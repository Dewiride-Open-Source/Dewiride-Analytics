using System.ComponentModel.DataAnnotations;

namespace Dewiride.Analytics.Api.Configuration;

/// <summary>
/// Limits applied to the screens people sign in to.
/// </summary>
public sealed class DashboardOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:Dashboard";

    /// <summary>
    /// Attempts to sign in, or to set an install up, allowed from one network address in five
    /// minutes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Locking an account after repeated failures stops somebody working through passwords for
    /// one account. This stops them working through accounts instead, which lockout cannot see
    /// because each attempt lands on a different one.
    /// </para>
    /// <para>
    /// Configurable because an address is a poor proxy for a person: a household is one address,
    /// and so is an office of two hundred people behind one connection. Ten is comfortably more
    /// than mistyping produces and far less than guessing needs.
    /// </para>
    /// </remarks>
    [Range(1, 100000)]
    public int SignInAttemptsPerFiveMinutes { get; init; } = 10;
}
