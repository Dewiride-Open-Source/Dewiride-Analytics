namespace Dewiride.Analytics.Domain.Sites;

/// <summary>
/// Which hosts a website is published at.
/// </summary>
/// <remarks>
/// <para>
/// One definition, because two things have to agree about it. The collector asks whether a report
/// may be stored against a website, once per report, and the engine asks whether an address is one
/// it may read on a website's behalf. A website that was measured at an address and refused a
/// reading of it, or the other way about, would be a website described by two different rules.
/// </para>
/// <para>
/// Hosts are compared exactly as they are given, so whoever asks settles the spelling first: the
/// collector in lower case, the engine in the ASCII form a name server is asked for.
/// </para>
/// </remarks>
public static class PublishedHosts
{
    /// <summary>The label a website's second name usually begins with.</summary>
    private const string TwinLabel = "www.";

    /// <summary>
    /// Whether a website is published at a host: at those it permits, or at its own domain and the
    /// addresses below it where it names none.
    /// </summary>
    /// <remarks>
    /// The permitted hosts arrive as a span because the collector asks this of every report, and
    /// what it holds them in is a value that any list-shaped parameter would box.
    /// </remarks>
    /// <param name="domain">The website's domain.</param>
    /// <param name="allowedOrigins">The hosts it permits, which replace the domain when there are any.</param>
    /// <param name="host">The host being asked about.</param>
    /// <returns>Whether the website is published at it.</returns>
    public static bool Covers(string domain, ReadOnlySpan<string> allowedOrigins, string host)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(host);

        if (allowedOrigins.IsEmpty)
        {
            return Matches(host, domain);
        }

        foreach (var allowed in allowedOrigins)
        {
            if (Matches(host, allowed))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a host is a permitted name or an address below it.
    /// </summary>
    /// <remarks>
    /// The whole label is compared, and that is the point of the dot: without it,
    /// <c>notexample.com</c> would count as an address below <c>example.com</c>.
    /// </remarks>
    /// <param name="candidate">The host being asked about.</param>
    /// <param name="allowed">The permitted name.</param>
    /// <returns>Whether the candidate is that name or lies below it.</returns>
    public static bool Matches(string candidate, string allowed)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(allowed);

        return string.Equals(candidate, allowed, StringComparison.Ordinal)
            || candidate.EndsWith('.' + allowed, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether an address a sitemap leads to is on the website that sitemap speaks for: the host the
    /// sitemap is on, or that host with or without <c>www.</c> in front of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The location rule of the sitemaps.org protocol, which says a sitemap may only list addresses
    /// on its own host. It is narrower than <see cref="Covers"/> on purpose. A website's domain is
    /// typed by whoever adds it and nothing checks that it is theirs, so it can name a whole hosting
    /// platform; this is what stops one sitemap on that platform being read as a list of everybody
    /// else's pages on it, each of them fetched on the strength of a file a stranger wrote.
    /// </para>
    /// <para>
    /// The one allowance is the twin, because a website served at both names routinely writes one
    /// of them into its sitemap and is read at the other. A sibling such as <c>shop.example.com</c>
    /// and an address below such as <c>blog.example.com</c> are other websites as far as a sitemap on
    /// <c>example.com</c> goes. Neither side needs to be free of a trailing dot, which is how a host
    /// can arrive from an address.
    /// </para>
    /// </remarks>
    /// <param name="sitemapHost">The host of the sitemap doing the listing.</param>
    /// <param name="otherHost">The host of the address it leads to.</param>
    /// <returns>Whether the two are one website.</returns>
    public static bool SameWebsite(string sitemapHost, string otherHost)
    {
        ArgumentNullException.ThrowIfNull(sitemapHost);
        ArgumentNullException.ThrowIfNull(otherHost);

        return WithoutTwinLabel(sitemapHost).Equals(WithoutTwinLabel(otherHost), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The one name a host and its twin go by, for counting them as one website.
    /// </summary>
    /// <remarks>
    /// How often a website is asked for anything is a limit on a server, and two names that
    /// <see cref="SameWebsite"/> treats as one are, as far as anybody can tell, one server.
    /// </remarks>
    /// <param name="host">The host.</param>
    /// <returns>The name, in lower case, which two hosts share exactly when they are one website.</returns>
    public static string WebsiteName(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return WithoutTwinLabel(host).ToString().ToLowerInvariant();
    }

    /// <summary>
    /// A host without its trailing dot, and without <c>www.</c> where a name is left behind.
    /// </summary>
    /// <remarks>
    /// <c>www.com</c> stays as it is. What would be left is a bare suffix, which is nobody's website,
    /// so it has no twin to be one with.
    /// </remarks>
    private static ReadOnlySpan<char> WithoutTwinLabel(string host)
    {
        var name = host.AsSpan().TrimEnd('.');

        if (!name.StartsWith(TwinLabel, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        var apex = name[TwinLabel.Length..];

        return apex.Contains('.') ? apex : name;
    }
}
