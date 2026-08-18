using System.Collections.Immutable;
using System.ComponentModel;

namespace Dewiride.Analytics.Application.Sites;

/// <summary>
/// Everything the collector needs to know about a site, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// A read model rather than the site aggregate. The collector resolves a site on every single
/// report, so the answer is cached — and a cache is free to store what it was given by writing it
/// out and reading it back later. An aggregate does not survive that: its state is guarded by
/// private setters and restored through whichever constructor the serialiser can reach, so
/// everything set afterwards comes back at its default. The failure is silent and it is not
/// harmless — the settings lost that way are the ones that decide whether query strings are kept
/// and which origins may report for the site.
/// </para>
/// <para>
/// This type is a plain record of independent values, so writing it out and reading it back
/// returns the same thing. It is declared immutable so the cache may also hand out the same
/// instance without copying, which on this path is worth having.
/// </para>
/// </remarks>
[ImmutableObject(true)]
public sealed record SiteSnapshot
{
    /// <summary>The site's identity, which is also its public tracker identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Primary hostname, lower-cased and without scheme or trailing dot.</summary>
    public required string Domain { get; init; }

    /// <summary>Whether query strings are kept on this site's events.</summary>
    public required bool RetainQueryStrings { get; init; }

    /// <summary>
    /// Origins permitted to report for this site. Empty means the site's own domain and its
    /// subdomains only.
    /// </summary>
    public required ImmutableArray<string> AllowedOrigins { get; init; }
}
