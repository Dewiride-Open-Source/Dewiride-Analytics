using Dewiride.Analytics.Api.Configuration;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Configuration keys the suite overrides on the hosts it builds.
/// </summary>
/// <remarks>
/// Written as keys rather than literals so that renaming a setting breaks the build here instead
/// of quietly leaving a test running against the shipped default.
/// </remarks>
internal static class TestSettings
{
    /// <summary>Attempts to sign in allowed from one address in five minutes.</summary>
    public static readonly string SignInAllowance =
        $"{DashboardOptions.SectionName}:{nameof(DashboardOptions.SignInAttemptsPerFiveMinutes)}";

    /// <summary>
    /// An allowance no test will reach, for the hosts whose subject is something else.
    /// </summary>
    public const string NoPracticalLimit = "100000";
}
