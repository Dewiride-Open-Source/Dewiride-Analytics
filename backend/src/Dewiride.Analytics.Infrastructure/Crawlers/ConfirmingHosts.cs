using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Identity;

namespace Dewiride.Analytics.Infrastructure.Crawlers;

/// <summary>One domain that stands for one company, and the form a match is tested against.</summary>
/// <param name="Suffix">The domain, as the company documents it.</param>
/// <param name="Dotted">The same domain with a leading dot, which is what a subdomain ends with.</param>
/// <param name="Operator">The company, spelt as the crawler catalogue spells it.</param>
internal readonly record struct HostClaim(string Suffix, string Dotted, string Operator);

/// <summary>
/// Decides which company a name belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Taken from the crawler catalogue rather than listed again, on the same terms as the address
/// files: which domains stand for which company is part of what the engine reasons with, and a
/// second copy here would be the one that went stale.
/// </para>
/// <para>
/// A name matches only on a label boundary — it is the domain itself, or something below it. That
/// is the whole security of this check on the reading side. Anybody may register
/// <c>not-googlebot.com</c> or <c>googlebot.com.example.net</c> and point a name at their own
/// machine, and a match written as "ends with the string" would hand them Google's name; neither
/// is the domain and neither sits below it, so neither matches here.
/// </para>
/// <para>
/// The longest domain is tried first, so a company that documents both a domain and something
/// under it is answered by the more specific of the two.
/// </para>
/// </remarks>
internal static class ConfirmingHosts
{
    /// <summary>Every domain a company documents as its crawlers', longest first.</summary>
    public static ImmutableArray<HostClaim> Claims { get; } =
    [
        .. CrawlerCatalogue.Proofs
            .SelectMany(
                proof => proof.ConfirmingHosts,
                (proof, host) => new HostClaim(host, "." + host, proof.Operator))
            .OrderByDescending(claim => claim.Suffix.Length)
            .ThenBy(claim => claim.Suffix, StringComparer.Ordinal),
    ];

    /// <summary>
    /// Finds the company a name belongs to.
    /// </summary>
    /// <param name="hostName">The name an address answered to. Chosen by whoever holds the address.</param>
    /// <returns>
    /// The company, or <see langword="null"/> where the name is nobody's — which is the answer for
    /// almost every name and is never a finding.
    /// </returns>
    public static string? OperatorOf(string? hostName)
    {
        if (string.IsNullOrEmpty(hostName))
        {
            return null;
        }

        // A name server may or may not put the root's own dot on the end, and the two spellings
        // are the same name.
        var host = hostName.TrimEnd('.');
        var found = Claims.FirstOrDefault(claim => Covers(claim, host));

        return string.IsNullOrEmpty(found.Operator) ? null : found.Operator;
    }

    private static bool Covers(HostClaim claim, string host) =>
        host.EndsWith(claim.Dotted, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, claim.Suffix, StringComparison.OrdinalIgnoreCase);
}
