namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Passwords the suite signs in with.
/// </summary>
/// <remarks>
/// Named rather than repeated, because the account rules are enforced in one place and a change
/// to them should break one line here instead of every test that ever created an account. The
/// obvious candidate for a test passphrase — the one from the well-known comic strip — is
/// deliberately not used: it is on the blocklist, which is what
/// <c>PredictablePasswordTests</c> is for.
/// </remarks>
internal static class Passwords
{
    /// <summary>A passphrase that satisfies every account rule.</summary>
    public const string Acceptable = "vermilion tractor almanac";
}
