namespace Dewiride.Analytics.Domain.Sites;

/// <summary>
/// A website whose traffic is being observed.
/// </summary>
/// <remarks>
/// All telemetry is keyed by site, never by organisation. Sites get transferred between
/// organisations, and re-keying a billion telemetry rows to follow an ownership change is
/// not a migration anyone survives.
/// </remarks>
public sealed class Site
{
    /// <summary>
    /// Identity of the site. This value is public: it is embedded in the tracker snippet the
    /// customer pastes into their pages, and it is what the collector matches an incoming
    /// event against. It identifies a site; it grants nothing.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>Organisation that currently owns the site.</summary>
    public Guid OrganizationId { get; private set; }

    /// <summary>Primary hostname, lower-cased and without scheme or trailing dot.</summary>
    public string Domain { get; private set; }

    /// <summary>Name shown in the dashboard. Defaults to the domain.</summary>
    public string DisplayName { get; private set; }

    /// <summary>
    /// IANA time zone the site's owner thinks in, used to bucket "yesterday" and daily
    /// reports. Stored as an identifier rather than an offset so that daylight-saving
    /// transitions are handled correctly.
    /// </summary>
    public string TimeZoneId { get; private set; }

    /// <summary>When the site was added.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Whether query strings are retained on collected events. Off by default: query strings
    /// on content sites routinely carry tracking parameters and occasionally carry things
    /// that should never have been in a URL.
    /// </summary>
    public bool RetainQueryStrings { get; private set; }

    private readonly List<string> _allowedOrigins = [];

    /// <summary>
    /// Origins permitted to submit events for this site. Empty means the site's own domain
    /// and its subdomains only. The collector is a public unauthenticated endpoint, so this
    /// is the first line of defence against someone pointing a firehose at another
    /// customer's site identifier.
    /// </summary>
    public IReadOnlyCollection<string> AllowedOrigins => _allowedOrigins.AsReadOnly();

    private Site()
    {
        Domain = string.Empty;
        DisplayName = string.Empty;
        TimeZoneId = "Etc/UTC";
    }

    /// <summary>Registers a site.</summary>
    /// <param name="id">Identity to assign; also the public tracker identifier.</param>
    /// <param name="organizationId">Owning organisation.</param>
    /// <param name="domain">Primary hostname.</param>
    /// <param name="timeZoneId">IANA time zone identifier.</param>
    /// <param name="createdAt">Creation time, from the injected clock.</param>
    /// <exception cref="ArgumentException">
    /// The domain is empty or whitespace, or the time zone is not a known IANA identifier.
    /// </exception>
    public Site(Guid id, Guid organizationId, string domain, string timeZoneId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        // The identifier reaches the telemetry store as the time zone its daily buckets are cut
        // in, so an unrecognised one has to be refused where it is set rather than surfacing as
        // a failed report later.
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
        {
            throw new ArgumentException(
                $"'{timeZoneId}' is not a known IANA time zone identifier.",
                nameof(timeZoneId));
        }

        Id = id;
        OrganizationId = organizationId;
        Domain = NormalizeDomain(domain);
        DisplayName = Domain;
        TimeZoneId = timeZoneId;
        CreatedAt = createdAt;
    }

    /// <summary>Sets the name shown in the dashboard.</summary>
    /// <param name="displayName">The new display name.</param>
    /// <exception cref="ArgumentException">The name is empty or whitespace.</exception>
    public void SetDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
    }

    /// <summary>Sets whether query strings are retained on collected events.</summary>
    /// <param name="retain">Whether to retain them.</param>
    public void SetQueryStringRetention(bool retain) => RetainQueryStrings = retain;

    /// <summary>Replaces the set of origins permitted to submit events.</summary>
    /// <param name="origins">The permitted origins. Null or empty restores the default.</param>
    public void ReplaceAllowedOrigins(IEnumerable<string>? origins)
    {
        _allowedOrigins.Clear();

        if (origins is null)
        {
            return;
        }

        _allowedOrigins.AddRange(
            origins.Where(origin => !string.IsNullOrWhiteSpace(origin))
                   .Select(NormalizeDomain));
    }

    /// <summary>Moves the site to a different organisation.</summary>
    /// <param name="organizationId">The organisation to move it to.</param>
    public void TransferTo(Guid organizationId) => OrganizationId = organizationId;

    private static string NormalizeDomain(string value) =>
        value.Trim().TrimEnd('.').ToLowerInvariant();
}
